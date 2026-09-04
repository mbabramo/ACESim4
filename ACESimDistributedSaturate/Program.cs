using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingAlgorithms;
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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimDistributedSaturate
{
    internal static class Program
    {
        private const string SmokeCoordinatorFilename = "smoke-coordinator.bin";
        private const string ProductionSuiteDirectoryName = "ALER Production";

        public static async Task<int> Main(string[] args)
        {
            // Preserve the established Visual Studio workflow: Ctrl+F5 supplies no arguments,
            // which starts production using every processor available to this process.
            string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run-suite";
            try
            {
                return command switch
                {
                    "preflight" => RunPreflight(args.Skip(1).ToArray()),
                    "preflight-suite" => RunSuitePreflight(args.Skip(1).ToArray()),
                    "run" => await RunProductionAsync(args.Skip(1).ToArray()),
                    "run-suite" => await RunProductionSuiteAsync(args.Skip(1).ToArray()),
                    "status" => ShowStatus(args.Skip(1).ToArray()),
                    "recover" => Recover(args.Skip(1).ToArray()),
                    "aggregate" => AggregateAndReport(args.Skip(1).ToArray()),
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

        private static LitigGameCorrelatedSignalsArticleLauncher CreateLauncher(string[] args)
        {
            string resultsDirectory = OptionalArgument(args, "--results-directory");
            if (!string.IsNullOrWhiteSpace(resultsDirectory))
                Environment.SetEnvironmentVariable(
                    FolderFinder.ReportResultsDirectoryEnvironmentVariable,
                    Path.GetFullPath(resultsDirectory));
            return new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ParseProductionRunPlan(
                    OptionalArgument(args, "--plan") ?? "focused"));
        }

        private static int RunSuitePreflight(string[] args)
        {
            string resultsDirectory = ConfigureSuiteResultsDirectory(
                args,
                CaptureSourceState().GitCommit,
                createDirectory: false);
            Console.WriteLine($"Required ALER production suite: {resultsDirectory}");
            foreach (LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan plan in
                LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans)
            {
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(plan);
                LitigGameCorrelatedSignalsArticleLauncher.ProductionMatrixAudit audit =
                    launcher.ValidateProductionMatrix(launcher.GetOptionsSets());
                TaskCoordinator coordinator = launcher.GetUninitializedTaskList();
                Console.WriteLine(
                    $"  {launcher.MasterReportNameForDistributedProcessing}: " +
                    $"{audit.OptionSetCount} option sets, {coordinator.NumIndividualTasks} tasks, " +
                    $"plan {coordinator.PlanFingerprint}");
            }
            Console.WriteLine(
                "The four required 15-offer cases are integrated into CS004; " +
                "CS005O15 is not required by the suite.");
            return 0;
        }

        private static async Task<int> RunProductionSuiteAsync(string[] args)
        {
#if DEBUG
            throw new InvalidOperationException("Production must be launched from a Release build.");
#endif
            SourceState source = CaptureSourceState();
            if (source.ChangedPaths.Count > 0 &&
                !args.Contains("--allow-dirty", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The required production suite will not run from a dirty working tree. " +
                    "Commit or remove these paths, or pass --allow-dirty only for a nonfinal diagnostic run:" +
                    Environment.NewLine + string.Join(Environment.NewLine, source.ChangedPaths));

            string resultsDirectory = ConfigureSuiteResultsDirectory(args, source.GitCommit);
            Console.WriteLine($"ALER production suite output: {resultsDirectory}");
            Console.WriteLine($"Source commit: {source.GitCommit}");

            foreach (LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan plan in
                LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans)
            {
                string[] planArgs = WithOption(
                    WithOption(args, "--results-directory", resultsDirectory),
                    "--plan",
                    PlanArgument(plan));
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(plan);
                launcher.ValidateProductionMatrix(launcher.GetOptionsSets());
                Console.WriteLine(
                    $"Starting required plan {launcher.MasterReportNameForDistributedProcessing} ({plan}).");
                int runResult = await RunProductionAsync(planArgs);
                if (runResult != 0)
                    return runResult;
                int artifactCount = ValidateEquilibriumArtifacts(launcher);
                Console.WriteLine(
                    $"Validated {artifactCount} equilibrium/report artifacts for " +
                    $"{launcher.MasterReportNameForDistributedProcessing}.");
                int aggregateResult = AggregateAndReport(planArgs);
                if (aggregateResult != 0)
                    return aggregateResult;
                ValidateAggregatedOutputs(launcher);
            }

            SourceState finalSource = CaptureSourceState();
            if (!string.Equals(source.GitCommit, finalSource.GitCommit, StringComparison.OrdinalIgnoreCase) ||
                !source.ChangedPaths.SequenceEqual(finalSource.ChangedPaths, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    "The source state changed while the production suite was running. " +
                    "The results were not certified as a completed suite.");
            WriteSuiteManifest(resultsDirectory, finalSource);
            Console.WriteLine("Required ALER production suite completed and validated.");
            return 0;
        }

        private static string ConfigureSuiteResultsDirectory(
            string[] args,
            string gitCommit = null,
            bool createDirectory = true)
        {
            string requested = OptionalArgument(args, "--results-directory");
            string fullPath;
            if (!string.IsNullOrWhiteSpace(requested))
                fullPath = Path.GetFullPath(requested);
            else
            {
                string reportRoot = FolderFinder.GetFolderToWriteTo("ReportResults").FullName;
                string revision = string.IsNullOrWhiteSpace(gitCommit)
                    ? "preflight"
                    : gitCommit[..Math.Min(12, gitCommit.Length)];
                fullPath = Path.Combine(
                    reportRoot,
                    "Production Runs",
                    $"{ProductionSuiteDirectoryName} {revision}");
            }
            if (createDirectory)
            {
                Directory.CreateDirectory(fullPath);
                Environment.SetEnvironmentVariable(
                    FolderFinder.ReportResultsDirectoryEnvironmentVariable,
                    fullPath);
            }
            return fullPath;
        }

        private static string[] WithOption(
            IEnumerable<string> args,
            string option,
            string value)
        {
            var revised = new List<string>();
            string[] source = args.ToArray();
            for (int index = 0; index < source.Length; index++)
            {
                if (string.Equals(source[index], option, StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }
                revised.Add(source[index]);
            }
            revised.Add(option);
            revised.Add(value);
            return revised.ToArray();
        }

        private static int RunPreflight(string[] args)
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher(args);
            List<GameOptions> optionSets = launcher.GetOptionsSets();
            LitigGameCorrelatedSignalsArticleLauncher.ProductionMatrixAudit audit =
                launcher.ValidateProductionMatrix(optionSets);
            TaskCoordinator coordinator = launcher.GetUninitializedTaskList();

            Console.WriteLine("Correlated-signals production preflight passed.");
            Console.WriteLine($"Plan: {launcher.RunPlan} ({launcher.MasterReportNameForDistributedProcessing})");
            Console.WriteLine($"Option sets: {audit.OptionSetCount}");
            Console.WriteLine($"Core combinations: {audit.CoreCombinationCount}");
            string comparisonLabel = launcher.RunPlan ==
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits
                    ? "Baseline specification comparisons"
                    : "Complete structure comparison groups";
            Console.WriteLine($"{comparisonLabel}: {audit.PairedComparisonCount}");
            if (launcher.RunPlan ==
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
                Console.WriteLine($"American/British comparisons: {audit.FeeRegimeComparisonCount}");
            Console.WriteLine($"Worker tasks: {coordinator.NumIndividualTasks}");
            Console.WriteLine($"Task-plan fingerprint: {coordinator.PlanFingerprint}");
            foreach (var count in audit.CountsByInformationAndRisk)
                Console.WriteLine($"  {count.Key}: {count.Value}");

            Console.WriteLine();
            Console.WriteLine(
                "TaskID,OptionSetName,SignalStructure,InformationLevel,PartySignalSigma,CourtSignalSigma," +
                "CostsMultiplier,FeeShiftingMultiplier,RiskAversion,Generator,Offers,LiabilitySignals," +
                "CourtLiabilitySignals,LiabilityStrengthPoints,LiabilityShaping,DamagesShaping");
            for (int taskId = 0; taskId < optionSets.Count; taskId++)
            {
                var options = (LitigGameOptions)optionSets[taskId];
                string generator = options.LitigGameDisputeGenerator switch
                {
                    LitigGameExogenousDisputeGenerator => "CaseQuality",
                    LitigGameExogenousDirectSignalDisputeGenerator => "BinaryTruth",
                    LitigGameUniformQualityDisputeGenerator => "UniformQuality",
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
                    options.NumCourtLiabilitySignals.ToString(CultureInfo.InvariantCulture),
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
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher(args);
            launcher.ValidateProductionMatrix(launcher.GetOptionsSets());
            int processorCount = ParseProcessorCount(args);
            SourceState source = CaptureSourceState();
            if (source.ChangedPaths.Count > 0 &&
                !args.Contains("--allow-dirty", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Production will not run from a dirty working tree. Commit or remove these paths, " +
                    "or pass --allow-dirty only for a nonfinal diagnostic run:" + Environment.NewLine +
                    string.Join(Environment.NewLine, source.ChangedPaths));
            string workerExecutable = Path.Combine(AppContext.BaseDirectory, "ACESimDistributed.exe");
            if (!File.Exists(workerExecutable))
                throw new FileNotFoundException(
                    "Distributed worker executable was not found. Build the Release configuration first.",
                    workerExecutable);
            WritePlanManifest(launcher, "Running", source, processorCount, null);

            Console.WriteLine(
                $"Launching {processorCount} visible worker windows for {launcher.GetUninitializedTaskList().NumIndividualTasks} tasks " +
                $"using Environment.ProcessorCount={Environment.ProcessorCount}.");

            var workers = new List<Process>();
            try
            {
                for (int workerId = 0; workerId < processorCount; workerId++)
                    workers.Add(StartVisibleWorkerProcess(
                        workerExecutable,
                        workerId,
                        launcher.RunPlan,
                        launcher.GetReportFolder()));

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
                    catch (IOException exception)
                    {
                        if (workers.All(x => x.HasExited))
                            throw new InvalidOperationException(
                                "The coordinator could not be read after all workers exited.",
                                exception);
                        Console.WriteLine(
                            $"{DateTime.Now:O} Coordinator file is temporarily busy; workers are continuing and status will be retried.");
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
                        int artifactCount = ValidateEquilibriumArtifacts(launcher);
                        WritePlanManifest(launcher, "Solved", source, processorCount, artifactCount);
                        Console.WriteLine(
                            $"All worker tasks completed successfully and {artifactCount} required artifacts were validated. " +
                            "Aggregation has not been started.");
                        return 0;
                    }
                    if (workers.All(x => x.HasExited))
                        throw new InvalidOperationException(
                            "All worker processes exited before the coordinator completed. Run 'status' and recover pending tasks after confirming no workers remain.");
                }
            }
            finally
            {
                foreach (Process worker in workers)
                    worker.Dispose();
            }
        }

        private static int ShowStatus(string[] args)
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher(args);
            try
            {
                TaskCoordinator coordinator = launcher.LoadTaskCoordinatorStatus();
                Console.WriteLine(coordinator);
                foreach (IndividualTask task in coordinator.Tasks.Where(x => x.Failed || x.Started != null && !x.Complete))
                    Console.WriteLine(task);
                Console.WriteLine($"Coordinator: {launcher.GetReportFullPath(null, "Coordinator")}");
                Console.WriteLine($"Process logs: {Path.Combine(launcher.GetReportFolder(), "Process Logs")}");
                Console.WriteLine(
                    $"Failure logs: {Path.Combine(launcher.GetReportFolder(), launcher.MasterReportNameForDistributedProcessing + " FAILURE *.txt")}");
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

            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher(args);
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
            foreach (string markerPath in Directory.GetFiles(
                processLogDirectory,
                launcher.MasterReportNameForDistributedProcessing + " worker-*.active"))
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

        private static int AggregateAndReport(string[] args)
        {
            LitigGameCorrelatedSignalsArticleLauncher launcher = CreateLauncher(args);
            SourceState source = CaptureSourceState();
            if (source.ChangedPaths.Count > 0 &&
                !args.Contains("--allow-dirty", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Production aggregation will not run from a dirty working tree. " +
                    "Commit or remove these paths, or pass --allow-dirty only for a nonfinal diagnostic run:" +
                    Environment.NewLine + string.Join(Environment.NewLine, source.ChangedPaths));
            TaskCoordinator coordinator = launcher.EnsureDistributedRunReadyForAggregation();
            Console.WriteLine("Coordinator and all primary result files validated: " + coordinator);
            int artifactCount = ValidateEquilibriumArtifacts(launcher);
            Runner.ProcessLitigationGameData(
                Runner.DataBeingAnalyzed.CorrelatedSignalsArticle,
                launcher,
                preserveExistingResults: true);
            ValidateAggregatedOutputs(launcher);
            WritePlanManifest(
                launcher,
                "Aggregated",
                source,
                null,
                artifactCount);
            return 0;
        }

        private static int ValidateEquilibriumArtifacts(
            LitigGameCorrelatedSignalsArticleLauncher launcher)
        {
            int artifactCount = 0;
            foreach (LitigGameOptions option in launcher.GetOptionsSets().Cast<LitigGameOptions>())
            {
                var settings = new EvolutionSettings();
                option.ModifyEvolutionSettings?.Invoke(settings);
                if (!settings.GenerateInformationSetActionReport)
                    throw new InvalidDataException(
                        $"{option.Name} does not enable the information-set/action report.");

                string equilibriaPath = launcher.GetReportFullPath(option.Name, "-equ.csv");
                RequireNonemptyFile(equilibriaPath);
                artifactCount++;
                int equilibriumCount = File.ReadLines(equilibriaPath)
                    .Count(line => !string.IsNullOrWhiteSpace(line));
                if (equilibriumCount < 1)
                    throw new InvalidDataException($"No equilibrium was recorded in '{equilibriaPath}'.");
                if (launcher.RunPlan !=
                        LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness &&
                    equilibriumCount != 1)
                    throw new InvalidDataException(
                        $"{option.Name} recorded {equilibriumCount} equilibria; expected exactly one.");

                bool numberedEquilibria = launcher.RunPlan ==
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness;
                if (numberedEquilibria)
                {
                    string recoveryPath = launcher.GetReportFullPath(
                        option.Name,
                        $"-{SequenceForm.EquilibriumRecoveryReportSuffix}.csv");
                    RequireNonemptyFile(recoveryPath);
                    RequireCsvDataRows(recoveryPath);
                    RequireHeaderColumns(
                        recoveryPath,
                        "Requested Priors",
                        "Attempted Solves",
                        "Inexact Attempts",
                        "Exact Attempts",
                        "Verified Recoveries",
                        "Distinct Reported Strategy Profiles",
                        "Recovery Count",
                        "Recovery Share of Verified Recoveries",
                        "Verification Status",
                        "Distinctness Criterion");
                    artifactCount++;
                }
                for (int equilibrium = 1; equilibrium <= equilibriumCount; equilibrium++)
                {
                    string outcomeSuffix = numberedEquilibria
                        ? $"-Eq{equilibrium}.csv"
                        : ".csv";
                    string actionSuffix = numberedEquilibria
                        ? $"-Eq{equilibrium}-{InformationSetActionReport.ReportSuffix}.csv"
                        : $"-{InformationSetActionReport.ReportSuffix}.csv";
                    string outcomePath = launcher.GetReportFullPath(option.Name, outcomeSuffix);
                    string actionPath = launcher.GetReportFullPath(option.Name, actionSuffix);
                    RequireNonemptyFile(outcomePath);
                    RequireNonemptyFile(actionPath);
                    RequireCsvDataRows(outcomePath);
                    RequireCsvDataRows(actionPath);
                    RequireHeaderColumns(
                        actionPath,
                        "OptionSetName",
                        "Equilibrium Number",
                        "Equilibrium Reach Probability",
                        "Equilibrium Action Probability",
                        "Conditional Action Utility",
                        "Utility Loss from Best Action",
                        "Off Path");
                    artifactCount += 2;
                }
            }
            return artifactCount;
        }

        private static void ValidateAggregatedOutputs(
            LitigGameCorrelatedSignalsArticleLauncher launcher)
        {
            if (launcher.RunPlan ==
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
            {
                string path = launcher.GetReportFullPath("numerical results", ".csv");
                string specificationPath = launcher.GetReportFullPath("specification comparisons", ".csv");
                string feeRegimePath = launcher.GetReportFullPath("fee regime comparisons", ".csv");
                string strategiesPath = launcher.GetReportFullPath("signal strategies", ".csv");
                RequireNonemptyFile(path);
                RequireNonemptyFile(specificationPath);
                RequireNonemptyFile(feeRegimePath);
                RequireNonemptyFile(strategiesPath);
                RequireCsvDataRows(path);
                RequireCsvDataRows(specificationPath);
                RequireCsvDataRows(feeRegimePath);
                RequireCsvDataRows(strategiesPath);
                RequireHeaderColumns(
                    path,
                    CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn,
                    CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn,
                    CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn,
                    CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn,
                    CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn,
                    CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn,
                    CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn);
                RequireHeaderColumns(
                    strategiesPath,
                    "Signal Owner",
                    "Signal Index",
                    "P Filing Probability",
                    "D Answering Probability Conditional on Filing",
                    "P Offer Mean",
                    "D Offer Mean",
                    "P Offer 1 Action 10",
                    "D Offer 1 Action 10");
            }
            else if (launcher.RunPlan ==
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness)
            {
                string outcomesPath = launcher.GetReportFullPath("equilibrium outcomes", ".csv");
                string rangesPath = launcher.GetReportFullPath("equilibrium ranges", ".csv");
                RequireNonemptyFile(outcomesPath);
                RequireNonemptyFile(rangesPath);
                RequireCsvDataRows(outcomesPath);
                RequireCsvDataRows(rangesPath);
                RequireHeaderColumns(
                    outcomesPath,
                    "Requested Priors",
                    "Attempted Solves",
                    "Verified Recoveries",
                    "Distinct Reported Strategy Profiles",
                    "Equilibrium Recovery Count",
                    "Recovery Share of Verified Recoveries",
                    "Verification Status",
                    "Distinctness Criterion",
                    CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn,
                    CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn,
                    CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn,
                    CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn);
                RequireHeaderColumns(
                    rangesPath,
                    "Attempted Solves",
                    "Verified Recoveries",
                    $"Range {CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn}",
                    "Range P Offer",
                    "Range Trial");
            }
        }

        private static void RequireNonemptyFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new FileNotFoundException("A required production artifact is missing or empty.", path);
        }

        private static void RequireHeaderColumns(string path, params string[] columns)
        {
            string header = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
            foreach (string column in columns)
                if (!header.Contains(column, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Required column '{column}' is absent from '{path}'.");
        }

        private static void RequireCsvDataRows(string path)
        {
            if (File.ReadLines(path).Take(2).Count(line => !string.IsNullOrWhiteSpace(line)) < 2)
                throw new InvalidDataException(
                    $"Required CSV '{path}' contains a header but no data rows.");
        }

        private sealed class ProductionRunManifest
        {
            public int ManifestSchemaVersion { get; set; }
            public string RunStartedUtc { get; set; }
            public string LastUpdatedUtc { get; set; }
            public string Status { get; set; }
            public string Plan { get; set; }
            public string ReportName { get; set; }
            public string RepositoryRoot { get; set; }
            public string ResultsDirectory { get; set; }
            public int OptionSetCount { get; set; }
            public List<string> OptionSetNames { get; set; }
            public int WorkerTaskCount { get; set; }
            public string TaskPlanFingerprint { get; set; }
            public int? ProcessorCount { get; set; }
            public int LogicalProcessorCount { get; set; }
            public int? ValidatedArtifactCount { get; set; }
            public string GitCommit { get; set; }
            public bool WorkingTreeWasDirty { get; set; }
            public List<string> ChangedPaths { get; set; }
            public string BuildConfiguration { get; set; }
            public string CoordinatorAssemblySha256 { get; set; }
            public string WorkerExecutableSha256 { get; set; }
            public string WorkerAssemblySha256 { get; set; }
            public string ACESimBaseAssemblySha256 { get; set; }
            public string LitigChartsAssemblySha256 { get; set; }
            public string DotNetRuntime { get; set; }
            public string OperatingSystem { get; set; }
            public string ProcessArchitecture { get; set; }
            public List<string> ReproductionCommands { get; set; }
            public List<ReusedEquilibriumArtifact> ReusedEquilibria { get; set; }
        }

        private sealed class ReusedEquilibriumArtifact
        {
            public string OptionSetName { get; set; }
            public string FileName { get; set; }
            public string Sha256BeforeRun { get; set; }
        }

        private sealed record SourceState(
            string RepositoryRoot,
            string GitCommit,
            List<string> ChangedPaths);

        private static void WritePlanManifest(
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            string status,
            SourceState source,
            int? processorCount,
            int? artifactCount)
        {
            string path = launcher.GetReportFullPath("run manifest", ".json");
            TaskCoordinator coordinator = launcher.GetUninitializedTaskList();
            string coordinatorAssemblyHash = HashFile(Assembly.GetExecutingAssembly().Location);
            string workerExecutableHash = HashFile(Path.Combine(AppContext.BaseDirectory, "ACESimDistributed.exe"));
            string workerAssemblyHash = HashFile(Path.Combine(AppContext.BaseDirectory, "ACESimDistributed.dll"));
            string baseAssemblyHash = HashFile(typeof(Launcher).Assembly.Location);
            string chartsAssemblyHash = HashFile(typeof(Runner).Assembly.Location);
            ProductionRunManifest manifest = null;
            if (File.Exists(path))
            {
                try
                {
                    manifest = JsonSerializer.Deserialize<ProductionRunManifest>(File.ReadAllText(path));
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException($"The existing run manifest is invalid: '{path}'.", exception);
                }
                if (manifest != null &&
                    !string.Equals(manifest.GitCommit, source.GitCommit, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"The existing run manifest in '{path}' belongs to commit {manifest.GitCommit}, " +
                        $"not {source.GitCommit}. Use a fresh results directory.");
                if (manifest != null)
                {
                    RequireManifestMatch(path, "production plan", manifest.Plan, launcher.RunPlan.ToString());
                    RequireManifestMatch(
                        path,
                        "task-plan fingerprint",
                        manifest.TaskPlanFingerprint,
                        coordinator.PlanFingerprint);
                    RequireManifestMatch(
                        path,
                        "coordinator assembly hash",
                        manifest.CoordinatorAssemblySha256,
                        coordinatorAssemblyHash);
                    RequireManifestMatch(
                        path,
                        "worker executable hash",
                        manifest.WorkerExecutableSha256,
                        workerExecutableHash);
                    RequireManifestMatch(
                        path,
                        "worker assembly hash",
                        manifest.WorkerAssemblySha256,
                        workerAssemblyHash);
                    RequireManifestMatch(
                        path,
                        "ACESimBase assembly hash",
                        manifest.ACESimBaseAssemblySha256,
                        baseAssemblyHash);
                    RequireManifestMatch(
                        path,
                        "LitigCharts assembly hash",
                        manifest.LitigChartsAssemblySha256,
                        chartsAssemblyHash);
                }
            }

            bool creatingManifest = manifest == null;
            manifest ??= new ProductionRunManifest
            {
                RunStartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };
            if (creatingManifest)
                manifest.ReusedEquilibria = FindPreexistingEquilibria(launcher);
            manifest.LastUpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            manifest.ManifestSchemaVersion = 1;
            manifest.Status = status;
            manifest.Plan = launcher.RunPlan.ToString();
            manifest.ReportName = launcher.MasterReportNameForDistributedProcessing;
            manifest.RepositoryRoot = source.RepositoryRoot;
            manifest.ResultsDirectory = launcher.GetReportFolder();
            manifest.OptionSetCount = launcher.GetOptionsSets().Count;
            manifest.OptionSetNames = launcher.GetOptionsSets()
                .Select(option => option.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            manifest.WorkerTaskCount = coordinator.NumIndividualTasks;
            manifest.TaskPlanFingerprint = coordinator.PlanFingerprint;
            manifest.ProcessorCount = processorCount ?? manifest.ProcessorCount;
            manifest.LogicalProcessorCount = Environment.ProcessorCount;
            manifest.ValidatedArtifactCount = artifactCount ?? manifest.ValidatedArtifactCount;
            manifest.GitCommit = source.GitCommit;
            manifest.WorkingTreeWasDirty |= source.ChangedPaths.Count > 0;
            manifest.ChangedPaths = (manifest.ChangedPaths ?? new List<string>())
                .Concat(source.ChangedPaths)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
#if DEBUG
            manifest.BuildConfiguration = "Debug";
#else
            manifest.BuildConfiguration = "Release";
#endif
            manifest.CoordinatorAssemblySha256 = coordinatorAssemblyHash;
            manifest.WorkerExecutableSha256 = workerExecutableHash;
            manifest.WorkerAssemblySha256 = workerAssemblyHash;
            manifest.ACESimBaseAssemblySha256 = baseAssemblyHash;
            manifest.LitigChartsAssemblySha256 = chartsAssemblyHash;
            manifest.DotNetRuntime = RuntimeInformation.FrameworkDescription;
            manifest.OperatingSystem = RuntimeInformation.OSDescription;
            manifest.ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
            string resultsArgument = QuoteCommandArgument(launcher.GetReportFolder());
            string planArgument = PlanArgument(launcher.RunPlan);
            manifest.ReproductionCommands = new List<string>
            {
                $"ACESimDistributedSaturate run --processors all --plan {planArgument} --results-directory {resultsArgument}",
                $"ACESimDistributedSaturate status --plan {planArgument} --results-directory {resultsArgument}",
                $"ACESimDistributedSaturate recover --failed --include-pending --plan {planArgument} --results-directory {resultsArgument}",
                $"ACESimDistributedSaturate aggregate --plan {planArgument} --results-directory {resultsArgument}",
            };
            WriteJsonAtomically(path, manifest);
        }

        private static List<ReusedEquilibriumArtifact> FindPreexistingEquilibria(
            LitigGameCorrelatedSignalsArticleLauncher launcher)
        {
            var artifacts = new List<ReusedEquilibriumArtifact>();
            foreach (GameOptions option in launcher.GetOptionsSets())
            {
                var settings = new EvolutionSettings();
                option.ModifyEvolutionSettings?.Invoke(settings);
                if (!settings.UseExistingEquilibriaIfAvailable)
                    continue;
                string path = launcher.GetReportFullPath(option.Name, "-equ.csv");
                if (!File.Exists(path))
                    continue;
                artifacts.Add(new ReusedEquilibriumArtifact
                {
                    OptionSetName = option.Name,
                    FileName = Path.GetFileName(path),
                    Sha256BeforeRun = HashFile(path),
                });
            }
            return artifacts;
        }

        private static void RequireManifestMatch(
            string manifestPath,
            string field,
            string recorded,
            string current)
        {
            if (!string.IsNullOrWhiteSpace(recorded) &&
                !string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The {field} recorded in '{manifestPath}' does not match the current production binary. " +
                    "Use the original build or a fresh results directory.");
        }

        private static void WriteSuiteManifest(string resultsDirectory, SourceState source)
        {
            string path = Path.Combine(resultsDirectory, "ALER production suite manifest.json");
            var plans = LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans
                .Select(plan =>
                {
                    var launcher = new LitigGameCorrelatedSignalsArticleLauncher(plan);
                    string manifestPath = launcher.GetReportFullPath("run manifest", ".json");
                    RequireNonemptyFile(manifestPath);
                    ProductionRunManifest planManifest =
                        JsonSerializer.Deserialize<ProductionRunManifest>(File.ReadAllText(manifestPath)) ??
                        throw new InvalidDataException($"The run manifest is invalid: '{manifestPath}'.");
                    if (!string.Equals(planManifest.Status, "Aggregated", StringComparison.Ordinal) ||
                        !string.Equals(planManifest.GitCommit, source.GitCommit, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"The run manifest '{manifestPath}' is not an aggregated result for commit {source.GitCommit}.");
                    return new
                    {
                        Plan = plan.ToString(),
                        ReportName = launcher.MasterReportNameForDistributedProcessing,
                        OptionSetCount = launcher.GetOptionsSets().Count,
                        WorkerTaskCount = launcher.GetUninitializedTaskList().NumIndividualTasks,
                        TaskPlanFingerprint = launcher.GetUninitializedTaskList().PlanFingerprint,
                        ManifestFile = Path.GetFileName(manifestPath),
                        ManifestSha256 = HashFile(manifestPath),
                    };
                })
                .ToArray();
            var manifest = new
            {
                ManifestSchemaVersion = 1,
                CompletedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RepositoryRoot = source.RepositoryRoot,
                ResultsDirectory = resultsDirectory,
                GitCommit = source.GitCommit,
                WorkingTreeWasDirty = source.ChangedPaths.Count > 0,
                RequiredPlans = plans,
                IntegratedOfferGridCases = LitigGameCorrelatedSignalsArticleLauncher.IncreasedOfferGridOptionSetCount,
                SeparateOfferGridPlanRequired = false,
                AllRequiredArtifactsValidated = true,
                AllRequiredAggregatesValidated = true,
                ReproductionCommand =
                    $"ACESimDistributedSaturate run-suite --processors all --results-directory {QuoteCommandArgument(resultsDirectory)}",
            };
            WriteJsonAtomically(path, manifest);
        }

        private static string QuoteCommandArgument(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

        private static void WriteJsonAtomically<T>(string path, T value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path))
                return null;
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static SourceState CaptureSourceState()
        {
            string repositoryRoot = RunGit("rev-parse", "--show-toplevel").Trim();
            string commit = RunGit("rev-parse", "HEAD").Trim();
            List<string> changes = RunGit("status", "--porcelain", "--untracked-files=all")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            return new SourceState(repositoryRoot, commit, changes);
        }

        private static string RunGit(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Unable to start git for production provenance.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Unable to capture git provenance: " + error.Trim());
            return output;
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

        private static Process StartVisibleWorkerProcess(
            string executablePath,
            int workerId,
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan runPlan,
            string resultsDirectory)
        {
            // This intentionally mirrors the established ACESimDistributedSaturate interface:
            // ShellExecute opens each console application in its own visible window so the user
            // can watch and stop workers individually.
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            startInfo.ArgumentList.Add("--worker-id");
            startInfo.ArgumentList.Add(workerId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--plan");
            startInfo.ArgumentList.Add(PlanArgument(runPlan));
            startInfo.ArgumentList.Add("--results-directory");
            startInfo.ArgumentList.Add(resultsDirectory);
            return Process.Start(startInfo) ?? throw new InvalidOperationException(
                $"Unable to start visible worker {workerId}.");
        }

        private static string PlanArgument(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan runPlan) => runPlan switch
        {
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.LegacyTwoStructure => "legacy",
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UniformBaselineSupplement => "supplemental",
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UnifiedThreeStructure => "unified",
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits => "focused",
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness => "multiple-equilibria",
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.IncreasedOfferGridRobustness => "offers-15",
            _ => throw new NotSupportedException(),
        };

        private static int ParseProcessorCount(string[] args)
        {
            // DEBUG: Temporarily default to 16 workers so this production run leaves capacity
            // for interactive computer use. Revisit before the next unattended saturation run.
            string text = OptionalArgument(args, "--processors") ?? "16";
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
            Console.WriteLine("  <no arguments>              (required CS004 + CS004ME suite; 16 workers; aggregate and validate)");
            Console.WriteLine("  preflight-suite [--results-directory PATH]");
            Console.WriteLine("  run-suite [--processors all|N] [--results-directory PATH]");
            Console.WriteLine("  preflight [--plan focused|multiple-equilibria|offers-15|unified|supplemental|legacy]");
            Console.WriteLine("  run --processors all|N [--plan focused|multiple-equilibria|offers-15|unified|supplemental|legacy]");
            Console.WriteLine("  status [--plan focused|multiple-equilibria|offers-15|unified|supplemental|legacy]");
            Console.WriteLine("  recover --failed [--include-pending] [--plan focused|multiple-equilibria|offers-15|unified|supplemental|legacy]");
            Console.WriteLine("  aggregate [--plan focused|multiple-equilibria|offers-15|unified|supplemental|legacy]");
            Console.WriteLine("  smoke-test");
            Console.WriteLine("  The suite defaults to ReportResults/Production Runs/ALER Production <commit>.");
            Console.WriteLine("  Add --results-directory PATH to select a different dedicated results folder.");
            Console.WriteLine("  Final production and aggregation require a clean working tree.");
            return 0;
        }
    }
}
