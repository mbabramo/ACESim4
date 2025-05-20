using ACESim;
using ACESimBase;
using ACESimBase.Games.LitigGame;
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
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tensorflow;
using static LitigCharts.DataProcessingUtils;

namespace LitigCharts
{
    public class FeeShiftingDataProcessing : DataProcessingBase
    {
        #region CSV reports

        public static void BuildMainFeeShiftingReport(LitigGameLauncherBase launcher)
        {
            List<string> rowsToGet = new List<string> { "All", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> replacementRowNames = new List<string> { "All", "Not Litigated", "Litigated", "Settles", "Tried", "P Loses", "P Wins", "Truly Liable", "Truly Not Liable" };
            List<string> columnsToGet = new List<string> { "Exploit", "Seconds", "PFiles", "DAnswers", "POffer1", "DOffer1", "Trial", "PWinPct", "PWealth", "DWealth", "TotWealth", "WealthLoss", "PWelfare", "DWelfare", "PDSWelfareLoss", "SWelfareLoss", "TotExpense", "False+", "False-", "ValIfSettled", "PDoesntFile", "DDoesntAnswer", "SettlesBR1", "PAbandonsBR1", "DDefaultsBR1", "P Loses", "P Wins", "PrimaryAction" };
            List<string> replacementColumnNames = new List<string> { "Exploitability", "Calculation Time", "P Files", "D Answers", "P Offer", "D Offer", "Trial", "P Win Probability", "P Wealth", "D Wealth", "Total Wealth", "Wealth Loss", "P Welfare", "D Welfare", "Pre-Dispute Social Welfare Loss", "Social Welfare Loss", "Expenditures", "False Positive Inaccuracy", "False Negative Inaccuracy", "Value If Settled", "No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins", "Appropriation" };
            BuildReport(launcher, rowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "output");
        }

        private static void BuildReport(LitigGameLauncherBase launcher, List<string> rowsToGet, List<string> replacementRowNames, List<string> columnsToGet, List<string> replacementColumnNames, string endOfFileName)
        {
            bool onlyAllFilter = false;
            if (onlyAllFilter)
            {
                rowsToGet = rowsToGet.Take(1).ToList();
                replacementRowNames = replacementRowNames.Take(1).ToList();
            }

            var gameOptionsSets = launcher.GetOptionsSets();
            var map = launcher.NameMap; // name to find (avoids redundancies in naming)
            string path = Launcher.ReportFolder();
            string outputFileFullPath = Path.Combine(path, filePrefix(launcher) + $"-{endOfFileName}.csv");
            string cumResults = "";

            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList();

            // look up particular settings here if desired (not usually needed)
            bool findSpecificSettings = false;
            List<(string, string)> settingsToFind = launcher.DefaultVariableValues;
            var matches = distinctOptionSets.Where(x => !findSpecificSettings || settingsToFind.All(y => x.VariableSettings[y.Item1].ToString() == y.Item2.ToString())).ToList();
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
                string altFileSuffix = firstEquilibriumFileSuffix;
                TabbedText.WriteLine($"Processing equilibrium type {fileSuffix}");
                bool includeHeader = firstEqOnly || fileSuffix == correlatedEquilibriumFileSuffix;
                List<List<string>> outputLines = GetCSVLines(distinctOptionSets.Select(x => (GameOptions)x).ToList(), map, rowsToGet, replacementRowNames, filePrefix(launcher), fileSuffix, altFileSuffix, path, includeHeader, columnsToGet, replacementColumnNames);
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

            BuildReport(launcher, filtersOfRowsToGet, replacementRowNames, columnsToGet, replacementColumnNames, "offers");
        }

        #endregion

        #region Latex diagrams for individual simulation results

        static bool includeHeatmaps = false;
        internal static void ExecuteLatexProcessesForExisting() => ExecuteLatexProcessesForExisting(result => includeHeatmaps || (!result.Contains("offers") && !result.Contains("fileans")));

        internal static void ProduceLatexDiagramsFromTexFiles(LitigGameLauncherBase launcher)
        {
            bool workExists = true;
            int numAttempts = 0; // sometimes the Latex processes fail, so we try again if any of our files to create are missing
            while (workExists && numAttempts < 5)
            {
                List<(string path, string combinedPath, string optionSetName, string fileSuffix)> processesToLaunch = new List<(string path, string combinedPath, string optionSetName, string fileSuffix)>();
                workExists = processesToLaunch.Any() || !avoidProcessingIfPDFExists; // if we're avoiding processing if the PDF exists, then we'll indicate that there's no more work, because that's our only way of telling
                foreach (string fileSuffix in equilibriumTypeSuffixes)
                {
                    List<(string path, string combinedPath, string optionSetName, string fileSuffix)> someToDo = FeeShiftingDataProcessing.GetLatexProcessPlans(launcher, new string[] { "-stagecostlight" + fileSuffix, "-stagecostdark" + fileSuffix, "-offers" + fileSuffix, "-fileans" + fileSuffix });
                    processesToLaunch.AddRange(someToDo);
                }
                ProduceLatexDiagrams(processesToLaunch);
            }
        }

        #endregion

        public static List<(string folderName, string[] extensions)> GetFilePlacementRules()
        {
            var placementRules = new List<(string folderName, string[] extensions)>
            {
                ("First Equilibrium", GetFileTypeExtensionsForEquilibriumType("Eq1")),
                ("EFG Files", new[] { ".efg" }),
                ("Equilibria Files", new[] { "-equ.csv" }),
                ("Logs", new[] { "-log.txt" }),
                ("Latex underlying data", expandToIncludeAdditionalEquilibria(new[] { "-offers.tex", "-fileans.tex", "-stagecostlight.tex", "-stagecostdark.tex" })),
                ("Latex files", expandToIncludeAdditionalEquilibria(new[] { "-offers.tex", "-fileans.tex", "-stagecostlight.tex", "-stagecostdark.tex" })),
                ("File-Answer Diagrams", expandToIncludeAdditionalEquilibria( new[] { "-fileans.pdf" })),
                ("Offer Heatmaps", expandToIncludeAdditionalEquilibria( new[] { "-offers.pdf" })),
                ("Stage Costs Diagrams (Normal)", expandToIncludeAdditionalEquilibria( new[] { "-stagecostlight.pdf" })),
                ("Stage Costs Diagrams (Dark Mode)", expandToIncludeAdditionalEquilibria( new[] { "-stagecostdark.pdf" })),
                ("Stage Costs Diagrams (Underlying Data)", expandToIncludeAdditionalEquilibria( new[] { "-stagecostlight.csv", "-stagecostdark.csv" })),
                ("Cross Tabs", new[] { ".csv" }),
            };

            string[] expandToIncludeAdditionalEquilibria(string[] original)
            {
                if (firstEqOnly)
                {
                    return original;
                }
                var l = original.ToList();
                foreach (var item in original)
                    foreach (int i in Enumerable.Range(1, 100))
                        l.Add(item.Replace(".", $"-Eq{i}."));
                return l.ToArray();
            }

            if (!firstEqOnly)
            {
                placementRules.InsertRange(0, new List<(string, string[])>
                {
                    ("Correlated Equilibrium", GetFileTypeExtensionsForEquilibriumType("Corr")),
                    ("Average Equilibrium", GetFileTypeExtensionsForEquilibriumType("Avg")),
                });

                for (int i = 2; i <= 100; i++)
                    placementRules.Add(("Additional Equilibria", GetFileTypeExtensionsForEquilibriumType($"Eq{i}")));
            }

            return placementRules;
        }

        private static string[] GetFileTypeExtensionsForEquilibriumType(string eqType) => new[]
        {
            firstEqOnly ? ".csv" : $"-{eqType}.csv",
            $"-offers-{eqType}.pdf", $"-offers-{eqType}.tex",
            $"-fileans-{eqType}.pdf", $"-fileans-{eqType}.tex",
            $"-stagecostlight-{eqType}.pdf", $"-stagecostlight-{eqType}.tex", $"-stagecostlight-{eqType}.csv",
            $"-stagecostdark-{eqType}.pdf", $"-stagecostdark-{eqType}.tex", $"-stagecostdark-{eqType}.csv",
        };

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

        public record AggregatedGraphInfo(string topicName, List<string> columnsToGet, List<string> lineScheme, string minorXAxisLabel = "Fee Shifting Multiplier", string minorXAxisLabelShort = "Fee Shift Mult.", string minorYAxisLabel = "\\$", string majorYAxisLabel = "Costs Multiplier", double? maximumValueMicroY = null, TikzAxisSet.GraphType graphType = TikzAxisSet.GraphType.Line, Func<double?, double?> scaleMiniGraphValues = null, string filter = "All");

        public static void ProduceLatexDiagramsAggregatingReports(LitigGameLauncherBase launcher)
        {
            string reportFolder = Launcher.ReportFolder();
            string filename = launcher.MasterReportNameForDistributedProcessing + "--output.csv";
            string pathAndFilename = Path.Combine(reportFolder, filename);
            string outputFolderName = "Aggregated Data";
            string outputFolderPath = Path.Combine(reportFolder, outputFolderName);
            if (!Directory.GetDirectories(reportFolder).Any(x => x == outputFolderName))
                Directory.CreateDirectory(outputFolderPath);

            var sets = launcher.GetSetsOfGameOptions(false, false);
            var map = launcher.NameMap; // name to find (avoids redundancies)
            var setNames = launcher.NamesOfVariationSets;
            string masterReportName = launcher.MasterReportNameForDistributedProcessing;
            List<(List<GameOptions> theSet, string setName)> setsWithNames = sets.Zip(setNames, (s, sn) => (s, sn)).ToList();

            foreach (bool useRiskAversionForNonRiskReports in new bool[] { false, true })
            {
                var variations = launcher.GetArticleVariationInfoList_PossiblyFixingRiskAversion(useRiskAversionForNonRiskReports);

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
                    new AggregatedGraphInfo($"Social Welfare Loss{riskAversionString}", new List<string>() { "Social Welfare Loss" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), maximumValueMicroY: 2, scaleMiniGraphValues: x => x / 2.0),
                    new AggregatedGraphInfo($"Wealth Loss{riskAversionString}", new List<string>() { "Wealth Loss" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), maximumValueMicroY: 2, scaleMiniGraphValues:  x => x / 2.0),
                    new AggregatedGraphInfo($"Trial{riskAversionString}", new List<string>() { "Trial" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0),
                    new AggregatedGraphInfo($"Trial Outcomes{riskAversionString}", new List<string>() { "P Win Probability" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0),
                    new AggregatedGraphInfo($"Disposition{riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar),
                    new AggregatedGraphInfo($"Accuracy and Expenditures (Truly Liable){riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList(), filter:"Truly Liable"),
                    new AggregatedGraphInfo($"Accuracy and Expenditures (Truly Not Liable){riskAversionString}", new List<string>() { "False Negative Inaccuracy", "False Positive Inaccuracy",  "Expenditures" }, plaintiffDefendantAndOthersLineScheme.ToList(), filter: "Truly Not Liable"),
                    new AggregatedGraphInfo($"Disposition (Truly Liable){riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar, filter:"Truly Liable"),
                    new AggregatedGraphInfo($"Disposition (Truly Not Liable){riskAversionString}", new List<string>() {"No Suit", "No Answer", "Settles", "P Abandons", "D Defaults", "P Loses", "P Wins"}, dispositionLineScheme, minorYAxisLabel:"Proportion", maximumValueMicroY: 1.0, graphType:TikzAxisSet.GraphType.StackedBar, filter:"Truly Not Liable"),
                };

                if (launcher is LitigGameEndogenousDisputesLauncher endog && endog.GameToPlay == LitigGameEndogenousDisputesLauncher.UnderlyingGame.AppropriationGame)
                {
                    // Appropriation is PrimaryAction (yes = 1, no = 2). So, 1.33 would indicate that 66% of the time, the plaintiff appropriates, and 33% of the time, they do not; 2 would indicate that appropriation never occurs. Thus, to translate the reported PrimaryAction average value to a proportion, we need 1 => 1, 2 => 0, so the formula is x => 2 - x
                    welfareMeasureColumns.Add(new AggregatedGraphInfo($"Appropriation{riskAversionString}", new List<string>() { "Appropriation" }, plaintiffDefendantAndOthersLineScheme.Take(1).ToList(), minorYAxisLabel: "Proportion", maximumValueMicroY: 1.0, scaleMiniGraphValues: x => 2 - x));
                }

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

        private static void ProcessForWelfareMeasure(LitigGameLauncherBase launcher, string pathAndFilename, string outputFolderPath, List<LitigGameEndogenousDisputesLauncher.ArticleVariationInfoSets> variations, AggregatedGraphInfo aggregatedGraphInfo, double? limitToCostsMultiplier)
        {
            Func<double?, double?> scaleMiniGraphValues = aggregatedGraphInfo.scaleMiniGraphValues ?? (x => x);
            List<(string columnName, string expectedText)[]> collectedRowsToFind = new List<(string columnName, string expectedText)[]>();
            double?[,] valuesFromCSVAllRows = null;
            int collectedValuesIndex = 0;
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

                        var requirementsForEachVariation = variation.requirementsForEachVariation;
                        List<List<TikzLineGraphData>> lineGraphData = new List<List<TikzLineGraphData>>();
                        List<double> costsMultipliers = limitToCostsMultiplier == null ? launcher.CriticalCostsMultipliers.OrderBy(x => x).ToList() : new List<double> { (double)limitToCostsMultiplier };
                        double maxY = 0;
                        foreach (double macroYValue in costsMultipliers)
                        {
                            List<TikzLineGraphData> lineGraphDataForRow = new List<TikzLineGraphData>();
                            foreach (var macroXValue in requirementsForEachVariation)
                            {
                                var columnsToMatch = macroXValue.columnMatches.ToList();
                                columnsToMatch.Add(("Filter", aggregatedGraphInfo.filter));
                                columnsToMatch.Add(("Equilibrium Type", equilibriumType));

                                List<List<double?>> dataForMiniGraph = null;

                                foreach (var microXValue in launcher.CriticalFeeShiftingMultipliers.OrderBy(x => x))
                                {
                                    if (stepDefiningRowsToFind)
                                    {
                                        var modifiedRowsToFind = columnsToMatch.WithReplacement("Fee Shifting Multiplier", microXValue.ToString()).WithReplacement("Costs Multiplier", macroYValue.ToString()).Select(x => (x.Item1, x.Item2.ToString())).ToArray();
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

                        // round maxY up to the nearest 0.1 (e.g., 1.43 -> 1.5). This will then be set as the top of the y-axis for every mini-graph for this variation. Note that different variations will not all have the same value, but that's OK, because people will generally look at one set of minigraphs at a time (if that). 
                        maxY = Math.Ceiling(maxY * 10) / 10;
                        if (maxY >= 0.6 && maxY <= 0.9)
                            maxY = 1.0; // use round number
                        // change aggregatedGraphInfo so that the microY axis value is multiplied by the new value.
                        aggregatedGraphInfo = aggregatedGraphInfo with { maximumValueMicroY = Math.Max(aggregatedGraphInfo.maximumValueMicroY ?? 1, 1.0) * maxY };
                        // now, change each individual mini graph so that the y value is divided by maxY (since we've increased the scale on the graph).
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
                                            macroCell.proportionalHeights[i][j] /= maxY;
                                    }
                                }
                            }
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

        private static void CreateAggregatedLineGraphFromData(LitigGameLauncherBase launcher, string outputFolderPath, AggregatedGraphInfo aggregatedGraphInfo, string equilibriumType, LitigGameEndogenousDisputesLauncher.ArticleVariationInfoSets variation, List<LitigGameEndogenousDisputesLauncher.ArticleVariationInfo> requirementsForEachVariation, List<List<TikzLineGraphData>> lineGraphData, double? limitToCostsMultiplier)
        {
            string subfolderName = Path.Combine(outputFolderPath, variation.nameOfSet);
            if (!Directory.GetDirectories(outputFolderPath).Any(x => x == subfolderName))
                Directory.CreateDirectory(subfolderName);
            string costsLevel = "";
            if (limitToCostsMultiplier != null)
                costsLevel = $" Costs {limitToCostsMultiplier}";
            string equilibriumTypeAdj = equilibriumType == "First Eq" ? "" : " (" + equilibriumType + ")";
            string outputFilename = Path.Combine(subfolderName, $"{aggregatedGraphInfo.topicName} {(variation.nameOfSet.Contains("Baseline") == false ? "Varying " : "")}{variation.nameOfSet}{costsLevel}{equilibriumTypeAdj}{(limitToCostsMultiplier != null ? $" Costs Multiplier {limitToCostsMultiplier}" : "")}.tex");

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
                majorXValueNames = requirementsForEachVariation.Select(x => x.nameOfVariation).ToList(),
                majorXAxisLabel = variation.nameOfSet == "Baseline" ? "" : variation.nameOfSet,
                majorYValueNames = limitToCostsMultiplier == null ? launcher.CriticalCostsMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList() : new List<string>() { limitToCostsMultiplier.ToString() },
                majorYAxisLabel = aggregatedGraphInfo.majorYAxisLabel,
                minorXValueNames = launcher.CriticalFeeShiftingMultipliers.OrderBy(x => x).Select(y => y.ToString()).ToList(),
                minorXAxisLabel = requirementsForEachVariation.Count > 3 ? aggregatedGraphInfo.minorXAxisLabelShort : aggregatedGraphInfo.minorXAxisLabel,
                minorYValueNames = Enumerable.Range(0, 11).Select(y => y switch { 0 => "0", 10 => maximumValueMicroY.ToString(), _ => " " }).ToList(),
                minorYAxisLabel = aggregatedGraphInfo.minorYAxisLabel,
                yAxisSpaceMicro = 0.8,
                yAxisLabelOffsetMicro = 0.4,
                xAxisSpaceMicro = 1.1,
                xAxisLabelOffsetMicro = 0.8,
                graphType = aggregatedGraphInfo.graphType,
                lineGraphData = lineGraphData,
            };
            var result = r.GetStandaloneDocument();

            TextFileManage.CreateTextFile(outputFilename, result);
            TabbedText.WriteLine($"Launching {outputFilename}");
            string expectedOutput = outputFilename.Replace(".tex", ".pdf");
            if (!avoidProcessingIfPDFExists || !File.Exists(expectedOutput))
                ExecuteLatexProcess(subfolderName, outputFilename);
        }

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
