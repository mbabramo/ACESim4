using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Serialization;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimDistributed
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            int workerId = ReadIntArgument(args, "--worker-id") ?? 0;
            int? processorAffinity = ReadIntArgument(args, "--processor-affinity");
            if (processorAffinity != null)
                ApplyProcessorAffinity(processorAffinity.Value);

            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };

            Launcher launcher = Launcher.GetLauncher();
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string logFileName =
                $"{launcher.MasterReportNameForDistributedProcessing} worker-{workerId:D3} pid-{Environment.ProcessId} {timestamp}.log.txt";
            string processLogDirectory = Path.Combine(Launcher.ReportFolder(), "Process Logs");
            string localLogPath = Path.Combine(processLogDirectory, logFileName);
            string activeMarkerPath = null;

            if (!launcher.SaveToAzureBlob)
            {
                Directory.CreateDirectory(processLogDirectory);
                activeMarkerPath = Path.Combine(
                    processLogDirectory,
                    Path.ChangeExtension(logFileName, ".active"));
                using Process currentProcess = Process.GetCurrentProcess();
                File.WriteAllText(
                    activeMarkerPath,
                    $"{Environment.ProcessId}|{currentProcess.StartTime.ToUniversalTime().Ticks}");
            }

            void LogMessage(string text)
            {
                string line = $"{DateTime.UtcNow:O} {text}{Environment.NewLine}";
                if (launcher.SaveToAzureBlob)
                {
                    AzureBlob.WriteTextToFileOrAzure(
                        "results",
                        null,
                        logFileName,
                        true,
                        line,
                        useAzure: true);
                }
                else
                {
                    File.AppendAllText(localLogPath, line);
                }
                TabbedText.WriteLine(text);
            }

            try
            {
                LogMessage(
                    $"Worker {workerId} starting; PID {Environment.ProcessId}; " +
                    $"plan {launcher.GetUninitializedTaskList().PlanFingerprint}.");
                if (launcher.LaunchSingleOptionsSetOnly)
                    throw new InvalidOperationException(
                        "LaunchSingleOptionsSetOnly is not valid for a distributed worker.");

                await launcher.ParticipateInDistributedProcessing(
                    launcher.MasterReportNameForDistributedProcessing,
                    cancellationSource.Token,
                    LogMessage);
                LogMessage($"Worker {workerId} completed normally.");
                return 0;
            }
            catch (OperationCanceledException)
            {
                LogMessage($"Worker {workerId} cancelled.");
                return 2;
            }
            catch (Exception ex)
            {
                LogMessage($"Worker {workerId} failed: {ex}");
                return 1;
            }
            finally
            {
                if (activeMarkerPath != null && File.Exists(activeMarkerPath))
                    File.Delete(activeMarkerPath);
            }
        }

        private static int? ReadIntArgument(string[] args, string name)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value))
                    throw new ArgumentException($"{name} requires an integer value.");
                return value;
            }

            return null;
        }

        private static void ApplyProcessorAffinity(int processorNumber)
        {
            if (processorNumber < 0 || processorNumber >= Math.Min(Environment.ProcessorCount, 64))
                throw new ArgumentOutOfRangeException(
                    nameof(processorNumber),
                    $"Processor affinity must be between 0 and {Math.Min(Environment.ProcessorCount, 64) - 1}.");

#pragma warning disable CA1416
            Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)(1L << processorNumber);
#pragma warning restore CA1416
        }
    }
}
