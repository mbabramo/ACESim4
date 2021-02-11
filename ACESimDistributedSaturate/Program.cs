using ACESim;
using ACESim.Util;
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
            // DEBUG -- maybe doesn't matter  =======>>>>>>> IMPORTANT NOTE: Make sure to build before running and confirm that ACESimDistributed is built recently.
            string currentPath = Process.GetCurrentProcess().MainModule.FileName;
            bool allowDebugging = false;
            if (!allowDebugging && currentPath.Contains("Debug"))
                throw new Exception("Set to release mode before saturating.");
            string targetPath = currentPath.Replace("ACESimDistributedSaturate", "ACESimDistributed");
            Console.WriteLine(targetPath);
            const int maxNumProcessors = 32; 
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

            Stopwatch s = new Stopwatch();
            s.Start();

            while (true)
            {
                await Task.Delay(10_000);
                try
                {
                    TaskCoordinator coordinator = (TaskCoordinator)AzureBlob.GetSerializedObjectFromFileOrAzure(Launcher.ReportFolder(), "results", masterReportName + " Coordinator", useAzure);
                    Console.WriteLine($"Proportion complete {coordinator.ProportionComplete} after {s.Elapsed}");
                    if (coordinator.Complete)
                    {
                        foreach (var process in processes)
                            process.Kill();
                        return;
                    }
                }
                catch
                {

                }
            }
        }
    }
}
