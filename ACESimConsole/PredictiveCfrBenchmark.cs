using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ACESim.Util;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Debugging;
using Microsoft.VisualBasic.FileIO;

namespace ACESim
{
    /// <summary>Opt-in standalone benchmark. Never calls Launcher.Launch or RunAlgorithm.</summary>
    public static class PredictiveCfrBenchmark
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
        private static readonly string[] AllMethods = { "legacy", "pcfr+", "dcfr+", "pdcfr+" };
        private static readonly string[] DefaultCases = { "baseline", "fee", "damages", "risk" };
        private static readonly string[] AllCases = DefaultCases.Concat(new[] { "hard-indeterminacy" }).ToArray();
        private static readonly HashSet<string> OutcomeColumns = new(StringComparer.Ordinal)
        {
            "All", "DisputeArises", "Activity", "Accident", "WrongAttrib", "PrecPower", "PrecLevel", "BCRatio",
            "PFiles", "DAnswers", "Trial", "Settles", "PWelfare", "DWelfare", "PDSWelfareLoss", "SWelfareLoss",
            "TotExpense", "PWealth", "DWealth", "PAbandons", "DDefaults", "ValIfSettled"
        };
        private const string HistoricalDefault = @"C:\Users\Admin\source\repos\endogenous-disputes-article\ReportResults\PREC010 output.csv";
        public const string CsvHeader = "case,method,iteration,profile,raw_p0,raw_p1,normalized_p0,normalized_p1,score_range_p0,score_range_p1,E_half_raw,E_half_normalized,max_raw,max_normalized,setup_seconds,solve_seconds,evaluation_seconds,cumulative_evaluation_seconds,elapsed_seconds,gebr_max_br_utility_error,acceptable_max_1e-5";

        private sealed class BenchmarkLauncher : LitigGameEndogenousDisputesLauncher
        {
            private readonly bool fullReports;
            public BenchmarkLauncher(bool fullReports = false) { this.fullReports = fullReports; }
            public override GameDefinition GetGameDefinition() => new BenchmarkGameDefinition { FullReports = fullReports };
        }

        [Serializable]
        private sealed class BenchmarkGameDefinition : LitigGameDefinition
        {
            public bool FullReports;
            public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
            {
                // Preserve the production report's exact filters, column evaluators,
                // denominators, and latent-state expansion; omit unrelated report cells.
                var definitions = base.GetSimpleReportDefinitions();
                if (FullReports) return definitions;
                var report = definitions.First();
                report.RowFilters = report.RowFilters.Where(x => x.Name is "All" or "DisputeArises").ToList();
                report.ColumnItems = report.ColumnItems.Where(x => OutcomeColumns.Contains(x.Name)).ToList();
                return new List<SimpleReportDefinition> { report };
            }
        }

        public sealed class Arguments
        {
            public string Output;
            public string[] Methods = AllMethods;
            public string[] Cases = DefaultCases;
            public int Iterations = 1000;
            public int Checkpoint = 100;
            public string HistoricalCsv = HistoricalDefault;
            public string OutcomesFrom;
            public bool DryRun, SelfTest, SelfTestOutcomes, Outcomes = true, CrossCheck = true;
            public GeneralizedVanillaFlavor LegacyFlavor = GeneralizedVanillaFlavor.Fast;
            public static Arguments Parse(string[] args)
            {
                var result = new Arguments();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    if (!seen.Add(key)) throw new ArgumentException("Duplicate argument: " + key);
                    string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + key);
                    switch (key)
                    {
                        case "--output": result.Output = Path.GetFullPath(Value()); break;
                        case "--methods": result.Methods = Select(Value(), AllMethods.Concat(new[] { "cfr+" }).ToArray(), key); break;
                        case "--cases": result.Cases = Select(Value(), AllCases, key); break;
                        case "--iterations": result.Iterations = Positive(Value(), key); break;
                        case "--checkpoint": result.Checkpoint = Positive(Value(), key); break;
                        case "--historical-csv": result.HistoricalCsv = Path.GetFullPath(Value()); break;
                        case "--outcomes-from": result.OutcomesFrom = Path.GetFullPath(Value()); break;
                        case "--legacy-flavor": result.LegacyFlavor = Value().ToLowerInvariant() switch { "fast" => GeneralizedVanillaFlavor.Fast, "regular" => GeneralizedVanillaFlavor.Regular, _ => throw new ArgumentException("Use fast or regular for --legacy-flavor") }; break;
                        case "--dry-run": result.DryRun = true; break;
                        case "--self-test": result.SelfTest = true; break;
                        case "--self-test-outcomes": result.SelfTest = result.SelfTestOutcomes = true; break;
                        case "--outcomes": result.Outcomes = true; break;
                        case "--no-outcomes": result.Outcomes = false; break;
                        case "--no-gebr-crosscheck": result.CrossCheck = false; break;
                        case "--help": throw new ArgumentException(Usage);
                        default: throw new ArgumentException("Unknown argument: " + key + "\n" + Usage);
                    }
                }
                if (!result.SelfTest && result.Output == null) throw new ArgumentException("An explicit --output directory is required.\n" + Usage);
                return result;
            }
            private static int Positive(string value, string key) => int.TryParse(value, NumberStyles.None, Invariant, out int n) && n > 0 ? n : throw new ArgumentException(key + " must be a positive integer.");
            private static string[] Select(string value, string[] allowed, string key)
            {
                var selected = value.ToLowerInvariant().Split(',');
                if (selected.Length == 0 || selected.Any(x => !allowed.Contains(x)) || selected.Distinct().Count() != selected.Length)
                    throw new ArgumentException("Invalid " + key + ". Choices: " + string.Join(",", allowed));
                return selected;
            }
        }

        public const string Usage = "--predictive-cfr-benchmark --output <new-or-empty-directory> [--methods legacy,pcfr+,dcfr+,pdcfr+] [--cases baseline,fee,damages,risk,hard-indeterminacy (default: first four)] [--iterations 1000] [--checkpoint 100] [--historical-csv <path>] [--legacy-flavor fast|regular] [--outcomes-from <existing-benchmark-directory>] [--dry-run] [--no-outcomes] [--no-gebr-crosscheck] | --self-test | --self-test-outcomes";

        public static LitigGameOptions CreateCase(string caseId)
        {
            if (!AllCases.Contains(caseId)) throw new ArgumentException("Unknown case " + caseId);
            var launcher = new LitigGameEndogenousDisputesLauncher();
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(new PrecautionNegligenceOptionsGeneratorSettings
            {
                ParameterForMultipleOptions = 8,
                PredeterminedAbandonAndDefaults = false
            });
            options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = options.CourtLiabilityNoiseStdev = 0.2;
            options = caseId == "risk" ? launcher.GetAndTransform_ModeratelyRiskAverse(options) : launcher.GetAndTransform_RiskNeutral(options);
            if (caseId == "hard-indeterminacy")
            {
                options = launcher.GetAndTransform_CostsMultiplier(options, 4);
                options = launcher.GetAndTransform_CourtNoise(options,
                    new LitigGameEndogenousDisputesLauncher.CourtNoiseInfo(.2, true, .5, "High Indet"));
                options = launcher.GetAndTransform_PrecautionCost(options, 1.2E-05);
                options.Name = options.Name.Trim();
                return options;
            }
            options = launcher.GetAndTransform_CostsMultiplier(options, 1);
            options = launcher.GetAndTransform_FeeShiftingMultiplier(options, caseId == "fee" ? 1 : 0);
            options = launcher.GetAndTransform_DamagesMultiplier(options, caseId == "damages" ? 2 : 1);
            options = launcher.GetAndTransform_LiabilityThreshold(options, 1);
            options = launcher.GetAndTransform_PrecautionCost(options, 1E-05);
            options.Name = options.Name.Trim();
            return options;
        }

        private static EvolutionSettings CreateSettings(Arguments args, string method)
        {
            var settings = new LitigGameEndogenousDisputesLauncher().GetEvolutionSettings();
            settings.Algorithm = method == "legacy" ? GameApproximationAlgorithm.GeneralizedVanilla : GameApproximationAlgorithm.PredictiveCFR;
            settings.GeneralizedVanillaFlavor = args.LegacyFlavor;
            settings.TotalIterations = args.Iterations;
            settings.ParallelOptimization = settings.DynamicSetParallel = false;
            settings.SaveToAzureBlob = settings.CreateEquilibriaFile = settings.UseExistingEquilibriaIfAvailable = false;
            settings.ConsiderInitializingToMostRecentEquilibrium = false;
            settings.ReportEveryNIterations = settings.BestResponseEveryMIterations = settings.MiniReportEveryPIterations = null;
            settings.GenerateManualReports = settings.PCA_PerformPrincipalComponentAnalysis = settings.ModelSuccessTracking = false;
            settings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = settings.RoundOffLowProbabilitiesBeforeReporting = false;
            settings.UseAcceleratedBestResponse = true;
            settings.CalculatePerturbedBestResponseRefinement = false;
            settings.PruneOnOpponentStrategy = settings.CFRBR = settings.CFR_OpponentSampling = false;
            settings.RecalculateScoreReachWhenCalculatingBestResponseImprovement = false;
            settings.SimulatedAnnealingEveryNIterations = EvolutionSettings.EffectivelyNever;
            // Explicit historical modern-legacy defaults. Canonical solvers ignore these.
            settings.UseContinuousRegretsDiscounting = true;
            settings.UseStandardDiscounting = settings.DiscountRegrets = false;
            settings.PerturbationInitial = 0.001;
            settings.PerturbationFinal = 0;
            if (method != "legacy") settings.PredictiveCFR = new PredictiveCFRSettings { Variant = method switch
            {
                "cfr+" => PredictiveCFRVariant.CFRPlus,
                "pcfr+" => PredictiveCFRVariant.PCFRPlus,
                "dcfr+" => PredictiveCFRVariant.DCFRPlus,
                "pdcfr+" => PredictiveCFRVariant.PDCFRPlus,
                _ => throw new ArgumentException("Unknown method " + method)
            }};
            return settings;
        }

        public sealed record Checkpoint(string Case, string Method, int Iteration, string Profile, double[] Raw,
            double[] Normalized, double[] ScoreRange, double EHalfRaw, double EHalfNormalized, double MaxRaw,
            double MaxNormalized, double SetupSeconds, double SolveSeconds, double EvaluationSeconds,
            double CumulativeEvaluationSeconds, double ElapsedSeconds, double? GebrMaxBrUtilityError)
        {
            public bool Acceptable => MaxNormalized <= 1E-5;
            public string Csv() => string.Join(",", new object[] { Case, Method, Iteration, Profile, Raw[0], Raw[1], Normalized[0], Normalized[1], ScoreRange[0], ScoreRange[1], EHalfRaw, EHalfNormalized, MaxRaw, MaxNormalized, SetupSeconds, SolveSeconds, EvaluationSeconds, CumulativeEvaluationSeconds, ElapsedSeconds, GebrMaxBrUtilityError, Acceptable }.Select(Format));
        }

        public static async Task RunAsync(string[] argv)
        {
            var args = Arguments.Parse(argv);
            if (args.SelfTest) { SelfTest(); if (args.SelfTestOutcomes) await SelfTestOutcomeReportsAsync(); return; }
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Invariant;
            TabbedText.DisableConsoleProgressString = true;
            TabbedText.DisableOutput();
            if (Directory.Exists(args.Output) && Directory.EnumerateFileSystemEntries(args.Output).Any())
                throw new IOException("Refusing to overwrite nonempty output directory: " + args.Output);
            Directory.CreateDirectory(args.Output);
            var names = args.Cases.ToDictionary(x => x, x => CreateCase(x).Name);
            var historical = ReadHistorical(args.HistoricalCsv, names);
            var git = new { commit = Git("rev-parse HEAD"), status = Git("status --porcelain"), diffSha256 = HashText(Git("diff --binary HEAD")) };
            WriteJson(Path.Combine(args.Output, "provenance.json"), new
            {
                schemaVersion = 2, startedUtc = DateTime.UtcNow, command = Environment.CommandLine, arguments = args,
                git, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processorCount = Environment.ProcessorCount,
                assemblySha256 = HashFile(typeof(PredictiveCfrBenchmark).Assembly.Location), baseAssemblySha256 = HashFile(typeof(PredictiveCFR).Assembly.Location), historicalCsv = new { path = args.HistoricalCsv, sha256 = HashFile(args.HistoricalCsv), matchedRows = historical },
                sourceFiles = new[] { "ACESimConsole/PredictiveCfrBenchmark.cs", "ACESimConsole/Program.cs", "ACESimBase/GameSolvingAlgorithms/PredictiveCFR.cs", "ACESimBase/GameSolvingAlgorithms/GeneralizedVanilla.Benchmark.cs", "ACESimBase/GameSolvingAlgorithms/StrategiesDeveloperBase.Benchmark.cs", "ACESimBase/GameSolvingSupport/Settings/PredictiveCFRSettings.cs", "ACESimBase/GameSolvingSupport/Settings/EvolutionSettings.cs", "ACESimBase/GameSolvingSupport/Settings/Launcher.cs", "ACESimBase/GameSolvingSupport/StrategiesDeveloperBase.cs" }.Where(File.Exists).Select(path => new { path, sha256 = HashFile(path) }).ToArray(),
                historicalLogs = HistoricalLogs(args.HistoricalCsv, names),
                gameReconstruction = "Published parameters reconstructed on current model: grid8, P/D/court liability noise0.2, predetermined abandon/defaults false. Not a bitwise historical reproduction.",
                comparisonCaveat = "Legacy uses simultaneous reach-normalized regret updates, geometric0.99 regret discounting, and perturbation0.001 decaying to0. Canonical solvers use alternating standard counterfactual regrets. Legacy comparisons do not isolate prediction.",
                historicalPruningCaveat = "Historical source enabled opponent probability pruning below1e-4; modern legacy code disables that threshold. This harness traverses the full game without pruning.",
                averagePolicyEvaluation = "The exact average policy is normalized cumulative strategy mass, including every nonzero probability. At checkpoints only, its probabilities temporarily replace cached average probabilities for exact BR evaluation, then original caches are restored before training resumes. This bypasses legacy cached-average thresholding below1e-5 without changing training.",
                historicalOutcomeCaveat = "Historical logs report outcomes playing CurrentProbability while saved Exploitability is final average-policy E_half, which may differ from the best recorded historical checkpoint (fee best iteration600, risk best400). Those fields do not describe the same selected profile. New reports label final and best current and average separately.",
                metrics = "Raw unilateral deviation and deviation/each-player terminal utility range. E_half=sum/2 (two players); also max. Exact unrounded profile. Best observed checkpoint is distinct from final.",
                acceptance = new { maxIndividualNormalizedDeviation = 1E-5, secondaryEHalfNormalized = 5E-6 },
                outcomeDenominators = "Filter=All is unconditional over the ex ante game. Dispute Arises and other filters are conditional on that event. Root deviations can be tiny because disputes are rare (~1e-4); compare economic outcomes as well.",
                outcomeEvaluation = new { approach = "Exact native report path enumeration with all latent-state expansion; only the original All/DisputeArises row filters and selected original column evaluators are retained.", originalColumnNames = OutcomeColumns.OrderBy(x => x).ToArray(), reuseIdenticalBestAndFinal = true },
                cases = args.Cases.Select(x => new { id = x, name = names[x], snapshot = Snapshot(CreateCase(x)), snapshotSha256 = HashText(JsonSerializer.Serialize(Snapshot(CreateCase(x)), Json)) }).ToArray(),
                methods = args.Methods.Select(x => new { id = x, settings = Snapshot(CreateSettings(args, x)) }).ToArray()
            });
            if (args.DryRun) { Console.WriteLine("Benchmark configuration written to " + args.Output); return; }
            if (args.OutcomesFrom != null) { await RunOutcomesFromAsync(args); return; }
            var summaries = new List<object>();
            using var checkpointFile = new StreamWriter(Path.Combine(args.Output, "checkpoints.csv"), false, new UTF8Encoding(false)) { AutoFlush = true };
            checkpointFile.WriteLine(CsvHeader);
            foreach (string caseId in args.Cases)
                foreach (string method in args.Methods)
                {
                    Console.WriteLine($"Starting {caseId}/{method} ({args.Iterations} iterations)");
                    var wall = Stopwatch.StartNew();
                    var setup = Stopwatch.StartNew();
                    var options = CreateCase(caseId);
                    var settings = CreateSettings(args, method);
                    var launcher = new BenchmarkLauncher();
                    var developer = (StrategiesDeveloperBase)await launcher.GetInitializedDevelper(options, options.Name, settings);
                    // All methods start from exactly uniform policies, without a solving warmup.
                    foreach (var node in developer.InformationSets)
                    {
                        node.SetCurrentAndAverageProbabilities(Enumerable.Repeat(1.0 / node.NumPossibleActions, node.NumPossibleActions).ToArray());
                        for (int action = 0; action < node.NumPossibleActions; action++)
                        {
                            node.NodeInformation[InformationSetNode.cumulativeStrategyDimension, action] = 0;
                            node.NodeInformation[InformationSetNode.cumulativeRegretDimension, action] = 0;
                        }
                    }
                    if (developer.InformationSets.Any(node => node.GetCumulativeStrategiesAsArray().Any(x => x != 0)))
                        throw new InvalidOperationException("Nonzero initial strategy accumulator.");
                    setup.Stop();
                    string runDirectory = Path.Combine(args.Output, caseId + "-" + method.Replace("+", "plus"));
                    Directory.CreateDirectory(runDirectory);
                    var tree = developer.CountGameTreeNodes();
                    if (developer.Status.ScoreRange == null || developer.Status.ScoreRange.Any(x => !double.IsFinite(x) || x <= 0))
                        throw new InvalidOperationException("A finite positive terminal utility range is required for each player's normalized deviation.");
                    WriteJson(Path.Combine(runDirectory, "configuration.json"), new { caseId, method, optionsName = options.Name, options = Snapshot(options), generator = Snapshot(options.LitigGameDisputeGenerator), settings = Snapshot(settings), tree = new { chance = tree.Chance, decision = tree.Decision, final = tree.Final, informationSets = developer.InformationSets.Count }, scoreRange = developer.Status.ScoreRange, setupSeconds = setup.Elapsed.TotalSeconds });
                    var solve = new Stopwatch();
                    double evaluationTotal = 0;
                    var checkpoints = new List<Checkpoint>();
                    var best = new Dictionary<string, Checkpoint>();
                    for (int iteration = 1; iteration <= args.Iterations; iteration++)
                    {
                        solve.Start();
                        if (developer is GeneralizedVanilla legacy) legacy.RunBenchmarkIteration(iteration);
                        else ((PredictiveCFR)developer).RunIteration();
                        solve.Stop();
                        if (iteration % args.Checkpoint != 0 && iteration != args.Iterations) continue;
                        var cachedAverages = SynchronizeAverageForEvaluation(developer);
                        try
                        {
                        ValidatePolicies(developer);
                        foreach (bool current in new[] { true, false })
                        {
                            var evaluation = Stopwatch.StartNew();
                            settings.UseCurrentStrategyForBestResponse = current;
                            developer.CalculateBestResponse(false);
                            double[] raw = developer.Status.BestResponseImprovement.ToArray();
                            double[] normalized = developer.Status.BestResponseImprovementAdj.ToArray();
                            double[] ranges = developer.Status.ScoreRange.ToArray();
                            double? crosscheck = null;
                            if (args.CrossCheck && iteration == Math.Min(args.Checkpoint, args.Iterations))
                            {
                                var br = developer.Status.BestResponseUtilities.ToArray();
                                crosscheck = Enumerable.Range(0, developer.NumNonChancePlayers).Max(p => Math.Abs(developer.CalculateBestResponse((byte)p, current ? ActionStrategies.CurrentProbability : ActionStrategies.AverageStrategy) - br[p]));
                                if (crosscheck > 1E-8) throw new InvalidOperationException("Accelerated/GEBR best-response mismatch: " + crosscheck);
                            }
                            evaluation.Stop();
                            evaluationTotal += evaluation.Elapsed.TotalSeconds;
                            var row = new Checkpoint(caseId, method, iteration, current ? "current" : "average", raw, normalized, ranges, raw.Sum() / 2, normalized.Sum() / 2, raw.Max(), normalized.Max(), setup.Elapsed.TotalSeconds, solve.Elapsed.TotalSeconds, evaluation.Elapsed.TotalSeconds, evaluationTotal, wall.Elapsed.TotalSeconds, crosscheck);
                            if (raw.Any(x => !double.IsFinite(x) || x < -1E-10) || normalized.Any(x => !double.IsFinite(x))) throw new ArithmeticException("Invalid deviation metric.");
                            checkpoints.Add(row);
                            checkpointFile.WriteLine(row.Csv());
                            if (!best.TryGetValue(row.Profile, out var prior) || row.EHalfNormalized < prior.EHalfNormalized)
                            {
                                best[row.Profile] = row;
                                SaveProfile(developer, Path.Combine(runDirectory, "best-" + row.Profile + ".json.gz"), row);
                            }
                            Console.WriteLine($"{caseId}/{method} {iteration} {row.Profile} E={row.EHalfNormalized:G8} max={row.MaxNormalized:G8} solve={row.SolveSeconds:F2}s");
                        }
                        }
                        finally { RestoreAverageCaches(developer, cachedAverages); }
                    }
                    var finals = checkpoints.Where(x => x.Iteration == args.Iterations).ToArray();
                    SaveProfile(developer, Path.Combine(runDirectory, "final-profile.json.gz"), finals);
                    double outcomeSeconds = 0;
                    var outcomeRows = new Dictionary<string, object>();
                    if (args.Outcomes)
                    {
                        var timer = Stopwatch.StartNew();
                        foreach (var policy in new[] { ActionStrategies.CurrentProbability, ActionStrategies.AverageStrategy })
                        {
                            bool current = policy == ActionStrategies.CurrentProbability;
                            string profile = current ? "current" : "average";
                            var finalOutcome = await WriteOutcomes(developer, settings, runDirectory, "final-" + profile, current);
                            outcomeRows["final-" + profile] = finalOutcome;
                            if (best[profile].Iteration == args.Iterations)
                            {
                                outcomeRows["best-" + profile] = finalOutcome with { selection = "best-" + profile };
                                foreach (string path in Directory.EnumerateFiles(runDirectory, "outcomes-final-" + profile + "-*.csv"))
                                    File.Copy(path, Path.Combine(runDirectory, Path.GetFileName(path).Replace("outcomes-final-", "outcomes-best-")));
                            }
                            else
                            {
                                LoadProfile(developer, Path.Combine(runDirectory, "best-" + profile + ".json.gz"));
                                outcomeRows["best-" + profile] = await WriteOutcomes(developer, settings, runDirectory, "best-" + profile, current);
                                LoadProfile(developer, Path.Combine(runDirectory, "final-profile.json.gz"));
                            }
                        }
                        outcomeSeconds = timer.Elapsed.TotalSeconds;
                    }
                    var thresholds = new[] { 1E-2, 1E-3, 1E-4, 1E-5, 1E-7, 1E-8, 1E-9 }.SelectMany(epsilon => new[] { "current", "average" }.Select(profile => new
                    {
                        epsilon, profile, metric = "max normalized individual deviation", firstObservedCheckpoint = checkpoints.FirstOrDefault(x => x.Profile == profile && x.MaxNormalized <= epsilon)
                    })).ToArray();
                    var summary = new { caseId, method, optionsName = options.Name, final = finals, bestObserved = best.Values.ToArray(), thresholds, secondaryEHalf5eMinus6 = new[] { "current", "average" }.Select(profile => new { profile, firstObservedCheckpoint = checkpoints.FirstOrDefault(x => x.Profile == profile && x.EHalfNormalized <= 5E-6) }).ToArray(), setupSeconds = setup.Elapsed.TotalSeconds, solveSeconds = solve.Elapsed.TotalSeconds, evaluationSeconds = evaluationTotal, outcomeSeconds, wallSeconds = wall.Elapsed.TotalSeconds, outcomes = outcomeRows, historical = historical[caseId] };
                    summaries.Add(summary);
                    WriteJson(Path.Combine(runDirectory, "summary.json"), summary);
                    WriteJson(Path.Combine(args.Output, "summary.json"), summaries);
                    using (var summaryCsv = new StreamWriter(Path.Combine(args.Output, "summary.csv")))
                    {
                        summaryCsv.WriteLine("selection," + CsvHeader);
                        foreach (var priorCase in args.Cases)
                            foreach (var priorMethod in args.Methods)
                            {
                                string file = Path.Combine(args.Output, priorCase + "-" + priorMethod.Replace("+", "plus"), "summary.json");
                                if (!File.Exists(file)) continue;
                                using var document = JsonDocument.Parse(File.ReadAllText(file));
                                foreach (string selection in new[] { "final", "bestObserved" })
                                    foreach (var element in document.RootElement.GetProperty(selection).EnumerateArray())
                                        summaryCsv.WriteLine(selection + "," + JsonSerializer.Deserialize<Checkpoint>(element.GetRawText(), Json).Csv());
                            }
                    }
                }
            WriteJson(Path.Combine(args.Output, "completion.json"), new { completedUtc = DateTime.UtcNow, completedRuns = summaries.Count, expectedRuns = args.Cases.Length * args.Methods.Length });
            Console.WriteLine("Benchmark complete: " + args.Output);
        }

        private static void SaveProfile(StrategiesDeveloperBase developer, string path, object selection)
        {
            using var stream = File.Create(path);
            using var zip = new GZipStream(stream, CompressionLevel.Optimal);
            JsonSerializer.Serialize(zip, new { schemaVersion = 1, optionsName = developer.GameDefinition.OptionSetName, selection, informationSets = developer.InformationSets.Select(node => new { node.PlayerIndex, node.DecisionIndex, node.DecisionByteCode, identity = node.InformationSetWithAlphabeticalLabels(developer.GameDefinition), current = node.GetCurrentProbabilitiesAsArray(), average = node.GetAverageStrategiesAsArray() }).ToArray() }, Json);
        }

        private static async Task RunOutcomesFromAsync(Arguments args)
        {
            string sourceProvenance = Path.Combine(args.OutcomesFrom, "provenance.json");
            if (!File.Exists(sourceProvenance)) throw new FileNotFoundException("--outcomes-from must identify an existing benchmark directory.", sourceProvenance);
            WriteJson(Path.Combine(args.Output, "outcomes-source.json"), new { sourceDirectory = args.OutcomesFrom, provenanceSha256 = HashFile(sourceProvenance), command = Environment.CommandLine });
            var allResults = new List<object>();
            foreach (string caseId in args.Cases)
                foreach (string method in args.Methods)
                {
                    string runName = caseId + "-" + method.Replace("+", "plus");
                    string source = Path.Combine(args.OutcomesFrom, runName);
                    if (!File.Exists(Path.Combine(source, "final-profile.json.gz"))) throw new FileNotFoundException("Missing completed profile for " + runName + ". Select existing --cases and --methods explicitly.");
                    Console.WriteLine("Postprocessing exact outcomes for " + runName);
                    var timer = Stopwatch.StartNew();
                    var options = CreateCase(caseId);
                    using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "configuration.json")));
                    if (original.RootElement.GetProperty("optionsName").GetString() != options.Name) throw new InvalidDataException("Source option identity differs.");
                    var settings = CreateSettings(args, method);
                    var developer = (StrategiesDeveloperBase)await new BenchmarkLauncher().GetInitializedDevelper(options, options.Name, settings);
                    var counts = developer.CountGameTreeNodes();
                    var tree = original.RootElement.GetProperty("tree");
                    if (tree.GetProperty("chance").GetInt32() != counts.Chance || tree.GetProperty("decision").GetInt32() != counts.Decision || tree.GetProperty("final").GetInt32() != counts.Final || tree.GetProperty("informationSets").GetInt32() != developer.InformationSets.Count)
                        throw new InvalidDataException("Reconstructed outcome tree differs from source benchmark.");
                    string destination = Path.Combine(args.Output, runName);
                    Directory.CreateDirectory(destination);
                    var outcomes = new Dictionary<string, OutcomeResult>();
                    foreach (string profile in new[] { "current", "average" })
                    {
                        string finalPath = Path.Combine(source, "final-profile.json.gz");
                        LoadProfile(developer, finalPath);
                        ValidatePolicies(developer);
                        var final = await WriteOutcomes(developer, settings, destination, "final-" + profile, profile == "current");
                        outcomes["final-" + profile] = final;
                        string bestPath = Path.Combine(source, "best-" + profile + ".json.gz");
                        if (ProfileContentHash(bestPath) == ProfileContentHash(finalPath))
                        {
                            outcomes["best-" + profile] = final with { selection = "best-" + profile };
                            foreach (string path in Directory.EnumerateFiles(destination, "outcomes-final-" + profile + "-*.csv"))
                                File.Copy(path, Path.Combine(destination, Path.GetFileName(path).Replace("outcomes-final-", "outcomes-best-")));
                        }
                        else
                        {
                            LoadProfile(developer, bestPath);
                            ValidatePolicies(developer);
                            outcomes["best-" + profile] = await WriteOutcomes(developer, settings, destination, "best-" + profile, profile == "current");
                        }
                    }
                    var result = new { caseId, method, optionsName = options.Name, elapsedSeconds = timer.Elapsed.TotalSeconds, sourceProfiles = new[] { "final-profile.json.gz", "best-current.json.gz", "best-average.json.gz" }.Select(name => new { path = Path.Combine(source, name), sha256 = HashFile(Path.Combine(source, name)) }).ToArray(), outcomes };
                    WriteJson(Path.Combine(destination, "outcomes-summary.json"), result);
                    allResults.Add(result);
                    WriteJson(Path.Combine(args.Output, "outcomes-summary.json"), allResults);
                    Console.WriteLine($"Completed outcomes {runName} in {timer.Elapsed.TotalSeconds:F2}s");
                }
            WriteJson(Path.Combine(args.Output, "completion.json"), new { completedUtc = DateTime.UtcNow, completedRuns = allResults.Count, expectedRuns = args.Cases.Length * args.Methods.Length, mode = "outcomes-from" });
        }

        private static string ProfileContentHash(string path)
        {
            using var file = File.OpenRead(path);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var profile = JsonDocument.Parse(zip);
            return HashText(profile.RootElement.GetProperty("informationSets").GetRawText());
        }

        private static void LoadProfile(StrategiesDeveloperBase developer, string path)
        {
            using var stream = File.OpenRead(path);
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(zip);
            var sets = document.RootElement.GetProperty("informationSets").EnumerateArray().ToArray();
            if (sets.Length != developer.InformationSets.Count) throw new InvalidDataException("Saved profile has different information set count.");
            for (int index = 0; index < sets.Length; index++)
            {
                var node = developer.InformationSets[index];
                var saved = sets[index];
                if (saved.GetProperty("identity").GetString() != node.InformationSetWithAlphabeticalLabels(developer.GameDefinition))
                    throw new InvalidDataException("Saved profile information set identity differs.");
                var current = saved.GetProperty("current").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                var average = saved.GetProperty("average").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                for (int action = 0; action < node.NumPossibleActions; action++)
                {
                    node.NodeInformation[InformationSetNode.currentProbabilityDimension, action] = current[action];
                    node.NodeInformation[InformationSetNode.currentProbabilityForOpponentDimension, action] = current[action];
                    node.NodeInformation[InformationSetNode.averageStrategyProbabilityDimension, action] = average[action];
                    node.NodeInformation[InformationSetNode.cumulativeStrategyDimension, action] = average[action];
                }
            }
        }

        private static void ValidatePolicies(StrategiesDeveloperBase developer)
        {
            foreach (var node in developer.InformationSets)
                foreach (var probabilities in new[] { node.GetCurrentProbabilitiesAsArray(), node.GetAverageStrategiesAsArray() })
                    if (probabilities.Any(x => !double.IsFinite(x) || x < -1E-12 || x > 1 + 1E-12) || Math.Abs(probabilities.Sum() - 1) > 1E-9)
                        throw new ArithmeticException("Profile is not a finite probability simplex at information set " + node.GetInformationSetNodeNumber());
        }

        private static double[][] SynchronizeAverageForEvaluation(StrategiesDeveloperBase developer)
        {
            var original = new double[developer.InformationSets.Count][];
            for (int index = 0; index < original.Length; index++)
            {
                var node = developer.InformationSets[index];
                original[index] = new double[node.NumPossibleActions];
                node.GetAverageStrategyProbabilities(original[index]);
                var unrounded = node.GetAverageStrategiesAsArray();
                for (int action = 0; action < unrounded.Length; action++)
                    node.NodeInformation[InformationSetNode.averageStrategyProbabilityDimension, action] = unrounded[action];
            }
            return original;
        }

        private static void RestoreAverageCaches(StrategiesDeveloperBase developer, double[][] original)
        {
            for (int index = 0; index < original.Length; index++)
                for (int action = 0; action < original[index].Length; action++)
                    developer.InformationSets[index].NodeInformation[InformationSetNode.averageStrategyProbabilityDimension, action] = original[index][action];
        }

        private sealed record OutcomeResult(string selection, string profile, double[] rawDeviation, double[] normalizedDeviation, double[] evaluatedUtilities, double[] enumeratedUtilities, double utilityCrosscheckMaxError, List<Dictionary<string, string>> selectedReportRows);

        private static async Task<OutcomeResult> WriteOutcomes(StrategiesDeveloperBase developer, EvolutionSettings settings, string directory, string selection, bool current)
        {
            settings.UseCurrentStrategyForBestResponse = current;
            developer.ActionStrategy = current ? ActionStrategies.CurrentProbability : ActionStrategies.AverageStrategy;
            SynchronizeAverageForEvaluation(developer);
            developer.CalculateBestResponse(false);
            double[] evaluatedUtilities = developer.Status.UtilitiesOverall.ToArray();
            var reports = await developer.GenerateBenchmarkReportsAsync();
            var selectedRows = new List<Dictionary<string, string>>();
            for (int report = 0; report < reports.csvReports.Count; report++)
            {
                string csv = reports.csvReports[report];
                File.WriteAllText(Path.Combine(directory, $"outcomes-{selection}-{report}.csv"), csv);
                using var parser = new TextFieldParser(new StringReader(csv)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
                parser.SetDelimiters(",");
                var header = parser.ReadFields();
                while (!parser.EndOfData)
                {
                    var values = parser.ReadFields();
                    if (values.Length != header.Length) continue;
                    var row = header.Zip(values).GroupBy(x => x.First).ToDictionary(x => x.Key, x => x.Last().Second);
                    if (row.TryGetValue("Filter", out string filter) && (filter is "All" or "Dispute Arises" or "DisputeArises")) selectedRows.Add(row);
                }
            }
            double[] enumeratedUtilities = developer.BenchmarkLastEnumeratedUtilities;
            return new OutcomeResult(selection, current ? "current" : "average", developer.Status.BestResponseImprovement.ToArray(), developer.Status.BestResponseImprovementAdj.ToArray(), evaluatedUtilities, enumeratedUtilities, evaluatedUtilities.Zip(enumeratedUtilities, (a, b) => Math.Abs(a - b)).Max(), selectedRows);
        }

        private static object[] HistoricalLogs(string csvPath, Dictionary<string, string> names)
        {
            string root = Path.Combine(Path.GetDirectoryName(csvPath), "Individual Simulation Results");
            if (!Directory.Exists(root)) return Array.Empty<object>();
            var expected = names.Values.Select(x => "PREC010 " + x + " -log.txt").ToHashSet(StringComparer.Ordinal);
            return Directory.EnumerateFiles(root, "*-log.txt", System.IO.SearchOption.AllDirectories).Where(path => expected.Contains(Path.GetFileName(path))).OrderBy(x => x).Select(path => (object)new { path, sha256 = HashFile(path), lastWriteTimeUtc = File.GetLastWriteTimeUtc(path) }).ToArray();
        }

        public static Dictionary<string, object> ReadHistorical(string path, Dictionary<string, string> names)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Historical CSV required for provenance; pass --historical-csv.", path);
            var matches = names.Keys.ToDictionary(x => x, _ => new List<object>());
            using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            var header = parser.ReadFields();
            while (!parser.EndOfData)
            {
                long line = parser.LineNumber;
                var values = parser.ReadFields();
                if (values.Length != header.Length) throw new FormatException("CSV field count at line " + line);
                var row = header.Zip(values).ToDictionary(x => x.First, x => x.Second);
                if (row["Filter"] != "All") continue;
                foreach (var name in names)
                    if (row["OptionSetName"] == name.Value)
                        matches[name.Key].Add(new { sourceLine = line, exactRow = row, eHalfNormalized = double.Parse(row["Exploitability"], Invariant), reportedCalculationSeconds = double.Parse(row["Calculation Time"], Invariant) });
            }
            foreach (var match in matches)
                if (match.Value.Count != 1) throw new InvalidDataException($"Expected exactly one Filter=All historical row for {names[match.Key]}; found {match.Value.Count}.");
            return matches.ToDictionary(x => x.Key, x => x.Value.Single());
        }

        private static object Snapshot(object value, int depth = 0)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (value is string || value is bool || type.IsPrimitive || value is decimal) return value;
            if (type.IsEnum) return value.ToString();
            if (value is Delegate) return "delegate:" + type.FullName;
            if (depth > 3) return type.FullName;
            if (value is IDictionary dictionary)
            {
                var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary) result[Convert.ToString(entry.Key, Invariant)] = Snapshot(entry.Value, depth + 1);
                return result;
            }
            if (value is IEnumerable sequence) return sequence.Cast<object>().Select(x => Snapshot(x, depth + 1)).ToArray();
            var fields = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["$type"] = type.FullName };
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name))
                fields[field.Name] = Snapshot(field.GetValue(value), depth + 1);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.CanRead && x.GetIndexParameters().Length == 0).OrderBy(x => x.Name))
                try { fields[property.Name] = Snapshot(property.GetValue(value), depth + 1); } catch { fields[property.Name] = "unavailable"; }
            return fields;
        }
        private static string Format(object value) => value switch { null => "", double d => d.ToString("G17", Invariant), bool b => b ? "true" : "false", _ => Convert.ToString(value, Invariant) };
        private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
        private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        private static string Git(string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? result.TrimEnd() : "unavailable: " + process.StandardError.ReadToEnd();
        }

        private static async Task SelfTestOutcomeReportsAsync()
        {
            Dictionary<string, Dictionary<string, string>>[] results = new Dictionary<string, Dictionary<string, string>>[2];
            string fixture = Path.GetTempFileName();
            double[][] expectedCurrent = null, expectedAverage = null;
            try
            {
            for (int test = 0; test < 2; test++)
            {
                var options = CreateCase("baseline");
                options.NumLiabilitySignals = options.NumLiabilityStrengthPoints = options.NumOffers = 2;
                var generator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
                generator.PrecautionPowerLevels = generator.RelativePrecautionLevels = 2;
                var settings = CreateSettings(new Arguments { Iterations = 1 }, "pdcfr+");
                var developer = (StrategiesDeveloperBase)await new BenchmarkLauncher(fullReports: test == 0).GetInitializedDevelper(options, options.Name, settings);
                foreach (var node in developer.InformationSets)
                {
                    node.SetCurrentAndAverageProbabilities(Enumerable.Repeat(1.0 / node.NumPossibleActions, node.NumPossibleActions).ToArray());
                    for (int action = 0; action < node.NumPossibleActions; action++) node.NodeInformation[InformationSetNode.cumulativeStrategyDimension, action] = 0;
                }
                if (test == 0)
                {
                    for (int iteration = 0; iteration < 3; iteration++) ((PredictiveCFR)developer).RunIteration();
                    expectedCurrent = developer.InformationSets.Select(x => x.GetCurrentProbabilitiesAsArray()).ToArray();
                    expectedAverage = developer.InformationSets.Select(x => x.GetAverageStrategiesAsArray()).ToArray();
                    if (!expectedAverage.Any(probabilities => probabilities.Any(p => Math.Abs(p - 1.0 / probabilities.Length) > 1E-6))) throw new Exception("Profile roundtrip test requires a nonuniform average policy.");
                    SaveProfile(developer, fixture, null);
                }
                else
                {
                    LoadProfile(developer, fixture);
                    for (int index = 0; index < developer.InformationSets.Count; index++)
                        if (expectedCurrent[index].Zip(developer.InformationSets[index].GetCurrentProbabilitiesAsArray(), (a, b) => Math.Abs(a - b)).Max() > 1E-14 || expectedAverage[index].Zip(developer.InformationSets[index].GetAverageStrategiesAsArray(), (a, b) => Math.Abs(a - b)).Max() > 1E-14) throw new Exception("Saved current/average policy roundtrip failed.");
                }
                results[test] = new Dictionary<string, Dictionary<string, string>>();
                foreach (bool current in new[] { true, false })
                {
                settings.UseCurrentStrategyForBestResponse = current;
                developer.ActionStrategy = current ? ActionStrategies.CurrentProbability : ActionStrategies.AverageStrategy;
                SynchronizeAverageForEvaluation(developer);
                developer.CalculateBestResponse(false);
                var reports = await developer.GenerateBenchmarkReportsAsync();
                foreach (string csv in reports.csvReports)
                {
                    using var parser = new TextFieldParser(new StringReader(csv)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
                    parser.SetDelimiters(",");
                    var header = parser.ReadFields();
                    while (!parser.EndOfData)
                    {
                        var values = parser.ReadFields();
                        if (values.Length != header.Length) continue;
                        var row = header.Zip(values).GroupBy(x => x.First).ToDictionary(x => x.Key, x => x.Last().Second);
                        if (row.TryGetValue("Filter", out string filter) && filter is "All" or "DisputeArises") results[test][(current ? "current/" : "average/") + filter] = row;
                    }
                }
                }
            }
            foreach (string profile in new[] { "current/", "average/" })
            foreach (string filter in new[] { "All", "DisputeArises" })
                foreach (string column in OutcomeColumns)
                    if (results[0][profile + filter][column] != results[1][profile + filter][column])
                        throw new Exception($"Restricted/full exact report mismatch: {profile}{filter}/{column}.");
            Console.WriteLine("Nonuniform saved current/average profile roundtrip, exact outcome utility invariant, and restricted/full report equivalence passed on a two-point game.");
            }
            finally { File.Delete(fixture); }
        }

        private static void SelfTest()
        {
            var parsed = Arguments.Parse(new[] { "--output", ".", "--methods", "legacy,pdcfr+", "--cases", "fee", "--iterations", "7", "--checkpoint", "3" });
            if (parsed.Iterations != 7 || parsed.Checkpoint != 3 || parsed.Methods.Length != 2 || parsed.Cases.Single() != "fee") throw new Exception("Argument selection failed.");
            foreach (var bad in new[] { new[] { "--iterations", "0" }, new[] { "--output", ".", "--cases", "unknown" }, new[] { "--output", ".", "--methods", "legacy,legacy" }, new[] { "--output" }, Array.Empty<string>() })
            {
                bool rejected = false;
                try { Arguments.Parse(bad); } catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid arguments accepted.");
            }
            var baseline = CreateCase("baseline");
            if (baseline.Name != "Risk Neutral CM 1 FSM 0 DM 1 LT 1 UPC 1E-05" || baseline.NumLiabilitySignals != 8 || baseline.PLiabilityNoiseStdev != .2 || baseline.PredeterminedAbandonAndDefaults) throw new Exception("Publication case reconstruction failed.");
            var defaults = Arguments.Parse(new[] { "--output", "." });
            if (!defaults.Cases.SequenceEqual(new[] { "baseline", "fee", "damages", "risk" })) throw new Exception("Default four-case selection changed.");
            if (CreateCase("fee").Name != "Risk Neutral CM 1 FSM 1 DM 1 LT 1 UPC 1E-05"
                || CreateCase("damages").Name != "Risk Neutral CM 1 FSM 0 DM 2 LT 1 UPC 1E-05"
                || CreateCase("risk").Name != "MODRA CM 1 FSM 0 DM 1 LT 1 UPC 1E-05") throw new Exception("Existing publication case names changed.");
            var hardArguments = Arguments.Parse(new[] { "--output", ".", "--cases", "hard-indeterminacy" });
            var hard = CreateCase(hardArguments.Cases.Single());
            var hardGenerator = (PrecautionNegligenceDisputeGenerator)hard.LitigGameDisputeGenerator;
            if (hard.Name != "Risk Neutral CM 4 CN High Indet UPC 1.2E-05"
                || hard.CostsMultiplier != 4 || hard.PTrialCosts != .1 || hard.DTrialCosts != .1 || hard.PFilingCost != .1 || hard.DAnswerCost != .1
                || hardGenerator.UnitPrecautionCost != 1.2E-05 || hardGenerator.LiabilityThreshold != 1
                || hard.LoserPays || hard.LoserPaysMultiple != 0 || hard.DamagesMultiplier != 1)
                throw new Exception("Hard indeterminacy case name, costs, or seed defaults are incorrect.");
            if (hard.CourtLiabilityNoiseStdev != .2 || hard.PLiabilityNoiseStdev != .2 || hard.DLiabilityNoiseStdev != .2
                || hardGenerator.CourtDecisionRule != CourtDecisionRule.ProbitThreshold || hardGenerator.CourtProbitScale != .5
                || hard.NumLiabilitySignals != 8 || hard.NumOffers != 8 || hardGenerator.PrecautionPowerLevels != 8
                || hardGenerator.RelativePrecautionLevels != 8 || hard.PredeterminedAbandonAndDefaults || !hard.CollapseChanceDecisions)
                throw new Exception("Hard case must increase court indeterminacy, preserve signal noise 0.2, and use the full publication grid.");
            var row = new Checkpoint("baseline", "legacy", 7, "average", new[] { .1, .2 }, new[] { .01, .02 }, new[] { 10.0, 10.0 }, .15, .015, .2, .02, 1, 2, 3, 4, 5, null);
            if (row.Csv().Split(',').Length != CsvHeader.Split(',').Length || JsonSerializer.Deserialize<Checkpoint>(JsonSerializer.Serialize(row, Json), Json).EHalfNormalized != row.EHalfNormalized) throw new Exception("Checkpoint schema failed.");
            if (HashText("reproducible") != HashText("reproducible") || HashText("a") == HashText("b")) throw new Exception("Provenance hashing failed.");
            string fixture = Path.GetTempFileName();
            try
            {
                File.WriteAllText(fixture, "Filter,OptionSetName,Exploitability,Calculation Time,Quoted\nAll,Example,1.611E-07,132,\"exact, quoted\"\nDispute Arises,Example,99,99,x\n");
                var matches = ReadHistorical(fixture, new Dictionary<string, string> { ["baseline"] = "Example" });
                string preserved = JsonSerializer.Serialize(matches["baseline"], Json);
                if (!preserved.Contains("exact, quoted") || !preserved.Contains("1.611E-07")) throw new Exception("Historical provenance lost exact source strings.");
                File.AppendAllText(fixture, "All,Example,2E-07,133,x\n");
                bool duplicateRejected = false;
                try { ReadHistorical(fixture, new Dictionary<string, string> { ["baseline"] = "Example" }); } catch (InvalidDataException) { duplicateRejected = true; }
                if (!duplicateRejected) throw new Exception("Ambiguous historical row accepted.");
            }
            finally { File.Delete(fixture); }
            Console.WriteLine("Benchmark argument, publication-case, schema, and provenance self-tests passed.");
        }
    }
}
