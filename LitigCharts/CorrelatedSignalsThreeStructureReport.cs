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
    /// Produces a three-structure comparison either from one unified run or by joining the
    /// uniform-only supplement to the retained CS001 baseline rows.
    /// </summary>
    public static class CorrelatedSignalsThreeStructureReport
    {
        public sealed record ValidationSummary(
            int NewRunSourceRowCount,
            int ComparisonRowCount,
            int OutcomeMeasureCount);

        private static readonly string[] StructureLabels =
        {
            LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel,
            LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel,
            LitigGameCorrelatedSignalsArticleLauncher.UniformQualityLabel,
        };

        private static readonly string[] ComparisonMetadataColumns =
        {
            "GroupName",
            "Information Level",
            "Costs Multiplier",
            "Fee Shifting Multiplier",
            "Risk Aversion",
            "Fee Shifting Rule",
            "Relative Costs",
            "Allow Abandon and Defaults",
            "Probability Truly Liable",
            "Noise to Produce Case Strength",
            "Issue",
            "Proportion of Costs at Beginning",
            "Liability Signal Shaping",
            "Damages Signal Shaping",
            "Number of Signals",
            "Number of Court Signals",
            "Number of Offers",
        };

        private static readonly string[] ModelSpecificMetadataColumns =
        {
            "Signal Structure",
            "Party Signal Sigma",
            "Court Signal Sigma",
            "Quality Distribution",
            "Quality-Truth Link",
            "Integration Method",
            "Quadrature Order",
        };

        public static ValidationSummary BuildAndValidate(
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            string newRunCsvPath,
            string comparisonCsvPath,
            string legacyCsvPath = null)
        {
            if (launcher == null)
                throw new ArgumentNullException(nameof(launcher));
            if (launcher.RunPlan == LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.LegacyTwoStructure)
                throw new ArgumentException("The legacy plan uses the retained paired report.", nameof(launcher));

            List<GameOptions> newRunOptions = launcher.GetOptionsSets();
            launcher.ValidateProductionMatrix(newRunOptions);
            CsvTable newRun = ReadRows(newRunCsvPath);
            ValidateNewRunRows(newRun, newRunOptions);

            List<Dictionary<string, string>> comparisonRows;
            string[] commonHeaders;
            int expectedComparisonGroups;
            if (launcher.RunPlan == LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UniformBaselineSupplement)
            {
                if (string.IsNullOrWhiteSpace(legacyCsvPath))
                    throw new ArgumentNullException(nameof(legacyCsvPath));
                CsvTable legacy = ReadRows(legacyCsvPath);
                RequireColumns(legacy.Headers, RequiredColumns());
                List<Dictionary<string, string>> legacyBaselineRows = legacy.Rows.Where(row =>
                    row["Information Level"] == LitigGameCorrelatedSignalsArticleLauncher.BaselineInformationLevelLabel &&
                    row["Risk Aversion"] == "Risk Neutral").ToList();
                int expectedLegacyRows = 2 *
                    LitigGameCorrelatedSignalsArticleLauncher.SupplementalOptionSetCount *
                    CorrelatedSignalsPairedReport.ExpectedFilters.Count;
                if (legacyBaselineRows.Count != expectedLegacyRows)
                    throw new InvalidDataException(
                        $"CS001 contributes {legacyBaselineRows.Count} baseline rows; expected {expectedLegacyRows}.");
                comparisonRows = legacyBaselineRows.Concat(newRun.Rows).ToList();
                commonHeaders = newRun.Headers.Intersect(legacy.Headers, StringComparer.OrdinalIgnoreCase).ToArray();
                expectedComparisonGroups =
                    LitigGameCorrelatedSignalsArticleLauncher.SupplementalComparisonGroupCount *
                    CorrelatedSignalsPairedReport.ExpectedFilters.Count;
            }
            else
            {
                comparisonRows = newRun.Rows;
                commonHeaders = newRun.Headers;
                expectedComparisonGroups =
                    LitigGameCorrelatedSignalsArticleLauncher.UnifiedComparisonGroupCount *
                    CorrelatedSignalsPairedReport.ExpectedFilters.Count;
            }

            RequireColumns(commonHeaders, RequiredColumns().Concat(ComparisonMetadataColumns));
            var excludedMetadata = new HashSet<string>(
                RequiredColumns()
                    .Concat(ComparisonMetadataColumns)
                    .Concat(ModelSpecificMetadataColumns),
                StringComparer.OrdinalIgnoreCase);
            string[] outcomeMeasures = commonHeaders
                .Where(header => !excludedMetadata.Contains(header))
                .ToArray();
            if (outcomeMeasures.Length == 0)
                throw new InvalidDataException("The reports contain no shared numerical outcome measures.");

            var groups = comparisonRows
                .GroupBy(ComparisonIdentity, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            if (groups.Count != expectedComparisonGroups)
                throw new InvalidDataException(
                    $"The comparison contains {groups.Count} groups; expected {expectedComparisonGroups}.");

            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = Environment.NewLine,
            }))
            {
                WriteHeader(csv, outcomeMeasures);
                foreach (var group in groups)
                    WriteGroup(csv, group, outcomeMeasures);
            }

            string directory = Path.GetDirectoryName(comparisonCsvPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(comparisonCsvPath, writer.ToString());
            return new ValidationSummary(newRun.Rows.Count, groups.Count, outcomeMeasures.Length);
        }

        private static void ValidateNewRunRows(CsvTable table, IReadOnlyList<GameOptions> optionSets)
        {
            RequireColumns(table.Headers, RequiredColumns());
            string[] duplicateHeaders = table.Headers
                .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateHeaders.Length > 0)
                throw new InvalidDataException("The new report has duplicate columns: " + string.Join(", ", duplicateHeaders));

            Dictionary<string, GameOptions> optionsByName = optionSets.ToDictionary(option => option.Name, StringComparer.Ordinal);
            var duplicateRows = table.Rows
                .GroupBy(RowIdentity, StringComparer.Ordinal)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateRows.Length > 0)
                throw new InvalidDataException("The new report has duplicate rows: " + string.Join(", ", duplicateRows.Take(25)));

            var rowsByIdentity = table.Rows.ToDictionary(RowIdentity, StringComparer.Ordinal);
            foreach (GameOptions optionSet in optionSets)
            {
                foreach (string filter in CorrelatedSignalsPairedReport.ExpectedFilters)
                {
                    string identity = RowIdentity(optionSet.Name, filter, "Only Eq");
                    if (!rowsByIdentity.TryGetValue(identity, out Dictionary<string, string> row))
                        throw new InvalidDataException($"The new report is missing '{identity}'.");
                    foreach (var setting in optionSet.VariableSettings)
                    {
                        string expected = Convert.ToString(setting.Value, CultureInfo.InvariantCulture);
                        if (!row.TryGetValue(setting.Key, out string actual) || actual != expected)
                            throw new InvalidDataException(
                                $"Option set '{optionSet.Name}' reports {setting.Key}='{actual}', expected '{expected}'.");
                    }
                }
            }

            int expectedRows = optionSets.Count * CorrelatedSignalsPairedReport.ExpectedFilters.Count;
            if (table.Rows.Count != expectedRows)
                throw new InvalidDataException(
                    $"The new report contains {table.Rows.Count} rows; expected exactly {expectedRows}.");
            if (table.Rows.Any(row => row["Equilibrium Type"] != "Only Eq"))
                throw new InvalidDataException("The comparison requires one 'Only Eq' result per option set.");
            if (table.Rows.Any(row => !optionsByName.ContainsKey(row["OptionSetName"])))
                throw new InvalidDataException("The new report contains an unexpected option set.");
        }

        private static IEnumerable<string> RequiredColumns() => new[]
        {
            "Equilibrium Type",
            "Filter",
            "OptionSetName",
            "Signal Structure",
            "Information Level",
            "Party Signal Sigma",
            "Court Signal Sigma",
        };

        private static string RowIdentity(Dictionary<string, string> row) =>
            RowIdentity(row["OptionSetName"], row["Filter"], row["Equilibrium Type"]);

        private static string RowIdentity(string optionSet, string filter, string equilibrium) =>
            string.Join("|", optionSet, filter, equilibrium);

        private static string ComparisonIdentity(Dictionary<string, string> row) =>
            string.Join("|", new[] { row["Equilibrium Type"], row["Filter"] }
                .Concat(ComparisonMetadataColumns.Select(column => $"{column}={row[column]}")));

        private static void WriteHeader(CsvWriter csv, IEnumerable<string> outcomeMeasures)
        {
            csv.WriteField("Equilibrium Type");
            csv.WriteField("Filter");
            foreach (string metadata in ComparisonMetadataColumns)
                csv.WriteField(metadata);
            foreach (string structure in StructureLabels)
            {
                csv.WriteField($"{structure} OptionSetName");
                csv.WriteField($"{structure} Party Signal Sigma");
                csv.WriteField($"{structure} Court Signal Sigma");
            }
            foreach (string measure in outcomeMeasures)
            {
                foreach (string structure in StructureLabels)
                    csv.WriteField($"{measure} — {structure}");
                csv.WriteField($"{measure} — Difference (Uniform quality - Case quality)");
                csv.WriteField($"{measure} — Difference (Uniform quality - Binary truth)");
                csv.WriteField($"{measure} — Difference (Binary truth - Case quality)");
            }
            csv.NextRecord();
        }

        private static void WriteGroup(
            CsvWriter csv,
            IGrouping<string, Dictionary<string, string>> group,
            IEnumerable<string> outcomeMeasures)
        {
            if (group.Count() != StructureLabels.Length)
                throw new InvalidDataException(
                    $"Comparison group '{group.Key}' has {group.Count()} rows instead of three.");
            var byStructure = StructureLabels.ToDictionary(
                label => label,
                label => SingleStructureRow(group, label),
                StringComparer.Ordinal);
            Dictionary<string, string> first = byStructure[StructureLabels[0]];

            csv.WriteField(first["Equilibrium Type"]);
            csv.WriteField(first["Filter"]);
            foreach (string metadata in ComparisonMetadataColumns)
            {
                if (byStructure.Values.Any(row => row[metadata] != first[metadata]))
                    throw new InvalidDataException(
                        $"Comparison group '{group.Key}' combines different values of '{metadata}'.");
                csv.WriteField(first[metadata]);
            }
            foreach (string structure in StructureLabels)
            {
                Dictionary<string, string> row = byStructure[structure];
                csv.WriteField(row["OptionSetName"]);
                csv.WriteField(row["Party Signal Sigma"]);
                csv.WriteField(row["Court Signal Sigma"]);
            }
            foreach (string measure in outcomeMeasures)
            {
                string caseQuality = byStructure[StructureLabels[0]][measure];
                string binaryTruth = byStructure[StructureLabels[1]][measure];
                string uniformQuality = byStructure[StructureLabels[2]][measure];
                csv.WriteField(caseQuality);
                csv.WriteField(binaryTruth);
                csv.WriteField(uniformQuality);
                csv.WriteField(Difference(uniformQuality, caseQuality, group.Key, measure));
                csv.WriteField(Difference(uniformQuality, binaryTruth, group.Key, measure));
                csv.WriteField(Difference(binaryTruth, caseQuality, group.Key, measure));
            }
            csv.NextRecord();
        }

        private static Dictionary<string, string> SingleStructureRow(
            IEnumerable<Dictionary<string, string>> rows,
            string structure)
        {
            List<Dictionary<string, string>> matches = rows
                .Where(row => row["Signal Structure"] == structure)
                .ToList();
            if (matches.Count != 1)
                throw new InvalidDataException(
                    $"Expected one '{structure}' row but found {matches.Count}.");
            return matches[0];
        }

        private static string Difference(string minuend, string subtrahend, string group, string measure)
        {
            if (string.IsNullOrWhiteSpace(minuend) || string.IsNullOrWhiteSpace(subtrahend))
                return "";
            if (!double.TryParse(minuend, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(subtrahend, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                throw new InvalidDataException(
                    $"Comparison '{group}' has a non-numeric value for '{measure}'.");
            return (a - b).ToString("G17", CultureInfo.InvariantCulture);
        }

        private static CsvTable ReadRows(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A required correlated-signals report was not found.", path);
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
            });
            if (!csv.Read() || !csv.ReadHeader())
                throw new InvalidDataException($"Report '{path}' has no header.");
            string[] headers = csv.HeaderRecord;
            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string header in headers)
                    row[header] = csv.GetField<string>(header) ?? "";
                rows.Add(row);
            }
            return new CsvTable(headers, rows);
        }

        private static void RequireColumns(IEnumerable<string> headers, IEnumerable<string> required)
        {
            HashSet<string> available = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] missing = required.Where(column => !available.Contains(column)).Distinct().ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("A report is missing columns: " + string.Join(", ", missing));
        }

        private sealed record CsvTable(
            string[] Headers,
            List<Dictionary<string, string>> Rows);
    }
}
