using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimDistributedSaturate;

/// <summary>The existing process coordinator with explicit final-case manifests and permanent claims.</summary>
internal static class FinalArticleQueue
{
    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus memory);

    private static void WriteNew(string file, object value)
    {
        using var stream = new FileStream(file,FileMode.CreateNew,FileAccess.Write);
        JsonSerializer.Serialize(stream,value,FinalArticleExecution.Json); stream.Flush(true);
    }
    public static async Task<int> RunAsync(string command, string[] args)
    {
#if DEBUG
        throw new InvalidOperationException("Final article commands require Release.");
#endif
        string manifest = null; int? processors = null;
        for (int i=0;i<args.Length;i++)
            switch(args[i])
            {
                case "--manifest": manifest=Path.GetFullPath(args[++i]); break;
                case "--processors": processors=int.Parse(args[++i]); break;
                default: throw new ArgumentException("Unknown final queue argument: "+args[i]);
            }
        if (manifest == null) throw new ArgumentException("Supply --manifest FILE; run also requires --processors N.");
        var launcher = new FinalArticleLauncher(manifest);
        var contract = launcher.Manifest;
        Environment.SetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable,contract.ResultsDirectory);
        int runnable = launcher.GetOptionsSets().Count;
        if (command == "final-preflight")
        {
            Console.WriteLine($"Validated {contract.Cases.Length} cases: 30 import-only, {runnable} new primary solves. Zero workers started.");
            return 0;
        }
        if (command == "final-status")
        {
            try { Console.WriteLine(launcher.LoadTaskCoordinatorStatus()); }
            catch(FileNotFoundException) { Console.WriteLine("No final coordinator has been started."); }
            return 0;
        }
        if (command != "final-run" || processors == null) throw new ArgumentException("final-run requires an explicit --processors N.");
        int count=processors.Value;
        FinalArticleExecution.ValidateBudget(contract.Resources,count);
        if(count>FinalArticleExecution.AvailableNewWorkerSlots(contract)) throw new InvalidOperationException("Initial workers exceed currently free shared capacity.");
        if (runnable < count) throw new ArgumentException("Do not allocate more workers than new cases.");
        var memory = new MemoryStatus();
        if (!OperatingSystem.IsWindows() || !GlobalMemoryStatusEx(memory)) throw new InvalidOperationException("Cannot inspect available Windows memory.");
        double availableGiB=memory.AvailablePhysical/1073741824.0;
        if (availableGiB < count*contract.Resources.EstimatedPeakWorkerGiB+contract.Resources.MemoryHeadroomGiB)
            throw new InvalidOperationException("Available memory does not cover the measured per-worker budget and headroom.");
        string executable=Path.Combine(AppContext.BaseDirectory,"ACESimDistributed.exe");
        if (!contract.BuildFiles.Any(f => string.Equals(Path.GetFullPath(f.Path),executable,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Worker executable is absent from the frozen dependency manifest.");
        if (!contract.BuildFiles.Any(f => string.Equals(Path.GetFullPath(f.Path),typeof(FinalArticleQueue).Assembly.Location,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Scheduler assembly is absent from the frozen dependency manifest.");
        Directory.CreateDirectory(contract.ResultsDirectory);
        // A permanent launch marker makes scheduler restarts explicit. No timer or
        // failure path resets claims, retries solved cases, or polls another queue.
        string marker=Path.Combine(contract.ResultsDirectory,"final-launch.json");
        WriteNew(marker,new { StartedUtc=DateTime.UtcNow, SchedulerPid=Environment.ProcessId, Manifest=manifest,
            launcher.ManifestSha256, Processors=count, AvailableGiB=availableGiB, Resources=contract.Resources,
            NewCases=runnable, ExternalReservations=30, HiddenWorkers=true, ThreadsPerSolve=1,
            AutomaticExpansionMaximum=contract.Resources.MaximumNewWorkers,ConcurrentPrimaryCohorts=contract.ConcurrentPrimaryCohorts });
        var workers=new List<Process>();
        var watchers=new List<FileSystemWatcher>();
        using var changed=new SemaphoreSlim(0,1);
        void Wake() { try { if(changed.CurrentCount==0) changed.Release(); } catch(SemaphoreFullException) { } }
        try
        {
            void StartWorker()
            {
                int id=workers.Count;
                var info=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden };
                foreach(string key in new[] { "DOTNET_PROCESSOR_COUNT","OMP_NUM_THREADS","MKL_NUM_THREADS","OPENBLAS_NUM_THREADS" }) info.Environment[key]="1";
                foreach(string value in new[] { "--worker-id",id.ToString(),"--final-manifest",manifest,"--results-directory",contract.ResultsDirectory }) info.ArgumentList.Add(value);
                var worker=Process.Start(info) ?? throw new InvalidOperationException("Could not start final worker.");
                workers.Add(worker);
                WriteNew(Path.Combine(contract.ResultsDirectory,$"worker-{id:D3}.launch.json"),new { worker.Id, StartedUtc=DateTime.UtcNow, Executable=executable, Arguments=info.ArgumentList.ToArray() });
            }
            foreach(var file in contract.ConcurrentPrimaryCohorts ?? [])
            {
                string path=FinalArticleExecution.ConcurrentCoordinatorPath(FinalArticleExecution.ReadConcurrentCohort(file,contract));
                var watcher=new FileSystemWatcher(Path.GetDirectoryName(path),Path.GetFileName(path)) {
                    NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size };
                watcher.Changed+=(_,_)=>Wake(); watcher.Created+=(_,_)=>Wake(); watcher.Renamed+=(_,_)=>Wake();
                watcher.Error+=(_,_)=>Wake(); watcher.EnableRaisingEvents=true; watchers.Add(watcher);
            }
            for(int id=0;id<count;id++) StartWorker();
            while(true)
            {
                int desired=Math.Min(runnable,FinalArticleExecution.AvailableNewWorkerSlots(contract));
                int additional=desired-workers.Count;
                if(additional>0)
                {
                    var now=new MemoryStatus();
                    if(!GlobalMemoryStatusEx(now)) throw new InvalidOperationException("Cannot recheck memory before capacity expansion.");
                    int affordable=Math.Max(0,(int)Math.Floor((now.AvailablePhysical/1073741824.0-contract.Resources.MemoryHeadroomGiB)/contract.Resources.EstimatedPeakWorkerGiB));
                    additional=Math.Min(additional,affordable);
                    if(additional>0)
                    {
                        WriteNew(Path.Combine(contract.ResultsDirectory,$"capacity-expansion-{workers.Count:D3}.json"),new {
                            Utc=DateTime.UtcNow,Before=workers.Count,Additional=additional,SharedAvailableSlots=desired,
                            AvailableGiB=now.AvailablePhysical/1073741824.0,Trigger="Completed tasks in a separate new article cohort; event-driven" });
                        for(int n=0;n<additional;n++) StartWorker();
                    }
                }
                // Watch only the NEW article cohort. No timer, original-study polling,
                // process interruption, or parallelism inside an individual solve.
                var exited=Task.WhenAll(workers.Select(p=>p.WaitForExitAsync()));
                using var cancel=new CancellationTokenSource();
                var notification=changed.WaitAsync(cancel.Token);
                if(await Task.WhenAny(exited,notification)==exited) { cancel.Cancel(); await exited; break; }
                await notification;
                await Task.Delay(250); // Coalesce file-write events before reading a complete coordinator record.
            }
            var coordinator=launcher.LoadTaskCoordinatorStatus();
            bool passed=workers.All(p => p.ExitCode==0) && coordinator.AllComplete && !coordinator.HasFailures;
            WriteNew(Path.Combine(contract.ResultsDirectory,"final-scheduler-result.json"),new {
                Passed=passed, FinishedUtc=DateTime.UtcNow, Coordinator=coordinator.ToString(),
                Workers=workers.Select(p => new { p.Id,p.ExitCode }).ToArray(),
                IndividualProfileValidationPending=true, AutomaticRetries=0 });
            return passed ? 0 : 1;
        }
        catch(Exception ex)
        {
            // Preserve running workers and every permanent claim on scheduler errors.
            // Recovery requires an explicit audit; it never means duplicate dispatch.
            WriteNew(Path.Combine(contract.ResultsDirectory,"final-scheduler-failure.json"),new { Error=ex.ToString(),Utc=DateTime.UtcNow,
                StartedWorkerIds=workers.Select(p=>p.Id).ToArray(), WorkersWereNotStopped=true });
            throw;
        }
        finally { foreach(var watcher in watchers) watcher.Dispose(); foreach(var worker in workers) worker.Dispose(); }
    }
}
