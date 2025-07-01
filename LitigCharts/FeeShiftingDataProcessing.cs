using ACESim;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.Tikz;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tensorflow;
using static ACESimBase.Games.LitigGame.ManualReports.CostBreakdownReport;
using static LitigCharts.DataProcessingUtils;
using static LitigCharts.Runner;

namespace LitigCharts
{
    public class FeeShiftingDataProcessing : DataProcessingBase
    {
        #region CSV reports and files

        public static void BuildMainReport(LitigGameLauncherBase launcher, DataBeingAnalyzed article)
        {
            List<string> rowsToGet = new List<string> { "All", "DisputeArises", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> replacementRowNames = new List<string> { "All", "Dispute Arises", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> columnsToGet = new List<string> { "Exploit", "Seconds", "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "TotWealth", "WealthLoss", "PWelfare", "DWelfare", "PDSWelfareLoss", "SWelfareLoss", "OpportunityCost", "HarmCost", "TrulyLiableHarmCost", "TrulyNotLiableHarmCost", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins", "PrimaryAction" };
            List<string> replacementColumnNames = new List<string> { "Exploitability", "Calculation Time", "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Probability", "P Wealth", "D Wealth", "Total Wealth", "Wealth Loss", "P Welfare", "D Welfare", "Pre-Dispute Social Welfare Loss", "Social Welfare Loss", "Opportunity Cost", "Harm Cost", "Truly Liable Harm Cost", "Truly Not Liable Harm Cost","Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins", "Appropriation" };
            if (article == DataBeingAnalyzed.EndogenousDisputesArticle)
            {
                columnsToGet.AddRange(["Activity", "Accident", "WrongAttrib", "PrecPower", "PrecLevel", "BCRatio"]);
                replacementColumnNames.AddRange(["Engages in Activity", "Accident Occurs", "Wrongful Attribution", "Precaution Power", "Precaution Level", "Benefit-Cost Ratio"]);
                for (int i = 1; i <= 5; i++)
                {
                    columnsToGet.Add($"PrecPower{i}");
                    replacementColumnNames.Add($"Precaution Power {i}");
                }
            }
            BuildReportHelper(launcher, rowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "output");
        }

        private static void BuildReportHelper(LitigGameLauncherBase launcher, List<string> rowsToGet, List<string> replacementRowNames, List<string> columnsToGet, List<string> replacementColumnNames, string filenameCore)
        {
            bool onlyAllFilter = false;
            if (onlyAllFilter)
            {
                rowsToGet = rowsToGet.Take(1).ToList();
                replacementRowNames = replacementRowNames.Take(1).ToList();
            }

            var gameOptionsSets = launcher.GetOptionsSets();
            string path = launcher.GetReportFolder();
            string outputFileFullPath = launcher.GetReportFullPath(filenameCore, ".csv");
            string cumResults = "";

            // look up particular settings here if desired (not usually needed, so findSpecificSettings should be false)
            // Note that the settings are determined by specific launcher classes, including values in DefaultVariableValues (if something is omitted then the output file may be out of alignment)
            bool findSpecificSettings = false;
            List<(string, string)> settingsToFind = launcher.DefaultVariableValues;
            var matches = gameOptionsSets.Where(x => !findSpecificSettings || settingsToFind.All(y => x.VariableSettings[y.Item1].ToString() == y.Item2.ToString())).ToList();
            var namesOfMatches = matches.Select(x => x.Name).ToList();

            var mappedNames = gameOptionsSets.OrderBy(x => x.Name).ToList();
            var numDistinctNames = mappedNames.OrderBy(x => x.Name).Distinct().Count();
            if (numDistinctNames != gameOptionsSets.Count())
                throw new Exception();
            var numFullNames = gameOptionsSets.Select(x => x.Name).Distinct().Count();
            var variableSettingsList = gameOptionsSets.Select(x => x.VariableSettings).ToList();
            var formattedTableOfOptionsSets = variableSettingsList.ToFormattedTable();
            TabbedText.WriteLine($"Processing {numDistinctNames} option sets (from a list of {gameOptionsSets.Count} potentially containing redundancies) "); // redundancies may exist because the options set list repeats the baseline value -- but here we want only the distinct ones (this will be filtered by GetCSVLines)
            TabbedText.WriteLine("All options sets (including redundancies)");
            TabbedText.WriteLine(formattedTableOfOptionsSets);

            int excelRowIndex = 1;
            foreach (string fileSuffix in equilibriumTypeSuffixes)
            {
                TabbedText.WriteLine($"Processing equilibrium type {fileSuffix}");
                bool includeHeader = singleEquilibriumOnly || fileSuffix == correlatedEquilibriumFileSuffix;
                List<List<string>> outputLines = GetCSVLines(gameOptionsSets.Select(x => (GameOptions)x).ToList(), rowsToGet, replacementRowNames, filePrefix(launcher), fileSuffix, path, includeHeader, columnsToGet, replacementColumnNames);
                if (includeHeader)
                    outputLines[0].Insert(0, "Equilibrium Type");
                string equilibriumType = fileSuffix switch
                {
                    correlatedEquilibriumFileSuffix => "Correlated",
                    averageEquilibriumFileSuffix => "Average",
                    firstEquilibriumFileSuffix => "First Eq",
                    "" => "Only Eq",
                    _ => "Other"
                };
                foreach (List<string> bodyLine in outputLines.Skip(includeHeader ? 1 : 0))
                    bodyLine.Insert(0, equilibriumType);
                string resultForEquilibrium = MakeString(outputLines);
                cumResults += resultForEquilibrium;
                excelRowIndex++;
            }
            TextFileManage.CreateTextFile(outputFileFullPath, cumResults);
        }

        public static void BuildOffersReport(LitigGameLauncherBase launcher)
        {
            var gameDefinition = new LitigGameDefinition();
            gameDefinition.Setup(launcher.GetOptionsSets().First());
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

            BuildReportHelper(launcher, filtersOfRowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "offers");
        }

        private static List<(string[] stringValues, double[] numericValues)> GetDataFromCSV(string filename, int columnToMatch, string requiredContentsOfColumnToMatch, int[] stringColumns, int[] numericColumns)
        {
            var lines = new List<(string[] stringValues, double[] numericValues)>();
            using (var reader = new StreamReader(filename))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split(',');
                    if (values[columnToMatch] == requiredContentsOfColumnToMatch)
                    {
                        var stringValues = stringColumns.Select(x => values[x]).ToArray();
                        var numericValues = numericColumns.Select(x => values[x] == "" ? 0.0 : double.Parse(values[x])).ToArray();
                        lines.Add((stringValues, numericValues));
                    }
                }
            }
            return lines;
        }

        public static List<(string folderName, string[] extensions)> GetFilePlacementRules(DataBeingAnalyzed article)
        {
            HashSet<string> alreadyProcessed = new();

            var placementRules = new List<(string folderName, string[] extensions)>
            {
                // ("First Equilibrium", GetFileTypeExtensionsForEquilibriumType("Eq1")),
                ("EFG Files", new[] { ".efg" }),
                ("Equilibria Files", new[] { "-equ.csv" }),
                ("Logs", new[] { "-log.txt" }),
                ("Latex underlying data", expandToIncludeAdditionalEquilibria(new[] { "-offers.csv", "-fileans.csv", "-stagecostlight.csv", "-stagecostdark.csv", "-costbreakdowndata.csv" })),
                ("Latex files", expandToIncludeAdditionalEquilibria(new[] { "-offers.tex", "-fileans.tex", "-stagecostlight.tex", "-stagecostdark.tex", "-costbreakdownlight.tex", "-costbreakdowndark.tex" })),
                ("File-Answer Diagrams", expandToIncludeAdditionalEquilibria( new[] { "-fileans.pdf" })),
                ("Offer Heatmaps", expandToIncludeAdditionalEquilibria( new[] { "-offers.pdf" })),
                ("Cost Breakdown Diagrams (Normal)", expandToIncludeAdditionalEquilibria( new[] { "-costbreakdownlight.pdf" })),
                ("Cost Breakdown Diagrams (Dark Mode)", expandToIncludeAdditionalEquilibria( new[] { "-costbreakdowndark.pdf" })),
                ("Cross Tabs", expandToIncludeAdditionalEquilibria(new[] { ".csv" })),
            };

            if (article != DataBeingAnalyzed.EndogenousDisputesArticle)
            {
                placementRules.AddRange(new List<(string folderName, string[] extensions)>
                {
                    ("Stage Costs Diagrams (Normal)", expandToIncludeAdditionalEquilibria(new[] { "-stagecostlight.pdf" })),
                    ("Stage Costs Diagrams (Dark Mode)", expandToIncludeAdditionalEquilibria(new[] { "-stagecostdark.pdf" })),
                }
                );
            }

            string[] notAlreadyProcessed(string[] original)
            {
                List<string> result = new();
                foreach (string o in original)
                    if (!alreadyProcessed.Contains(o))
                    {
                        result.Add(o);
                        alreadyProcessed.Add(o);
                    }
                return result.ToArray();
            }

            string[] expandToIncludeAdditionalEquilibria(string[] original)
            {
                if (singleEquilibriumOnly)
                {
                    return original;
                }
                var l = original.ToList();
                foreach (var item in original)
                    foreach (int i in Enumerable.Range(1, 100))
                        l.Add(item.Replace(".", $"-Eq{i}."));
                return notAlreadyProcessed(l.ToArray());
            }

            string[] expandToIncludeSpecificDiagrams(string eqType) => notAlreadyProcessed(new[]
            {
                singleEquilibriumOnly ? ".csv" : $"-{eqType}.csv",
                $"-offers-{eqType}.pdf", $"-offers-{eqType}.tex",
                $"-fileans-{eqType}.pdf", $"-fileans-{eqType}.tex",
                $"-stagecostlight-{eqType}.pdf", $"-stagecostlight-{eqType}.tex", $"-stagecostlight-{eqType}.csv",
                $"-stagecostdark-{eqType}.pdf", $"-stagecostdark-{eqType}.tex", $"-stagecostdark-{eqType}.csv",
            });

            if (!singleEquilibriumOnly)
            {
                placementRules.InsertRange(0, new List<(string, string[])>
                {
                    ("Correlated Equilibrium", expandToIncludeSpecificDiagrams("Corr")),
                    ("Average Equilibrium", expandToIncludeSpecificDiagrams("Avg")),
                });

                for (int i = 2; i <= 100; i++)
                    placementRules.Add(("Additional Equilibria", expandToIncludeSpecificDiagrams($"Eq{i}")));
            }

            return placementRules;
        }

        #endregion

        #region Latex diagrams for individual simulation results

        static bool includeHeatmaps = false;
        internal static void ExecuteLatexProcessesForExisting() => ExecuteLatexProcessesForExisting(result => includeHeatmaps || (!result.Contains("offers") && !result.Contains("fileans")));

        internal static void ProduceLatexDiagramsFromTexFiles(LitigGameLauncherBase launcher, DataBeingAnalyzed article)
        {
            bool workExists = true;
            int numAttempts = 0; // sometimes the Latex processes fail, so we try again if any of our files to create are missing
            const int maxAttempts = 10;
            while (workExists && numAttempts < maxAttempts)
            {
                List<(string path, string combinedPath, string optionSetName, string fileSuffix)> processesToLaunch = new List<(string path, string combinedPath, string optionSetName, string fileSuffix)>();
                workExists = processesToLaunch.Any();
                foreach (string fileSuffix in equilibriumTypeSuffixes)
                {
                    List<string> extensions = ["-costbreakdownlight" + fileSuffix, "-costbreakdowndark" + fileSuffix, "-offers" + fileSuffix, "-fileans" + fileSuffix];
                    if (article != DataBeingAnalyzed.EndogenousDisputesArticle)
                        extensions.AddRange(["-stagecostlight" + fileSuffix, "-stagecostdark" + fileSuffix]);
                    List<(string path, string combinedPath, string optionSetName, string fileSuffix)> someToDo = FeeShiftingDataProcessing.GetLatexProcessPlans(launcher, extensions.ToArray(), avoidProcessingIfPDFExists || numAttempts > 1);
                    processesToLaunch.AddRange(someToDo);
                }
                ProduceLatexDiagrams(processesToLaunch);
                numAttempts++;
            }
        }

        #endregion

        #region Latex diagrams for aggregated results

        public static void ExampleLatexDiagramsAggregatingReports(TikzAxisSet.GraphType graphType = TikzAxisSet.GraphType.Line)
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
                if (graphType != TikzAxisSet.GraphType.StackedBar)
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
                graphType = graphType,
                lineGraphData = lineGraphData,
            };


            var drawCommands = r.GetStandaloneDocument();
        }

        public record AggregatedGraphInfo(string topicName, List<string> columnsToGet, List<string> lineScheme, string minorXAxisLabel = "TBD", string minorXAxisLabelShort = "TBD", string minorYAxisLabel = "\\$", string majorYAxisLabel = "Costs Multiplier", double? maximumValueMicroY = null, TikzAxisSet.GraphType graphType = TikzAxisSet.GraphType.Line, Func<double?, double?> scaleMiniGraphValues = null, string filter = "All");

        public static void ProduceLatexDiagramsAggregatingReports(LitigGameLauncherBase launcher, DataBeingAnalyzed article)
        {
            string reportFolder = Launcher.ReportFolder();
            string pathAndFilename = launcher.GetReportFullPath(null, "output.csv");
            string outputFolderName = "Aggregated Data";
            string outputFolderPath = Path.Combine(reportFolder, outputFolderName);
            if (!VirtualizableFileSystem.Directory.GetDirectories(reportFolder).Any(x => x == outputFolderName))
                VirtualizableFileSystem.Directory.CreateDirectory(outputFolderPath);

            foreach (bool useRiskAversionForNonRiskReports in new bool[] { false, true })
            {
                var variations = launcher.GetSimulationSetsIdentifiers(useRiskAversionForNonRiskReports ? LitigGameLauncherBase.RequireModerateRiskAversion : null);

                var plaintiffDefendantAndOthersLineScheme = new List<string>()
                {
                  "blue, opacity=0.70, line width=0.5mm, double",
                  "orange, opacity=0.70, line width=1mm, dashed",
                  "green, opacity=0.70, line width=1mm, solid",
                };

                var lossesLineScheme = new List<string>()
                {
                  "green, opacity=0.70, line width=0.5mm, dotted",
                  "yellow, opacity=0.70, line width=0.75mm, dashed",
                  "blue, opacity=0.70, line width=1mm, dashdotdotted",
                  "red, opacity=0.70, line width=1mm, solid",
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

                string generalFilter = article switch
                {
                    DataBeingAnalyzed.CorrelatedSignalsArticle => "All",
                    DataBeingAnalyzed.EndogenousDisputesArticle => "Dispute Arises",
                    _ => "All"
                };

                string riskAversionString = useRiskAversionForNonRiskReports ? " (Risk Averse)" : "";
                List<AggregatedGraphInfo> welfareMeasureColumns = new List<AggregatedGraphInfo>()
                {
                    new AggregatedGraphInfo($"Accuracy and Expenditures{riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList(), filter:generalFilter),
                    new AggregatedGraphInfo($"Accuracy{riskAversionString}", new List<string>() { "False Positive Inaccuracy", "False Negative Inaccuracy" }, plaintiffDefendantAndOthersLineScheme.Take(2).ToList(), filter:generalFilter),
                    new AggregatedGraphInfo($"Expenditures{riskAversionString}", new List<string>() { "Expenditures" }, plaintiffDefendantAndOthersLineScheme.Skip(2).Take(1).ToList(), filter:generalFilter),
                    new AggregatedGraphInfo($"Offers{riskAversionString}", new List<string>() { "P Offer", "D Offer" }, plaintiffDefendantAndOthersLineScheme.Take(2).ToList(), filter:generalFilter),
                    new AggregatedGraphInfo($"Social Welfare Loss{riskAversionString}", new List<string>() { "Opportunity Cost", "Harm Cost", "Expenditures", "Social Welfare Loss" }, lossesLineScheme),
                    new AggregatedGraphInfo($"Wealth Loss{riskAversionString}", new List<string>() { "Wealth Loss" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList()),
                    new AggregatedGraphInfo($"Trial{riskAversionString}", new List<string>() { "Trial" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0, filter:generalFilter),
                    new AggregatedGraphInfo($"Trial Outcomes{riskAversionString}", new List<string>() { "P Win Probability" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0, filter:generalFilter),
                    new AggregatedGraphInfo($"Disposition{riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar, filter:generalFilter),
                    new AggregatedGraphInfo($"Accuracy and Expenditures (Truly Liable){riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList(), filter:"Truly Liable"),
                    new AggregatedGraphInfo($"Accuracy and Expenditures (Truly Not Liable){riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList(), filter: "Truly Not Liable"),
                    new AggregatedGraphInfo($"Disposition (Truly Liable){riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar, filter:"Truly Liable"),
                    new AggregatedGraphInfo($"Disposition (Truly Not Liable){riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar, filter:"Truly Not Liable"),
                };

                if (launcher is LitigGameEndogenousDisputesLauncher endog && endog.GameToPlay == LitigGameEndogenousDisputesLauncher.UnderlyingGame.AppropriationGame)
                {
                    // Appropriation is PrimaryAction (yes = 1, no = 2). So, 1.33 would indicate that 66% of the time, the plaintiff appropriates, and 33% of the time, they do not; 2 would indicate that appropriation never occurs. Thus, to translate the reported PrimaryAction average value to a proportion, we need 1 => 1, 2 => 0, so the formula is x => 2 - x
                    welfareMeasureColumns.Add(new AggregatedGraphInfo($"Appropriation{riskAversionString}", new List<string>() { "Appropriation" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0, scaleMiniGraphValues: x => 2 - x, filter: generalFilter));
                }

                List<string> feeShiftingMultipliers = launcher.CriticalFeeShiftingMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList();
                List<(string minorXName, string minorXAbbrev, List<string> minorXValues)> minorXToRuns = [("Fee Shifting Multiplier", "Fee Shift Mult.", feeShiftingMultipliers)];
                if (article == DataBeingAnalyzed.EndogenousDisputesArticle)
                {
                    LitigGameEndogenousDisputesLauncher endogLauncher = (LitigGameEndogenousDisputesLauncher)launcher;
                    List<string> damagesMultipliers = endogLauncher.CriticalDamagesMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList();
                    minorXToRuns.Add(("Damages Multiplier", "Damages Mult.", damagesMultipliers));
                }
                foreach (var minorXToRun in minorXToRuns)
                {
                    foreach (double? limitToCostsMultiplier in new double?[] { 1.0, null })
                    {
                        foreach (var welfareMeasureInfo in welfareMeasureColumns)
                        {
                            var welfareMeasureWithCorrectedMinorX = welfareMeasureInfo with { minorXAxisLabel = minorXToRun.minorXName, minorXAxisLabelShort = minorXToRun.minorXAbbrev };
                            PlannedPath plannedPath = new PlannedPath(outputFolderPath, new List<(string name, int priority)>());
                            plannedPath.AddSubpath(useRiskAversionForNonRiskReports ? "Risk Averse" : "Risk Neutral", 3);
                            List<string> costsMultipliers = limitToCostsMultiplier == null ? launcher.CriticalCostsMultipliers.OrderBy(x => x).Select(x => x.ToString()).ToList() : new List<string> { limitToCostsMultiplier.ToString() };
                            if (limitToCostsMultiplier == 1.0)
                                plannedPath.AddSubpath("Single Row", 2);
                            else
                                plannedPath.AddSubpath("All Rows", 2);
                            if (article == DataBeingAnalyzed.EndogenousDisputesArticle)
                            {
                                var outputFolderPathOriginal = outputFolderPath;
                                string subfolderName = "Minor X " + minorXToRun.minorXName;
                                plannedPath.AddSubpath(subfolderName, 4);
                            }

                            CreateAggregatedReportVariationsForWelfareMeasure(launcher, pathAndFilename, plannedPath, variations, welfareMeasureWithCorrectedMinorX, costsMultipliers, minorXToRun.minorXValues);
                        }
                    }
                }
            }

            WaitForProcessesToFinish();
            Task.Delay(1000);
            DeleteAuxiliaryFiles(outputFolderPath);
        }

        private static void CreateAggregatedReportVariationsForWelfareMeasure(LitigGameLauncherBase launcher, string sourceDataPathAndFilename, PlannedPath outputFolderPath, List<PermutationalLauncher.SimulationSetsIdentifier> variations, AggregatedGraphInfo aggregatedGraphInfo, List<string> macroYValues, List<string> microXValues)
        {
            Func<double?, double?> scaleMiniGraphValues = aggregatedGraphInfo.scaleMiniGraphValues ?? (x => x);
            List<(string columnName, string expectedText)[]> collectedRowsToFind = new List<(string columnName, string expectedText)[]>();
            double?[,] valuesFromCSVAllRows = null;
            int collectedValuesIndex = 0;
            // We do this as a two-step process, first defining the rows to find and then acting on that information. So we go through the basic logic twice, checking at various points which step we are on.
            foreach (bool stepDefiningRowsToFind in new bool[] { true, false })
            {
                foreach (string equilibriumType in eqToRun)
                {
                    string eqAbbreviation = equilibriumType switch { "Correlated" => "-Corr", "Average" => "-Avg", "First Eq" => "-Eq1", "Only Eq" => "", _ => equilibriumType[1..] };
                    foreach (var variation in variations)
                    {
                        // This is a full report. The variation controls the big x axis. The big y axis is the costs multiplier.
                        // The small x axis is the fee shifting multiplier. And the y axis represents the values that we are loading. 
                        // We then have a different line for each data series.

                        // Load all the data
                        var simulationIdentifiers = variation.simulationIdentifiers;
                        List<List<TikzLineGraphData>> lineGraphData = new List<List<TikzLineGraphData>>();
                        double maxY = 0;
                        foreach (string macroYValue in macroYValues)
                        {
                            List<TikzLineGraphData> lineGraphDataForRow = new List<TikzLineGraphData>();
                            foreach (var macroXValue in simulationIdentifiers)
                            {
                                var columnsToMatch = macroXValue.columnMatches.ToList();
                                columnsToMatch.Add(("Filter", aggregatedGraphInfo.filter));
                                columnsToMatch.Add(("Equilibrium Type", equilibriumType));

                                List<List<double?>> dataForMiniGraph = null;

                                foreach (var microXValue in microXValues)
                                {
                                    if (stepDefiningRowsToFind)
                                    {
                                        var modifiedRowsToFind = columnsToMatch.WithReplacement(aggregatedGraphInfo.minorXAxisLabel, microXValue.ToString()).WithReplacement(aggregatedGraphInfo.majorYAxisLabel, macroYValue).Select(x => (x.Item1, x.Item2.ToString())).ToArray();
                                        collectedRowsToFind.Add(modifiedRowsToFind);
                                    }
                                    else
                                    {
                                        // We found the rows earlier and collected the data for the columns. Now we need to assemble that data into our minigraphs.
                                        int welfareColumnsCount = aggregatedGraphInfo.columnsToGet.Count();
                                        if (dataForMiniGraph == null)
                                        {
                                            dataForMiniGraph = new List<List<double?>>();
                                            for (int i = 0; i < welfareColumnsCount; i++)
                                                dataForMiniGraph.Add(new List<double?>());
                                        }
                                        for (int i = 0; i < welfareColumnsCount; i++)
                                        {
                                            dataForMiniGraph[i].Add(scaleMiniGraphValues(valuesFromCSVAllRows[collectedValuesIndex, i]));
                                        }
                                        collectedValuesIndex++;
                                    }
                                }
                                if (!stepDefiningRowsToFind)
                                {
                                    if (dataForMiniGraph.Any(x => x.Any(y => y < 0)))
                                    {
                                        throw new Exception("Data for mini graph cannot be negative");
                                    }
                                    maxY = Math.Max(maxY, (dataForMiniGraph.Max(x => x.Max(y => y ?? 0))));
                                    TikzLineGraphData miniGraphData = new TikzLineGraphData(dataForMiniGraph, aggregatedGraphInfo.lineScheme, aggregatedGraphInfo.columnsToGet);
                                    lineGraphDataForRow.Add(miniGraphData);
                                }
                            }
                            if (!stepDefiningRowsToFind)
                                lineGraphData.Add(lineGraphDataForRow);
                        }

                        // Change aggregatedGraphInfo so that the top of the microY axis, if not already set to a high enough value, is set to maxY
                        double originalMaxY = maxY;
                        maxY = RoundAxisLimit(originalMaxY);
                        aggregatedGraphInfo = aggregatedGraphInfo with { maximumValueMicroY = Math.Max(aggregatedGraphInfo.maximumValueMicroY ?? 0, maxY) };
                        // now, change each individual mini graph so that the y proportional heights value is divided by maximumValueMicroY
                        foreach (var macroRow in lineGraphData)
                        {
                            for (int macroColumnIndex = 0; macroColumnIndex < macroRow.Count; macroColumnIndex++)
                            {
                                TikzLineGraphData macroCell = macroRow[macroColumnIndex];
                                for (int i = 0; i < macroCell.proportionalHeights.Count(); i++)
                                {
                                    for (int j = 0; j < macroCell.proportionalHeights[i].Count(); j++)
                                    {
                                        if (macroCell.proportionalHeights[i][j] != null)
                                        {
                                            macroCell.proportionalHeights[i][j] /= aggregatedGraphInfo.maximumValueMicroY;
                                            if (macroCell.proportionalHeights[i][j] > 1)
                                                macroCell.proportionalHeights[i][j] = 1.05; // just use a generic number to mean above the graph (though that shouldn't happen given the formula above)
                                        }
                                    }
                                }
                            }
                        }

                        // Now create the aggregated line graph. This is the very last thing we'll do in this method.
                        if (!stepDefiningRowsToFind && variation.nameOfSet != aggregatedGraphInfo.minorXAxisLabel) // we don't want to have graphs in a Fee Shifting Multiplier folder, if we're already including Fee Shifting Multiplier as the minor x axis (this would actually create the same graph multiple times)
                            CreateAggregatedLineGraphFromData(launcher, outputFolderPath, aggregatedGraphInfo, equilibriumType, variation, simulationIdentifiers, lineGraphData, macroYValues, microXValues);

                    }
                }
                if (stepDefiningRowsToFind)
                {
                    // When we get here, we've identified the rows to pull out of the CSV for all equilibria that we need data for.
                    // We're just copying data from the CSV right now. We're not creating the graph yet (that will be in the second step within this method).
                    bool validate = false; // validation is time consuming but can help identify mistakes
                    if (validate)
                        valuesFromCSVAllRows = CSVData.GetCSVData_MultiPassValidated(sourceDataPathAndFilename, collectedRowsToFind.ToArray(), aggregatedGraphInfo.columnsToGet.ToArray(), cacheFile: true);
                    else
                        valuesFromCSVAllRows = CSVData.GetCSVData_SinglePass(sourceDataPathAndFilename, collectedRowsToFind.ToArray(), aggregatedGraphInfo.columnsToGet.ToArray(), cacheFile: true);
                        
                }
            }

        }

        private static void CreateAggregatedLineGraphFromData(LitigGameLauncherBase launcher, PlannedPath plannedPath, AggregatedGraphInfo aggregatedGraphInfo, string equilibriumType, LitigGameEndogenousDisputesLauncher.SimulationSetsIdentifier variation, List<LitigGameEndogenousDisputesLauncher.SimulationIdentifier> simulationIdentifiers, List<List<TikzLineGraphData>> lineGraphData, List<string> macroYValueNames, List<string> microXValues)
        {
            plannedPath = plannedPath.DeepClone();
            plannedPath.AddSubpath(variation.nameOfSet, 5);
            string equilibriumTypeAdj = equilibriumType is "First Eq" or "Only Eq" ? "" : " (" + equilibriumType + ")";
            string outputFilename = $"{aggregatedGraphInfo.topicName} {(variation.nameOfSet.Contains("Baseline") == false ? "Varying " : "")}{variation.nameOfSet}{equilibriumTypeAdj}.tex";

            // make all data proportional to rounded up maximum value
            double maximumValueMicroY;
            if (aggregatedGraphInfo.maximumValueMicroY is not double presetMax)
            {
                throw new Exception("MaximumValueMicroY must be set");
                //var values = lineGraphData.SelectMany(macroRow => macroRow.SelectMany(macroColumn => macroColumn.proportionalHeights.SelectMany(microRow => microRow))).Where(x => x != null);
                //maximumValueMicroY = values.Any() ? values.Select(x => (double)x).Max() : 1.0;
            }
            else
                maximumValueMicroY = presetMax;

            maximumValueMicroY = RoundAxisLimit(maximumValueMicroY);

            foreach (var macroRow in lineGraphData)
            {
                for (int macroColumnIndex = 0; macroColumnIndex < macroRow.Count; macroColumnIndex++)
                {
                    TikzLineGraphData macroCell = macroRow[macroColumnIndex];
                    if (aggregatedGraphInfo.graphType == TikzAxisSet.GraphType.StackedBar)
                    {
                        int numThingsToStack = macroCell.proportionalHeights.Count();
                        int numMicroXValues = macroCell.proportionalHeights.First().Count();
                        for (int i = 0; i < numMicroXValues; i++)
                        {
                            double sum = macroCell.proportionalHeights.Sum(x => x[i] ?? 0);
                            if (sum != 0)
                            {
                                for (int j = 0; j < numThingsToStack; j++)
                                {
                                    if (macroCell.proportionalHeights[j][i] != null)
                                        macroCell.proportionalHeights[j][i] /= sum;
                                }
                            }
                        }
                    }
                }
            }

            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = simulationIdentifiers.Select(x => x.nameForSimulation).ToList(),
                majorXAxisLabel = variation.nameOfSet == "Baseline" ? "" : variation.nameOfSet,
                majorYValueNames = macroYValueNames,
                majorYAxisLabel = aggregatedGraphInfo.majorYAxisLabel,
                minorXValueNames = microXValues,
                minorXAxisLabel = simulationIdentifiers.Count > 3 ? aggregatedGraphInfo.minorXAxisLabelShort : aggregatedGraphInfo.minorXAxisLabel,
                minorYValueNames = Enumerable.Range(0, 11).Select(y => y switch { 0 => "0", 10 => FormatMinorYAxisMaxTick(maximumValueMicroY), _ => " " }).ToList(),
                minorYAxisLabel = FormatMinorYAxisLabel(aggregatedGraphInfo.minorYAxisLabel, maximumValueMicroY),
                yAxisSpaceMicro = 0.8,
                yAxisLabelOffsetMicro = 0.4,
                xAxisSpaceMicro = 1.1,
                xAxisLabelOffsetMicro = 0.8,
                graphType = aggregatedGraphInfo.graphType,
                lineGraphData = lineGraphData,
            };
            var result = r.GetStandaloneDocument();
            GenerateLatex(plannedPath, outputFilename, result);
        }

        private static double RoundAxisLimit(double value)
        {
            if (value == 0) return 0;

            var sign = Math.Sign(value);
            var abs = Math.Abs(value);

            var exponent = Math.Floor(Math.Log10(abs));
            var scale = Math.Pow(10, exponent);       // 10^n
            var scaled = abs / scale;                  // now in [1,10)

            double niceScaled =
                scaled <= 1 ? 1 :
                scaled <= 2 ? 2 :
                scaled <= 5 ? 5 : 10;

            return sign * niceScaled * scale;
        }

        private static string FormatMinorYAxisMaxTick(double maxValueMicroY)
        {
            var p = SplitMantissaAndExponent(maxValueMicroY);
            return p.MantissaWrapped;
        }

        private static string FormatMinorYAxisLabel(string baseLabel, double maxValueMicroY)
        {
            var p = SplitMantissaAndExponent(maxValueMicroY);
            return p.ExponentSuffixWrapped.Length == 0
                   ? baseLabel
                   : $"{baseLabel} {p.ExponentSuffixWrapped}";
        }

        private static (string Mantissa,
                       string MantissaWrapped,
                       string ExponentSuffix,
                       string ExponentSuffixWrapped)
            SplitMantissaAndExponent(double value, int thresholdExponent = -2)
        {
            if (value == 0)
            {
                const string zero = "0";
                return (zero, $"${zero}$", string.Empty, string.Empty);
            }

            var sign = Math.Sign(value);
            var abs = Math.Abs(value);
            var exponent = (int)Math.Floor(Math.Log10(abs));

            if (exponent >= thresholdExponent)
            {
                var plain = value.ToString("G", CultureInfo.InvariantCulture);
                return (plain, $"${plain}$", string.Empty, string.Empty);
            }

            var powerOfTen = Math.Pow(10, exponent);
            var mantissaVal = sign * abs / powerOfTen;
            var mantissa = mantissaVal.ToString("G", CultureInfo.InvariantCulture)
                                           .TrimEnd('0').TrimEnd('.');

            var exponentTxt = $"\\cdot10^{{{exponent}}}";

            return (mantissa,
                    $"${mantissa}$",
                    exponentTxt,
                    $"${exponentTxt}$");
        }
        #endregion

        #region Aggregate cost breakdown reports

        public static void BuildCombinedCostBreakdownReport(LitigGameLauncherBase launcher)
        {
            var optionSets = launcher.GetOptionsSets();

            string outputFile = launcher.GetReportFullPath("Combined costbreakdown", ".csv");  // same folder as the other combined reports

            StringBuilder csv = new StringBuilder();
            bool wroteHeader = false;

            foreach (var opt in optionSets)
            {
                string coreName = opt.Name;
                string inputPath = Launcher.ReportFullPath(filePrefix(launcher), coreName, "-costbreakdowndata.csv");
                
                if (!VirtualizableFileSystem.File.Exists(inputPath))
                    continue;                             // option-set had no cost-breakdown file

                using var reader = new StreamReader(inputPath);

                string localHeader = reader.ReadLine();   // first row of the source file
                if (localHeader is null)
                    continue;

                // write the combined header once (variable columns + group + option + original header)
                if (!wroteHeader)
                {
                    var varHeadings = opt.VariableSettings
                                          .OrderBy(kv => kv.Key.ToString())
                                          .Select(kv => kv.Key.ToString());

                    csv.AppendLine(string.Join(",",
                        varHeadings
                        .Concat(new[] { "GroupName", "OptionSetName" })
                        .Concat(localHeader.Split(','))));

                    wroteHeader = true;
                }

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var varValues = opt.VariableSettings
                                       .OrderBy(kv => kv.Key.ToString())
                                       .Select(kv => (kv.Value?.ToString() ?? "").Replace(",", "-"));

                    IEnumerable<string> combinedRow = varValues
                        .Concat(new[]
                        {
                            (opt.GroupName ?? "").Replace(",", "-"),
                            coreName.Replace(",", "-")
                        })
                        .Concat(line.Split(','));

                    csv.AppendLine(string.Join(",", combinedRow));
                }
            }

            TextFileManage.CreateTextFile(outputFile, csv.ToString());
        }

        /// <summary>One CSV line parsed into variable settings → <see cref="Slice"/>.</summary>
        private sealed record CostRow(IDictionary<string, string> Vars, Slice Slice);

        /// <summary>Parses the combined cost-breakdown CSV once and caches the rows.</summary>
        private static IReadOnlyList<CostRow> LoadCombinedCostRows(string csvPath)
        {
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("Combined cost-breakdown report not found", csvPath);

            using var reader = new StreamReader(csvPath);
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                throw new InvalidDataException("CSV header row missing");

            string[] head = headerLine.Split(',');

            // locate fixed columns -- must correspond to OutputHeaders in CostBreakdownReport
            int widthIdx      = Array.IndexOf(head, "Width");
            int precautionIdx= Array.IndexOf(head, "Precaution");
            int trulyLiableHarmIdx          = Array.IndexOf(head, "TLHarm");
            int trulyNotLiableHarmIdx       = Array.IndexOf(head, "TNLHarm");
            int filingIdx     = Array.IndexOf(head, "File");
            int answerIdx     = Array.IndexOf(head, "Answer");
            int bargainIdx    = Array.IndexOf(head, "Bargain");
            int trialIdx      = Array.IndexOf(head, "Trial");
            if (widthIdx == -1 || precautionIdx == -1 ||
                trulyLiableHarmIdx == -1 || trulyNotLiableHarmIdx == -1 ||
                filingIdx == -1 || answerIdx == -1 || bargainIdx == -1 || trialIdx == -1)
            {
                throw new InvalidDataException("CSV header row missing required columns");
            }

            // variable columns come before “GroupName”
            int groupNameIdx = Array.IndexOf(head, "GroupName");
            var variableCols = head.Take(groupNameIdx).ToArray();

            var rows = new List<CostRow>();

            string? raw;
            while ((raw = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string[] f = raw.Split(',');

                var vars = new Dictionary<string, string>(variableCols.Length);
                for (int i = 0; i < variableCols.Length; i++)
                    vars[variableCols[i]] = f[i];

                rows.Add(new CostRow(
                    vars,
                    new Slice(
                        double.Parse(f[widthIdx], CultureInfo.InvariantCulture),
                        double.Parse(f[precautionIdx], CultureInfo.InvariantCulture),
                        double.Parse(f[trulyLiableHarmIdx],        CultureInfo.InvariantCulture),
                        double.Parse(f[trulyNotLiableHarmIdx],        CultureInfo.InvariantCulture),
                        double.Parse(f[filingIdx],      CultureInfo.InvariantCulture),
                        double.Parse(f[answerIdx],      CultureInfo.InvariantCulture),
                        double.Parse(f[bargainIdx],     CultureInfo.InvariantCulture),
                        double.Parse(f[trialIdx],       CultureInfo.InvariantCulture))));
            }

            return rows;
        }

        /// <summary>Returns the merged <see cref="Slice"/> list for a given simulation.</summary>
        private static List<Slice> GetSlices(
            PermutationalLauncher.SimulationIdentifier sim,
            IEnumerable<CostRow> rows)
        {
            static List<Slice> Merge(IEnumerable<Slice> src)
            {
                var acc = new List<Slice>();
                foreach (var s in src)
                {
                    var hit = acc.FirstOrDefault(a =>
                        Math.Abs(a.opportunity - s.opportunity) < 1e-12 &&
                        Math.Abs(a.trulyLiableHarm - s.trulyLiableHarm) < 1e-12 &&
                        Math.Abs(a.trulyNotLiableHarm - s.trulyNotLiableHarm) < 1e-12 &&
                        Math.Abs(a.filing      - s.filing     ) < 1e-12 &&
                        Math.Abs(a.answer      - s.answer     ) < 1e-12 &&
                        Math.Abs(a.bargaining  - s.bargaining ) < 1e-12 &&
                        Math.Abs(a.trial       - s.trial      ) < 1e-12);

                    if (hit is null)
                        acc.Add(s);
                    else
                        acc[acc.IndexOf(hit)] = hit with { width = hit.width + s.width };
                }
                return acc;
            }

            bool Match(CostRow r) => sim.columnMatches.All(cm =>
                r.Vars.TryGetValue(cm.columnName, out var val) && val == cm.expectedValue);

            return Merge(rows.Where(Match).Select(r => r.Slice));
        }

        public static void ProduceRepeatedCostBreakdownReports(LitigGameLauncherBase launcher, DataBeingAnalyzed article)
        {
            if (article != DataBeingAnalyzed.EndogenousDisputesArticle)
                return;

            string reportFolder = Launcher.ReportFolder();
            string pathAndFilename = launcher.GetReportFullPath("Combined costbreakdown", ".csv");
            string outputFolderName = "Aggregated Data";
            string outputFolderPath = Path.Combine(reportFolder, outputFolderName);
            var allRows = LoadCombinedCostRows(pathAndFilename);

            foreach (bool useRiskAversionForNonRiskReports in new bool[] { false, true })
            {
                var variations = launcher.GetSimulationSetsIdentifiers(useRiskAversionForNonRiskReports ? LitigGameLauncherBase.RequireModerateRiskAversion : null);

                foreach (double? limitToCostsMultiplier in new double?[] { 1.0, null })
                {
                    PlannedPath plannedPath = new PlannedPath(outputFolderPath, new List<(string name, int priority)>());
                    plannedPath.AddSubpath(useRiskAversionForNonRiskReports ? "Risk Averse" : "Risk Neutral", 3);
                    
                    if (limitToCostsMultiplier == 1.0)
                        plannedPath.AddSubpath("Single Row", 2);
                    else
                        plannedPath.AddSubpath("All Rows", 2);
                    
                    string subfolderName = "Cost Breakdown";
                    plannedPath.AddSubpath(subfolderName, 4);

                    foreach (PermutationalLauncher.SimulationSetsIdentifier variation in variations)
                    {
                        BuildRepeatedCostBreakdownForVariation(launcher, allRows, limitToCostsMultiplier, plannedPath, variation);

                    }
                }
            }

            WaitForProcessesToFinish();
            Task.Delay(1000);
            DeleteAuxiliaryFiles(outputFolderPath);
        }

        private static void BuildRepeatedCostBreakdownForVariation(
            LitigGameLauncherBase launcher,
            IReadOnlyList<CostRow> allRows,
            double? limitToCostsMultiplier,
            PlannedPath plannedPath,
            PermutationalLauncher.SimulationSetsIdentifier variation)
        {
            plannedPath = plannedPath.DeepClone();
            plannedPath.AddSubpath(variation.nameOfSet, 5);

            var costMultipliersForLabels = limitToCostsMultiplier.HasValue
                ? new List<double> { limitToCostsMultiplier.Value }
                : launcher.CriticalCostsMultipliers.OrderBy(x => x).ToList();

            // 3-D matrix: [row → costs-multiplier] × [col → simulation] × Slice list
            var matrix = costMultipliersForLabels
                .Select(cm => variation.simulationIdentifiers
                                       .Select(sim => sim.With("Costs Multiplier", cm.ToString()))
                                       .Select(sim => GetSlices(sim, allRows))
                                       .ToList())
                .ToList();

            var majorXLabels = variation.simulationIdentifiers
                                         .Select(s => s.nameForSimulation)
                                         .ToList();

            var majorYLabels = costMultipliersForLabels
                .Select(x => x.ToString(CultureInfo.InvariantCulture))
                .ToList();

            // ── combined light-mode grid ────────────────────────────────────────────
            GenerateRepeatedReport(limitToCostsMultiplier,
                                   plannedPath,
                                   variation,
                                   matrix,
                                   majorXLabels,
                                   majorYLabels);

            // ── individual dark-mode panels ─────────────────────────────────────────
            var sliceGrid = matrix.SelectMany(r => r).ToList();
            var axisScalingInfos = CostBreakdownReport
                .ComputeScaling(sliceGrid, peakProportion: 0.8);   // :contentReference[oaicite:0]{index=0}

            BuildIndividualCostBreakdownPanelsForVariation(
                plannedPath,
                variation,
                matrix,
                axisScalingInfos,
                majorXLabels,
                majorYLabels);
        }

        private static void BuildIndividualCostBreakdownPanelsForVariation(
            PlannedPath                                             plannedPath,
            PermutationalLauncher.SimulationSetsIdentifier          variation,
            List<List<List<Slice>>>                                 matrix,
            List<CostBreakdownReport.AxisScalingInfo>               axisScalingInfos,
            List<string>                                            majorXLabels,
            List<string>                                            majorYLabels)
        {
            // ── build a base path that is a sibling of the “Single Row” / “All Rows” folders ──
            var basePath = new PlannedPath(plannedPath.originalPath, new());
            foreach (var (name, priority) in plannedPath.prioritizedSubpaths)
                if (priority != 2)                       // skip the existing row-level folder
                    basePath.AddSubpath(name, priority); // keep everything else (variation, etc.)

            basePath.AddSubpath("Individual Panels", 2);      // same priority as row-folders

            // ── generate one dark-mode panel per simulation ───────────────────────────────────
            int rows = matrix.Count;
            int cols = matrix[0].Count;
            int scaleIdx = 0;

            for (int r = 0; r < rows; r++)
            {
                var rowPath = basePath.DeepClone();
                rowPath.AddSubpath($"Costs {majorYLabels[r]}x", 1); // optional subfolder per row

                for (int c = 0; c < cols; c++)
                {
                    var slices  = matrix[r][c];
                    var scaling = axisScalingInfos[scaleIdx++];

                    string filename =
                        $"Cost Breakdown – {variation.nameOfSet} – CM {majorYLabels[r]} – {majorXLabels[c]} (dark).tex";

                    string tikz = CostBreakdownReport.TikzScaled(
                        slices,
                        scaling,
                        pres: true,                      // dark mode
                        title: "",
                        splitRareHarmPanel: CostBreakdownReport.HasTwoPanels(slices),
                        standalone: true,
                        includeLegend: true,
                        includeAxisLabels: false,
                        includeDisputeLabels: false);

                    GenerateLatex(rowPath, filename, tikz);
                }
            }
        }

        private static void GenerateRepeatedReport(double? limitToCostsMultiplier, PlannedPath plannedPath, PermutationalLauncher.SimulationSetsIdentifier variation, List<List<List<Slice>>> matrix, List<string> majorXLabels, List<string> majorYLabels)
        {
            majorXLabels = majorXLabels.Select(ConvertFraction).ToList();
            majorYLabels = majorYLabels.Select(ConvertFraction).ToList();
            string tikzSource = RepeatedCostBreakdownReport.GenerateRepeatedReport(
                            matrix,
                            majorXLabels,
                            majorYLabels,
                            variation.nameOfSet,           // big-X caption
                            "Costs Multiplier",            // big-Y caption
                            peakProportion: 0.8,
                            keepAxisLabels: false,
                            keepAxisTicks: true);

            string core = $"Cost Breakdown {variation.nameOfSet} " +
                          (limitToCostsMultiplier == 1.0 ? "(Single Row)" : "(All Rows)");

            GenerateLatex(plannedPath, $"{core}.tex", tikzSource);
        }

            // helper converts decimal strings to TeX fractions for axis labels
        static string ConvertFraction(string s)
        {
            switch (s.Trim())
            {
                case ".1":
                case "0.1":
                    return "$\\frac{1}{10}$";
                case ".125":
                case "0.125":
                    return "$\\frac{1}{8}$";
                case ".2":
                case "0.2":
                    return "$\\frac{1}{5}$";
                case ".25":
                case "0.25":
                case "0.250":
                    return "$\\frac{1}{4}$";
                case ".3":
                case "0.3":
                    return "$\\frac{3}{10}$";
                case ".4":
                case "0.4":
                    return "$\\frac{2}{5}$";
                case ".5":
                case "0.5":
                case "0.50":
                case "0.500":
                    return "$\\frac{1}{2}$";
                case ".6":
                case "0.6":
                    return "$\\frac{3}{5}$";
                case ".7":
                case "0.7":
                    return "$\\frac{7}{10}$";
                case ".75":
                case "0.75":
                case "0.750":
                    return "$\\frac{3}{4}$";
                case ".8":
                case "0.8":
                    return "$\\frac{4}{5}$";
                case ".9":
                case "0.9":
                    return "$\\frac{9}{10}$";
                default:
                    return s;
            }
        }

        #endregion

        #region Coefficient of variation report

        public static void CalculateAverageCoefficientOfVariation()
        {
            string filename = "G:\\My Drive\\Articles, books in progress\\Machine learning model of litigation\\Independent signals -- small tree results\\FS037--output each equilibrium.csv";
            int[] stringColumns = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
            int[] numericColumns = Enumerable.Range(18, 21).ToArray();
            var fromCSVFile = GetDataFromCSV(filename, 13, "All", stringColumns, numericColumns);
            var grouped = fromCSVFile.GroupBy(x => String.Join(",", x.stringValues));
            List<List<double[]>> resultsForGroupedEquilibria = grouped
                .Select(group => group.Select(x => x.numericValues).ToList())
                .ToList();
            CompleteCoefficientOfVariationCalculation(resultsForGroupedEquilibria, false);
            Console.WriteLine($"Eliminating trivial equilibria");
            resultsForGroupedEquilibria = grouped
                .Select(group => group.Select(x => x.numericValues).ToList())
                .ToList();
            CompleteCoefficientOfVariationCalculation(resultsForGroupedEquilibria, true);
        }

        private static void CompleteCoefficientOfVariationCalculation(List<List<double[]>> resultsForGroupedEquilibria, bool eliminateTrivialEquilibria)
        {
            // We don't want multiple trivial equilibria, so we allow no more than one of each equilibrium.
            foreach (List<double[]> resultsForGroupedEquilibrium in resultsForGroupedEquilibria)
            {
                List<int> itemsInListToRemove = new List<int>();
                bool alreadyFoundPlaintiffNeverFiles = false;
                bool alreadyFoundPlaintiffAlwaysFilesButDefendantNeverAnswers = false;
                for (int i = 0; i < resultsForGroupedEquilibrium.Count; i++)
                {
                    var resultsForSingleEquilibrium = resultsForGroupedEquilibrium[i];
                    bool plaintiffNeverFiles = resultsForSingleEquilibrium[0] == 0;
                    if (plaintiffNeverFiles)
                    {
                        if (alreadyFoundPlaintiffNeverFiles || eliminateTrivialEquilibria)
                            itemsInListToRemove.Add(i);
                        else
                            alreadyFoundPlaintiffNeverFiles = true;
                    }
                    bool plaintiffAlwaysFilesButDefendantNeverAnswers = resultsForSingleEquilibrium[0] == 1 && resultsForSingleEquilibrium[1] == 0;
                    if (plaintiffAlwaysFilesButDefendantNeverAnswers)
                    {
                        if (alreadyFoundPlaintiffAlwaysFilesButDefendantNeverAnswers || eliminateTrivialEquilibria)
                            itemsInListToRemove.Add(i);
                        else
                            alreadyFoundPlaintiffAlwaysFilesButDefendantNeverAnswers = true;
                    }
                    // now, remove the items in the list
                    foreach (int index in itemsInListToRemove.OrderByDescending(x => x))
                        resultsForGroupedEquilibrium.RemoveAt(index);
                }
            }
            var numEquilibria = resultsForGroupedEquilibria.Select(x => x.Count()).OrderByDescending(x => x).ToArray();
            List<double[]> coefficientsOfVariation = new List<double[]>();
            // Now, we want to calculate the coefficient of variation for each of the numeric columns for each group. In the event that there is only a single item in a group, we count that as zero for each numeric column.
            int[] numericColumns = Enumerable.Range(0, resultsForGroupedEquilibria.First().First().Length).ToArray(); // now, we don't have to index into the original CSV
            foreach (var resultsForGroupedEquilibrium in resultsForGroupedEquilibria)
            {
                if (resultsForGroupedEquilibrium.Count <= 1)
                {
                    coefficientsOfVariation.Add(Enumerable.Repeat(0.0, numericColumns.Length).ToArray());
                }
                else
                {
                    var averages = numericColumns.Select(x => resultsForGroupedEquilibrium.Select(y => y[x]).Average()).ToArray();
                    var standardDeviations = numericColumns.Select(x => Math.Sqrt(resultsForGroupedEquilibrium.Select(y => y[x]).Select(y => Math.Pow(y - averages[x], 2)).Average())).ToArray();
                    var coefficients = numericColumns.Select(x => averages[x] == 0 ? 0 : standardDeviations[x] / averages[x]).ToArray();
                    coefficientsOfVariation.Add(coefficients);
                }
            }
            // Finally, let's get the average coefficient of variation across columns
            var averageCoefficients = numericColumns.Select(x => coefficientsOfVariation.Select(y => y[x]).Average()).ToArray();
            Console.WriteLine($"Average coefficient of variation: {String.Join(", ", averageCoefficients)}");
        }

        #endregion

        #region Directory and Latex helpers

        record PlannedPath(string originalPath, List<(string name, int priority)> prioritizedSubpaths)
        {
            public void AddSubpath(string name, int priority)
            {
                prioritizedSubpaths.Add((name, priority));
            }

            public string GetPlannedPathString()
            {
                var orderedSubpaths = prioritizedSubpaths.OrderByDescending(x => x.priority).Select(x => x.name).ToList();
                string currentPath = originalPath;
                foreach (string subpath in orderedSubpaths)
                {
                    string nextPath = Path.Combine(currentPath, subpath);
                    if (!VirtualizableFileSystem.Directory.GetDirectories(currentPath).Any(x => x == subpath))
                        VirtualizableFileSystem.Directory.CreateDirectory(nextPath);
                    currentPath = nextPath;
                }
                return currentPath;
            }

            public string GetCombinedPlannedPathString(string filename) => Path.Combine(GetPlannedPathString(), filename);

            public PlannedPath DeepClone() => new PlannedPath(originalPath, prioritizedSubpaths.ToList());
        }

        private static void GenerateLatex(PlannedPath plannedPath, string outputFilename, string standaloneDocument)
        {
            string outputPath = plannedPath.GetPlannedPathString();
            string outputCombinedPath = plannedPath.GetCombinedPlannedPathString(outputFilename);
            TextFileManage.CreateTextFile(outputCombinedPath, standaloneDocument);
            string expectedOutput = outputCombinedPath.Replace(".tex", ".pdf");
            if (!avoidProcessingIfPDFExists || !DataProcessingBase.VirtualizableFileSystem.File.Exists(expectedOutput))
            {
                TabbedText.WriteLine($"Generating {outputFilename} in {outputPath}");
                ExecuteLatexProcess(outputPath, outputCombinedPath);
            }
        }

        #endregion
    }
}
