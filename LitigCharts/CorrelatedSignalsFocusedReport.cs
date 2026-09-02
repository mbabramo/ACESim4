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
    /// Validates and reports the focused CS003 continuous-merits production design.
    /// Generated files are deliberately separate from simulation inputs and are not committed.
    /// </summary>
    public static class CorrelatedSignalsFocusedReport
    {
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
            if (launcher.RunPlan !=
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits)
                throw new ArgumentException("Focused reporting requires the CS003 launcher.", nameof(launcher));

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
                "D Answers",
                "Settles",
                "No Answer",
                "P Abandons",
                "D Defaults",
                "P Loses",
                "P Wins",
                "Value If Settled",
                "Expenditures",
            });

            List<Dictionary<string, string>> numericalRows = numericalSource.Rows
                .Where(row => row["Filter"] == "All")
                .OrderBy(row => row["OptionSetName"], StringComparer.Ordinal)
                .Select(row => AddDerivedNumericalValues(
                    row,
                    optionsByName[row["OptionSetName"]]))
                .ToList();
            string[] numericalHeaders = numericalSource.Headers
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
                throw new InvalidDataException("CS003 numerical results contain no outcome measures.");

            int specificationComparisonCount = WriteSpecificationComparisons(
                specificationComparisonsCsvPath,
                numericalRows,
                outcomeMeasures);
            int feeComparisonCount = WriteFeeRegimeComparisons(
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

            if (numericalRows.Count != LitigGameCorrelatedSignalsArticleLauncher.FocusedOptionSetCount)
                throw new InvalidDataException(
                    $"CS003 contains {numericalRows.Count} numerical result rows; expected " +
                    $"{LitigGameCorrelatedSignalsArticleLauncher.FocusedOptionSetCount}.");
            if (specificationComparisonCount !=
                LitigGameCorrelatedSignalsArticleLauncher.FocusedSpecificationComparisonCount)
                throw new InvalidDataException(
                    $"CS003 contains {specificationComparisonCount} specification comparisons; expected " +
                    $"{LitigGameCorrelatedSignalsArticleLauncher.FocusedSpecificationComparisonCount}.");
            if (feeComparisonCount !=
                LitigGameCorrelatedSignalsArticleLauncher.FocusedFeeRegimeComparisonCount)
                throw new InvalidDataException(
                    $"CS003 contains {feeComparisonCount} fee-regime comparisons; expected " +
                    $"{LitigGameCorrelatedSignalsArticleLauncher.FocusedFeeRegimeComparisonCount}.");
            int expectedSignalStrategies =
                LitigGameCorrelatedSignalsArticleLauncher.FocusedOptionSetCount * SignalFilters.Count;
            if (signalStrategyCount != expectedSignalStrategies)
                throw new InvalidDataException(
                    $"CS003 contains {signalStrategyCount} signal-strategy rows; expected " +
                    $"{expectedSignalStrategies}.");

            return new ValidationSummary(
                numericalRows.Count,
                specificationComparisonCount,
                feeComparisonCount,
                signalStrategyCount,
                outcomeMeasures.Length);
        }

        private static Dictionary<string, string> AddDerivedNumericalValues(
            Dictionary<string, string> source,
            LitigGameOptions options)
        {
            var row = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
            double reachesBargaining = RequiredValue(row, "D Answers");
            double settles = RequiredValue(row, "Settles");
            double liabilityTransfer =
                RequiredValue(row, "No Answer") * options.DamagesMax * options.DamagesMultiplier +
                RequiredValue(row, "D Defaults") * options.DamagesMax * options.DamagesMultiplier +
                settles * OptionalValue(row, "Value If Settled").GetValueOrDefault() +
                RequiredValue(row, "P Wins") * options.DamagesMax * options.DamagesMultiplier;

            double pTrialPathCost = options.CostsMultiplier *
                (options.PFilingCost + options.PerPartyCostsLeadingUpToBargainingRound + options.PTrialCosts);
            double dTrialPathCost = options.CostsMultiplier *
                (options.DAnswerCost + options.PerPartyCostsLeadingUpToBargainingRound + options.DTrialCosts);
            double feeTransfer = options.LoserPaysMultiple *
                (pTrialPathCost * RequiredValue(row, "P Wins") -
                 dTrialPathCost * RequiredValue(row, "P Loses"));

            row["Reaches Bargaining"] = Format(reachesBargaining);
            row["Settlement Unconditional"] = Format(settles);
            row["Settlement Conditional on Reaching Bargaining"] =
                FormatRatio(settles, reachesBargaining);
            row["Real Litigation Costs"] = row["Expenditures"];
            row["Liability Transfer to Plaintiff"] = Format(liabilityTransfer);
            row["Fee-Shifting Transfer to Plaintiff"] = Format(feeTransfer);
            row["Total Net Transfer to Plaintiff"] = Format(liabilityTransfer + feeTransfer);
            return row;
        }

        private static int WriteSpecificationComparisons(
            string path,
            IReadOnlyList<Dictionary<string, string>> numericalRows,
            IReadOnlyList<string> outcomes)
        {
            var groups = numericalRows
                .GroupBy(row => $"{row["Costs Multiplier"]}|{row["Fee Regime"]}", StringComparer.Ordinal)
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
                .GroupBy(row => $"{row["Specification"]}|{row["Costs Multiplier"]}", StringComparer.Ordinal)
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
                double pAbandons = RequiredValue(source, "P Abandons");
                double dDefaults = RequiredValue(source, "D Defaults");
                double trial = RequiredValue(source, "Trial");

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
                throw new InvalidDataException("CS003 reporting requires one 'Only Eq' result per option set.");

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
                        string expected = Convert.ToString(
                            optionSet.VariableSettings[settingHeader],
                            CultureInfo.InvariantCulture);
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
                throw new FileNotFoundException("A required CS003 report was not found.", path);
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
