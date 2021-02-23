using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using ACESimBase.Util.Tikz;
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

        private static string[] equilibriumTypeSuffixes_All = new string[] { correlatedEquilibriumFileSuffix, averageEquilibriumFileSuffix, firstEquilibriumFileSuffix };
        private static string[] equilibriumTypeSuffixes_One = new string[] { firstEquilibriumFileSuffix };
        private static string[] equilibriumTypeWords_All = new string[] { "Correlated", "Average", "First" };
        private static string[] equilibriumTypeWords_One = new string[] { "First" };

        static bool firstEqOnly => new EvolutionSettings().SequenceFormNumPriorsToUseToGenerateEquilibria == 1;
        static string[] eqToRun => firstEqOnly ? equilibriumTypeWords_One : equilibriumTypeWords_All;
        static string[] equilibriumTypeSuffixes => firstEqOnly ? equilibriumTypeSuffixes_One : equilibriumTypeSuffixes_All;


        // TODO: Move all this to a separate class

        static List<Process> ProcessesList = new List<Process>();
        static int maxProcesses = Environment.ProcessorCount;

        static void CleanupCompletedProcesses()
        {
            ProcessesList = ProcessesList.Where(x => !x.HasExited).ToList();
        }

        static void WaitForProcessesToFinish()
        {
            while (ProcessesList.Any())
                CleanupCompletedProcesses();
        }

        static void WaitUntilFewerThanMaxProcessesAreRunning()
        {
            while (ProcessesList.Count() >= maxProcesses)
            {
                Task.Delay(100);
                CleanupCompletedProcesses();
            }
        }


        public static void BuildMainFeeShiftingReport()
        {
            List<string> rowsToGet = new List<string> { "All", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> replacementRowNames = new List<string> { "All", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> columnsToGet = new List<string> { "Exploit", "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "PWelfare", "DWelfare", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins" };
            List<string> replacementColumnNames = new List<string> { "Exploitability", "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Probability", "P Wealth", "D Wealth", "P Welfare", "D Welfare", "Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins" };
            BuildReport(rowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "output");
        }

        internal static void ProduceLatexDiagrams(string fileSuffix)
        {
            var gameDefinition = new LitigGameDefinition();
            List<LitigGameOptions> litigGameOptionsSets = GetFeeShiftingGameOptionsSets();
            string path = Launcher.ReportFolder();

            var launcher = new LitigGameLauncher();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)

            foreach (var gameOptionsSet in litigGameOptionsSets)
            {
                string filenameCore, combinedPath;
                GetFileInfo(map, filePrefix, ".tex", ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath);
                if (!File.Exists(combinedPath))
                    throw new Exception("File not found");
                ExecuteLatexProcess(path, combinedPath);
            }
            WaitForProcessesToFinish();

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

        private static void ExecuteLatexProcess(string path, string combinedPath)
        {
            WaitUntilFewerThanMaxProcessesAreRunning();

            string texFileInQuotes = $"\"{combinedPath}\"";
            string outputDirectoryInQuotes = $"\"{path}\"";
            bool backupComputer = false;
            string pdflatexProgram = backupComputer ? @"C:\Program Files\MiKTeX 2.9\miktex\bin\x64" : @"C:\Users\Admin\AppData\Local\Programs\MiKTeX\miktex\bin\x64\pdflatex.exe";
            string arguments = @$"{texFileInQuotes} -output-directory={outputDirectoryInQuotes}";

            ProcessStartInfo processStartInfo = new ProcessStartInfo(pdflatexProgram)
            {
                Arguments = arguments,
                UseShellExecute = false,
            };

            var handle = Process.GetCurrentProcess().MainWindowHandle;
            Process result = Process.Start(processStartInfo);
            ProcessesList.Add(result);
        }

        private static void BuildReport(List<string> rowsToGet, List<string> replacementRowNames, List<string> columnsToGet, List<string> replacementColumnNames, string endOfFileName)
        {
            bool onlyAllFilter = false; 
            if (onlyAllFilter)
            {
                rowsToGet = rowsToGet.Take(1).ToList();
                replacementRowNames = replacementRowNames.Take(1).ToList();
            }

            var launcher = new LitigGameLauncher();
            var gameOptionsSets = GetFeeShiftingGameOptionsSets();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            string path = Launcher.ReportFolder();
            string outputFileFullPath = Path.Combine(path, filePrefix + $"-{endOfFileName}.csv");
            string cumResults = "";

            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList();

            // look up particular settings here if desired (not usually needed)
            var settingsToFind = new List<(string, object)>()
            {
                ("Costs Multiplier", "1"),
                ("Fee Shifting Multiplier", "0"),
                ("Risk Aversion", "Risk Neutral"),
                ("Fee Shifting Rule", "English"),
                ("Relative Costs", "1"),
                ("Noise Multiplier P", "1"),
                ("Noise Multiplier D", "1"),
                ("Allow Abandon and Defaults", "true"),
                ("Probability Truly Liable", "0.5"),
                ("Noise to Produce Case Strength", "0.35"),
                ("Issue", "Liability"),
                ("Proportion of Costs at Beginning", "0.5"),
            };
            var matches = distinctOptionSets.Where(x => settingsToFind.All(y => x.VariableSettings[y.Item1].ToString() == y.Item2.ToString())).ToList();
            var namesOfMatches = matches.Select(x => x.Name).ToList();
            var mappedNamesOfMatches = namesOfMatches.Select(x => map[x]).ToList();

            var mappedNames = distinctOptionSets.OrderBy(x => x.Name).ToList();
            var numDistinctNames = mappedNames.OrderBy(x => x.Name).Distinct().Count();
            if (numDistinctNames != distinctOptionSets.Count())
                throw new Exception();
            var numFullNames = gameOptionsSets.Select(x => x.Name).Distinct().Count();
            var variableSettingsList = gameOptionsSets.Select(x => x.VariableSettings).ToList();
            var formattedTableOfOptionsSets = variableSettingsList.ToFormattedTable();
            TabbedText.WriteLine($"Processing {numDistinctNames} option sets (from a list of {gameOptionsSets.Count} containing redundancies) "); // redundancies may exist because the options set list repeats the baseline value -- but here we want only the distinct ones (this will be filtered by GetCSVLines)
            TabbedText.WriteLine("All options sets (including redundancies)");
            TabbedText.WriteLine(formattedTableOfOptionsSets);

            foreach (string fileSuffix in equilibriumTypeSuffixes)
            {
                TabbedText.WriteLine($"Processing equilibrium type {fileSuffix}");
                bool includeHeader = firstEqOnly || fileSuffix == correlatedEquilibriumFileSuffix;
                List<List<string>> outputLines = GetCSVLines(distinctOptionSets, map, rowsToGet, replacementRowNames, filePrefix, fileSuffix, path, includeHeader, columnsToGet, replacementColumnNames);
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
            TextFileManage.CreateTextFile(outputFileFullPath, cumResults);
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

        private static List<LitigGameOptions> GetFeeShiftingGameOptionsSets()
        {
            var launcher = new LitigGameLauncher();
            return launcher.GetFeeShiftingArticleGamesSets(false, true).SelectMany(x => x).ToList();
        }

        private static List<List<string>> GetCSVLines(List<LitigGameOptions> gameOptionsSets, Dictionary<string, string> map, List<string> rowsToGet, List<string> replacementRowNames, string filePrefix, string fileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames)
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

        private static string MakeString(List<List<string>> values)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < values.Count(); i++)
                b.AppendLine(String.Join(",", values[i]));
            return b.ToString();
        }

        private static void FileFixer()
        {
            string reportFolder = Launcher.ReportFolder();
            var files = Directory.GetFiles(reportFolder);
            foreach (var filename in files.Where(x => x.EndsWith(".tex")))
            {
                string[] lines = TextFileManage.GetLinesOfFile(filename);
                bool changeNeeded = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains("NaN"))
                    {
                        changeNeeded = true;
                        int initialNumberStart = line.IndexOf("(") + 1;
                        int numberCharacters = 4;
                        string correctNumberString = new string(line.Skip(initialNumberStart).Take(numberCharacters).ToArray());
                        int wrongNumberStart = line.IndexOf("NaN");
                        numberCharacters = 3;
                        string correctString = line.Replace("NaN", correctNumberString);
                        lines[i] = correctString;
                        //TabbedText.WriteLine($"{file}: {line}");
                    }
                }
                if (changeNeeded)
                {
                    string revisedFileContents = String.Join(Environment.NewLine, lines);
                    TextFileManage.CreateTextFile(filename, revisedFileContents);
                }
            }
        }

        internal static void ProduceLatexDiagramsFromTexFiles()
        {
            foreach (string fileSuffix in equilibriumTypeSuffixes)
            {
                FeeShiftingDataProcessing.ProduceLatexDiagrams("-scr" + fileSuffix);
                FeeShiftingDataProcessing.ProduceLatexDiagrams("-heatmap" + fileSuffix);
            }
        }

        public static void OrganizeIntoFolders(bool doDeletion)
        {
            string reportFolder = Launcher.ReportFolder();
            string[] filesInFolder = DeleteAuxiliaryFiles(reportFolder);
            filesInFolder = Directory.GetFiles(reportFolder);

            string[] getExtensions(string eqType) => new string[] { firstEqOnly ? $".csv" : $"-{eqType}.csv", $"-heatmap-{eqType}.pdf", $"-heatmap-{eqType}.tex", $"-scr-{eqType}.pdf", $"-scr-{eqType}.tex", $"-scr-{eqType}.csv" };
            List<(string folderName, string[] extensions)> placementRules = new List<(string folderName, string[] extensions)>()
            {
                ("First Equilibrium", getExtensions("Eq1")),
                ("First Equilibrium", getExtensions("eq1")), // we're inconsistent in capitalization
                ("EFG Files", new string[] { ".efg" }),
                ("Equilibria Files", new string[] { "-equ.csv" }),
                ("Logs", new string[] { "-log.txt" }),
            };
            if (!firstEqOnly)
            {
                placementRules.InsertRange(0, new List<(string folderName, string[] extensions)>()
                {
                    ("Correlated Equilibrium", getExtensions("Corr")),
                    ("Average Equilibrium", getExtensions("Avg")),
                }
                );
            }

            var launcher = new LitigGameLauncher();
            var sets = launcher.GetFeeShiftingArticleGamesSets(false, true);
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            var setNames = launcher.NamesOfFeeShiftingArticleSets;
            string masterReportName = launcher.MasterReportNameForDistributedProcessing;
            List<(List<LitigGameOptions> theSet, string setName)> setsWithNames = sets.Zip(setNames, (s, sn) => (s, sn)).ToList();
            foreach (var setWithName in setsWithNames)
            {
                string subfolderName = Path.Combine(reportFolder, setWithName.setName);
                if (!Directory.GetDirectories(reportFolder).Any(x => x == subfolderName))
                    Directory.CreateDirectory(subfolderName);
                foreach (string folderName in placementRules.Select(x => x.folderName))
                {
                    var subsubfolderName = Path.Combine(subfolderName, folderName);
                    if (!Directory.GetDirectories(subfolderName).Any(x => x == subsubfolderName))
                        Directory.CreateDirectory(subsubfolderName);
                }
                foreach (var optionsSet in setWithName.theSet)
                {
                    string originalName = optionsSet.Name;
                    string filenameMapped = map[originalName];
                    foreach (var placementRule in placementRules)
                    {
                        var subsubfolderName = Path.Combine(subfolderName, placementRule.folderName);
                        foreach (var extension in placementRule.extensions)
                        {
                            string combinedNameSource = Path.Combine(reportFolder, masterReportName + "-" + filenameMapped + extension);
                            string targetFileName = originalName.Replace("FSA ", "").Replace("-Eq1", "-eq1").Replace("  ", " ") + extension;
                            if (File.Exists(combinedNameSource))
                            {
                                string combinedNameTarget = Path.Combine(subsubfolderName, targetFileName);
                                File.Copy(combinedNameSource, combinedNameTarget, true);
                                if (doDeletion)
                                    File.Delete(combinedNameSource);
                            }
                        }
                    }
                    string fullOriginalFilename = Path.Combine(reportFolder, filenameMapped);
                }
            }

        }



        private static string[] DeleteAuxiliaryFiles(string reportFolder)
        {
            string[] filesInFolder = Directory.GetFiles(reportFolder);
            string[] fileExtensionsTriggeringDeletion = new string[] { ".aux", ".log", ".gz" };
            foreach (string file in filesInFolder)
            {
                foreach (string deletionTrigger in fileExtensionsTriggeringDeletion)
                    if (file.EndsWith(deletionTrigger))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }

                    }
            }

            return filesInFolder;
        }

        public static void ExampleLatexDiagramsAggregatingReports(bool isStacked = true)
        {

            var lineScheme = new List<string>()
                {
                    "blue, opacity=0.70, line width=0.5mm, double",
                    "red, opacity=0.70, line width=1mm, dashed",
                    "green, opacity=0.70, line width=1mm, solid",
                };
            var dataSeriesNames = new List<string>()
            {
                "Blue series",
                "Red series",
                "Green series"
            };

            int numMiniGraphDataSeries = lineScheme.Count();
            Random ran = new Random();
            int numMiniGraphXValues = 8;
            int numMiniGraphYValues = 10;
            int numMacroGraphXValues = 6;
            int numMacroGraphYValues = 4;
            List<double?> getMiniGraphData() => Enumerable.Range(0, numMiniGraphXValues).Select(x => (double?)ran.NextDouble()).ToList();
            List<List<double?>> getMiniGraph() => Enumerable.Range(0, numMiniGraphDataSeries).Select(x => getMiniGraphData()).ToList();
            List<List<double?>> getMiniGraphAdjusted()
            {
                var miniGraphData = getMiniGraph();
                if (!isStacked)
                    return miniGraphData;

                // for stacked graphs, we need to make sure that the total height is no greater than 1.0.
                int numToStack = miniGraphData.Count();
                int numXAxisPoints = miniGraphData.First().Count();
                for (int c = 0; c < numXAxisPoints; c++)
                {
                    double sum = 0;
                    for (int r = 0; r < numToStack; r++)
                    {
                        sum += miniGraphData[r][c] ?? 0;
                    }
                    if (sum > 1.0)
                    {
                        for (int r = 0; r < numToStack; r++)
                        {
                            if (miniGraphData[r][c] != null)
                                miniGraphData[r][c] /= sum;
                        }
                    }
                }
                return miniGraphData;
            }
            TikzLineGraphData miniGraphData() => new TikzLineGraphData(getMiniGraphAdjusted(), lineScheme, dataSeriesNames);

            List<TikzLineGraphData> lineGraphDataX() => Enumerable.Range(0, numMacroGraphXValues).Select(x => miniGraphData()).ToList();
            List<List<TikzLineGraphData>> lineGraphDataXAndY() => Enumerable.Range(0, numMacroGraphYValues).Select(x => lineGraphDataX()).ToList();

            var lineGraphData = lineGraphDataXAndY();

            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = Enumerable.Range(0, numMacroGraphXValues).Select(x => $"X{x}").ToList(),
                majorXAxisLabel = "Major X",
                majorYValueNames = Enumerable.Range(0, numMacroGraphYValues).Select(y => $"Y{y}").ToList(),
                majorYAxisLabel = "Major Y",
                minorXValueNames = Enumerable.Range(0, numMiniGraphXValues).Select(x => $"x{x}").ToList(),
                minorXAxisLabel = "Minor X",
                minorYValueNames = Enumerable.Range(0, numMiniGraphYValues).Select(y => $"y{y}").ToList(),
                minorYAxisLabel = "Minor Y",
                isStacked = isStacked,
                lineGraphData = lineGraphData,
            };


            var result = TikzHelper.GetStandaloneDocument(r.GetDrawCommands(), new List<string>() { "xcolor" }, additionalHeaderInfo: $@"
\usetikzlibrary{{calc}}
\usepackage{{relsize}}
\tikzset{{fontscale/.style = {{font=\relsize{{#1}}}}}}");
        }

        public record AggregatedGraphInfo(string topicName, List<string> columnsToGet, List<string> lineScheme, string minorXAxisLabel = "Fee Shifting Multiplier", string minorYAxisLabel = "\\$", string majorYAxisLabel = "Costs Multiplier", double? maximumValueMicroY = null, bool isStacked = false);

        public static void ProduceLatexDiagramsAggregatingReports()
        {
            string reportFolder = Launcher.ReportFolder();
            LitigGameLauncher launcher = new LitigGameLauncher();
            string filename = launcher.MasterReportNameForDistributedProcessing + "--output.csv";
            string pathAndFilename = Path.Combine(reportFolder, filename);
            string outputFolderName = "Aggregated Data";
            string outputFolderPath = Path.Combine(reportFolder, outputFolderName);
            if (!Directory.GetDirectories(reportFolder).Any(x => x == outputFolderName))
                Directory.CreateDirectory(outputFolderPath);

            var sets = launcher.GetFeeShiftingArticleGamesSets(false, true);
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            var setNames = launcher.NamesOfFeeShiftingArticleSets;
            string masterReportName = launcher.MasterReportNameForDistributedProcessing;
            List<(List<LitigGameOptions> theSet, string setName)> setsWithNames = sets.Zip(setNames, (s, sn) => (s, sn)).ToList();

            foreach (bool useRiskAversionForNonRiskReports in new bool[] { false, true })
            {

                List<LitigGameLauncher.FeeShiftingArticleVariationSetInfo> variations = launcher.GetFeeShiftingArticleVariationInfoList(useRiskAversionForNonRiskReports);

                var plaintiffDefendantAndOthersLineScheme = new List<string>()
                {
                  "blue, opacity=0.70, line width=0.5mm, double",
                  "orange, opacity=0.70, line width=1mm, dashed",
                  "green, opacity=0.70, line width=1mm, solid",
                };

                var dispositionLineScheme = new List<string>()
                {
                  "violet, line width=3mm, solid",
                  "magenta, line width=2.5mm, dashed",
                  "blue, line width=1mm, double",
                  "green, line width=1.5mm, densely dotted",
                  "yellow, line width=1.5mm, dotted",
                  "orange, line width=1mm, solid",
                  "red, line width=0.5mm, densely dashed",
                };


                string riskAversionString = useRiskAversionForNonRiskReports ? " (Risk Averse)" : "";
                List<AggregatedGraphInfo> welfareMeasureColumns = new List<AggregatedGraphInfo>()
                {
                    new AggregatedGraphInfo($"Accuracy and Expenditures{riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList()),
                    new AggregatedGraphInfo($"Accuracy{riskAversionString}", new List<string>() { "False Positive Inaccuracy", "False Negative Inaccuracy" }, plaintiffDefendantAndOthersLineScheme.Take(2).ToList()),
                    new AggregatedGraphInfo($"Expenditures{riskAversionString}", new List<string>() { "Expenditures" }, plaintiffDefendantAndOthersLineScheme.Skip(2).Take(1).ToList()),
                    new AggregatedGraphInfo($"Offers{riskAversionString}", new List<string>() { "P Offer", "D Offer" }, plaintiffDefendantAndOthersLineScheme.Take(2).ToList()),
                    new AggregatedGraphInfo($"Trial{riskAversionString}", new List<string>() { "Trial" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0),
                    new AggregatedGraphInfo($"Trial Outcomes{riskAversionString}", new List<string>() { "P Win Probability" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0),
                    new AggregatedGraphInfo($"Disposition{riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, isStacked:true)
                };

                foreach (double? limitToCostsMultiplier in new double?[] { 1.0, null })
                {
                    foreach (var welfareMeasureInfo in welfareMeasureColumns)
                    {
                        ProcessForWelfareMeasure(launcher, pathAndFilename, outputFolderPath, variations, welfareMeasureInfo, limitToCostsMultiplier);
                    }
                }
            }

            WaitForProcessesToFinish();
            Task.Delay(1000);
            DeleteAuxiliaryFiles(outputFolderPath);
        }

        private static void ProcessForWelfareMeasure(LitigGameLauncher launcher, string pathAndFilename, string outputFolderPath, List<LitigGameLauncher.FeeShiftingArticleVariationSetInfo> variations, AggregatedGraphInfo aggregatedGraphInfo, double? limitToCostsMultiplier)
        {
            List<(string columnName, string expectedText)[]> collectedRowsToFind = new List<(string columnName, string expectedText)[]>();
            double?[,] valuesFromCSVAllRows = null;
            int collectedValuesIndex = 0;
            foreach (bool stepDefiningRowsToFind in new bool[] { true, false })
            {
                foreach (string equilibriumType in eqToRun)
                {
                    string eqAbbreviation = equilibriumType switch { "Correlated" => "-Corr", "Average" => "-Avg", "First" => "-Eq1", _ => throw new NotImplementedException() };
                    foreach (var variation in variations)
                    {
                        // This is a full report. The variation controls the big x axis. The big y axis is the costs multiplier.
                        // The small x axis is the fee shifting multiplier. And the y axis represents the values that we are loading. 
                        // We then have a different line for each data series.

                        var requirementsForEachVariation = variation.requirementsForEachVariation;
                        List<List<TikzLineGraphData>> lineGraphData = new List<List<TikzLineGraphData>>();
                        foreach (double macroYValue in limitToCostsMultiplier == null ? launcher.CriticalCostsMultipliers.OrderBy(x => x).ToList() : new List<double> { (double) limitToCostsMultiplier })
                        {
                            List<TikzLineGraphData> lineGraphDataForRow = new List<TikzLineGraphData>();
                            foreach (var macroXValue in requirementsForEachVariation)
                            {
                                var columnsToMatch = macroXValue.columnMatches.ToList();
                                columnsToMatch.Add(("Filter", "All"));
                                columnsToMatch.Add(("Equilibrium Type", equilibriumType));

                                List<List<double?>> dataForMiniGraph = null;

                                foreach (var microXValue in launcher.CriticalFeeShiftingMultipliers.OrderBy(x => x))
                                {
                                    if (stepDefiningRowsToFind)
                                    {
                                        var modifiedRowsToFind = columnsToMatch.WithReplacement("Fee Shifting Multiplier", microXValue).WithReplacement("Costs Multiplier", macroYValue).Select(x => (x.Item1, x.Item2.ToString())).ToArray();
                                        collectedRowsToFind.Add(modifiedRowsToFind);
                                    }
                                    else
                                    {
                                        int welfareColumnsCount = aggregatedGraphInfo.columnsToGet.Count();
                                        if (dataForMiniGraph == null)
                                        {
                                            dataForMiniGraph = new List<List<double?>>();
                                            for (int i = 0; i < welfareColumnsCount; i++)
                                                dataForMiniGraph.Add(new List<double?>());
                                        }
                                        for (int i = 0; i < welfareColumnsCount; i++)
                                        {
                                            dataForMiniGraph[i].Add(valuesFromCSVAllRows[collectedValuesIndex, i]);
                                        }
                                        collectedValuesIndex++;
                                    }
                                }
                                if (!stepDefiningRowsToFind)
                                {
                                    TikzLineGraphData miniGraphData = new TikzLineGraphData(dataForMiniGraph, aggregatedGraphInfo.lineScheme, aggregatedGraphInfo.columnsToGet);
                                    lineGraphDataForRow.Add(miniGraphData);
                                }
                            }
                            if (!stepDefiningRowsToFind)
                                lineGraphData.Add(lineGraphDataForRow);
                        }

                        if (!stepDefiningRowsToFind)
                            CreateAggregatedLineGraphFromData(launcher, outputFolderPath, aggregatedGraphInfo, equilibriumType, variation, requirementsForEachVariation, lineGraphData, limitToCostsMultiplier);

                    }
                }
                if (stepDefiningRowsToFind)
                {
                    valuesFromCSVAllRows = CSVData.GetCSVData_SinglePass(pathAndFilename, collectedRowsToFind.ToArray(), aggregatedGraphInfo.columnsToGet.ToArray(), cacheFile: true);
                }
            }
        }

        private static void CreateAggregatedLineGraphFromData(LitigGameLauncher launcher, string outputFolderPath, AggregatedGraphInfo aggregatedGraphInfo, string equilibriumType, LitigGameLauncher.FeeShiftingArticleVariationSetInfo variation, List<LitigGameLauncher.FeeShiftingArticleVariationInfo> requirementsForEachVariation, List<List<TikzLineGraphData>> lineGraphData, double? limitToCostsMultiplier)
        {
            // make all data proportional to rounded up maximum value
            double maximumValueMicroY;
            if (aggregatedGraphInfo.maximumValueMicroY is not double presetMax)
            {
                var values = lineGraphData.SelectMany(macroRow => macroRow.SelectMany(macroColumn => macroColumn.proportionalHeights.SelectMany(microRow => microRow))).Where(x => x != null);
                maximumValueMicroY = values.Any() ? values.Select(x => (double)x).Max() : 1.0;
            }
            else
                maximumValueMicroY = presetMax;

            double RoundUp(double input, int places)
            {
                double multiplier = Math.Pow(10, Convert.ToDouble(places));
                return Math.Ceiling(input * multiplier) / multiplier;
            }
            maximumValueMicroY = RoundUp(maximumValueMicroY, 1);
            if (maximumValueMicroY is 0.7 or 0.8 or 0.9)
                maximumValueMicroY = 1.0; // just round all the way up

            foreach (var macroRow in lineGraphData)
            {
                for (int i = 0; i < macroRow.Count; i++)
                {
                    TikzLineGraphData macroColumn = macroRow[i];
                    macroRow[i] = macroColumn with { proportionalHeights = macroColumn.proportionalHeights.Select(x => x.Select(y => y / maximumValueMicroY).ToList()).ToList() };
                }
            }

            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = requirementsForEachVariation.Select(x => x.nameOfVariation).ToList(),
                majorXAxisLabel = variation.nameOfSet,
                majorYValueNames = limitToCostsMultiplier == null ? launcher.CriticalCostsMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList() : new List<string>() { limitToCostsMultiplier.ToString() },
                majorYAxisLabel = aggregatedGraphInfo.majorYAxisLabel,
                minorXValueNames = launcher.CriticalFeeShiftingMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList(),
                minorXAxisLabel = aggregatedGraphInfo.minorXAxisLabel,
                minorYValueNames = Enumerable.Range(0, 11).Select(y => y switch { 0 => "0", 10 => maximumValueMicroY.ToString(), _ => " " }).ToList(),
                minorYAxisLabel = aggregatedGraphInfo.minorYAxisLabel,
                yAxisSpaceMicro = 0.8,
                yAxisLabelOffsetMicro = 0.4,
                xAxisSpaceMicro = 1.1,
                xAxisLabelOffsetMicro = 0.8,
                isStacked = aggregatedGraphInfo.isStacked,
                lineGraphData = lineGraphData,
            };
            var result = r.GetStandaloneDocument();

            string costsLevel = "";
            if (limitToCostsMultiplier != null)
                costsLevel = $" Costs {limitToCostsMultiplier}";
            string equilibriumTypeAdj = equilibriumType == "First" ? "" : " (" + equilibriumType + ")";

            string subfolderName = Path.Combine(outputFolderPath, variation.nameOfSet);
            if (!Directory.GetDirectories(outputFolderPath).Any(x => x == subfolderName))
                Directory.CreateDirectory(subfolderName);

            string outputFilename = Path.Combine(subfolderName, $"{aggregatedGraphInfo.topicName} Varying {variation.nameOfSet}{costsLevel}{equilibriumTypeAdj}.tex");
            TextFileManage.CreateTextFile(outputFilename, result);
            ExecuteLatexProcess(outputFolderPath, outputFilename);
        }



        //private string SeparateOptionSetsByRelevantVariable(List<LitigGameOptions> optionSets)
        //{
        //    foreach (var e in optionSets.First().VariableSettings)
        //    {
        //        int count = optionSets.Count(x => x.VariableSettings[e.Key] == e.Value);
        //        if (count == 1)
        //        {
        //            string variableThatDiffers = e.Key;
        //        }
        //    }
        //}
    }
}
