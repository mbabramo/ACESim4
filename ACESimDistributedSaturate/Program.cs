using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.TaskManagement;
using LitigCharts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ACESimDistributedSaturate
{
    internal static class Program
    {
        private const string SmokeCoordinatorFilename = "smoke-coordinator.bin";

        public static async Task<int> Main(string[] args)
        {
            // Preserve the established Visual Studio workflow: Ctrl+F5 supplies no arguments,
            // which starts production using every processor available to this process.
            string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
            try
            {
                return command switch
                {
                    "preflight" => RunPreflight(),
                    "run" => await RunProductionAsync(args.Skip(1).ToArray()),
                    "status" => ShowStatus(),
                    "recover" => Recover(args.Skip(1).ToArray()),
                    "aggregate" => AggregateAndReport(),
                    "smoke-test" => await RunSmokeTestAsync(),
                    "smoke-worker" => await RunSmokeWorkerAsync(args.Skip(1).ToArray()),
                    "help" or "--help" or "-h" => ShowHelp(),
                    _ => throw new ArgumentException($"Unknown command '{command}'. Use 'help' for usage."),
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static LitigGameCorrelatedSignalsArticleLauncher CreateLauncher() => new();

        private static int RunPreflight()
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher();
            List<GameOptions> optionSets = launcher.GetOptionsSets();
            LitigGameCorrelatedSignalsArticleLauncher.ProductionMatrixAudit audit =
                launcher.ValidateProductionMatrix(optionSets);
            TaskCoordinator coordinator = launcher.GetUninitializedTaskList();

            Console.WriteLine("Correlated-signals production preflight passed.");
            Console.WriteLine($"Option sets: {audit.OptionSetCount}");
            Console.WriteLine($"Core combinations: {audit.CoreCombinationCount}");
            Console.WriteLine($"Matched structure pairs: {audit.PairedComparisonCount}");
            Console.WriteLine($"Worker tasks: {coordinator.NumIndividualTasks}");
            Console.WriteLine($"Task-plan fingerprint: {coordinator.PlanFingerprint}");
            foreach (var count in audit.CountsByInformationAndRisk)
                Console.WriteLine($"  {count.Key}: {count.Value}");

            Console.WriteLine();
            Console.WriteLine(
                "TaskID,OptionSetName,SignalStructure,InformationLevel,PartySignalSigma,CourtSignalSigma," +
                "CostsMultiplier,FeeShiftingMultiplier,RiskAversion,Generator,Offers,LiabilitySignals," +
                "LiabilityStrengthPoints,LiabilityShaping,DamagesShaping");
            for (int taskId = 0; taskId < optionSets.Count; taskId++)
            {
                var options = (LitigGameOptions)optionSets[taskId];
                string generator = options.LitigGameDisputeGenerator switch
                {
                    LitigGameExogenousDisputeGenerator => "CaseQuality",
                    LitigGameExogenousDirectSignalDisputeGenerator => "BinaryTruth",
                    _ => options.LitigGameDisputeGenerator.GetType().Name,
                };
                Console.WriteLine(string.Join(",", new[]
                {
                    taskId.ToString(CultureInfo.InvariantCulture),
                    Csv(options.Name),
                    Csv(Setting(options, "Signal Structure")),
                    Csv(Setting(options, "Information Level")),
                    Csv(Setting(options, "Party Signal Sigma")),
                    Csv(Setting(options, "Court Signal Sigma")),
                    Csv(Setting(options, "Costs Multiplier")),
                    Csv(Setting(options, "Fee Shifting Multiplier")),
                    Csv(Setting(options, "Risk Aversion")),
                    generator,
                    options.NumOffers.ToString(CultureInfo.InvariantCulture),
                    options.NumLiabilitySignals.ToString(CultureInfo.InvariantCulture),
                    options.NumLiabilityStrengthPoints.ToString(CultureInfo.InvariantCulture),
                    options.LiabilitySignalShapeParameters.Mode.ToString(),
                    options.DamagesSignalShapeParameters.Mode.ToString(),
                }));
            }

            return 0;
        }

        private static async Task<int> RunProductionAsync(string[] args)
        {
#if DEBUG
            throw new InvalidOperationException("Production must be launched from a Release build.");
#endif
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher();
            launcher.ValidateProductionMatrix(launcher.GetOptionsSets());
            int processorCount = ParseProcessorCount(args);
            string workerExecutable = Path.Combine(AppContext.BaseDirectory, "ACESimDistributed.exe");
            if (!File.Exists(workerExecutable))
                throw new FileNotFoundException(
                    "Distributed worker executable was not found. Build the Release configuration first.",
                    workerExecutable);

            Console.WriteLine(
                $"Launching {processorCount} visible worker windows for {launcher.GetUninitializedTaskList().NumIndividualTasks} tasks " +
                $"using Environment.ProcessorCount={Environment.ProcessorCount}.");

            var workers = new List<Process>();
            try
            {
                for (int workerId = 0; workerId < processorCount; workerId++)
                    workers.Add(StartVisibleWorkerProcess(workerExecutable, workerId));

                string lastStatus = null;
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    TaskCoordinator coordinator;
                    try
                    {
                        coordinator = launcher.LoadTaskCoordinatorStatus();
                    }
                    catch (FileNotFoundException)
                    {
                        if (workers.All(x => x.HasExited))
                            throw new InvalidOperationException("All workers exited before creating the coordinator.");
                        continue;
                    }

                    string status = coordinator.ToString();
                    if (!string.Equals(status, lastStatus, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"{DateTime.Now:O} {status}");
                        lastStatus = status;
                    }

                    if (coordinator.HasFailures)
                        throw new InvalidOperationException(
                            "Production stopped with failed tasks. Run 'status', inspect failure logs, then use " +
                            "'recover --failed --include-pending' after all workers have stopped.");
                    if (coordinator.AllComplete)
                    {
                        Console.WriteLine("All worker tasks completed successfully. Aggregation has not been started.");
                        return 0;
                    }
                    if (workers.All(x => x.HasExited))
                        throw new InvalidOperationException(
                            "All worker processes exited before the coordinator completed. Run 'status' and recover pending tasks after confirming no workers remain.");
                }
            }
            finally
            {
                foreach (Process worker in workers.Where(x => !x.HasExited))
                {
                    try
                    {
                        worker.Kill(entireProcessTree: true);
                        worker.WaitForExit(10_000);
                    }
                    catch
                    {
                        // Best effort during coordinator shutdown.
                    }
                }
                foreach (Process worker in workers)
                    worker.Dispose();
            }
        }

        private static int ShowStatus()
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher();
            try
            {
                TaskCoordinator coordinator = launcher.LoadTaskCoordinatorStatus();
                Console.WriteLine(coordinator);
                foreach (IndividualTask task in coordinator.Tasks.Where(x => x.Failed || x.Started != null && !x.Complete))
                    Console.WriteLine(task);
                Console.WriteLine($"Coordinator: {launcher.GetReportFullPath(null, "Coordinator")}");
                Console.WriteLine($"Process logs: {Path.Combine(launcher.GetReportFolder(), "Process Logs")}");
                Console.WriteLine($"Failure logs: {Path.Combine(launcher.GetReportFolder(), "CS001 FAILURE *.txt")}");
                return coordinator.HasFailures ? 1 : 0;
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Production has not been started; no coordinator exists.");
                return 0;
            }
        }

        private static int Recover(string[] args)
        {
            bool resetFailed = args.Length == 0 || args.Contains("--failed", StringComparer.OrdinalIgnoreCase);
            bool resetPending = args.Contains("--include-pending", StringComparer.OrdinalIgnoreCase);
            if (!resetFailed && !resetPending)
                throw new ArgumentException("Recovery requires --failed and/or --include-pending.");

            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher();
            IReadOnlyList<string> activeWorkers = FindActiveWorkers(launcher);
            if (activeWorkers.Count > 0)
                throw new InvalidOperationException(
                    "Refusing recovery while distributed workers are running: " +
                    string.Join(", ", activeWorkers) + ". Stop them first.");

            var result = launcher.ResetIncompleteDistributedTasks(resetFailed, resetPending);
            Console.WriteLine($"Reset failed tasks: {result.failedReset}");
            Console.WriteLine($"Reset pending tasks: {result.pendingReset}");
            Console.WriteLine(result.coordinator);
            Console.WriteLine("Restart with the production 'run --processors all' command.");
            return 0;
        }

        private static IReadOnlyList<string> FindActiveWorkers(Launcher launcher)
        {
            string processLogDirectory = Path.Combine(launcher.GetReportFolder(), "Process Logs");
            if (!Directory.Exists(processLogDirectory))
                return Array.Empty<string>();

            var active = new List<string>();
            foreach (string markerPath in Directory.GetFiles(processLogDirectory, "CS001 worker-*.active"))
            {
                string[] fields;
                try
                {
                    fields = File.ReadAllText(markerPath).Split('|');
                }
                catch (IOException)
                {
                    active.Add(Path.GetFileName(markerPath));
                    continue;
                }

                if (fields.Length != 2 ||
                    !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int processId) ||
                    !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expectedStartTicks))
                    continue;

                try
                {
                    using Process process = Process.GetProcessById(processId);
                    long actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
                    if (!process.HasExited && actualStartTicks == expectedStartTicks)
                        active.Add($"PID {processId} ({Path.GetFileName(markerPath)})");
                }
                catch (ArgumentException)
                {
                    // The marker is stale because the process no longer exists.
                }
            }
            return active;
        }

        private static int AggregateAndReport()
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher();
            TaskCoordinator coordinator = launcher.EnsureDistributedRunReadyForAggregation();
            Console.WriteLine("Coordinator and all primary result files validated: " + coordinator);
            Runner.ProcessLitigationGameData(Runner.DataBeingAnalyzed.CorrelatedSignalsArticle);
            return 0;
        }

        private static async Task<int> RunSmokeTestAsync()
        {
            string smokeDirectory = Path.Combine(
                Path.GetTempPath(),
                "ACESim4-correlated-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(smokeDirectory);
            string coordinatorPath = Path.Combine(smokeDirectory, SmokeCoordinatorFilename);
            TaskCoordinator coordinator = CreateSmokeCoordinator();
            File.WriteAllBytes(coordinatorPath, coordinator.StatusAsByteArray());

            string selfAssembly = Assembly.GetExecutingAssembly().Location;
            var workers = new[]
            {
                StartDotnetProcess(selfAssembly, "smoke-worker", "--directory", smokeDirectory, "--worker-id", "0"),
                StartDotnetProcess(selfAssembly, "smoke-worker", "--directory", smokeDirectory, "--worker-id", "1"),
            };

            bool succeeded = false;
            try
            {
                await Task.WhenAll(workers.Select(worker => worker.WaitForExitAsync()));
                if (workers.Any(worker => worker.ExitCode != 0))
                    throw new InvalidOperationException(
                        "A smoke worker failed: " + string.Join(", ", workers.Select(x => x.ExitCode)));

                coordinator.StatusFromByteArray(File.ReadAllBytes(coordinatorPath));
                if (!coordinator.AllComplete || coordinator.NumTasksComplete != 4 || coordinator.HasFailures)
                    throw new InvalidOperationException("Smoke coordinator did not record four successful distinct tasks: " + coordinator);

                string[] resultFiles = Directory.GetFiles(smokeDirectory, "smoke-result-*.csv");
                if (resultFiles.Length != 4 || resultFiles.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                    throw new InvalidOperationException("Smoke worker output filenames collided or are missing.");

                string firstAggregation = AggregateSmokeResults(resultFiles);
                string secondAggregation = AggregateSmokeResults(resultFiles.Reverse().ToArray());
                if (!string.Equals(firstAggregation, secondAggregation, StringComparison.Ordinal))
                    throw new InvalidOperationException("Smoke aggregation is not deterministic.");

                string[] pairedLines = firstAggregation.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (pairedLines.Length != 3 || pairedLines.Skip(1).Any(line => !line.Contains("Case quality") || !line.Contains("Binary truth")))
                    throw new InvalidOperationException("Smoke reports did not pair both signal structures for both settings.");

                Console.WriteLine("Bounded two-process smoke test passed.");
                Console.WriteLine(coordinator);
                Console.WriteLine(firstAggregation);
                succeeded = true;
                return 0;
            }
            finally
            {
                foreach (Process worker in workers)
                    worker.Dispose();
                if (succeeded && Directory.Exists(smokeDirectory))
                    Directory.Delete(smokeDirectory, recursive: true);
                else
                    Console.Error.WriteLine($"Smoke artifacts retained for diagnosis: {smokeDirectory}");
            }
        }

        private static async Task<int> RunSmokeWorkerAsync(string[] args)
        {
            string directory = RequiredArgument(args, "--directory");
            int workerId = int.Parse(RequiredArgument(args, "--worker-id"), CultureInfo.InvariantCulture);
            string coordinatorPath = Path.Combine(directory, SmokeCoordinatorFilename);
            var completed = new List<IndividualTask>();

            while (true)
            {
                List<IndividualTask> claimed = null;
                bool allComplete = false;
                AzureBlob.TransformSharedFileByteArray(directory, SmokeCoordinatorFilename, bytes =>
                {
                    TaskCoordinator coordinator = CreateSmokeCoordinator();
                    coordinator.StatusFromByteArray(bytes);
                    coordinator.Update(completed, null, true, 1, out claimed, out allComplete);
                    completed = new List<IndividualTask>();
                    return coordinator.StatusAsByteArray();
                });

                if (allComplete)
                    return 0;
                if (claimed == null)
                {
                    await Task.Delay(10);
                    continue;
                }

                IndividualTask task = claimed.Single();
                string structure = task.ID % 2 == 0
                    ? LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel
                    : LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel;
                int pair = task.ID / 2;
                string outputPath = Path.Combine(
                    directory,
                    $"smoke-result-task{task.ID}-worker{workerId}-{(structure == LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel ? "case-quality" : "binary-truth")}.csv");
                using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine("Pair,Signal Structure,Value");
                    writer.WriteLine($"{pair},{structure},{task.ID + 1}");
                }
                completed.Add(task);
            }
        }

        private static TaskCoordinator CreateSmokeCoordinator() =>
            new(new List<TaskStage>
            {
                new(new List<RepeatedTask>
                {
                    new("Smoke", 0, 1, null),
                    new("Smoke", 1, 1, null),
                    new("Smoke", 2, 1, null),
                    new("Smoke", 3, 1, null),
                }),
            });

        private static string AggregateSmokeResults(IEnumerable<string> resultFiles)
        {
            var rows = resultFiles
                .Select(path => File.ReadLines(path).Skip(1).Single().Split(','))
                .Select(fields => new
                {
                    Pair = int.Parse(fields[0], CultureInfo.InvariantCulture),
                    Structure = fields[1],
                    Value = fields[2],
                })
                .GroupBy(row => row.Pair)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var caseQuality = group.Single(row => row.Structure == LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel);
                    var binaryTruth = group.Single(row => row.Structure == LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel);
                    return $"{group.Key},{caseQuality.Structure},{caseQuality.Value},{binaryTruth.Structure},{binaryTruth.Value}";
                });
            return "Pair,Case quality label,Case quality value,Binary truth label,Binary truth value" +
                Environment.NewLine + string.Join(Environment.NewLine, rows) + Environment.NewLine;
        }

        private static Process StartDotnetProcess(string assemblyPath, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(assemblyPath);
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);
            return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start child process.");
        }

        private static Process StartVisibleWorkerProcess(string executablePath, int workerId)
        {
            // This intentionally mirrors the established ACESimDistributedSaturate interface:
            // ShellExecute opens each console application in its own visible window so the user
            // can watch and stop workers individually.
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                Arguments = $"--worker-id {workerId.ToString(CultureInfo.InvariantCulture)}",
            };
            return Process.Start(startInfo) ?? throw new InvalidOperationException(
                $"Unable to start visible worker {workerId}.");
        }

        private static int ParseProcessorCount(string[] args)
        {
            string text = OptionalArgument(args, "--processors") ?? "all";
            if (string.Equals(text, "all", StringComparison.OrdinalIgnoreCase))
                return Environment.ProcessorCount;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
                throw new ArgumentException("--processors must be 'all' or a positive integer.");
            return count;
        }

        private static string RequiredArgument(string[] args, string name) =>
            OptionalArgument(args, name) ?? throw new ArgumentException($"Missing required argument {name}.");

        private static string OptionalArgument(string[] args, string name)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"Argument {name} requires a value.");
                return args[index + 1];
            }
            return null;
        }

        private static string Setting(GameOptions options, string name) =>
            Convert.ToString(options.VariableSettings[name], CultureInfo.InvariantCulture);

        private static string Csv(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? '"' + value.Replace("\"", "\"\"") + '"'
                : value;

        private static int ShowHelp()
        {
            Console.WriteLine("ACESim4 correlated-signals production commands:");
            Console.WriteLine("  <no arguments>              (production on all processors; normal Ctrl+F5 path)");
            Console.WriteLine("  preflight");
            Console.WriteLine("  run --processors all|N");
            Console.WriteLine("  status");
            Console.WriteLine("  recover --failed [--include-pending]");
            Console.WriteLine("  aggregate");
            Console.WriteLine("  smoke-test");
            return 0;
        }
    }
}
