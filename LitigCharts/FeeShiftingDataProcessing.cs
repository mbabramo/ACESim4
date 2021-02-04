using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class FeeShiftingDataProcessing
    {
        public static void ExecuteMain()
        {
            var launcher = new LitigGameLauncher();
            var gameOptionsSets = launcher.GetFeeShiftingArticleGamesSets(false, true).SelectMany(x => x).ToList();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            List<string> filtersOfRowsToGet = new List<string> { "All" };
            string filePrefix = "FS023-";
            string fileSuffix = "Corr";
            List<string> columnsToGet = new List<string> { "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "PWelfare", "DWelfare", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins", "Exploit"};
            List<string> replacementNames = new List<string> { "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Prob", "P Wealth", "D Wealth", "P Welfare", "D Welfare", "Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "P Doesn't File", "D Doesn't Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins", "Exploitability" };
            string path = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults";

            // Set the following on opening the first file
            List<List<string>> outputLines = null;
            LitigGameDefinition gameDefinition = null;

            foreach (var gameOptionsSet in gameOptionsSets.Where(x => map[x.Name] == x.Name)) // for this aggregation, we want only one copy of each report, so we exclude the redundant names that include the baseline values for a noncritical set
            {
                if (outputLines == null)
                {
                    outputLines = new List<List<string>>();
                    List<string> headings = gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Key).ToList();
                    headings.AddRange(replacementNames);
                    outputLines.Add(headings);

                    gameDefinition = new LitigGameDefinition();
                    gameDefinition.Setup(gameOptionsSet);
                    var reportDefinitions = gameDefinition.GetSimpleReportDefinitions();
                    var report = reportDefinitions.First();
                    var rows = report.RowFilters;
                    var pSignalRows = rows.Where(x => x.Name.StartsWith("Round 1 P 1")).ToList();
                    var dSignalRows = rows.Where(x => x.Name.StartsWith("Round 1 D 1")).ToList();
                    // DEBUG -- may wish to get more rows, but we also need to merge the Round 1 P 1 PLiabilitySignal 1 rows with PLiabilitySignal etc., and All with All. 
                }
                string filenameCore = map[gameOptionsSet.Name];
                string filename = filePrefix + filenameCore + fileSuffix + ".csv";
                string combined = Path.Combine(path, filename);
                (string columnName, string expectedText)[][] rowsToFind = new (string columnName, string expectedText)[filtersOfRowsToGet.Count()][];
                for (int f = 0; f < filtersOfRowsToGet.Count(); f++)
                {
                    rowsToFind[f] = new (string columnName, string expectedText)[2];
                    rowsToFind[f][0] = ("OptionSet", filenameCore);
                    rowsToFind[f][1] = ("Filter", filtersOfRowsToGet[f]);
                }
                // string[] columnsToGet = new string[] { "Trial", "AccSq", "POffer", "DOffer" };
                var resultsAllRows = CSVData.GetCSVData(combined, rowsToFind.ToArray(), columnsToGet.ToArray(), true);
                List<string> bodyRow = new List<string>();
                bodyRow.AddRange(gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Value?.ToString()));
                bodyRow.AddRange(resultsAllRows.GetRow(0).Select(x => x?.ToString()));
                outputLines.Add(bodyRow);
            }
            string outputFileFullPath = Path.Combine(path, filePrefix + "-output.csv");
            string csvResult = MakeString(outputLines);
            TextFileCreate.CreateTextFile(outputFileFullPath, csvResult);
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
