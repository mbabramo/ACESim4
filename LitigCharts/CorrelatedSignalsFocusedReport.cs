using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LitigCharts
{
    /// <summary>
    /// Validates and reports the focused CS004 continuous-merits production design.
    /// Generated files are deliberately separate from simulation inputs and are not committed.
    /// </summary>
    public static class CorrelatedSignalsFocusedReport
    {
        public const string DefendantExcessBurdenColumn =
            "Defendant Excess Net Monetary Burden";
        public const string PlaintiffRecoveryShortfallColumn =
            "Plaintiff Net Recovery Shortfall";
        public const string MeritoriousPlaintiffRecoveryShortfallColumn =
            "Meritorious-Plaintiff Net Recovery Shortfall";
        public const string NonliableDefendantNetBurdenColumn =
            "Nonliable-Defendant Net Burden";
        public const string LiableDefendantExcessNetBurdenColumn =
            "Liable-Defendant Excess Net Burden Above Damages";
        public const string NetOutcomeFidelityLossColumn =
            "Net Outcome Fidelity Loss";
        public const string MutualGiveUpBeforeAllocationColumn =
            "Mutual Give-Up Probability Before 50/50 Allocation";

        private const string LegacyFalsePositiveColumn = "False Positive Inaccuracy";
        private const string LegacyFalseNegativeColumn = "False Negative Inaccuracy";
        private const double AccountingTolerance = 1E-4;

        public sealed record ValidationSummary(
            int NumericalResultCount,
            int SpecificationComparisonCount,
            int FeeRegimeComparisonCount,
            int SignalStrategyCount,
            int OutcomeMeasureCount);

        public static readonly IReadOnlyList<string> NumericalFilters =
            CorrelatedSignalsPairedReport.ExpectedFilters;

        public static readonly IReadOnlyList<string> SignalFilters =
            Enumerable.Range(1, 10).Select(signal => $"PLiabilitySignal{signal}")
                .Concat(Enumerable.Range(1, 10).Select(signal => $"DLiabilitySignal{signal}"))
                .ToArray();

        private static readonly string[] DerivedNumericalColumns =
        {
            "Reaches Bargaining",
            "Settlement Unconditional",
            "Settlement Conditional on Reaching Bargaining",
            "Real Litigation Costs",
            "Liability Transfer to Plaintiff",
            "Fee-Shifting Transfer to Plaintiff",
            "Total Net Transfer to Plaintiff",
            MeritoriousPlaintiffRecoveryShortfallColumn,
            NonliableDefendantNetBurdenColumn,
            LiableDefendantExcessNetBurdenColumn,
            NetOutcomeFidelityLossColumn,
        };

        public static ValidationSummary BuildAndValidate(
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            string numericalSourceCsvPath,
            string signalSourceCsvPath,
            string numericalResultsCsvPath,
            string specificationComparisonsCsvPath,
            string feeRegimeComparisonsCsvPath,
            string signalStrategiesCsvPath)
        {
            if (launcher == null)
                throw new ArgumentNullException(nameof(launcher));
            bool exitFees = launcher.RunPlan ==
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting;
            if (!exitFees && launcher.RunPlan !=
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
                throw new ArgumentException("Focused reporting requires the CS004 or CS006EF launcher.", nameof(launcher));

            List<GameOptions> optionSets = launcher.GetOptionsSets();
            launcher.ValidateProductionMatrix(optionSets);
            Dictionary<string, LitigGameOptions> optionsByName = optionSets
                .Cast<LitigGameOptions>()
                .ToDictionary(option => option.Name, StringComparer.Ordinal);

            CsvTable numericalSource = ReadRows(numericalSourceCsvPath);
            ValidateRows(
                numericalSource,
                optionSets,
                NumericalFilters,
                launcher.DefaultVariableValues.Select(setting => setting.Item1));
            RequireColumns(numericalSource.Headers, new[]
            {
                "P Files",
                "D Answers",
                "Trial",
                "No Suit",
                "Settles",
                "No Answer",
                "P Abandons",
                "D Defaults",
                MutualGiveUpBeforeAllocationColumn,
                "P Loses",
                "P Wins",
                "Value If Settled",
                "Expenditures",
            });

            Dictionary<string, Dictionary<string, string>> numericalRowsByIdentity =
                numericalSource.Rows.ToDictionary(
                    row => $"{row["OptionSetName"]}|{row["Filter"]}",
                    StringComparer.Ordinal);
            List<Dictionary<string, string>> numericalRows = optionsByName
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => AddDerivedNumericalValues(
                    numericalRowsByIdentity[$"{pair.Key}|All"],
                    numericalRowsByIdentity[$"{pair.Key}|Truly Liable"],
                    numericalRowsByIdentity[$"{pair.Key}|Truly Not Liable"],
                    pair.Value))
                .ToList();
            foreach (Dictionary<string, string> row in numericalRows)
                ValidateNumericalAccounting(row, optionsByName[row["OptionSetName"]]);
            string[] numericalHeaders = numericalSource.Headers
                .Select(RenameLegacyAccuracyHeader)
                .Concat(DerivedNumericalColumns)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            WriteRows(numericalResultsCsvPath, numericalHeaders, numericalRows);

            var metadataHeaders = new HashSet<string>(
                launcher.DefaultVariableValues.Select(setting => setting.Item1)
                    .Concat(new[] { "Equilibrium Type", "Filter", "GroupName", "OptionSetName" }),
                StringComparer.OrdinalIgnoreCase);
            string[] outcomeMeasures = numericalHeaders
                .Where(header => !metadataHeaders.Contains(header))
                .ToArray();
            if (outcomeMeasures.Length == 0)
                throw new InvalidDataException("CS004 numerical results contain no outcome measures.");

            int specificationComparisonCount = WriteSpecificationComparisons(
                specificationComparisonsCsvPath,
                numericalRows,
                outcomeMeasures);
            int feeComparisonCount = exitFees ? 0 : WriteFeeRegimeComparisons(
                feeRegimeComparisonsCsvPath,
                numericalRows,
                outcomeMeasures);

            CsvTable signalSource = ReadRows(signalSourceCsvPath);
            ValidateRows(
                signalSource,
                optionSets,
                SignalFilters,
                launcher.DefaultVariableValues.Select(setting => setting.Item1));
            RequireColumns(signalSource.Headers, SignalRequiredColumns());
            int signalStrategyCount = WriteSignalStrategies(
                signalStrategiesCsvPath,
                launcher,
                signalSource.Rows);

            int expectedRows = exitFees ? LitigGameCorrelatedSignalsArticleLauncher.ExitFeeOptionSetCount
                : LitigGameCorrelatedSignalsArticleLauncher.FocusedOptionSetCount;
            int expectedComparisons = exitFees ? LitigGameCorrelatedSignalsArticleLauncher.ExitFeeSpecificationComparisonCount
                : LitigGameCorrelatedSignalsArticleLauncher.FocusedSpecificationComparisonCount;
            if (numericalRows.Count != expectedRows)
                throw new InvalidDataException(
                    $"{launcher.MasterReportNameForDistributedProcessing} contains {numericalRows.Count} numerical result rows; expected {expectedRows}.");
            if (specificationComparisonCount != expectedComparisons)
                throw new InvalidDataException(
                    $"{launcher.MasterReportNameForDistributedProcessing} contains {specificationComparisonCount} specification comparisons; expected {expectedComparisons}.");
            if (!exitFees && feeComparisonCount !=
                LitigGameCorrelatedSignalsArticleLauncher.FocusedFeeRegimeComparisonCount)
                throw new InvalidDataException(
                    $"CS004 contains {feeComparisonCount} fee-regime comparisons; expected " +
                    $"{LitigGameCorrelatedSignalsArticleLauncher.FocusedFeeRegimeComparisonCount}.");
            int expectedSignalStrategies =
                expectedRows * SignalFilters.Count;
            if (signalStrategyCount != expectedSignalStrategies)
                throw new InvalidDataException(
                    $"CS004 contains {signalStrategyCount} signal-strategy rows; expected " +
                    $"{expectedSignalStrategies}.");

            return new ValidationSummary(
                numericalRows.Count,
                specificationComparisonCount,
                feeComparisonCount,
                signalStrategyCount,
                outcomeMeasures.Length);
        }

        /// <summary>
        /// Allocates the pre-resolution mutual-give-up mass equally between plaintiff
        /// abandonment and defendant default. The mutual-give-up column is retained as an
        /// audit field. Call this only on a newly aggregated CSV, before producing charts.
        /// </summary>
        public static void AllocateMutualGiveUpInCsv(string path)
        {
            CsvTable table = ReadRows(path);
            RequireColumns(table.Headers, new[]
            {
                "P Abandons",
                "D Defaults",
                MutualGiveUpBeforeAllocationColumn,
            });
            foreach (Dictionary<string, string> row in table.Rows)
            {
                if (IsBlank(row, "P Abandons") &&
                    IsBlank(row, "D Defaults") &&
                    IsBlank(row, MutualGiveUpBeforeAllocationColumn))
                    continue;
                double mutualGiveUp = RequiredValue(row, MutualGiveUpBeforeAllocationColumn);
                row["P Abandons"] = Format(RequiredValue(row, "P Abandons") + 0.5 * mutualGiveUp);
                row["D Defaults"] = Format(RequiredValue(row, "D Defaults") + 0.5 * mutualGiveUp);
            }
            WriteRows(path, table.Headers, table.Rows);
        }

        private static Dictionary<string, string> AddDerivedNumericalValues(
            Dictionary<string, string> source,
            IReadOnlyDictionary<string, string> trulyLiableSource,
            IReadOnlyDictionary<string, string> trulyNotLiableSource,
            LitigGameOptions options)
        {
            var row = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
            RenameLegacyAccuracyValue(row, LegacyFalsePositiveColumn, DefendantExcessBurdenColumn);
            RenameLegacyAccuracyValue(row, LegacyFalseNegativeColumn, PlaintiffRecoveryShortfallColumn);

            double reachesBargaining = RequiredValue(row, "D Answers");
            double settles = RequiredValue(row, "Settles");
            double mutualGiveUp = RequiredValue(row, MutualGiveUpBeforeAllocationColumn);
            (double pAbandons, double dDefaults) = AllocateMutualGiveUpIfNeeded(
                reachesBargaining,
                settles,
                RequiredValue(row, "Trial"),
                RequiredValue(row, "P Abandons"),
                RequiredValue(row, "D Defaults"),
                mutualGiveUp,
                row["OptionSetName"]);
            row["P Abandons"] = Format(pAbandons);
            row["D Defaults"] = Format(dDefaults);
            double liabilityTransfer =
                RequiredValue(row, "No Answer") * options.DamagesMax * options.DamagesMultiplier +
                dDefaults * options.DamagesMax * options.DamagesMultiplier +
                settles * OptionalValue(row, "Value If Settled").GetValueOrDefault() +
                RequiredValue(row, "P Wins") * options.DamagesMax * options.DamagesMultiplier;

            double pTrialPathCost = options.CostsMultiplier *
                (options.PFilingCost + options.PerPartyCostsLeadingUpToBargainingRound + options.PTrialCosts);
            double dTrialPathCost = options.CostsMultiplier *
                (options.DAnswerCost + options.PerPartyCostsLeadingUpToBargainingRound + options.DTrialCosts);
            double feeTransfer = options.LoserPaysMultiple *
                (pTrialPathCost * RequiredValue(row, "P Wins") -
                 dTrialPathCost * RequiredValue(row, "P Loses"));
            if (options.LoserPaysAfterAbandonment)
                feeTransfer += options.LoserPaysMultiple * options.CostsMultiplier *
                    ((options.PFilingCost + options.PerPartyCostsLeadingUpToBargainingRound) * dDefaults -
                     (options.DAnswerCost + options.PerPartyCostsLeadingUpToBargainingRound) * pAbandons);
            if (options.LoserPaysAfterNonAnswer)
                feeTransfer += options.LoserPaysMultiple * options.CostsMultiplier * options.PFilingCost *
                    (1.0 - options.PFilingCost_PortionSavedIfDDoesntAnswer) * RequiredValue(row, "No Answer");

            row["Reaches Bargaining"] = Format(reachesBargaining);
            row["Settlement Unconditional"] = Format(settles);
            row["Settlement Conditional on Reaching Bargaining"] =
                FormatRatio(settles, reachesBargaining);
            row["Real Litigation Costs"] = row["Expenditures"];
            row["Liability Transfer to Plaintiff"] = Format(liabilityTransfer);
            row["Fee-Shifting Transfer to Plaintiff"] = Format(feeTransfer);
            row["Total Net Transfer to Plaintiff"] = Format(liabilityTransfer + feeTransfer);

            double probabilityTrulyLiable = RequiredValue(row, "Probability Truly Liable");
            double meritoriousPlaintiffRecoveryShortfall = AccuracyValue(
                trulyLiableSource,
                PlaintiffRecoveryShortfallColumn,
                LegacyFalseNegativeColumn);
            double nonliableDefendantNetBurden = AccuracyValue(
                trulyNotLiableSource,
                DefendantExcessBurdenColumn,
                LegacyFalsePositiveColumn);
            double liableDefendantExcessNetBurden = AccuracyValue(
                trulyLiableSource,
                DefendantExcessBurdenColumn,
                LegacyFalsePositiveColumn);
            double populationPlaintiffRecoveryShortfall =
                RequiredValue(row, PlaintiffRecoveryShortfallColumn);
            double populationDefendantExcessBurden =
                RequiredValue(row, DefendantExcessBurdenColumn);
            double netOutcomeFidelityLoss =
                populationPlaintiffRecoveryShortfall + populationDefendantExcessBurden;

            row[MeritoriousPlaintiffRecoveryShortfallColumn] =
                Format(meritoriousPlaintiffRecoveryShortfall);
            row[NonliableDefendantNetBurdenColumn] = Format(nonliableDefendantNetBurden);
            row[LiableDefendantExcessNetBurdenColumn] =
                Format(liableDefendantExcessNetBurden);
            row[NetOutcomeFidelityLossColumn] = Format(netOutcomeFidelityLoss);

            RequireApproximately(
                row["OptionSetName"],
                "truth-weighted plaintiff-shortfall identity",
                populationPlaintiffRecoveryShortfall,
                probabilityTrulyLiable * meritoriousPlaintiffRecoveryShortfall);
            RequireApproximately(
                row["OptionSetName"],
                "truth-weighted defendant-burden identity",
                populationDefendantExcessBurden,
                (1.0 - probabilityTrulyLiable) * nonliableDefendantNetBurden +
                probabilityTrulyLiable * liableDefendantExcessNetBurden);
            RequireApproximately(
                row["OptionSetName"],
                "net-outcome-fidelity identity",
                netOutcomeFidelityLoss,
                populationPlaintiffRecoveryShortfall + populationDefendantExcessBurden);
            return row;
        }

        private static double AccuracyValue(
            IReadOnlyDictionary<string, string> row,
            string currentName,
            string legacyName)
        {
            double? currentValue = OptionalValue(row, currentName);
            if (currentValue.HasValue)
                return currentValue.Value;
            return RequiredValue(row, legacyName);
        }

        private static (double pAbandons, double dDefaults) AllocateMutualGiveUpIfNeeded(
            double reachesBargaining,
            double settles,
            double trial,
            double pAbandons,
            double dDefaults,
            double mutualGiveUp,
            string identity)
        {
            double reportedPathTotal = settles + pAbandons + dDefaults + trial;
            if (Math.Abs(reachesBargaining - reportedPathTotal) <= AccountingTolerance)
                return (pAbandons, dDefaults);
            if (Math.Abs(reachesBargaining - reportedPathTotal - mutualGiveUp) <= AccountingTolerance)
                return (
                    pAbandons + 0.5 * mutualGiveUp,
                    dDefaults + 0.5 * mutualGiveUp);
            throw new InvalidDataException(
                $"Row '{identity}' cannot reconcile the bargaining path before or after the " +
                "50/50 mutual-give-up allocation.");
        }

        private static string RenameLegacyAccuracyHeader(string header) => header switch
        {
            LegacyFalsePositiveColumn => DefendantExcessBurdenColumn,
            LegacyFalseNegativeColumn => PlaintiffRecoveryShortfallColumn,
            _ => header,
        };

        private static void RenameLegacyAccuracyValue(
            IDictionary<string, string> row,
            string legacyName,
            string currentName)
        {
            if (!row.ContainsKey(currentName) && row.TryGetValue(legacyName, out string value))
                row[currentName] = value;
            row.Remove(legacyName);
        }

        private static void ValidateNumericalAccounting(
            IReadOnlyDictionary<string, string> row,
            LitigGameOptions options)
        {
            string identity = row["OptionSetName"];
            double pFiles = RequiredValue(row, "P Files");
            double dAnswers = RequiredValue(row, "D Answers");
            double trial = RequiredValue(row, "Trial");
            double noSuit = RequiredValue(row, "No Suit");
            double noAnswer = RequiredValue(row, "No Answer");
            double settles = RequiredValue(row, "Settles");
            double pAbandons = RequiredValue(row, "P Abandons");
            double dDefaults = RequiredValue(row, "D Defaults");
            double pLoses = RequiredValue(row, "P Loses");
            double pWins = RequiredValue(row, "P Wins");
            RequiredValue(row, DefendantExcessBurdenColumn);
            RequiredValue(row, PlaintiffRecoveryShortfallColumn);

            RequireApproximately(identity, "filing identity", pFiles, 1.0 - noSuit);
            RequireApproximately(identity, "answering identity", dAnswers, pFiles - noAnswer);
            RequireApproximately(identity, "trial identity", trial, pLoses + pWins);
            RequireApproximately(
                identity,
                "bargaining-path identity",
                dAnswers,
                settles + pAbandons + dDefaults + trial);
            RequireApproximately(
                identity,
                "terminal-disposition identity",
                1.0,
                noSuit + noAnswer + settles + pAbandons + dDefaults + pLoses + pWins);

            if (OptionalValue(row, "Total Wealth") is double totalWealth &&
                OptionalValue(row, "Expenditures") is double expenditures)
            {
                RequireApproximately(
                    identity,
                    "wealth/expenditure identity",
                    options.PInitialWealth + options.DInitialWealth - expenditures,
                    totalWealth);
            }
            if (OptionalValue(row, "P Wealth") is double pWealth &&
                OptionalValue(row, "D Wealth") is double dWealth)
            {
                double pExpenses = options.CostsMultiplier *
                    (options.PFilingCost * (pFiles - noAnswer * options.PFilingCost_PortionSavedIfDDoesntAnswer) +
                     options.PerPartyCostsLeadingUpToBargainingRound * dAnswers + options.PTrialCosts * trial);
                double dExpenses = options.CostsMultiplier *
                    ((options.DAnswerCost + options.PerPartyCostsLeadingUpToBargainingRound) * dAnswers +
                     options.DTrialCosts * trial);
                double transfer = RequiredValue(row, "Total Net Transfer to Plaintiff");
                RequireApproximately(identity, "plaintiff wealth/transfer identity",
                    options.PInitialWealth + transfer - pExpenses, pWealth);
                RequireApproximately(identity, "defendant wealth/transfer identity",
                    options.DInitialWealth - transfer - dExpenses, dWealth);
            }
        }

        private static void RequireApproximately(
            string optionSetName,
            string description,
            double expected,
            double actual)
        {
            if (Math.Abs(expected - actual) > AccountingTolerance)
                throw new InvalidDataException(
                    $"Option set '{optionSetName}' fails the {description}: expected {Format(expected)}, " +
                    $"found {Format(actual)} (tolerance {AccountingTolerance}).");
        }

        private static int WriteSpecificationComparisons(
            string path,
            IReadOnlyList<Dictionary<string, string>> numericalRows,
            IReadOnlyList<string> outcomes)
        {
            var groups = numericalRows
                .GroupBy(
                    row => $"{row["Costs Multiplier"]}|{row["Fee Regime"]}|{row["Number of Offers"]}",
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            var outputRows = new List<Dictionary<string, string>>();
            foreach (var group in groups)
            {
                Dictionary<string, string> baseline = group.Single(row =>
                    row["Specification"] == LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel);
                foreach (Dictionary<string, string> comparison in group
                    .Where(row => row["Specification"] !=
                        LitigGameCorrelatedSignalsArticleLauncher.FocusedBaselineLabel)
                    .OrderBy(row => row["Specification"], StringComparer.Ordinal))
                {
                    var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Costs Multiplier"] = baseline["Costs Multiplier"],
                        ["Fee Regime"] = baseline["Fee Regime"],
                        ["Number of Offers"] = baseline["Number of Offers"],
                        ["Baseline Specification"] = baseline["Specification"],
                        ["Comparison Specification"] = comparison["Specification"],
                        ["Baseline OptionSetName"] = baseline["OptionSetName"],
                        ["Comparison OptionSetName"] = comparison["OptionSetName"],
                    };
                    AddPairedOutcomes(output, baseline, comparison, outcomes, "Baseline", "Comparison");
                    outputRows.Add(output);
                }
            }

            string[] headers = ComparisonHeaders(
                new[]
                {
                    "Costs Multiplier",
                    "Fee Regime",
                    "Number of Offers",
                    "Baseline Specification",
                    "Comparison Specification",
                    "Baseline OptionSetName",
                    "Comparison OptionSetName",
                },
                outcomes,
                "Baseline",
                "Comparison",
                "Difference (Comparison - Baseline)");
            WriteRows(path, headers, outputRows);
            return outputRows.Count;
        }

        private static int WriteFeeRegimeComparisons(
            string path,
            IReadOnlyList<Dictionary<string, string>> numericalRows,
            IReadOnlyList<string> outcomes)
        {
            var groups = numericalRows
                .GroupBy(
                    row => $"{row["Specification"]}|{row["Costs Multiplier"]}|{row["Number of Offers"]}",
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            var outputRows = new List<Dictionary<string, string>>();
            foreach (var group in groups)
            {
                Dictionary<string, string> american = group.Single(row => row["Fee Regime"] == "American");
                Dictionary<string, string> british = group.Single(row => row["Fee Regime"] == "British");
                var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Specification"] = american["Specification"],
                    ["Costs Multiplier"] = american["Costs Multiplier"],
                    ["Number of Offers"] = american["Number of Offers"],
                    ["American OptionSetName"] = american["OptionSetName"],
                    ["British OptionSetName"] = british["OptionSetName"],
                };
                AddPairedOutcomes(output, american, british, outcomes, "American", "British");
                outputRows.Add(output);
            }

            string[] headers = ComparisonHeaders(
                new[]
                {
                    "Specification",
                    "Costs Multiplier",
                    "Number of Offers",
                    "American OptionSetName",
                    "British OptionSetName",
                },
                outcomes,
                "American",
                "British",
                "Difference (British - American)");
            WriteRows(path, headers, outputRows);
            return outputRows.Count;
        }

        private static void AddPairedOutcomes(
            IDictionary<string, string> output,
            IReadOnlyDictionary<string, string> first,
            IReadOnlyDictionary<string, string> second,
            IEnumerable<string> outcomes,
            string firstLabel,
            string secondLabel)
        {
            foreach (string outcome in outcomes)
            {
                output[$"{firstLabel} {outcome}"] = first.TryGetValue(outcome, out string firstText)
                    ? firstText
                    : string.Empty;
                output[$"{secondLabel} {outcome}"] = second.TryGetValue(outcome, out string secondText)
                    ? secondText
                    : string.Empty;
                double? firstValue = OptionalValue(first, outcome);
                double? secondValue = OptionalValue(second, outcome);
                output[$"Difference ({secondLabel} - {firstLabel}) {outcome}"] =
                    firstValue.HasValue && secondValue.HasValue
                        ? Format(secondValue.Value - firstValue.Value)
                        : string.Empty;
            }
        }

        private static string[] ComparisonHeaders(
            IEnumerable<string> metadata,
            IEnumerable<string> outcomes,
            string firstLabel,
            string secondLabel,
            string differenceLabel) =>
            metadata.Concat(outcomes.SelectMany(outcome => new[]
            {
                $"{firstLabel} {outcome}",
                $"{secondLabel} {outcome}",
                $"{differenceLabel} {outcome}",
            })).ToArray();

        private static int WriteSignalStrategies(
            string path,
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            IReadOnlyList<Dictionary<string, string>> sourceRows)
        {
            string[] offerColumns = Enumerable.Range(1, 10)
                .SelectMany(action => new[]
                {
                    $"P Offer 1 Action {action}",
                    $"D Offer 1 Action {action}",
                })
                .ToArray();
            string[] metadata = new[] { "Equilibrium Type", "OptionSetName" }
                .Concat(launcher.DefaultVariableValues.Select(setting => setting.Item1))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] strategyColumns =
            {
                "Signal Owner",
                "Signal Index",
                "Signal Probability",
                "P Filing Probability",
                "D Answering Probability Unconditional",
                "D Answering Probability Conditional on Filing",
                "Reaches Bargaining Probability",
                "P Offer Mean",
                "D Offer Mean",
                "Offer-Overlap Acceptance Conditional on Reaching Bargaining",
                "P Abandonment Probability Unconditional",
                "P Abandonment Probability Conditional on Reaching Bargaining",
                "D Default Probability Unconditional",
                "D Default Probability Conditional on Reaching Bargaining",
                MutualGiveUpBeforeAllocationColumn,
                "Settlement Probability Unconditional",
                "Settlement Probability Conditional on Reaching Bargaining",
                "Trial Probability Unconditional",
                "Trial Probability Conditional on Reaching Bargaining",
                "P Trial Win Probability Unconditional",
                "P Trial Loss Probability Unconditional",
            };

            var outputRows = new List<Dictionary<string, string>>();
            foreach (Dictionary<string, string> source in sourceRows
                .OrderBy(row => row["OptionSetName"], StringComparer.Ordinal)
                .ThenBy(row => row["Filter"], StringComparer.Ordinal))
            {
                string filter = source["Filter"];
                bool plaintiffSignal = filter.StartsWith("PLiabilitySignal", StringComparison.Ordinal);
                string signalText = filter.Substring("PLiabilitySignal".Length);
                double pFiles = RequiredValue(source, "P Files");
                double dAnswers = RequiredValue(source, "D Answers");
                double settles = RequiredValue(source, "Settles");
                double mutualGiveUp = RequiredValue(source, MutualGiveUpBeforeAllocationColumn);
                double trial = RequiredValue(source, "Trial");
                (double pAbandons, double dDefaults) = AllocateMutualGiveUpIfNeeded(
                    dAnswers,
                    settles,
                    trial,
                    RequiredValue(source, "P Abandons"),
                    RequiredValue(source, "D Defaults"),
                    mutualGiveUp,
                    $"{source["OptionSetName"]}|{filter}");

                RequireApproximately(
                    $"{source["OptionSetName"]}|{filter}",
                    "signal-conditional bargaining-path identity",
                    dAnswers,
                    settles + pAbandons + dDefaults + trial);

                var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string header in metadata)
                    output[header] = source.TryGetValue(header, out string value) ? value : string.Empty;
                output["Signal Owner"] = plaintiffSignal ? "Plaintiff" : "Defendant";
                output["Signal Index"] = signalText;
                output["Signal Probability"] = source["Signal Probability"];
                output["P Filing Probability"] = Format(pFiles);
                output["D Answering Probability Unconditional"] = Format(dAnswers);
                output["D Answering Probability Conditional on Filing"] = FormatRatio(dAnswers, pFiles);
                output["Reaches Bargaining Probability"] = Format(dAnswers);
                output["P Offer Mean"] = source["P Offer"];
                output["D Offer Mean"] = source["D Offer"];
                output["Offer-Overlap Acceptance Conditional on Reaching Bargaining"] =
                    FormatRatio(settles, dAnswers);
                output["P Abandonment Probability Unconditional"] = Format(pAbandons);
                output["P Abandonment Probability Conditional on Reaching Bargaining"] =
                    FormatRatio(pAbandons, dAnswers);
                output["D Default Probability Unconditional"] = Format(dDefaults);
                output["D Default Probability Conditional on Reaching Bargaining"] =
                    FormatRatio(dDefaults, dAnswers);
                output[MutualGiveUpBeforeAllocationColumn] = Format(mutualGiveUp);
                output["Settlement Probability Unconditional"] = Format(settles);
                output["Settlement Probability Conditional on Reaching Bargaining"] =
                    FormatRatio(settles, dAnswers);
                output["Trial Probability Unconditional"] = Format(trial);
                output["Trial Probability Conditional on Reaching Bargaining"] =
                    FormatRatio(trial, dAnswers);
                output["P Trial Win Probability Unconditional"] = source["P Wins"];
                output["P Trial Loss Probability Unconditional"] = source["P Loses"];
                foreach (string offerColumn in offerColumns)
                    output[offerColumn] = source[offerColumn];
                outputRows.Add(output);
            }

            WriteRows(path, metadata.Concat(strategyColumns).Concat(offerColumns).ToArray(), outputRows);
            return outputRows.Count;
        }

        private static IEnumerable<string> SignalRequiredColumns() =>
            new[]
            {
                "Signal Probability",
                "P Files",
                "D Answers",
                "P Offer",
                "D Offer",
                "Settles",
                "P Abandons",
                "D Defaults",
                MutualGiveUpBeforeAllocationColumn,
                "Trial",
                "P Loses",
                "P Wins",
            }.Concat(Enumerable.Range(1, 10).SelectMany(action => new[]
            {
                $"P Offer 1 Action {action}",
                $"D Offer 1 Action {action}",
            }));

        private static void ValidateRows(
            CsvTable table,
            IReadOnlyList<GameOptions> optionSets,
            IReadOnlyList<string> expectedFilters,
            IEnumerable<string> settingHeaders)
        {
            RequireColumns(table.Headers, new[]
            {
                "Equilibrium Type",
                "Filter",
                "OptionSetName",
            }.Concat(settingHeaders));
            int expectedRows = optionSets.Count * expectedFilters.Count;
            if (table.Rows.Count != expectedRows)
                throw new InvalidDataException(
                    $"Report contains {table.Rows.Count} rows; expected exactly {expectedRows}.");
            if (table.Rows.Any(row => row["Equilibrium Type"] != "Only Eq"))
                throw new InvalidDataException("CS004 reporting requires one 'Only Eq' result per option set.");

            var duplicateRows = table.Rows
                .GroupBy(row => $"{row["OptionSetName"]}|{row["Filter"]}", StringComparer.Ordinal)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicateRows.Count > 0)
                throw new InvalidDataException(
                    "Report contains duplicate option/filter rows: " +
                    string.Join(", ", duplicateRows.Take(25)));

            Dictionary<string, Dictionary<string, string>> rowsByIdentity = table.Rows.ToDictionary(
                row => $"{row["OptionSetName"]}|{row["Filter"]}",
                StringComparer.Ordinal);
            foreach (GameOptions optionSet in optionSets)
            {
                foreach (string filter in expectedFilters)
                {
                    string identity = $"{optionSet.Name}|{filter}";
                    if (!rowsByIdentity.TryGetValue(identity, out Dictionary<string, string> row))
                        throw new InvalidDataException($"Report is missing '{identity}'.");
                    foreach (string settingHeader in settingHeaders)
                    {
                        string expected = (Convert.ToString(
                            optionSet.VariableSettings[settingHeader],
                            CultureInfo.InvariantCulture) ?? string.Empty)
                            .Replace(",", "-");
                        if (!string.Equals(row[settingHeader], expected, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"Option set '{optionSet.Name}' reports {settingHeader}=" +
                                $"'{row[settingHeader]}', expected '{expected}'.");
                    }
                }
            }
        }

        private static double RequiredValue(IReadOnlyDictionary<string, string> row, string column) =>
            OptionalValue(row, column) ?? throw new InvalidDataException(
                $"Column '{column}' contains a missing or nonnumeric value.");

        private static bool IsBlank(IReadOnlyDictionary<string, string> row, string column) =>
            !row.TryGetValue(column, out string text) || string.IsNullOrWhiteSpace(text);

        private static double? OptionalValue(IReadOnlyDictionary<string, string> row, string column)
        {
            if (!row.TryGetValue(column, out string text) || string.IsNullOrWhiteSpace(text))
                return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;
        }

        private static string FormatRatio(double numerator, double denominator) =>
            denominator > 0.0 ? Format(numerator / denominator) : string.Empty;

        private static string Format(double value) =>
            value.ToString("0.################", CultureInfo.InvariantCulture);

        private static void RequireColumns(IEnumerable<string> headers, IEnumerable<string> required)
        {
            HashSet<string> actual = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] missing = required.Where(column => !actual.Contains(column)).Distinct().ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("Report is missing columns: " + string.Join(", ", missing));
        }

        private static CsvTable ReadRows(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A required CS004 report was not found.", path);
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
            });
            csv.Read();
            csv.ReadHeader();
            string[] headers = csv.HeaderRecord;
            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string header in headers)
                    row[header] = csv.GetField(header) ?? string.Empty;
                rows.Add(row);
            }
            return new CsvTable(headers, rows);
        }

        private static void WriteRows(
            string path,
            IReadOnlyList<string> headers,
            IEnumerable<IReadOnlyDictionary<string, string>> rows)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using var writer = new StreamWriter(path);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            foreach (string header in headers)
                csv.WriteField(header);
            csv.NextRecord();
            foreach (IReadOnlyDictionary<string, string> row in rows)
            {
                foreach (string header in headers)
                    csv.WriteField(row.TryGetValue(header, out string value) ? value : string.Empty);
                csv.NextRecord();
            }
        }

        private sealed record CsvTable(
            string[] Headers,
            List<Dictionary<string, string>> Rows);
    }
}
