using ACESim;
using ACESimBase.Games.AdditiveEvidenceGame;
using ACESimBase.Util;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.Tikz;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LitigCharts.DataProcessingUtils;

namespace LitigCharts
{
    public static class AdditiveEvidenceDataProcessing
    {
        static string PrefixForEachFile => new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing;
        static string PrefixForEachFileWithHyphen => PrefixForEachFile + "-";
        
        public static void GenerateCSV()
        {
            List<string> rowsToGet = new List<string>() { "All" }; // , "Settles", "Trial", "Shifting",   };
            List<string> replacementRowNames = rowsToGet.ToList();
            List<string> columnsToGet = new List<string>() { "Exploit", "Seconds", "All", "TrialCost", "FeeShifting", "FeeShiftingThreshold", "Alpha_Plaintiff_Quality", /* "Alpha_Plaintiff_Bias", */ "Evidence_Both_Quality", "Settles", "Trial", "PWelfare", "DWelfare", "PQuits", "DQuits", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "DMSAcc", "DMSAccL1", "DMSTrialRelAccL1", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "TrialRelativeAccSq", "TrialRelativeAccuracy", "TrialRelativeAccuracy_ForPlaintiff", "TrialRelativeAccuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PBestGuess", "DBestGuess" };
            List<string> replacementColumnNames = columnsToGet.ToList();
            string endOfFileName = "";

            bool onlyAllFilter = false;
            if (onlyAllFilter)
            {
                rowsToGet = rowsToGet.Take(1).ToList();
                replacementRowNames = replacementRowNames.Take(1).ToList();
            }

            AdditiveEvidenceGameLauncher launcher = new AdditiveEvidenceGameLauncher();
            var gameOptionsSets = launcher.GetOptionsSets();
            var map = launcher.GetAdditiveEvidenceNameMap();
            string path = Launcher.ReportFolder();
            string outputFileFullPath = Path.Combine(path, PrefixForEachFileWithHyphen + $"-{endOfFileName}.csv");
            string outputMetafileFullPath = Path.Combine(path, PrefixForEachFileWithHyphen + $"-avgacc.csv");
            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList();

            bool includeHeader = true;
            List<List<string>> outputLines = GetCSVLines(distinctOptionSets, map, rowsToGet, replacementRowNames, PrefixForEachFileWithHyphen, ".csv", "", path, includeHeader, columnsToGet, replacementColumnNames);
            
            AddAveragesAcrossQualityLevels(outputLines);
            string aggregatedReportContents = MakeString(outputLines);
            TextFileManage.CreateTextFile(outputFileFullPath, aggregatedReportContents);
            string averageAccuracyReportContents = MakeString(CreateAverageAccuracyAggregation(outputLines));
            TextFileManage.CreateTextFile(outputMetafileFullPath, averageAccuracyReportContents);
        }

        private static void AddAveragesAcrossQualityLevels(List<List<string>> outputLines)
        {
            List<string> headerRow = outputLines[0];
            var groupNameIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "GroupName").i;
            var trialCostIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "TrialCost").i;
            var feeShiftingThresholdIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "FeeShiftingThreshold").i;
            var qualityIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "Evidence_Both_Quality").i;

            var remainingLines = outputLines.Skip(1).ToList();
            var groups = remainingLines.GroupBy(x => (x[groupNameIndex], x[trialCostIndex], x[feeShiftingThresholdIndex]));
            foreach (var group in groups)
            {
                List<string> rowToAdd = new List<string>();
                for (int i = 0; i < headerRow.Count; i++)
                {
                    if (i == qualityIndex)
                        rowToAdd.Add("-1"); // use to represent average
                    else
                    {
                        var stringValues = group.Select(x => x[i]).ToList();
                        if (stringValues.All(x => double.TryParse(x, out _)))
                        {
                            var avg = stringValues.Select(x => double.Parse(x)).Average().ToString();
                            rowToAdd.Add(avg);
                        }
                        else
                            rowToAdd.Add(group.First()[i]);
                    }
                }
                outputLines.Add(rowToAdd);
            }
        }

        private static List<List<string>> CreateAverageAccuracyAggregation(List<List<string>> originalCSVRows)
        {
            (string codedName, string assignedLetter, bool firstAccuracy)[] assumptionSets = new (string codedName, string assignedLetter, bool firstAccuracy)[]
            {
                ("orig", "A", true),
                ("es", "B", true),
                ("es", "C", false),
                ("marges", "D", false),
                ("ordes", "E", false),
                ("wtaes", "F", false),
                ("wtaesq", "G", false),
                ("wtaesraq", "H", false),
            };

            List<string> headerRow = originalCSVRows[0];
            var groupNameIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "GroupName").i;
            var trialCostIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "TrialCost").i;
            var feeShiftingThresholdIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "FeeShiftingThreshold").i;
            var qualityIndex = headerRow.Select((x, i) => (x, i)).First(y => y.x == "Evidence_Both_Quality").i;
            var acc1Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "DMSAccL1").i;
            var acc2Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "DMSTrialRelAccL1").i;
            var perCaseAcc1Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "Accuracy").i;
            var perCaseAcc2Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "TrialRelativeAccuracy").i;
            var costIncAcc1Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "Accuracy_ForPlaintiff").i;
            var costIncAcc2Index = headerRow.Select((x, i) => (x, i)).First(y => y.x == "TrialRelativeAccuracy_ForPlaintiff").i;

            var remainingLines = originalCSVRows.Skip(1).ToList();
            List<List<string>> outputRows = new List<List<string>>()
            {
            };
            foreach (var line in remainingLines)
            {
                string quality = line[qualityIndex];
                if (quality != "-1")
                    continue;
                string groupName = line[groupNameIndex];
                if (!assumptionSets.Any(x => x.codedName == groupName))
                    continue;
                foreach (var assumptionSet in assumptionSets.Where(x => x.codedName == groupName))
                {
                    List<string> outputRow = new List<string>();
                    outputRow.Add(assumptionSet.assignedLetter);
                    outputRow.Add(line[trialCostIndex]);
                    outputRow.Add(line[feeShiftingThresholdIndex]);
                    if (assumptionSet.firstAccuracy)
                    {
                        outputRow.Add(line[acc1Index]);
                        outputRow.Add(line[perCaseAcc1Index]);
                        outputRow.Add(line[costIncAcc1Index]);
                    }
                    else
                    {
                        outputRow.Add(line[acc2Index]);
                        outputRow.Add(line[perCaseAcc2Index]);
                        outputRow.Add(line[costIncAcc2Index]);
                    }
                    outputRows.Add(outputRow);
                }
            }
            outputRows = outputRows.OrderBy(x => x[0]).ThenBy(x => x[1]).ThenBy(x => x[2]).ToList();
            outputRows.Insert(0,
                new List<string>() { "Model", "TrialCost", "FeeShiftingThreshold", "AverageAccuracy", "PerCaseAccuracy", "CostInclusiveAccuracy" });
            return outputRows;
        }

        public class AEData
        {
            public string GroupName { get; set; }
            public string Filter { get; set; } = "All";
            public double? c { get; set; }
            public double? q { get; set; }
            public double? t { get; set; }
            public double? mainVar { get; set; }

            public string modelName { get; set; }
        }

        public sealed class AEDataMap : ClassMap<AEData>
        {
            public static bool makeAverageAccuracyDiagram { get; set; }
            public static string mainVarName { get; set; }
            public static string macroYVarName { get; set; } 
            public static string cVarName { get; set; } 
            public static string tVarName { get; set; } 
            public AEDataMap()
            {
                if (makeAverageAccuracyDiagram)
                {
                    macroYVarName = "Model";
                    Map(m => m.modelName).Name(macroYVarName);
                }
                else
                {
                    macroYVarName = "Evidence_Both_Quality";
                    Map(m => m.q).Name(macroYVarName);
                    Map(m => m.Filter).Name("Filter");
                    Map(m => m.GroupName).Name("GroupName");
                }
                Map(m => m.c).Name(cVarName);
                Map(m => m.t).Name(tVarName);
                Map(m => m.mainVar).Name(mainVarName);
            }
        }

        public static void GenerateDiagramsFromCSV()
        {
            string folder = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults"; // @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\AE results\AE016 -- winner-take-all";
            string set = new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing;
            CreateAggregateDiagrams(folder, set);
            CreateAverageAccuracyDiagrams(folder, set);
        }

        private static void CreateAggregateDiagrams(string folder, string set)
        {
            var groupNames = new AdditiveEvidenceGameLauncher().GetGroupNames();
            foreach (string groupName in groupNames)
            {
                foreach ((string targetVariable, string targetAbbreviation) in new (string, string)[] { ("Trial", "$L$"), ("Accuracy", "$A$"), ("Accuracy_ForPlaintiff", "$A$"), ("Accuracy_ForDefendant", "$A$"), ("DMSAccL1", "$A$"), ("DMSTrialRelAccL1", "$A$"), ("TrialRelativeAccuracy", "$A$"), ("TrialRelativeAccuracy_ForPlaintiff", "$A$"), ("TrialRelativeAccuracy_ForDefendant", "$A$"), ("TrialRelativeAccSq", "$A$"), ("POffer", @"$\mathcal{B}$"), ("DOffer", @"$\mathcal{B}$"), ("PWelfare", @"$\mathcal{U}$"), ("DWelfare", @"$\mathcal{U}$"), ("PQuits", "$Q$"), ("DQuits", "$Q$"), ("Exploit", @"$\mathcal{E}$") })
                {
                    string csvFileName = set + "--.csv";
                    string fullFileName = Path.Combine(folder, csvFileName);
                    string texContents = GenerateAggregateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, HowManyQualityLevels.Full);
                    string outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};full.tex");
                    File.WriteAllText(outputFile, texContents);

                    texContents = GenerateAggregateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, HowManyQualityLevels.Shorter);
                    outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};shorter.tex");
                    File.WriteAllText(outputFile, texContents);

                    texContents = GenerateAggregateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, HowManyQualityLevels.Shortest);
                    outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};shortest.tex");
                    File.WriteAllText(outputFile, texContents);

                    texContents = GenerateAggregateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, HowManyQualityLevels.Average);
                    outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};average.tex");
                    File.WriteAllText(outputFile, texContents);
                }
            }
        }


        private static void CreateAverageAccuracyDiagrams(string folder, string set)
        {
            string csvFileName2 = set + "--avgacc.csv";
            string fullFileName2 = Path.Combine(folder, csvFileName2);
            foreach (string accuracyString in new string[] { "AverageAccuracy", "PerCaseAccuracy", "CostInclusiveAccuracy" })
            {
                string texContents = GenerateAverageAccuracyDiagramFromCSV(fullFileName2, accuracyString, "$A$");
                string outputFile = Path.Combine(folder, $"{set};avgacc;{accuracyString}.tex");
                File.WriteAllText(outputFile, texContents);
            }
        }

        public enum HowManyQualityLevels
        {
            Full,
            Shorter,
            Shortest,
            Average
        }

        public static string GenerateAggregateDiagramFromCSV(string csvFileName, string groupName, string mainVarName, string mainVarLabel, HowManyQualityLevels howManyLevels, string macroYVarName = "Evidence_Both_Quality", string cVarName = "TrialCost", string tVarName = "FeeShiftingThreshold")
        {
            AEDataMap.makeAverageAccuracyDiagram = false;
            List<AEData> aeData;
            AdditiveEvidenceGameLauncher launcher;
            double[] tVarVals, cVarVals;
            SetupAggregateDiagram(csvFileName, groupName, mainVarName, macroYVarName, cVarName, tVarName, out aeData, out launcher, out tVarVals, out cVarVals);

            double[] qVarVals = howManyLevels switch
            {
                HowManyQualityLevels.Average => launcher.QualityLevels_Average,
                HowManyQualityLevels.Shortest => launcher.QualityLevels_Shortest,
                HowManyQualityLevels.Shorter => launcher.QualityLevels_Shorter,
                HowManyQualityLevels.Full => launcher.QualityLevels,
                _ => throw new Exception()
            };

            List<List<List<List<double?>>>> graphData = new List<List<List<List<double?>>>>();
            foreach (var qVarVal in qVarVals)
            {
                bool IsVerticalAxisMatch(AEData ae) => Math.Abs((double)ae.q - qVarVal) < 0.001;
                Func<AEData, bool> isVerticalAxisMatch = IsVerticalAxisMatch;
                AddMacroRow(aeData, tVarVals, cVarVals, graphData, isVerticalAxisMatch);
            }

            string result = GenerateLatex(graphData, qVarVals.Select(q => qVarToString(q)).ToList(), cVarVals.Select(c => cVarToString(c)).ToList(), tVarVals.Select(t => tVarToString(t)).ToList(), mainVarLabel);

            return result;
        }

        public static string GenerateAverageAccuracyDiagramFromCSV(string csvFileName, string mainVarName, string mainVarLabel, string macroYVarName = "Model", string cVarName = "TrialCost", string tVarName = "FeeShiftingThreshold")
        {
            AEDataMap.makeAverageAccuracyDiagram = true;
            List<AEData> aeData;
            AdditiveEvidenceGameLauncher launcher;
            double[] tVarVals, cVarVals;
            SetupAggregateDiagram(csvFileName, null, mainVarName, macroYVarName, cVarName, tVarName, out aeData, out launcher, out tVarVals, out cVarVals);

            string[] modelNames = new string[] { "A", "B", "C", "D", "E", "F", "G", "H" };

            List<List<List<List<double?>>>> graphData = new List<List<List<List<double?>>>>();
            foreach (var modelName in modelNames)
            {
                bool IsVerticalAxisMatch(AEData ae) => ae.modelName == modelName;
                Func<AEData, bool> isVerticalAxisMatch = IsVerticalAxisMatch;
                AddMacroRow(aeData, tVarVals, cVarVals, graphData, isVerticalAxisMatch);
            }

            string result = GenerateLatex(graphData, modelNames.ToList(), cVarVals.Select(c => cVarToString(c)).ToList(), tVarVals.Select(t => tVarToString(t)).ToList(), mainVarLabel, "Model");

            return result;
        }

        private static void SetupAggregateDiagram(string csvFileName, string groupName, string mainVarName, string macroYVarName, string cVarName, string tVarName, out List<AEData> aeData, out AdditiveEvidenceGameLauncher launcher, out double[] tVarVals, out double[] cVarVals)
        {
            AEDataMap.macroYVarName = macroYVarName;
            AEDataMap.mainVarName = mainVarName;
            AEDataMap.cVarName = cVarName;
            AEDataMap.tVarName = tVarName;

            aeData = null;
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);

            using (var reader = new StreamReader(csvFileName))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap<AEDataMap>();
                if (groupName == null)
                    aeData = csv.GetRecords<AEData>().ToList();
                else
                    aeData = csv.GetRecords<AEData>().Where(x => x.Filter == "All").Where(x => x.GroupName == groupName).ToList();
            }

            launcher = new AdditiveEvidenceGameLauncher();
            tVarVals = launcher.FeeShiftingThresholds;
            cVarVals = launcher.CostsLevels;
        }

        private static void AddMacroRow(List<AEData> aeData, double[] tVarVals, double[] cVarVals, List<List<List<List<double?>>>> graphData, Func<AEData, bool> isVerticalAxisMatch)
        {
            List<List<List<double?>>> macroRow = new List<List<List<double?>>>();
            foreach (var cVarVal in cVarVals)
            {
                // right now, each individual graph consists of just one line
                List<List<double?>> individualGraph = new List<List<double?>>();
                List<double?> lineInIndividualGraph = new List<double?>();
                foreach (var tVarVal in tVarVals.ToArray())
                {
                    bool added = false;
                    foreach (var ae in aeData)
                    {
                        if (isVerticalAxisMatch(ae) && Math.Abs((double)ae.c - cVarVal) < 0.001 && Math.Abs((double)ae.t - tVarVal) < 0.001)
                        {
                            lineInIndividualGraph.Add(ae.mainVar);
                            added = true;
                        }
                    }
                    bool throwIfMissingDatum = true;
                    if (throwIfMissingDatum && !added)
                        throw new Exception($"Missing datum for t={tVarVal}");
                }
                individualGraph.Add(lineInIndividualGraph);
                macroRow.Add(individualGraph);
            }
            graphData.Add(macroRow);
        }

        private static string RemoveTrailingZeros(string input)
        {
            for (int i = input.Length - 1; i > 0; i--)
            {
                if (!input.Contains(".")) break;
                if (input[i].Equals('0'))
                {
                    input = input.Remove(i);
                }
                else break;
            }
            return input;
        }


        static string qVarToString(double x) => x switch
        {
            1.0 / 6.0 => @"$\frac{1}{6}$",
            2.0 / 6.0 => @"$\frac{1}{3}$",
            3.0 / 6.0 => @"$\frac{1}{2}$",
            4.0 / 6.0 => @"$\frac{2}{3}$",
            5.0 / 6.0 => @"$\frac{5}{6}$",
            -1 => @"$\overline{q}$",
            _ => throw new Exception()
        };
        static string tVarToString(double x)
        {
            if (Math.Abs(x) < 1E-6)
                return "0";
            else if (x == 1)
                return "1";
            else if (x == 0.5)
                return "0.5";
            else
                return "";
        }
        static string cVarToString(double x) => x switch
        {
            0 => "$0$",
            0.0625 => @"$\frac{1}{16}$",
            0.125 => @"$\frac{1}{8}$",
            0.25 => @"$\frac{1}{4}$",
            0.5 => @"$\frac{1}{2}$",
            _ => throw new Exception()
        };

        private static string GenerateLatex(List<List<List<List<double?>>>> overallData, List<string> qValueStrings, List<string> cValueStrings, List<string> tValueStrings, string microYAxisLabel, string majorYAxisLabel = "$q$")
        {
            bool useHollow = tValueStrings.Count >= 25;
            var lineScheme = new List<string>()
            {
                useHollow ? "red, opacity=0.2, line width=0.1mm, solid" : "red, fill=red, opacity=0.4, line width=0.1mm, solid",
            };
            var dataSeriesNames = new List<string>()
            {
                "",
            };

            int numMiniGraphDataSeries = lineScheme.Count();
            Random ran = new Random();

            var lineGraphData = overallData.Select(macroYData => macroYData.Select(individualMiniGraphData => new TikzLineGraphData(individualMiniGraphData.Select(l => l.Select(x => (x == null || x > 1.01 || x < -0.1) ? null : x).ToList()).ToList(), lineScheme, dataSeriesNames)).ToList()).ToList();

            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorYValueNames = qValueStrings, // major row values
                majorYAxisLabel = majorYAxisLabel,
                yAxisLabelOffsetLeft = 0.8,
                majorXValueNames = cValueStrings, // major column values
                majorXAxisLabel = "$c$",
                xAxisLabelOffsetDown = 1.0,
                minorXValueNames = tValueStrings,
                minorXAxisLabel = "$t$",
                minorYValueNames = new List<string>() { "0", "0.25", "0.5", "0.75", "1" },
                minorYAxisLabel = microYAxisLabel,
                graphType = TikzAxisSet.GraphType.Scatter,
                lineGraphData = lineGraphData,
            };


            var result = r.GetStandaloneDocument();
            return result;
        }

        public static void OrganizeIntoFolders()
        {
            string sourcePath = Launcher.ReportFolder();
            string destinationPath = @"C:\Primary results";
            var sourceDirectory = new DirectoryInfo(sourcePath);
            List<System.IO.FileInfo> fileList = sourceDirectory.GetFiles("*.*", System.IO.SearchOption.AllDirectories).Where(x => x.Name.StartsWith(PrefixForEachFile)).ToList();
            List<List<string>> filePaths = new List<List<string>>();
            foreach (var file in fileList)
            {
                string mainFileName = Path.GetFileNameWithoutExtension(file.FullName);
                if (mainFileName.EndsWith("Coordinator") || mainFileName.StartsWith("log") || mainFileName.Contains("CombineOptionSets"))
                    continue;
                string extension = Path.GetExtension(file.FullName);
                string locationComponents = mainFileName.Replace(PrefixForEachFileWithHyphen, "").Replace(PrefixForEachFile + ";", "");
                if (locationComponents == "-" && (extension == ".csv" || extension == ".xlsx"))
                    locationComponents = "Aggregated Report";
                if (locationComponents == "-avgacc" && (extension == ".csv" || extension == ".xlsx"))
                    locationComponents = "Average accuracy (selected models);Average Accuracy Report";
                List<string> locationComponentsList = locationComponents.Split(';').ToList();
                int numItems = locationComponentsList.Count;
                string lastComponent = locationComponentsList[numItems - 1];
                if (lastComponent.StartsWith("t0") || lastComponent.StartsWith("t1"))
                {
                    (string original, string replacement)?[] suffixesUsed = new (string original, string replacement)?[] { ("-equ", "Equilibria"), ("-log", "Logs"), ("-offers-eq1", "Offer Heatmaps") };
                    var suffix = suffixesUsed.FirstOrDefault(x => lastComponent.EndsWith(x.Value.original));
                    if (suffix == null)
                    {
                        locationComponentsList[numItems - 1] = "Reports";
                        locationComponentsList.Add(lastComponent);
                    }
                    else
                    {
                        locationComponentsList[numItems - 1] = suffix.Value.replacement; // put "Equilibria" etc. as a folder containing all of the files for that t
                        string lastComponentWithoutSuffix = lastComponent.Replace(suffix.Value.original, "");
                        locationComponentsList.Add(lastComponentWithoutSuffix);
                    }
                }
                else
                {
                    foreach (string moveFromEnd in new string[] { "full", "shorter", "shortest", "average" })
                    {
                        if (lastComponent == moveFromEnd)
                        {
                            locationComponentsList[numItems - 1] = locationComponentsList[numItems - 2];
                            locationComponentsList[numItems - 2] = moveFromEnd;
                        }
                    }
                }
                numItems = locationComponentsList.Count;

                for (int i = 0; i < numItems; i++)
                {
                    string component = locationComponentsList[i];
                    // add a space between the variable name and number
                    if (component.StartsWith("t") || component.StartsWith("q") || component.StartsWith("c"))
                    {
                        var restOfString = component[1..];
                        if (double.TryParse(restOfString, out double _))
                        {
                            component = component[0] + " " + restOfString;
                        }
                    }
                    // make other changes
                    component = component.Replace("full", "Scatterplots (full)").Replace("shorter", "Scatterplots (short)").Replace("shortest", "Scatterplots (shortest)").Replace("average", "Scatterplots (average)");
                    locationComponentsList[i] = component;
                }

                // replacement short series names with longer names
                (string original, string replacement)?[] replacements = new (string original, string replacement)?[] { ("dms", "DMS formulas"), ("orig", "Original"), ("es", "Equal information strength"), ("noshare", "No shared information"), ("pinfo00", "P has no info"), ("pinfo25", "P 25% of info"), ("wta", "Winner take all (otherwise original)"), ("wtaes", "Winner take all"), ("trialg", "Trial guaranteed"), ("wtaesra", "Winner take all, risk aversion"), ("ordes", "Ordinary fee shifting"), ("marges", "Margin-of-victory shifting"), ("avgacc", "Average accuracy (selected models)") };
                List<(string original, string replacement)?> replacementsList = replacements.ToList();
                foreach (var r in replacements)
                    replacementsList.Add((r.Value.original + "q", r.Value.replacement + ", with quitting"));
                var match = replacementsList.FirstOrDefault(x => x.Value.original == locationComponentsList[0]);
                if (match != null)
                    locationComponentsList[0] = match.Value.replacement;

                // copy to destination
                List<string> destinationPathComponents = destinationPath.Split("/").ToList();
                for (int i = destinationPathComponents.Count - 1; i >= 0; i--)
                {
                    locationComponentsList.Insert(0, destinationPathComponents[i]);
                }
                numItems = locationComponentsList.Count;
                locationComponentsList[numItems - 1] = locationComponentsList[numItems - 1] + extension;
                var exceptLast = locationComponentsList.ToArray()[..^1];
                var targetDirectory = String.Join("\\", exceptLast);
                if (!Directory.Exists(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);
                string targetPath = Path.Combine(locationComponentsList.ToArray());
                if (!File.Exists(targetPath))
                {
                    TabbedText.WriteLine($"Copying {file.FullName} to {targetPath}");
                    File.Copy(file.FullName, targetPath);
                }
            }
        }
    }
}
