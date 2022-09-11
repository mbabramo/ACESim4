using ACESim;
using ACESimBase.Games.AdditiveEvidenceGame;
using ACESimBase.Util;
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
        
        public static void BuildReport()
        {
            List<string> rowsToGet = new List<string>() { "All" }; // , "Settles", "Trial", "Shifting",   };
            List<string> replacementRowNames = rowsToGet.ToList();
            List<string> columnsToGet = new List<string>() { "Exploit", "Seconds", "All", "TrialCost", "FeeShifting", "FeeShiftingThreshold", "Alpha_Plaintiff_Quality", /* "Alpha_Plaintiff_Bias", */ "Evidence_Both_Quality", "Settles", "Trial", "PWelfare", "DWelfare", "PQuits", "DQuits", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "DMSAcc", "DMSAccL1Norm", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "TrialRelativeAccSq", "TrialRelativeAccuracy", "TrialRelativeAccuracy_ForPlaintiff", "TrialRelativeAccuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PBestGuess", "DBestGuess" };
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
            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList(); 

            bool includeHeader = true;
            List<List<string>> outputLines = GetCSVLines(distinctOptionSets, map, rowsToGet, replacementRowNames, PrefixForEachFileWithHyphen, ".csv", "", path, includeHeader, columnsToGet, replacementColumnNames);

            string result = MakeString(outputLines);
            TextFileManage.CreateTextFile(outputFileFullPath, result);
        }

        public class AEData
        {
            public string GroupName { get; set; }
            public string Filter { get; set; }
            public double? c { get; set; }
            public double? q { get; set; }
            public double? t { get; set; }
            public double? mainVar { get; set; }
        }
        
        public static void GenerateDiagramsFromCSV()
        {
            string folder = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults"; // @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\AE results\AE016 -- winner-take-all";
            var groupNames = new AdditiveEvidenceGameLauncher().GetGroupNames();
            foreach (string groupName in groupNames)
            {
                foreach ((string targetVariable, string targetAbbreviation) in new (string, string)[] { ("Trial", "L"), ("Accuracy", "A"), ("Accuracy_ForPlaintiff", "A"), ("Accuracy_ForDefendant", "A"), ("DMSAccL1Norm", "A"), ("TrialRelativeAccuracy", "A"), ("TrialRelativeAccuracy_ForPlaintiff", "A"), ("TrialRelativeAccuracy_ForDefendant", "A"), ("TrialRelativeAccSq", "A"), ("POffer", "Bid"), ("DOffer", "Bid"), ("PWelfare", "w"), ("DWelfare", "w"), ("PQuits", "Q"), ("DQuits", "Q"), ("Exploit", "E") })
                {
                    string set = new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing;
                    string csvFileName = set + "--.csv";
                    string fullFileName = Path.Combine(folder, csvFileName);
                    string texContents = GenerateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, false);
                    string outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};full.tex");
                    File.WriteAllText(outputFile, texContents);
                    texContents = GenerateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation, true);
                    outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable};short.tex");
                    File.WriteAllText(outputFile, texContents);
                }
            }
        }

        public sealed class AEDataMap : ClassMap<AEData>
        {
            public static string mainVarName { get; set; }
            public static string qVarName { get; set; } = "Evidence_Both_Quality";
            public static string cVarName { get; set; } = "TrialCost";
            public static string tVarName { get; set; } = "FeeShiftingThreshold";
            public AEDataMap()
            {
                Map(m => m.GroupName).Name("GroupName");
                Map(m => m.Filter).Name("Filter");
                Map(m => m.c).Name(cVarName);
                Map(m => m.q).Name(qVarName);
                Map(m => m.t).Name(tVarName);
                Map(m => m.mainVar).Name(mainVarName);
            }
        }

        public static string GenerateDiagramFromCSV(string csvFileName, string groupName, string mainVarName, string mainVarLabel, bool specificQualityLevelsOnly, string qVarName = "Evidence_Both_Quality", string cVarName = "TrialCost", string tVarName = "FeeShiftingThreshold")
        {
            AEDataMap.mainVarName = mainVarName;
            AEDataMap.qVarName = qVarName;
            AEDataMap.cVarName = cVarName;
            AEDataMap.tVarName = tVarName;

            List<AEData> aeData = null;
            
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            
            using (var reader = new StreamReader(csvFileName))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap<AEDataMap>();
                aeData = csv.GetRecords<AEData>().Where(x => x.Filter == "All").Where(x => x.GroupName == groupName).ToList();
            }

            AdditiveEvidenceGameLauncher launcher = new AdditiveEvidenceGameLauncher();
            double[] tVarVals = launcher.FeeShiftingThresholds; 
            double[] qVarVals = specificQualityLevelsOnly ? launcher.QualityLevels_Specific : launcher.QualityLevels;
            double[] cVarVals = launcher.CostsLevels;
            
            List<List<List<List<double?>>>> graphData = new List<List<List<List<double?>>>>();
            foreach (var qVarVal in qVarVals)
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
                            if (ae.q == qVarVal && ae.c == cVarVal && ae.t == tVarVal)
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
            string tVarToString(double x)
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
            string result = GenerateLatex(graphData, qVarVals.Select(q => q.ToString("0.00")).ToList(), cVarVals.Select(c => RemoveTrailingZeros(c.ToString("0.0000"))).ToList(), tVarVals.Select(t => tVarToString(t)).ToList(), mainVarLabel);
            
            return result;
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

        private static string GenerateLatex(List<List<List<List<double?>>>> overallData, List<string> qValueStrings, List<string> cValueStrings, List<string> tValueStrings, string microYAxisLabel)
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
                majorYAxisLabel = "q",
                yAxisLabelOffsetLeft = 1.4,
                majorXValueNames = cValueStrings, // major column values
                majorXAxisLabel = "c",
                xAxisLabelOffsetDown = 1.0,
                minorXValueNames = tValueStrings,
                minorXAxisLabel = "t",
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
                if (mainFileName.Contains("AE049;dms;TrialRelativeAccuracy;full"))
                {
                    var DEBUG = 0;
                }
                if (mainFileName.EndsWith("Coordinator") || mainFileName.StartsWith("log") || mainFileName.Contains("CombineOptionSets"))
                    continue;
                string extension = Path.GetExtension(file.FullName);
                string locationComponents = mainFileName.Replace(PrefixForEachFileWithHyphen, "").Replace(PrefixForEachFile + ";", "");
                if (locationComponents == "-" && (extension == ".csv" || extension == ".xlsx"))
                    locationComponents = "Aggregated Report";
                List<string> locationComponentsList = locationComponents.Split(';').ToList();
                int numItems = locationComponentsList.Count;
                string lastComponent = locationComponentsList[numItems - 1];
                if (lastComponent.StartsWith("t0") || lastComponent.StartsWith("t1"))
                {
                    (string original, string replacement)?[] suffixesUsed = new (string original, string replacement)?[] { ("-equ", "Equilibria"), ("-log", "Logs"), ("-heatmap-eq1", "Heatmaps") };
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
                    foreach (string moveFromEnd in new string[] { "full", "short" })
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
                    component = component.Replace("full", "Scatterplots (full)").Replace("short", "Scatterplots (short)");
                    locationComponentsList[i] = component;
                }

                // replacement short series names with longer names
                (string original, string replacement)?[] replacements = new (string original, string replacement)?[] { ("dms", "DMS formulas"), ("orig", "Original"), ("es", "Equal information strength"), ("noshare", "No shared information"), ("pinfo00", "P has no info"), ("pinfo25", "P 25% of info"), ("wta", "Winner take all"), ("wtaes", "Winner take all, equal information strength"), ("trialg", "Trial guaranteed"), ("wtaesra", "Winner take all, equal information strength, risk aversion") };
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
                    Console.WriteLine($"Copying {file.FullName} to {targetPath}");
                    File.Copy(file.FullName, targetPath);
                }
            }
        }
    }
}
