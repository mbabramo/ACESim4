using ACESim;
using ACESimBase.Games.AdditiveEvidenceGame;
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
            List<string> rowsToGet = new List<string>() { "All", "Settles", "Trial", "Shifting",   };
            List<string> replacementRowNames = rowsToGet.ToList();
            List<string> columnsToGet = new List<string>() { "Exploit", "Seconds", "All", "TrialCost", "FeeShifting", "FeeShiftingThreshold", "Alpha_Plaintiff_Quality", /* "Alpha_Plaintiff_Bias", */ "Evidence_Both_Quality", "Settles", "Trial", "PWelfare", "DWelfare", "PQuits", "DQuits", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PBestGuess", "DBestGuess" };
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
            public string Filter { get; set; }
            public double? c { get; set; }
            public double? q { get; set; }
            public double? t { get; set; }
            public double? mainVar { get; set; }
        }
        
        public static void GenerateDiagramsFromCSV()
        {
            string folder = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\AE results\AE016 -- winner-take-all";
            string csvFileName = "AE016--.csv";
            string fullFileName = Path.Combine(folder, csvFileName);
            string texContents = GenerateDiagramFromCSV(fullFileName, "AccSq");
            string outputFile = Path.Combine(folder, "AE016--AccSq.tex");
            File.WriteAllText(outputFile, texContents);
        }

        public sealed class AEDataMap : ClassMap<AEData>
        {
            public static string mainVarName { get; set; }
            public static string qVarName { get; set; } = "Evidence_Both_Quality";
            public static string cVarName { get; set; } = "TrialCost";
            public static string tVarName { get; set; } = "FeeShiftingThreshold";
            public AEDataMap()
            {
                Map(m => m.Filter).Name("Filter");
                Map(m => m.c).Name(cVarName);
                Map(m => m.q).Name(qVarName);
                Map(m => m.t).Name(tVarName);
                Map(m => m.mainVar).Name(mainVarName);
            }
        }

        public static string GenerateDiagramFromCSV(string csvFileName, string mainVarName, string qVarName = "Evidence_Both_Quality", string cVarName = "TrialCost", string tVarName = "FeeShiftingThreshold")
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
                aeData = csv.GetRecords<AEData>().Where(x => x.Filter == "All").ToList();
            }

            double[] tVarVals = Enumerable.Range(0, 101).Select(x => x / 100.0).ToArray();
            double[] qVarVals = new double[] { 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65 };
            double[] cVarVals = new double[] { 0, 0.0625, 0.125, 0.25 };
            
            List<List<List<List<double?>>>> graphData = new List<List<List<List<double?>>>>();
            foreach (var qVarVal in qVarVals)
            {
                List<List<List<double?>>> macroRow = new List<List<List<double?>>>();
                foreach (var cVarVal in cVarVals)
                {
                    // right now, each individual graph consists of just one line
                    List<List<double?>> individualGraph = new List<List<double?>>();
                    List<double?> lineInIndividualGraph = new List<double?>();
                    foreach (var tVarVal in tVarVals)
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
            string result = GenerateLatex(graphData, mainVarName);
            return result;
        }

        private static string GenerateLatex(List<List<List<List<double?>>>> overallData, string microYVariableName)
        {
            
            bool isStacked = false;

            var lineScheme = new List<string>()
            {
                "black, opacity=1.0, line width=0.5mm, solid",
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
                majorYValueNames = new List<string>() { "0.35", "0.4", "0.45", "0.5", "0.55", "0.6", "0.65", }, // major row values
                majorYAxisLabel = "q",
                majorXValueNames = new List<string>() { "0", "0.0625", "0.125", "0.25" }, // major column values
                majorXAxisLabel = "c",
                minorXValueNames = new List<string>() { "0", "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "0.9", "1" },
                minorXAxisLabel = "t",
                minorYValueNames = new List<string>() { "0", "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "0.9", "1" },
                minorYAxisLabel = microYVariableName,
                isStackedBar = isStacked,
                lineGraphData = lineGraphData,
            };


            var result = TikzHelper.GetStandaloneDocument(r.GetDrawCommands(), new List<string>() { "xcolor" }, additionalHeaderInfo: $@"
\usetikzlibrary{{calc}}
\usepackage{{relsize}}
\tikzset{{fontscale/.style = {{font=\relsize{{#1}}}}}}");
            return result;
        }
    }
}
