using ACESim;
using ACESim.Util;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimDistributed
{
    class Program
    {
        public async static Task Main(string[] args)
        {
            long iterations = 0;
            string dateTimeString = DateTime.Now.ToString("yyyy-mm-dd-hh-mm");
            Process currentProcess = Process.GetCurrentProcess();
            string processID = "p" + currentProcess.Id;
            var byName = Process.GetProcessesByName(currentProcess.ProcessName);
            var processorAffinities = Enumerable.Range(0, Environment.ProcessorCount).Select(x => (x, (IntPtr)(1L << x))).ToArray();
            var selectedProcessorAffinity = processorAffinities.OrderBy(x => byName.Count(y => y.ProcessorAffinity == x.Item2)).First().Item2;
            currentProcess.ProcessorAffinity = selectedProcessorAffinity;
            CancellationToken cancellationToken = new CancellationToken();
            string containerName = "results";
            string fileName = "log" + "-" + processID + "-" + dateTimeString;
            AzureBlob.SerializeObject(containerName, fileName, true, "Starting");

            void LogMessage(string text)
            {
                string original = AzureBlob.GetSerializedObject(containerName, fileName) as string ?? "";
                string revised = original + "\r\n" + text;
                AzureBlob.SerializeObject(containerName, fileName, true, revised);
            }

            while (iterations < 10)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    MyGameLauncher launcher = new MyGameLauncher();

                    if (launcher.LaunchSingleOptionsSetOnly)
                        throw new Exception("LaunchSingleOptionsSetOnly should only be used with ACESimConsole.");

                    await launcher.ParticipateInDistributedProcessing(
                        launcher.MasterReportNameForDistributedProcessing,
                        cancellationToken,
                        message => LogMessage(message)
                        );
                    return;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    try
                    {
                        LogMessage(ex.Message + ex.StackTrace);
                        iterations++;
                    }
                    catch
                    {

                    }
                }

            }
        }
    }
}
