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

        static string filePrefix => new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing + "-";
        
        public static void BuildReport()
        {
            List<string> rowsToGet = new List<string>() { "All" }; // , "Settles", "Trial", "Shifting",   };
            List<string> replacementRowNames = rowsToGet.ToList();
            List<string> columnsToGet = new List<string>() { "Exploit", "Seconds", "All", "TrialCost", "FeeShifting", "FeeShiftingThreshold", "Alpha_Plaintiff_Quality", /* "Alpha_Plaintiff_Bias", */ "Evidence_Both_Quality", "Settles", "Trial", "PWelfare", "DWelfare", "PQuits", "DQuits", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "TrialRelativeAccSq", "TrialRelativeAccuracy", "TrialRelativeAccuracy_ForPlaintiff", "TrialRelativeAccuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PBestGuess", "DBestGuess" };
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
            string outputFileFullPath = Path.Combine(path, filePrefix + $"-{endOfFileName}.csv");
            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList();

            bool includeHeader = true;
            List<List<string>> outputLines = GetCSVLines(distinctOptionSets, map, rowsToGet, replacementRowNames, filePrefix, ".csv", "", path, includeHeader, columnsToGet, replacementColumnNames);

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
                foreach ((string targetVariable, string targetAbbreviation) in new (string, string)[] { ("Trial", "L"), ("Accuracy", "A"), ("Accuracy_ForPlaintiff", "A"), ("Accuracy_ForDefendant", "A"), ("AccSq", "A"), ("TrialRelativeAccuracy", "A"), ("TrialRelativeAccuracy_ForPlaintiff", "A"), ("TrialRelativeAccuracy_ForDefendant", "A"), ("TrialRelativeAccSq", "A"), ("POffer", "Bid"), ("DOffer", "Bid"), ("PWelfare", "w"), ("DWelfare", "w"), ("PQuits", "Q"), ("DQuits", "Q"), ("Exploit", "E") })
                {
                    string set = new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing;
                    string csvFileName = set + "--.csv";
                    string fullFileName = Path.Combine(folder, csvFileName);
                    string texContents = GenerateDiagramFromCSV(fullFileName, groupName, targetVariable, targetAbbreviation);
                    string outputFile = Path.Combine(folder, $"{set};{groupName};{targetVariable}.tex");
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

        public static string GenerateDiagramFromCSV(string csvFileName, string groupName, string mainVarName, string mainVarLabel, string qVarName = "Evidence_Both_Quality", string cVarName = "TrialCost", string tVarName = "FeeShiftingThreshold")
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
            double[] qVarVals = launcher.QualityLevels;
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
                        if (!added)
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

            var lineScheme = new List<string>()
            {
                "red, opacity=0.2, line width=0.1mm, solid",
            };
            var dataSeriesNames = new List<string>()
            {
                "",
            };

            int numMiniGraphDataSeries = lineScheme.Count();
            Random ran = new Random();

            var lineGraphData = overallData.Select(macroYData => macroYData.Select(individualMiniGraphData => new TikzLineGraphData(individualMiniGraphData, lineScheme, dataSeriesNames)).ToList()).ToList();

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
    }
}
