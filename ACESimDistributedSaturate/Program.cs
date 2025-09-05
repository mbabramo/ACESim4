using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using LitigCharts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimDistributedSaturate
{
    class Program
    {
        async static Task Main(string[] args)
        {
            string currentPath = Process.GetCurrentProcess().MainModule.FileName;
            bool allowDebugging = false;
            if (!allowDebugging && currentPath.Contains("Debug"))
                throw new Exception("Set to release mode before saturating.");
            string targetPath = currentPath.Replace("ACESimDistributedSaturate", "ACESimDistributed");
            Console.WriteLine(targetPath);
            const int maxNumProcessors = 20;
            int numProcessors = Environment.ProcessorCount;
            if (numProcessors > maxNumProcessors)
                numProcessors = maxNumProcessors;
            Console.WriteLine($"Launching on {numProcessors} processors");
            string masterReportName = Launcher.GetLauncher().MasterReportNameForDistributedProcessing;
            bool useAzure = new EvolutionSettings().SaveToAzureBlob;
            List<Process> processes = new List<Process>();
            for (int i = 0; i < numProcessors; i++)
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(targetPath, i.ToString());
                processStartInfo.UseShellExecute = true;
                var process = Process.Start(processStartInfo);
                if (process != null)
                    processes.Add(process);
            }

            Console.WriteLine($"Press escape to kill a pending process.");

            // Added task to monitor for the Escape key press and kill a pending process.
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() =>
            {
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            var procToKill = processes.FirstOrDefault(p => !p.HasExited);
                            if (procToKill != null)
                            {
                                Console.WriteLine($"Escape pressed. Killing process ID: {procToKill.Id}");
                                try
                                {
                                    procToKill.Kill();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to kill process {procToKill.Id}: {ex.Message}");
                                }
                            }
                        }
                    }
                    Task.Delay(100).Wait();
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            Stopwatch s = new Stopwatch();
            s.Start();

            var launcher = Launcher.GetLauncher();
            var coordinator = launcher.GetUninitializedTaskList();
            while (true)
            {
                await Task.Delay(10_000);
                try
                {
                    // TODO: It would be good if we could use memory-mapped files for task coordination, at least within a particular machine. Maybe this process
                    // should use a real file (so that it can coordinate with another process on Azure), but on the machine, just memory-mapped files would be used.
                    // But that creates the further complication that the tasks have to be allocated by this central process, rather than in a completely decentralized
                    // way.

                    byte[] result = AzureBlob.GetByteArrayFromFileOrAzure(Launcher.ReportFolder(), "results", masterReportName + " Coordinator", useAzure);
                    coordinator.StatusFromByteArray(result);
                    Console.WriteLine($"Proportion complete {coordinator.ProportionComplete} after {s.Elapsed}");
                    if (coordinator.AllComplete)
                    {
                        foreach (var process in processes)
                            process.Kill();
                        // LitigCharts.Runner.FeeShiftingArticle(); // run the LitigCharts code -- usually preferable to run it afterward (after saving all reports)
                        return;
                    }
                }
                catch (Exception ex) when (!(ex.Message.Contains("datum")))
                {
                    Console.WriteLine($"Task coordination file busy after {s.Elapsed}");
                }
            }
        }
    }
}
