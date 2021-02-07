using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class FeeShiftingDataProcessing
    {
        const string correlatedEquilibriumFileSuffix = "-Corr";
        const string averageEquilibriumFileSuffix = "-Avg";
        const string firstEquilibriumFileSuffix = "-Eq1"; 

        public static void BuildMainFeeShiftingReport()
        {
            List<string> rowsToGet = new List<string> { "All", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" }; // DEBUG -- add Not Litigated to this and next
            List<string> replacementRowNames = new List<string> { "All", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> columnsToGet = new List<string> { "Exploit", "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "PWelfare", "DWelfare", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins", "Exploit" };
            List<string> replacementColumnNames = new List<string> { "Exploitability", "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Prob", "P Wealth", "D Wealth", "P Welfare", "D Welfare", "Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "P Doesn't File", "D Doesn't Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins", "Exploitability" };
            BuildReport(rowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "output");
        }

        public static void BuildOffersReport()
        {
            var gameDefinition = new LitigGameDefinition();
            gameDefinition.Setup(GetFeeShiftingGameOptionsSets().First());
            var reportDefinitions = gameDefinition.GetSimpleReportDefinitions();

            var report = reportDefinitions.Skip(1).First();
            var rows = report.RowFilters;
            var columns = report.ColumnItems;
            var pSignalRows = rows.Where(x => x.Name.StartsWith("Round 1 P 1 PLiabilitySignal ")).Select(x => x.Name).ToList();
            var pSignalRowsReplacement = pSignalRows.Select(x => x.Replace("Round 1 P 1 PLiabilitySignal ", "P Liability Signal ")).ToList();
            var pOfferColumns = columns.Where(x => x.Name.StartsWith("P1")).Select(x => x.Name).ToList();
            var pOfferColumnsReplacement = Enumerable.Range(0, pOfferColumns.Count()).Select(x => $"P Offer Level {x + 1}");

            report = reportDefinitions.Skip(2).First(); 
            rows = report.RowFilters;
            columns = report.ColumnItems;
            var dSignalRows = rows.Where(x => x.Name.StartsWith("Round 1 D 1 DLiabilitySignal ")).Select(x => x.Name).ToList();
            var dSignalRowsReplacement = dSignalRows.Select(x => x.Replace("Round 1 D 1 DLiabilitySignal ", "D Liability Signal ")).ToList();
            var dOfferColumns = columns.Where(x => x.Name.StartsWith("D1")).Select(x => x.Name).ToList();
            var dOfferColumnsReplacement = Enumerable.Range(0, dOfferColumns.Count()).Select(x => $"D Offer Level {x + 1}");

            List<string> filtersOfRowsToGet = new List<string>();
            filtersOfRowsToGet.AddRange(pSignalRows);
            filtersOfRowsToGet.AddRange(dSignalRows);

            List<string> replacementRowNames = new List<string>();
            replacementRowNames.AddRange(pSignalRowsReplacement);
            replacementRowNames.AddRange(dSignalRowsReplacement);

            List<string> columnsToGet = new List<string>();
            columnsToGet.AddRange(pOfferColumns);
            columnsToGet.AddRange(dOfferColumns);

            List<string> replacementColumnNames = new List<string>();
            replacementColumnNames.AddRange(pOfferColumnsReplacement);
            replacementColumnNames.AddRange(dOfferColumnsReplacement);

            BuildReport(filtersOfRowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "offers");
        }

        private static void BuildReport(List<string> rowsToGet, List<string> replacementRowNames, List<string> columnsToGet, List<string> replacementColumnNames, string endOfFileName)
        {
            var launcher = new LitigGameLauncher();
            var gameOptionsSets = GetFeeShiftingGameOptionsSets();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            string filePrefix = "FS023-";
            string path = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults";
            string outputFileFullPath = Path.Combine(path, filePrefix + $"-{endOfFileName}.csv");
            string cumResults = "";
            foreach (string fileSuffix in new string[] { correlatedEquilibriumFileSuffix, averageEquilibriumFileSuffix, firstEquilibriumFileSuffix })
            {
                bool includeHeader = fileSuffix == correlatedEquilibriumFileSuffix;
                List<List<string>> outputLines = GetCSVLines(gameOptionsSets, map, rowsToGet, replacementRowNames, filePrefix, fileSuffix, path, includeHeader, columnsToGet, replacementColumnNames);
                if (includeHeader)
                    outputLines[0].Insert(0, "Equilibrium Type");
                string equilibriumType = fileSuffix switch
                {
                    correlatedEquilibriumFileSuffix => "Correlated",
                    averageEquilibriumFileSuffix => "Average",
                    firstEquilibriumFileSuffix => "First",
                    _ => throw new NotImplementedException()
                };
                foreach (List<string> bodyLine in outputLines.Skip(includeHeader ? 1 : 0))
                    bodyLine.Insert(0, equilibriumType);
                string resultForEquilibrium = MakeString(outputLines);
                cumResults += resultForEquilibrium;
            }
            TextFileCreate.CreateTextFile(outputFileFullPath, cumResults);
        }

        private static List<LitigGameOptions> GetFeeShiftingGameOptionsSets()
        {
            var launcher = new LitigGameLauncher();
            return launcher.GetFeeShiftingArticleGamesSets(false, true).SelectMany(x => x).ToList();
        }

        private static List<List<string>> GetCSVLines(List<LitigGameOptions> gameOptionsSets, Dictionary<string, string> map, List<string> rowsToGet, List<string> replacementRowNames,  string filePrefix, string fileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames)
        {

            // Set the following on opening the first file
            List<List<string>> outputLines = null;

            foreach (var gameOptionsSet in gameOptionsSets.Where(x => map[x.Name] == x.Name)) // for this aggregation, we want only one copy of each report, so we exclude the redundant names that include the baseline values for a noncritical set
            {
                if (outputLines == null)
                {
                    outputLines = new List<List<string>>();

                    if (includeHeader)
                    {
                        List<string> headings = gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Key).ToList();
                        headings.Add("Filter");
                        headings.AddRange(replacementColumnNames);
                        outputLines.Add(headings);
                    }
                }
                double?[,] resultsAllRows = null;
                string filenameCore = map[gameOptionsSet.Name];
                string filename = filePrefix + filenameCore + fileSuffix + ".csv";
                string combinedPath = Path.Combine(path, filename);
                if (fileSuffix != firstEquilibriumFileSuffix && !File.Exists(combinedPath))
                {
                    fileSuffix = firstEquilibriumFileSuffix;
                    filename = filePrefix + filenameCore + fileSuffix + ".csv";
                    combinedPath = Path.Combine(path, filename);
                }
                (string columnName, string expectedText)[][] rowsToFind = new (string columnName, string expectedText)[rowsToGet.Count()][];
                for (int f = 0; f < rowsToGet.Count(); f++)
                {
                    rowsToFind[f] = new (string columnName, string expectedText)[2];
                    rowsToFind[f][0] = ("OptionSet", filenameCore);
                    rowsToFind[f][1] = ("Filter", rowsToGet[f]);
                }
                // string[] columnsToGet = new string[] { "Trial", "AccSq", "POffer", "DOffer" };
                resultsAllRows = CSVData.GetCSVData(combinedPath, rowsToFind.ToArray(), columnsToGet.ToArray(), true);
                for (int f = 0; f < rowsToGet.Count(); f++)
                {
                    List<string> bodyRow = new List<string>();
                    bodyRow.AddRange(gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Value?.ToString()));
                    bodyRow.Add(replacementRowNames[f]);
                    bodyRow.AddRange(resultsAllRows.GetRow(f).Select(x => x?.ToString()));
                    outputLines.Add(bodyRow);
                }
            }
            return outputLines;
        }

        public static string MakeString(List<List<string>> values)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < values.Count(); i++)
                b.AppendLine(String.Join(",", values[i]));
            return b.ToString();
        }
    }
}
