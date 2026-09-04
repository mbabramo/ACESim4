using ACESim;
using ACESimBase;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Settings;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LitigCharts
{
    /// <summary>
    /// Builds one outcome row per distinct recovered equilibrium and a dispersion summary
    /// for each fee regime in the CS004ME production plan.
    /// </summary>
    public static class CorrelatedSignalsMultipleEquilibriaReport
    {
        private const double ValidationTolerance = 1E-4;

        public sealed record ValidationSummary(
            int OptionSetCount,
            int EquilibriumCount,
            int RangeRowCount);

        private sealed record EquilibriumOutcome(
            Dictionary<string, string> Fields,
            Dictionary<string, double> Metrics);

        private sealed record EquilibriumRecovery(
            int RequestedPriors,
            int AttemptedSolves,
            int InexactAttempts,
            int ExactAttempts,
            int VerifiedRecoveries,
            int DistinctProfiles,
            int Equilibrium,
            int RecoveryCount,
            double RecoveryShare,
            string VerificationStatus,
            string DistinctnessCriterion);

        private static readonly string[] OutcomeHeaders =
        {
            "Specification",
            "Fee Regime",
            "Offers",
            "Party Signals",
            "Court Signals",
            "Requested Priors",
            "Attempted Solves",
            "Inexact Attempts",
            "Exact Attempts",
            "Verified Recoveries",
            "Distinct Reported Strategy Profiles",
            "Equilibrium",
            "Equilibrium Recovery Count",
            "Recovery Share of Verified Recoveries",
            "Verification Status",
            "Distinctness Criterion",
            "Exploitability",
            "Calculation Seconds",
            "P Files",
            "D Answers",
            "P Offer",
            "D Offer",
            "Settles",
            "P Abandons (Mutual Give-Up Allocated)",
            "D Defaults (Mutual Give-Up Allocated)",
            CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn,
            "Trial",
            "P Loses",
            "P Wins",
            "Expenditures",
            CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn,
            CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn,
            CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn,
            CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn,
            CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn,
            CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn,
            "Total Wealth",
            "Source File",
        };

        private static readonly string[] RangeMetrics =
        {
            "P Files",
            "D Answers",
            "P Offer",
            "D Offer",
            "Settles",
            "P Abandons (Mutual Give-Up Allocated)",
            "D Defaults (Mutual Give-Up Allocated)",
            CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn,
            "Trial",
            "P Loses",
            "P Wins",
            "Expenditures",
            CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn,
            CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn,
            CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn,
            CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn,
            CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn,
            CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn,
            "Total Wealth",
            "Exploitability",
        };

        public static ValidationSummary BuildAndValidate(
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            string equilibriumOutcomesCsvPath,
            string equilibriumRangesCsvPath)
        {
            if (launcher == null)
                throw new ArgumentNullException(nameof(launcher));
            if (launcher.RunPlan !=
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness)
                throw new ArgumentException("Multiple-equilibria reporting requires the CS004ME launcher.", nameof(launcher));

            List<LitigGameOptions> options = launcher.GetOptionsSets()
                .Cast<LitigGameOptions>()
                .OrderBy(option => Setting(option, "Fee Regime"), StringComparer.Ordinal)
                .ToList();
            if (options.Count != LitigGameCorrelatedSignalsArticleLauncher.MultipleEquilibriaOptionSetCount)
                throw new InvalidDataException(
                    $"CS004ME contains {options.Count} option sets; expected " +
                    $"{LitigGameCorrelatedSignalsArticleLauncher.MultipleEquilibriaOptionSetCount}.");

            var outcomes = new List<EquilibriumOutcome>();
            foreach (LitigGameOptions option in options)
            {
                string equilibriaPath = launcher.GetReportFullPath(option.Name, "-equ.csv");
                int distinctEquilibria = CountNonemptyLines(equilibriaPath);
                if (distinctEquilibria < 1)
                    throw new InvalidDataException($"No equilibria were recorded in '{equilibriaPath}'.");
                string recoveryPath = launcher.GetReportFullPath(
                    option.Name,
                    $"-{SequenceForm.EquilibriumRecoveryReportSuffix}.csv");
                IReadOnlyDictionary<int, EquilibriumRecovery> recoveries =
                    ReadRecoveryCatalog(option, recoveryPath, distinctEquilibria);

                for (int equilibrium = 1; equilibrium <= distinctEquilibria; equilibrium++)
                {
                    string reportPath = launcher.GetReportFullPath(option.Name, $"-Eq{equilibrium}.csv");
                    outcomes.Add(ReadOutcome(
                        option,
                        recoveries[equilibrium],
                        reportPath));
                }
            }

            WriteRows(
                equilibriumOutcomesCsvPath,
                OutcomeHeaders,
                outcomes.Select(outcome => outcome.Fields));
            int rangeRows = WriteRanges(equilibriumRangesCsvPath, outcomes);
            if (rangeRows != options.Count)
                throw new InvalidDataException(
                    $"CS004ME produced {rangeRows} dispersion rows; expected {options.Count}.");

            return new ValidationSummary(options.Count, outcomes.Count, rangeRows);
        }

        private static EquilibriumOutcome ReadOutcome(
            LitigGameOptions option,
            EquilibriumRecovery recovery,
            string reportPath)
        {
            Dictionary<string, Dictionary<string, string>> rows = ReadRowsByFilter(reportPath);
            Dictionary<string, string> all = RequiredFilter(rows, "All", reportPath);
            Dictionary<string, string> liable = RequiredFilter(rows, "Truly Liable", reportPath);
            Dictionary<string, string> nonliable = RequiredFilter(rows, "Truly Not Liable", reportPath);

            double probabilityLiable = double.Parse(
                Setting(option, "Probability Truly Liable"),
                CultureInfo.InvariantCulture);
            double probabilityNonliable = 1.0 - probabilityLiable;
            double mutualGiveUp = RequiredValue(all, "BothReadyToGiveUp", reportPath);
            double pAbandons = RequiredValue(all, "PAbandonsBR1", reportPath) + 0.5 * mutualGiveUp;
            double dDefaults = RequiredValue(all, "DDefaultsBR1", reportPath) + 0.5 * mutualGiveUp;
            double meritoriousPlaintiff = RequiredValue(liable, "False-", reportPath);
            double nonliableDefendant = RequiredValue(nonliable, "False+", reportPath);
            double liableDefendant = RequiredValue(liable, "False+", reportPath);
            double plaintiffAggregate = RequiredValue(all, "False-", reportPath);
            double defendantAggregate = RequiredValue(all, "False+", reportPath);
            double fidelityLoss = plaintiffAggregate + defendantAggregate;

            RequireApproximately(
                reportPath,
                "plaintiff truth weighting",
                plaintiffAggregate,
                probabilityLiable * meritoriousPlaintiff);
            RequireApproximately(
                reportPath,
                "defendant truth weighting",
                defendantAggregate,
                probabilityNonliable * nonliableDefendant + probabilityLiable * liableDefendant);
            RequireApproximately(
                reportPath,
                "trial outcomes",
                RequiredValue(all, "Trial", reportPath),
                RequiredValue(all, "P Loses", reportPath) + RequiredValue(all, "P Wins", reportPath));
            RequireApproximately(
                reportPath,
                "terminal disposition",
                1.0,
                RequiredValue(all, "PDoesntFile", reportPath) +
                RequiredValue(all, "DDoesntAnswer", reportPath) +
                RequiredValue(all, "SettlesBR1", reportPath) +
                pAbandons + dDefaults +
                RequiredValue(all, "P Loses", reportPath) +
                RequiredValue(all, "P Wins", reportPath));

            var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Exploitability"] = RequiredValue(all, "Exploit", reportPath),
                ["P Files"] = RequiredValue(all, "PFiles", reportPath),
                ["D Answers"] = RequiredValue(all, "DAnswers", reportPath),
                ["P Offer"] = RequiredValue(all, "POffer1", reportPath),
                ["D Offer"] = RequiredValue(all, "DOffer1", reportPath),
                ["Settles"] = RequiredValue(all, "SettlesBR1", reportPath),
                ["P Abandons (Mutual Give-Up Allocated)"] = pAbandons,
                ["D Defaults (Mutual Give-Up Allocated)"] = dDefaults,
                [CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn] = mutualGiveUp,
                ["Trial"] = RequiredValue(all, "Trial", reportPath),
                ["P Loses"] = RequiredValue(all, "P Loses", reportPath),
                ["P Wins"] = RequiredValue(all, "P Wins", reportPath),
                ["Expenditures"] = RequiredValue(all, "TotExpense", reportPath),
                [CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn] = meritoriousPlaintiff,
                [CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn] = nonliableDefendant,
                [CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn] = liableDefendant,
                [CorrelatedSignalsFocusedReport.DefendantExcessBurdenColumn] = defendantAggregate,
                [CorrelatedSignalsFocusedReport.PlaintiffRecoveryShortfallColumn] = plaintiffAggregate,
                [CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn] = fidelityLoss,
                ["Total Wealth"] = RequiredValue(all, "TotWealth", reportPath),
            };

            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Specification"] = Setting(option, "Specification"),
                ["Fee Regime"] = Setting(option, "Fee Regime"),
                ["Offers"] = option.NumOffers.ToString(CultureInfo.InvariantCulture),
                ["Party Signals"] = option.NumLiabilitySignals.ToString(CultureInfo.InvariantCulture),
                ["Court Signals"] = option.NumCourtLiabilitySignals.ToString(CultureInfo.InvariantCulture),
                ["Requested Priors"] = recovery.RequestedPriors.ToString(CultureInfo.InvariantCulture),
                ["Attempted Solves"] = recovery.AttemptedSolves.ToString(CultureInfo.InvariantCulture),
                ["Inexact Attempts"] = recovery.InexactAttempts.ToString(CultureInfo.InvariantCulture),
                ["Exact Attempts"] = recovery.ExactAttempts.ToString(CultureInfo.InvariantCulture),
                ["Verified Recoveries"] = recovery.VerifiedRecoveries.ToString(CultureInfo.InvariantCulture),
                ["Distinct Reported Strategy Profiles"] = recovery.DistinctProfiles.ToString(CultureInfo.InvariantCulture),
                ["Equilibrium"] = recovery.Equilibrium.ToString(CultureInfo.InvariantCulture),
                ["Equilibrium Recovery Count"] = recovery.RecoveryCount.ToString(CultureInfo.InvariantCulture),
                ["Recovery Share of Verified Recoveries"] = Format(recovery.RecoveryShare),
                ["Verification Status"] = recovery.VerificationStatus,
                ["Distinctness Criterion"] = recovery.DistinctnessCriterion,
                ["Calculation Seconds"] = Format(RequiredValue(all, "Seconds", reportPath)),
                ["Source File"] = Path.GetFileName(reportPath),
            };
            foreach ((string name, double value) in metrics)
                fields[name] = Format(value);
            return new EquilibriumOutcome(fields, metrics);
        }

        private static int WriteRanges(
            string path,
            IReadOnlyCollection<EquilibriumOutcome> outcomes)
        {
            string[] headers = new[]
            {
                "Fee Regime",
                "Requested Priors",
                "Attempted Solves",
                "Inexact Attempts",
                "Exact Attempts",
                "Verified Recoveries",
                "Distinct Reported Strategy Profiles",
            }.Concat(RangeMetrics.SelectMany(metric => new[]
            {
                $"Minimum {metric}",
                $"Mean {metric}",
                $"Maximum {metric}",
                $"Range {metric}",
                $"Standard Deviation {metric}",
                $"Coefficient of Variation {metric}",
            })).ToArray();

            List<Dictionary<string, string>> rows = outcomes
                .GroupBy(outcome => outcome.Fields["Fee Regime"], StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var row = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Fee Regime"] = group.Key,
                        ["Requested Priors"] = group.First().Fields["Requested Priors"],
                        ["Attempted Solves"] = group.First().Fields["Attempted Solves"],
                        ["Inexact Attempts"] = group.First().Fields["Inexact Attempts"],
                        ["Exact Attempts"] = group.First().Fields["Exact Attempts"],
                        ["Verified Recoveries"] = group.First().Fields["Verified Recoveries"],
                        ["Distinct Reported Strategy Profiles"] = group.Count().ToString(CultureInfo.InvariantCulture),
                    };
                    foreach (string metric in RangeMetrics)
                    {
                        double[] values = group.Select(outcome => outcome.Metrics[metric]).ToArray();
                        double minimum = values.Min();
                        double maximum = values.Max();
                        double mean = values.Average();
                        double standardDeviation = Math.Sqrt(
                            values.Select(value => Math.Pow(value - mean, 2)).Average());
                        row[$"Minimum {metric}"] = Format(minimum);
                        row[$"Mean {metric}"] = Format(mean);
                        row[$"Maximum {metric}"] = Format(maximum);
                        row[$"Range {metric}"] = Format(maximum - minimum);
                        row[$"Standard Deviation {metric}"] = Format(standardDeviation);
                        row[$"Coefficient of Variation {metric}"] = Math.Abs(mean) <= 1E-15
                            ? string.Empty
                            : Format(standardDeviation / Math.Abs(mean));
                    }
                    return row;
                })
                .ToList();
            WriteRows(path, headers, rows);
            return rows.Count;
        }

        private static IReadOnlyDictionary<int, EquilibriumRecovery> ReadRecoveryCatalog(
            LitigGameOptions option,
            string path,
            int distinctEquilibria)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "A required CS004ME equilibrium-recovery catalog was not found.",
                    path);
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
            });
            csv.Read();
            csv.ReadHeader();
            var recoveries = new Dictionary<int, EquilibriumRecovery>();
            while (csv.Read())
            {
                string reportedOptionSet = csv.GetField("OptionSetName");
                if (!string.Equals(reportedOptionSet, option.Name, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Recovery catalog '{path}' identifies option set '{reportedOptionSet}', not '{option.Name}'.");
                int equilibrium = csv.GetField<int>("Equilibrium Number");
                var recovery = new EquilibriumRecovery(
                    csv.GetField<int>("Requested Priors"),
                    csv.GetField<int>("Attempted Solves"),
                    csv.GetField<int>("Inexact Attempts"),
                    csv.GetField<int>("Exact Attempts"),
                    csv.GetField<int>("Verified Recoveries"),
                    csv.GetField<int>("Distinct Reported Strategy Profiles"),
                    equilibrium,
                    csv.GetField<int>("Recovery Count"),
                    csv.GetField<double>("Recovery Share of Verified Recoveries"),
                    csv.GetField("Verification Status"),
                    csv.GetField("Distinctness Criterion"));
                if (!recoveries.TryAdd(equilibrium, recovery))
                    throw new InvalidDataException(
                        $"Duplicate equilibrium {equilibrium} in '{path}'.");
            }

            int requested = int.Parse(Setting(option, "Initialization Starts"), CultureInfo.InvariantCulture);
            if (recoveries.Count != distinctEquilibria ||
                !recoveries.Keys.OrderBy(value => value).SequenceEqual(
                    Enumerable.Range(1, distinctEquilibria)))
                throw new InvalidDataException(
                    $"The recovery catalog '{path}' does not identify all {distinctEquilibria} equilibria.");
            EquilibriumRecovery first = recoveries[1];
            if (recoveries.Values.Any(value =>
                    value.RequestedPriors != requested ||
                    value.AttemptedSolves != first.AttemptedSolves ||
                    value.InexactAttempts != first.InexactAttempts ||
                    value.ExactAttempts != first.ExactAttempts ||
                    value.VerifiedRecoveries != first.VerifiedRecoveries ||
                    value.DistinctProfiles != distinctEquilibria))
                throw new InvalidDataException(
                    $"The repeated run totals in '{path}' are inconsistent.");
            if (first.AttemptedSolves != first.InexactAttempts + first.ExactAttempts ||
                first.AttemptedSolves < first.VerifiedRecoveries ||
                first.VerifiedRecoveries != requested ||
                recoveries.Values.Sum(value => value.RecoveryCount) != first.VerifiedRecoveries ||
                recoveries.Values.Any(value =>
                    value.RecoveryCount <= 0 ||
                    value.RecoveryShare <= 0 ||
                    string.IsNullOrWhiteSpace(value.VerificationStatus) ||
                    string.IsNullOrWhiteSpace(value.DistinctnessCriterion)))
                throw new InvalidDataException(
                    $"The solver-attempt and recovery totals in '{path}' are inconsistent.");
            RequireApproximately(
                path,
                "recovery shares",
                1.0,
                recoveries.Values.Sum(value => value.RecoveryShare));
            return recoveries;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadRowsByFilter(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A required CS004ME equilibrium report was not found.", path);
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
            });
            csv.Read();
            csv.ReadHeader();
            string[] headers = csv.HeaderRecord ?? Array.Empty<string>();
            var rows = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            while (csv.Read())
            {
                var row = headers.ToDictionary(
                    header => header,
                    header => csv.GetField(header) ?? string.Empty,
                    StringComparer.Ordinal);
                if (!row.TryGetValue("Filter", out string filter) || string.IsNullOrWhiteSpace(filter))
                    continue;
                if (!rows.TryAdd(filter, row))
                    throw new InvalidDataException($"Duplicate filter '{filter}' in '{path}'.");
            }
            return rows;
        }

        private static Dictionary<string, string> RequiredFilter(
            IReadOnlyDictionary<string, Dictionary<string, string>> rows,
            string filter,
            string path) =>
            rows.TryGetValue(filter, out Dictionary<string, string> row)
                ? row
                : throw new InvalidDataException($"Missing filter '{filter}' in '{path}'.");

        private static double RequiredValue(
            IReadOnlyDictionary<string, string> row,
            string column,
            string path)
        {
            if (!row.TryGetValue(column, out string text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value))
                throw new InvalidDataException($"Missing or invalid column '{column}' in '{path}'.");
            return value;
        }

        private static void RequireApproximately(
            string path,
            string identity,
            double expected,
            double actual)
        {
            if (Math.Abs(expected - actual) > ValidationTolerance)
                throw new InvalidDataException(
                    $"CS004ME {identity} identity failed in '{path}': " +
                    $"expected {Format(expected)}, found {Format(actual)}.");
        }

        private static int CountNonemptyLines(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A required CS004ME equilibrium file was not found.", path);
            return File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static string Setting(GameOptions option, string name) =>
            Convert.ToString(option.VariableSettings[name], CultureInfo.InvariantCulture) ?? string.Empty;

        private static void WriteRows(
            string path,
            IReadOnlyList<string> headers,
            IEnumerable<IReadOnlyDictionary<string, string>> rows)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var output = new StringBuilder();
            AppendRow(output, headers);
            foreach (IReadOnlyDictionary<string, string> row in rows)
                AppendRow(output, headers.Select(header => row.TryGetValue(header, out string value) ? value : string.Empty));
            File.WriteAllText(path, output.ToString());
        }

        private static void AppendRow(StringBuilder output, IEnumerable<string> values) =>
            output.AppendLine(string.Join(",", values.Select(CsvField)));

        private static string CsvField(string value) =>
            $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

        private static string Format(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
