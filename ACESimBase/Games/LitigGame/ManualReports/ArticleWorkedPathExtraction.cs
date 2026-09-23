using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>
    /// Read-only extraction from a saved production profile. Never calls RunAlgorithm.
    /// A path chooses a history, not a replacement strategy for utility calculations.
    /// Layout is intentionally left to the article's bespoke LaTeX source.
    /// </summary>
    public static class ArticleWorkedPathExtraction
    {
        public sealed record Choice(LitigGameDecisions Decision, byte Action);
        public sealed record PathRequest(string Name, string Purpose, Choice[] Choices);
        public sealed record Request(string OptionSetName, string EquilibriumFile,
            string ActionReportFile, int EquilibriumNumber, PathRequest[] Paths);
        public sealed record SourceFile(string Path, string Sha256);
        public sealed record ActionData(byte Action, string Label, double Probability,
            double? ConditionalActingPlayerUtility, double? UtilityLossFromBest);
        public sealed record StepData(LitigGameDecisions Decision, string Label, string Player,
            int? InformationSetNumber, string InformationSetLabels, string InformationSetContents,
            double HistoryReachProbability, double? InformationSetReachProbability,
            bool? OffPathInformationSet, bool UniformFallbackForUnspecifiedStrategy,
            double? ConditionalActingPlayerUtility, byte SelectedAction, ActionData[] Actions);
        public sealed record OutcomeData(string Outcome, byte? CourtSignal, double ConditionalProbability,
            double EquilibriumHistoryProbability, double TransferToPlaintiff,
            double PlaintiffNetMonetaryPayoff, double DefendantNetMonetaryPayoff,
            double PlaintiffFinalWealth, double DefendantFinalWealth,
            double PlaintiffUtility, double DefendantUtility,
            double PlaintiffNetLitigationExpense, double DefendantNetLitigationExpense);
        public sealed record PathData(string Name, string Purpose, double EquilibriumProbability,
            StepData[] Steps, double[] ExpectedFinalWealth, double[] ExpectedUtilityUnrounded,
            OutcomeData[] Outcomes);
        public sealed record Extraction(string SchemaVersion, string OptionSetName, int EquilibriumNumber,
            SourceFile EquilibriumSource, SourceFile ActionReportSource,
            int ValidatedActionReportRows, int MaxIntegralUtility, int RoundOffChanceDigits,
            Dictionary<string, string> VariableSettings, double[] InitialWealth,
            string[] Interpretation, PathData[] Paths);

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static LitigGameOptions CreateOptions(string name)
        {
            var launcher = name.StartsWith("Agreement-Enabled__", StringComparison.Ordinal)
                ? new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                : name.Contains("__Starts-", StringComparison.Ordinal)
                ? new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness)
                : name.Split("__").Contains("ExitFees-AllUnilateralExits", StringComparer.Ordinal)
                ? new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting)
                : NewLauncher();
            return launcher.GetOptionsSets().Cast<LitigGameOptions>().Single(x => x.Name == name);
        }

        private static LitigGameCorrelatedSignalsArticleLauncher NewLauncher() => new(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);

        public static async Task<StrategiesDeveloperBase> InitializeAsync(LitigGameOptions options)
        {
            var settings = new EvolutionSettings
            {
                Algorithm = GameApproximationAlgorithm.SequenceForm,
                ParallelOptimization = false,
                CreateEFGFile = false,
                CreateEquilibriaFile = false,
                GenerateManualReports = false,
                GenerateReportsByPlaying = false,
            };
            var developer = (StrategiesDeveloperBase)await NewLauncher()
                .GetInitializedDevelper(options, options.Name, settings);
            // SequenceForm.Initialize rounds terminal utilities. The solver normally rounds
            // chance probabilities when preparing the matrix; do that without solving it.
            foreach (var chance in developer.ChanceNodes)
                chance.GetProbabilitiesAsRationals(!settings.SequenceFormCutOffProbabilityZeroNodes,
                    EvolutionSettings.MaxIntegralUtility);
            return developer;
        }

        public static HashSet<int> LoadProfile(StrategiesDeveloperBase developer, double[] values)
        {
            var nodes = developer.InformationSets.OrderBy(x => x.PlayerIndex)
                .ThenBy(x => x.InformationSetNodeNumber).ToArray();
            if (values.Length != nodes.Sum(x => x.NumPossibleActions))
                throw new InvalidDataException("Saved profile length does not match the initialized game.");
            var fallbacks = new HashSet<int>();
            int offset = 0;
            foreach (var node in nodes)
            {
                var probabilities = values.Skip(offset).Take(node.NumPossibleActions).ToArray();
                if (probabilities.Any(x => !double.IsFinite(x) || x < 0 || x > 1))
                    throw new InvalidDataException("Invalid saved action probability.");
                double sum = probabilities.Sum();
                if (sum == 0)
                    fallbacks.Add(node.InformationSetNodeNumber);
                else
                    RequireNear(1, sum, "Saved action probabilities must sum to one.");
                offset += node.NumPossibleActions;
            }
            developer.SetInformationSetsToEquilibrium(values);
            return fallbacks;
        }

        public static async Task<Extraction> ExtractAsync(Request request, string requestDirectory)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                return await ExtractCoreAsync(request, requestDirectory);
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }

        private static async Task<Extraction> ExtractCoreAsync(Request request, string requestDirectory)
        {
            if (request.EquilibriumNumber < 1 || request.Paths == null || request.Paths.Length == 0 ||
                request.Paths.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != request.Paths.Length)
                throw new ArgumentException("Supply a positive equilibrium number and uniquely named paths.");
            string Resolve(string file) => Path.GetFullPath(file, requestDirectory);
            string equilibriumFile = Resolve(request.EquilibriumFile);
            string reportFile = Resolve(request.ActionReportFile);
            string selectedLine = File.ReadLines(equilibriumFile)
                .Skip(request.EquilibriumNumber - 1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(selectedLine))
                throw new InvalidDataException("The requested equilibrium is absent from the saved file.");
            double[] profile = selectedLine.Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
            var options = CreateOptions(request.OptionSetName);
            var developer = await InitializeAsync(options);
            var fallbacks = LoadProfile(developer, profile);
            // This semantic and numerical comparison detects equal-length but differently
            // ordered profiles, parameter drift, and mismatched source report/profile pairs.
            int validatedRows = ValidateActionReport(developer, request.EquilibriumNumber, reportFile);
            var calculator = new CalculateUtilitiesAtEachInformationSet();
            developer.TreeWalk_Tree(calculator);
            var recorder = new RecordGamePathsProcessor();
            developer.TreeWalk_Tree(recorder);
            RequireNear(1, recorder.Paths.Sum(x => x.Probability), "Total terminal probability");
            var results = request.Paths.Select(path => ExtractPath(developer, options, calculator,
                fallbacks, SelectPath(recorder.Paths, path.Choices), path.Name, path.Purpose)).ToArray();
            return new Extraction("1", options.Name, request.EquilibriumNumber,
                Fingerprint(equilibriumFile), Fingerprint(reportFile), validatedRows,
                EvolutionSettings.MaxIntegralUtility, EvolutionSettings.RoundOffChanceDigits,
                options.VariableSettings.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, CultureInfo.InvariantCulture)),
                new[] { options.PInitialWealth, options.DInitialWealth },
                new[]
                {
                    "Action utilities condition on the acting player's information set, not the selected signal pair; subsequent play retains the saved profile.",
                    "Information-set utilities use the production solver's rounded terminal utilities and chance probabilities; terminal monetary outcomes use the game's unrounded accounting.",
                    "History reach is not information-set reach. A zero-probability deviation at a reached information set still has a conditional action utility; an off-path information set does not.",
                    "All-zero saved strategies use the loader's uniform fallback, explicitly flagged; this is not evidence of equilibrium mixing at an unreached information set.",
                    "Collapsed terminal lotteries are expanded by deterministic game replay; their probabilities condition on the full selected history, not either player's private information set.",
                    "Net monetary payoff is change in wealth, not final wealth or net-outcome fidelity. Court findings are not true liability; continuous merits remain integrated out.",
                    "Path specifications omit only automatic one-action decisions. Omitted branches in the bespoke figure remain in the full game and are not renormalized.",
                }, results);
        }

        public static RecordGamePathsProcessor.GamePath SelectPath(
            IReadOnlyList<RecordGamePathsProcessor.GamePath> paths, Choice[] choices)
        {
            if (choices == null || choices.Length == 0 || choices.Any(x => x.Action == 0))
                throw new ArgumentException("A path needs an ordered, nonempty list of one-based actions.");
            var matches = paths.Where(path => path.Steps
                .Where(x => ((IAnyNode)x.FromNode).Decision.NumPossibleActions > 1)
                .Select(x => new Choice((LitigGameDecisions)((IAnyNode)x.FromNode).Decision.DecisionByteCode,
                    x.ActionIndex)).SequenceEqual(choices)).Take(2).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Expected one complete path, found {matches.Length}. " +
                    "Check action ranges, decision order, and premature or trailing choices.");
            return matches[0];
        }

        public static PathData ExtractPath(StrategiesDeveloperBase developer, LitigGameOptions options,
            CalculateUtilitiesAtEachInformationSet calculator, HashSet<int> fallbacks,
            RecordGamePathsProcessor.GamePath path, string name, string purpose)
        {
            double reach = 1;
            var steps = new List<StepData>();
            foreach (var item in path.Steps)
            {
                var node = (IAnyNode)item.FromNode;
                var decision = node.Decision;
                double[] probabilities = node.GetNodeValues();
                if (decision.NumPossibleActions > 1)
                {
                    var info = item.FromNode as InformationSetNode;
                    double? infoReach = null, utility = null;
                    bool? offPath = null;
                    double[] actionUtilities = null;
                    double? best = null;
                    if (info != null)
                    {
                        var values = calculator.GetUtilitiesAndReachProbability(info.InformationSetNodeNumber);
                        infoReach = values.reachProbability;
                        offPath = infoReach <= InformationSetActionReport.OffPathTolerance;
                        if (offPath == false)
                        {
                            utility = values.utilities[info.PlayerIndex];
                            actionUtilities = values.utilitiesAtSuccessors.Select(x => x[info.PlayerIndex]).ToArray();
                            best = actionUtilities.Max(); // LitigGame's two players maximize utility.
                        }
                    }
                    var actions = Enumerable.Range(1, probabilities.Length).Select(a => new ActionData(
                        (byte)a, developer.GameDefinition.GetActionString((byte)a, decision.DecisionByteCode),
                        probabilities[a - 1], actionUtilities?[a - 1],
                        actionUtilities == null ? null : best - actionUtilities[a - 1])).ToArray();
                    steps.Add(new StepData((LitigGameDecisions)decision.DecisionByteCode, decision.Name,
                        info == null ? "Chance" : developer.GameDefinition.Players[info.PlayerIndex].PlayerName,
                        info?.InformationSetNodeNumber,
                        info?.InformationSetWithLabels(developer.GameDefinition), info?.InformationSetContentsString,
                        reach, infoReach, offPath, info != null && fallbacks.Contains(info.InformationSetNodeNumber),
                        utility, item.ActionIndex, actions));
                }
                reach *= probabilities[item.ActionIndex - 1];
            }
            RequireNear(path.Probability, reach, "Selected path probability");
            // Replay is solely for outcome accounting/terminal lotteries. It cannot change
            // the initialized developer or the saved profile used by the utility calculator.
            var replayOptions = CreateOptions(options.Name);
            var meaningful = path.Steps.Where(x => ((IAnyNode)x.FromNode).Decision.NumPossibleActions > 1).ToArray();
            int cursor = 0;
            byte Override(Decision decision, GameProgress progress)
            {
                if (decision.NumPossibleActions == 1) return 1;
                if (cursor >= meaningful.Length ||
                    ((IAnyNode)meaningful[cursor].FromNode).Decision.DecisionByteCode != decision.DecisionByteCode)
                    throw new InvalidDataException("Replay decision order differs from the extracted path.");
                return meaningful[cursor++].ActionIndex;
            }
            var replay = LitigGameLauncherBase.PlayLitigGameOnce(replayOptions, Override);
            if (cursor != meaningful.Length || !replay.GameComplete)
                throw new InvalidDataException("The selected history did not replay to completion.");
            var endings = replay.AlternativeEndings ?? new() { (replay, 1.0) };
            RequireNear(1, endings.Sum(x => x.weight), "Terminal lottery probability");
            var outcomes = endings.Select(ending => Outcome(ending.completedGame, ending.weight, reach)).ToArray();
            RequireNear(replay.PWelfare, outcomes.Sum(x => x.ConditionalProbability * x.PlaintiffUtility), "P replay utility");
            RequireNear(replay.DWelfare, outcomes.Sum(x => x.ConditionalProbability * x.DefendantUtility), "D replay utility");
            return new PathData(name, purpose, reach, steps.ToArray(),
                new[] { replay.PFinalWealth, replay.DFinalWealth }, new[] { replay.PWelfare, replay.DWelfare }, outcomes);
        }

        private static OutcomeData Outcome(LitigGameProgress progress, double probability, double reach)
        {
            string label = !progress.PFiles ? "No filing" : !progress.DAnswers ? "No answer" :
                progress.CaseSettles ? "Settlement" : progress.PAbandons ? "Abandonment" :
                progress.DDefaults ? "Default" : progress.PWinsAtTrial ? "Court finds liable" : "Court finds not liable";
            double transfer = !progress.PFiles || progress.PAbandons ? 0 :
                !progress.DAnswers || progress.DDefaults ? progress.LitigGameOptions.DamagesMax * progress.LitigGameOptions.DamagesMultiplier :
                progress.SettlementValue ?? (progress.PWinsAtTrial ? progress.DamagesAwarded : 0);
            return new OutcomeData(label, progress.CLiabilitySignalDiscrete, probability, reach * probability,
                transfer, progress.PChangeWealth, progress.DChangeWealth,
                progress.PFinalWealth, progress.DFinalWealth, progress.PWelfare, progress.DWelfare,
                transfer - progress.PChangeWealth, -transfer - progress.DChangeWealth);
        }

        public static int ValidateActionReport(StrategiesDeveloperBase developer, int equilibrium, string file)
        {
            List<Dictionary<string, string>> Read(TextReader reader)
            {
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Read(); csv.ReadHeader();
                var rows = new List<Dictionary<string, string>>();
                while (csv.Read())
                    if (csv.GetField<int>("Equilibrium Number") == equilibrium)
                        rows.Add(csv.HeaderRecord.ToDictionary(h => h, h => csv.GetField(h)));
                return rows;
            }
            using var sourceReader = File.OpenText(file);
            var expected = Read(sourceReader);
            using var generatedReader = new StringReader(InformationSetActionReport.BuildCsv(developer, equilibrium));
            var actual = Read(generatedReader);
            string Key(Dictionary<string, string> row) => row["Player Index"] + "/" +
                row["Information Set Number"] + "/" + row["Action"];
            var actualByKey = actual.ToDictionary(Key);
            if (expected.Count != actual.Count || expected.Select(Key).Distinct().Count() != actual.Count)
                throw new InvalidDataException("Action report row count/identity differs from the initialized game.");
            string[] numeric = { "Equilibrium Reach Probability", "Equilibrium Action Probability",
                "Conditional Information-Set Utility", "Best Action Utility", "Conditional Action Utility",
                "Utility Loss from Best Action" };
            foreach (var row in expected)
            {
                if (!actualByKey.TryGetValue(Key(row), out var generated))
                    throw new InvalidDataException("Action report information-set identity mismatch.");
                foreach (string column in InformationSetActionReport.Headers)
                {
                    if (numeric.Contains(column) && row[column] != "" && generated[column] != "")
                        RequireNear(double.Parse(row[column], CultureInfo.InvariantCulture),
                            double.Parse(generated[column], CultureInfo.InvariantCulture), Key(row) + " " + column);
                    else if (row[column] != generated[column])
                        throw new InvalidDataException($"Action report mismatch: {Key(row)}, {column}.");
                }
            }
            return actual.Count;
        }

        private static void RequireNear(double expected, double actual, string description)
        {
            if (!double.IsFinite(expected) || !double.IsFinite(actual) ||
                Math.Abs(expected - actual) > 1E-9 * Math.Max(1, Math.Abs(expected)))
                throw new InvalidDataException($"{description}: expected {expected:G17}, got {actual:G17}.");
        }

        private static SourceFile Fingerprint(string file) => new(file,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());

        public static async Task WriteJsonAsync(string requestFile, string outputFile)
        {
            requestFile = Path.GetFullPath(requestFile);
            var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(requestFile), JsonOptions);
            var data = await ExtractAsync(request, Path.GetDirectoryName(requestFile));
            outputFile = Path.GetFullPath(outputFile);
            // No production file is ever an output of this command.
            bool InSourceDirectory(string source) => outputFile.StartsWith(
                Path.GetDirectoryName(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(Path.GetExtension(outputFile), ".json", StringComparison.OrdinalIgnoreCase) ||
                outputFile.Equals(requestFile, StringComparison.OrdinalIgnoreCase) ||
                InSourceDirectory(data.EquilibriumSource.Path) || InSourceDirectory(data.ActionReportSource.Path))
                throw new ArgumentException("Write a separate JSON outside the production-input directories; never overwrite the request.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(data, JsonOptions) + "\n");
            Console.WriteLine($"Extracted {data.Paths.Length} paths; validated {data.ValidatedActionReportRows} action-report rows.");
        }
    }
}
