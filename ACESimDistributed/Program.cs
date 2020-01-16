using ACESim;
using ACESim.Util;
using ACESimBase.Games.AdditiveEvidenceGame;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimDistributed
{
    class Program
    {
        public async static Task Main(string[] args)
        {
            // set processor affinity via argument
            if (args != null && args.Length > 0)
            {
                string arg = args[0];
                int processorNumber = Convert.ToInt32(arg);
                Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)(1L << processorNumber);
                Console.WriteLine($"Process ID {Process.GetCurrentProcess().Id} set to processor {processorNumber}");
            }

            long iterations = 0;
            string dateTimeString = DateTime.Now.ToString("yyyy-mm-dd-hh-mm");
            string processID = "p" + Process.GetCurrentProcess().Id;
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

                    // DEBUG
                    MyGameLauncher launcher = new MyGameLauncher();
                    //AdditiveEvidenceGameLauncher launcher = new AdditiveEvidenceGameLauncher();

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
