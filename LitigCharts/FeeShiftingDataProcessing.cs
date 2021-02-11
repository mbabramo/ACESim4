using ACESim;
using ACESimBase.Util;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class FeeShiftingDataProcessing
    {
        static string filePrefix => new LitigGameLauncher().MasterReportNameForDistributedProcessing + "-";
        const string correlatedEquilibriumFileSuffix = "-Corr";
        const string averageEquilibriumFileSuffix = "-Avg";
        const string firstEquilibriumFileSuffix = "-Eq1";

        private static string[] equilibriumTypeSuffixes = new string[] { correlatedEquilibriumFileSuffix, averageEquilibriumFileSuffix, firstEquilibriumFileSuffix };

        internal static void ProduceLatexDiagrams()
        {
            foreach (string fileSuffix in equilibriumTypeSuffixes)
            {
                FeeShiftingDataProcessing.ProduceLatexDiagrams("-scr" + fileSuffix);
                FeeShiftingDataProcessing.ProduceLatexDiagrams("-heatmap" + fileSuffix);
            }
        }

        internal static void ProduceLatexDiagrams(string fileSuffix)
        {
            var gameDefinition = new LitigGameDefinition();
            List<LitigGameOptions> litigGameOptionsSets = GetFeeShiftingGameOptionsSets();
            string path = Launcher.ReportFolder();

            var launcher = new LitigGameLauncher();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)

            List<Process> processesList = new List<Process>();
            int maxProcesses = 32;

            void CleanupCompletedProcesses()
            {
                processesList = processesList.Where(x => !x.HasExited).ToList();
            }

            foreach (var gameOptionsSet in litigGameOptionsSets)
            {
                string filenameCore, combinedPath;
                GetFileInfo(map, filePrefix, ".tex", ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath);
                if (!File.Exists(combinedPath))
                    throw new Exception("File not found");

                string texFileInQuotes = $"\"{combinedPath}\"";
                string outputDirectoryInQuotes = $"\"{path}\"";
                string pdflatexProgram = @"C:\Users\Admin\AppData\Local\Programs\MiKTeX\miktex\bin\x64\pdflatex.exe";
                string arguments = @$"{texFileInQuotes} -output-directory={outputDirectoryInQuotes}";

                while (processesList.Count() >= maxProcesses)
                {
                    Task.Delay(100);
                    CleanupCompletedProcesses();
                }

                ProcessStartInfo processStartInfo = new ProcessStartInfo(pdflatexProgram)
                {
                    Arguments = arguments,
                    UseShellExecute = true,
                };
                Process result = Process.Start(processStartInfo);
                if (result != null)
                    processesList.Add(result);
            }
            while (processesList.Any())
                CleanupCompletedProcesses();
            foreach (var optionSet in litigGameOptionsSets)
            {
                int failures = 0;
            retry:
                try
                {
                    File.Delete(Path.Combine(path, optionSet.Name + "{suffix}.aux"));
                    File.Delete(Path.Combine(path, optionSet.Name + "{suffix}.log"));
                    File.Delete(Path.Combine(path, optionSet.Name + ".synctex.gz"));
                    File.Delete(Path.Combine(path, optionSet.Name + "{suffix}.tex"));
                    failures = 0;
                }
                catch
                {
                    Task.Delay(1000);
                    failures++;
                    if (failures < 5)
                        goto retry;
                }
            }
        }

        public static void BuildMainFeeShiftingReport()
        {
            List<string> rowsToGet = new List<string> { "All", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" }; // DEBUG -- add Not Litigated to this and next
            List<string> replacementRowNames = new List<string> { "All", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> columnsToGet = new List<string> { "Exploit", "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "PWelfare", "DWelfare", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins" };
            List<string> replacementColumnNames = new List<string> { "Exploitability", "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Prob", "P Wealth", "D Wealth", "P Welfare", "D Welfare", "Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "P Doesn't File", "D Doesn't Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins" };
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
            string path = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults";
            string outputFileFullPath = Path.Combine(path, filePrefix + $"-{endOfFileName}.csv");
            string cumResults = "";
            foreach (string fileSuffix in equilibriumTypeSuffixes)
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
                string filenameCore, combinedPath;
                GetFileInfo(map, filePrefix, ".csv", ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath);
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

        private static void GetFileInfo(Dictionary<string, string> map, string filePrefix, string fileExtensionIncludingPeriod, ref string fileSuffix, string path, LitigGameOptions gameOptionsSet, out string filenameCore, out string combinedPath)
        {
            filenameCore = map[gameOptionsSet.Name];
            string filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
            combinedPath = Path.Combine(path, filename);
            if (!File.Exists(combinedPath))
            {
                fileSuffix = firstEquilibriumFileSuffix;
                filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
                combinedPath = Path.Combine(path, filename);
                if (!File.Exists(combinedPath))
                {
                    fileSuffix = "";
                    filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
                    combinedPath = Path.Combine(path, filename);
                }
            }
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
