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
    /// Validates the complete correlated-signals numerical report and pivots it into direct,
    /// deterministic Case quality / Binary truth comparisons.
    /// </summary>
    public static class CorrelatedSignalsPairedReport
    {
        public sealed record ValidationSummary(
            int SourceRowCount,
            int PairedRowCount,
            int OutcomeMeasureCount);

        public static readonly IReadOnlyList<string> ExpectedFilters = new[]
        {
            "All",
            "Dispute Arises",
            "Not Litigated",
            "Litigated",
            "Settles",
            "Tried",
            "P Loses",
            "P Wins",
            "Truly Liable",
            "Truly Not Liable",
        };

        public static ValidationSummary BuildAndValidate(
            LitigGameCorrelatedSignalsArticleLauncher launcher,
            string sourceCsvPath,
            string pairedCsvPath)
        {
            if (launcher == null)
                throw new ArgumentNullException(nameof(launcher));
            if (!File.Exists(sourceCsvPath))
                throw new FileNotFoundException("Combined correlated-signals report was not found.", sourceCsvPath);

            List<GameOptions> optionSets = launcher.GetOptionsSets();
            launcher.ValidateProductionMatrix(optionSets);
            Dictionary<string, GameOptions> optionsByName = optionSets.ToDictionary(
                x => x.Name,
                StringComparer.Ordinal);

            (string[] headers, List<Dictionary<string, string>> rows) = ReadRows(sourceCsvPath);
            RequireColumns(
                headers,
                new[] { "Equilibrium Type", "Filter", "GroupName", "OptionSetName", "Signal Structure", "Information Level", "Party Signal Sigma", "Court Signal Sigma" });

            string[] duplicateHeaders = headers
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (duplicateHeaders.Length > 0)
                throw new InvalidDataException("Combined report has duplicate columns: " + string.Join(", ", duplicateHeaders));

            var unexpectedOptionSets = rows
                .Select(row => row["OptionSetName"])
                .Where(name => !optionsByName.ContainsKey(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unexpectedOptionSets.Count > 0)
                throw new InvalidDataException(
                    "Combined report contains unexpected option sets: " + string.Join(", ", unexpectedOptionSets));

            string[] equilibriumTypes = rows
                .Select(row => row["Equilibrium Type"])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (equilibriumTypes.Length != 1 || equilibriumTypes[0] != "Only Eq")
                throw new InvalidDataException(
                    "Correlated-signals production reporting expects exactly one 'Only Eq' result set; found: " +
                    string.Join(", ", equilibriumTypes));

            var duplicateRows = rows
                .GroupBy(RowIdentity, StringComparer.Ordinal)
                .Where(group => group.Count() != 1)
                .ToList();
            if (duplicateRows.Count > 0)
                throw new InvalidDataException(
                    "Combined report has duplicate option/filter/equilibrium rows: " +
                    string.Join(", ", duplicateRows.Select(x => x.Key)));

            var rowsByIdentity = rows.ToDictionary(RowIdentity, StringComparer.Ordinal);
            var missingRows = new List<string>();
            foreach (GameOptions optionSet in optionSets)
            {
                foreach (string filter in ExpectedFilters)
                {
                    string identity = RowIdentity(optionSet.Name, filter, "Only Eq");
                    if (!rowsByIdentity.TryGetValue(identity, out Dictionary<string, string> row))
                    {
                        missingRows.Add(identity);
                        continue;
                    }
                    ValidateVariableSettings(optionSet, row);
                }
            }

            if (missingRows.Count > 0)
                throw new InvalidDataException(
                    $"Combined report is missing {missingRows.Count} expected rows:" + Environment.NewLine +
                    string.Join(Environment.NewLine, missingRows.Take(25)));

            int expectedSourceRows = optionSets.Count * ExpectedFilters.Count;
            if (rows.Count != expectedSourceRows)
                throw new InvalidDataException(
                    $"Combined report contains {rows.Count} rows; expected exactly {expectedSourceRows}. " +
                    "Unexpectedly combined result sets are not permitted.");

            var excludedMetadata = new HashSet<string>(
                launcher.DefaultVariableValues.Select(x => x.Item1)
                    .Concat(new[] { "Equilibrium Type", "Filter", "GroupName", "OptionSetName" }),
                StringComparer.OrdinalIgnoreCase);
            string[] outcomeMeasures = headers.Where(x => !excludedMetadata.Contains(x)).ToArray();
            if (outcomeMeasures.Length == 0)
                throw new InvalidDataException("Combined report contains no numerical outcome measures.");

            string[] pairedMetadataColumns = new[] { "GroupName" }
                .Concat(launcher.DefaultVariableValues.Select(x => x.Item1))
                .Where(x => x is not "Signal Structure" and not "Party Signal Sigma" and not "Court Signal Sigma")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var pairedGroups = rows
                .GroupBy(row => PairIdentity(row, pairedMetadataColumns), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            int expectedPairs = LitigGameCorrelatedSignalsArticleLauncher.ProductionPairedComparisonCount * ExpectedFilters.Count;
            if (pairedGroups.Count != expectedPairs)
                throw new InvalidDataException(
                    $"Combined report contains {pairedGroups.Count} paired rows; expected {expectedPairs}.");

            using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
            var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = Environment.NewLine,
            };
            using (var csv = new CsvWriter(stringWriter, csvConfiguration))
            {
                csv.WriteField("Equilibrium Type");
                csv.WriteField("Filter");
                foreach (string metadataColumn in pairedMetadataColumns)
                    csv.WriteField(metadataColumn);
                csv.WriteField("Case quality OptionSetName");
                csv.WriteField("Case quality Party Signal Sigma");
                csv.WriteField("Case quality Court Signal Sigma");
                csv.WriteField("Binary truth OptionSetName");
                csv.WriteField("Binary truth Party Signal Sigma");
                csv.WriteField("Binary truth Court Signal Sigma");
                foreach (string outcomeMeasure in outcomeMeasures)
                {
                    csv.WriteField($"{outcomeMeasure} — Case quality");
                    csv.WriteField($"{outcomeMeasure} — Binary truth");
                    csv.WriteField($"{outcomeMeasure} — Difference (Binary truth - Case quality)");
                }
                csv.NextRecord();

                foreach (var group in pairedGroups)
                {
                    if (group.Count() != 2)
                        throw new InvalidDataException(
                            $"Pair '{group.Key}' contains {group.Count()} rows instead of two.");
                    Dictionary<string, string> caseQuality = SingleStructureRow(
                        group,
                        LitigGameCorrelatedSignalsArticleLauncher.CaseQualityLabel);
                    Dictionary<string, string> binaryTruth = SingleStructureRow(
                        group,
                        LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel);

                    csv.WriteField(caseQuality["Equilibrium Type"]);
                    csv.WriteField(caseQuality["Filter"]);
                    foreach (string metadataColumn in pairedMetadataColumns)
                    {
                        if (!string.Equals(caseQuality[metadataColumn], binaryTruth[metadataColumn], StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"Pair '{group.Key}' unexpectedly combines {metadataColumn}: " +
                                $"'{caseQuality[metadataColumn]}' and '{binaryTruth[metadataColumn]}'.");
                        csv.WriteField(caseQuality[metadataColumn]);
                    }

                    csv.WriteField(caseQuality["OptionSetName"]);
                    csv.WriteField(caseQuality["Party Signal Sigma"]);
                    csv.WriteField(caseQuality["Court Signal Sigma"]);
                    csv.WriteField(binaryTruth["OptionSetName"]);
                    csv.WriteField(binaryTruth["Party Signal Sigma"]);
                    csv.WriteField(binaryTruth["Court Signal Sigma"]);

                    foreach (string outcomeMeasure in outcomeMeasures)
                    {
                        string caseQualityText = caseQuality[outcomeMeasure];
                        string binaryTruthText = binaryTruth[outcomeMeasure];
                        csv.WriteField(caseQualityText);
                        csv.WriteField(binaryTruthText);
                        csv.WriteField(Difference(caseQualityText, binaryTruthText, group.Key, outcomeMeasure));
                    }
                    csv.NextRecord();
                }
            }

            string pairedCsv = stringWriter.ToString();
            string outputDirectory = Path.GetDirectoryName(pairedCsvPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(pairedCsvPath, pairedCsv);

            return new ValidationSummary(rows.Count, pairedGroups.Count, outcomeMeasures.Length);
        }

        private static (string[] headers, List<Dictionary<string, string>> rows) ReadRows(string path)
        {
            using var reader = new StreamReader(path);
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
            };
            using var csv = new CsvReader(reader, configuration);
            if (!csv.Read() || !csv.ReadHeader())
                throw new InvalidDataException("Combined report header is missing.");
            string[] headers = csv.HeaderRecord;
            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string header in headers)
                    row[header] = csv.GetField<string>(header) ?? "";
                rows.Add(row);
            }
            return (headers, rows);
        }

        private static void RequireColumns(IEnumerable<string> headers, IEnumerable<string> required)
        {
            var headerSet = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] missing = required.Where(x => !headerSet.Contains(x)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("Combined report is missing columns: " + string.Join(", ", missing));
        }

        private static string RowIdentity(Dictionary<string, string> row) =>
            RowIdentity(row["OptionSetName"], row["Filter"], row["Equilibrium Type"]);

        private static string RowIdentity(string optionSetName, string filter, string equilibriumType) =>
            string.Join("|", optionSetName, filter, equilibriumType);

        private static string PairIdentity(
            Dictionary<string, string> row,
            IEnumerable<string> pairedMetadataColumns) =>
            string.Join("|", new[] { row["Equilibrium Type"], row["Filter"] }
                .Concat(pairedMetadataColumns.Select(column => $"{column}={row[column]}")));

        private static Dictionary<string, string> SingleStructureRow(
            IEnumerable<Dictionary<string, string>> rows,
            string structure)
        {
            var matching = rows.Where(row => row["Signal Structure"] == structure).ToList();
            if (matching.Count != 1)
                throw new InvalidDataException(
                    $"Expected exactly one '{structure}' row in a pair but found {matching.Count}.");
            return matching[0];
        }

        private static void ValidateVariableSettings(
            GameOptions optionSet,
            Dictionary<string, string> row)
        {
            foreach (var setting in optionSet.VariableSettings)
            {
                string expected = Convert.ToString(setting.Value, CultureInfo.InvariantCulture);
                if (!row.TryGetValue(setting.Key, out string actual))
                    throw new InvalidDataException(
                        $"Combined report is missing variable column '{setting.Key}'.");
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Option set '{optionSet.Name}' reports {setting.Key}='{actual}', expected '{expected}'.");
            }
        }

        private static string Difference(
            string caseQualityText,
            string binaryTruthText,
            string pairKey,
            string outcomeMeasure)
        {
            if (string.IsNullOrWhiteSpace(caseQualityText) && string.IsNullOrWhiteSpace(binaryTruthText))
                return "";
            if (!double.TryParse(caseQualityText, NumberStyles.Float, CultureInfo.InvariantCulture, out double caseQuality) ||
                !double.TryParse(binaryTruthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double binaryTruth))
                throw new InvalidDataException(
                    $"Pair '{pairKey}' has a non-numeric or one-sided value for '{outcomeMeasure}'.");
            return (binaryTruth - caseQuality).ToString("G17", CultureInfo.InvariantCulture);
        }
    }
}
