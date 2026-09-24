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
            NewCases=runnable, ExternalReservations=30, HiddenWorkers=true, ThreadsPerSolve=1 });
        var workers=new List<Process>();
        try
        {
            for(int id=0;id<count;id++)
            {
                var info=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden };
                foreach(string key in new[] { "DOTNET_PROCESSOR_COUNT","OMP_NUM_THREADS","MKL_NUM_THREADS","OPENBLAS_NUM_THREADS" }) info.Environment[key]="1";
                foreach(string value in new[] { "--worker-id",id.ToString(),"--final-manifest",manifest,"--results-directory",contract.ResultsDirectory }) info.ArgumentList.Add(value);
                var worker=Process.Start(info) ?? throw new InvalidOperationException("Could not start final worker.");
                workers.Add(worker);
                WriteNew(Path.Combine(contract.ResultsDirectory,$"worker-{id:D3}.launch.json"),new { worker.Id, StartedUtc=DateTime.UtcNow, Executable=executable, Arguments=info.ArgumentList.ToArray() });
            }
            // Event waits are idle; no process or original-study polling loop.
            await Task.WhenAll(workers.Select(p => p.WaitForExitAsync()));
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
        finally { foreach(var worker in workers) worker.Dispose(); }
    }
}
