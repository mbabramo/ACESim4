using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using System;
using System.IO;
using CsvHelper;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace LitigCharts
{
    class Program
    {
        static void Main(string[] args)
        {
            string prefix = "R113";
            //CopyAzureFiles(prefix);
            //InformationSetCharts();
            AggregateDMSModel(prefix);
            //var results_Original = MakeString(GetDataForCostsShiftingAndQualities(prefix, "noise25", "All", variable, aggregateToGetQualitySum: true));
        }

        private static void AggregateDMSModel(string prefix)
        {
            string[] inputColumnsOriginal = new string[] { "Settles", "Trial", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PWelfare", "DWelfare" };
            string[] outputColumnsOriginal = new string[] { "Settles", "Trial", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "AccuracySq", "Accuracy", "AccuracyForP", "AccuracyForD", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PWelfare", "DWelfare" };
            string[] noiseSets = allSets.Take(3).ToArray();
            string[] sharedSets = allSets.Skip(3).Take(5).ToArray();
            string[] pstrengthSets = allSets.Skip(8).ToArray();
            (string set, string column)[] inputColumnsWithSets = (
                from x in noiseSets
                from y in inputColumnsOriginal
                select (x, y)
                ).ToArray();
            string[] outputColumnsRevised = (
                from x in noiseSets
                from y in outputColumnsOriginal
                select x + "-" + y
                ).ToArray();


            noiseSets.Zip(inputColumnsOriginal, (s, c) => (s, c)).ToArray();
            string result = GetStringWithSeparateLineForEachCostThresholdAndQuality(prefix, "All", inputColumnsWithSets, outputColumnsRevised, true);
            Console.WriteLine(result);
        }

        static string[] allSets = new string[] { "noise00", "noise25", "noise50", "shared00", "shared25", "shared50", "shared75", "shared100", "pstrength00", "pstrength25", "pstrength50", "pstrength75", "pstrength100" }; // { "orig", "bl_es",  "bl", "es" };
        static double[] allQualities = new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 };
        static double[] allCosts = new double[] { 0, 0.15, 0.30, 0.45, 0.60 };
        static double[] allFeeShifting = new double[] { 0, 0.25, 0.50, 0.75, 1.0 };

        private static void CopyAzureFiles(string prefix)
        {
            string path = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\DMS nonlinear results 1";
            string settingString(string set, double quality, double costs, double feeShiftingThreshold) => $"{set}q{(int)(quality * 100)}c{(int)(costs * 100)}t{(int)(feeShiftingThreshold * 100)}";
            foreach (string set in allSets)
            foreach (double quality in allQualities)
                foreach (double costs in allCosts)
                    foreach (double feeShifting in allFeeShifting)
                    {
                            string filename = prefix + " " + settingString(set, quality, costs, feeShifting) + ".csv";
                            TextFileCreate.CopyFileFromAzure("results", filename, path);
                    }
        }

        private static string MakeString(double[][][] values)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < values.GetLength(0); i++)
                b.AppendLine(MakeString(values[i]));
            return b.ToString();
        }

        private static string MakeString(double[][] values)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < values.GetLength(0); i++)
                b.AppendLine(String.Join(",", values[i]));
            return b.ToString();
        }

        private static string GetStringWithSeparateLineForEachCostThresholdAndQuality(string prefix, string filterOfRowsToGet, (string set, string column)[] inputColumns, string[] outputColumns, bool aggregateToGetQualitySum)
        {
            int numQualities = aggregateToGetQualitySum ? 20 : allQualities.Length;
            double[] qualityValues = aggregateToGetQualitySum ? Enumerable.Range(0, 20).Select(x => 0.025 + x * 0.05).ToArray() : allQualities;

            StringBuilder b = new StringBuilder();
            double[][][][] results = inputColumns.Select(c => GetDataForCostsShiftingAndQualities(prefix, c.set, filterOfRowsToGet, c.column, aggregateToGetQualitySum)).ToArray();
            b.Append("CostCat,ThreshCat,QualCat,Cost,Threshold,Quality");
            foreach (string outputColumn in outputColumns)
                b.Append("," + outputColumn);
            b.AppendLine("");
            for (int c = 0; c < allCosts.Length; c++)
            {
                for (int f = 0; f < allFeeShifting.Length; f++)
                {
                    for (int q = 0; q < numQualities; q++)
                    {
                        b.Append($"{c + 1},{f + 1},{q + 1},{allCosts[c]},{allFeeShifting[f]},{qualityValues[q]}"); // the categorical variables start at 1
                        for (int columnToGet = 0; columnToGet < inputColumns.Length; columnToGet++)
                        {
                            b.Append(",");
                            b.Append(results[columnToGet][c][f][q].ToString());
                        }
                        b.AppendLine("");
                    }
                }
            }

            return b.ToString();
        }

        private static double[][][] GetDataForCostsShiftingAndQualities(string prefix, string set, string filterOfRowsToGet, string columnToGet, bool aggregateToGetQualitySum)
        {
            // This will give us the array of charts for our graph -- we go across (all costs, then all fee shifting)
            double[][][] results = new double[allCosts.Length][][];
            for (int c = 0; c < allCosts.Length; c++)
            {
                results[c] = new double[allFeeShifting.Length][];
                for (int f = 0; f < allFeeShifting.Length; f++)
                {
                    var resultForAllQualities = aggregateToGetQualitySum ? GetDataForQualities_Aggregating(prefix, set, allQualities, allCosts[c], allFeeShifting[f], columnToGet) : GetDataForQualities(prefix, set, allQualities, allCosts[c], allFeeShifting[f], filterOfRowsToGet, columnToGet);
                    results[c][f] = resultForAllQualities;
                }
            }
            return results;
        }

        private static double[] GetDataForQualities_Aggregating(string prefix, string set, double[] originalQualityValues, double costs, double feeShifting,  string columnToGet)
        {
            string[] filterOfRowsToGet = Enumerable.Range(1, 20).Select(x => $"QualitySum{x}").ToArray();
            var results = GetDataForSpecificSettings_AggregatedAcrossQualityValues(prefix, set, originalQualityValues, costs, feeShifting, filterOfRowsToGet, new string[] { columnToGet });
            int length = results.GetLength(0);
            var oneColumnOnly = Enumerable.Range(0, length).Select(i => results[i, 0] ?? 0).ToArray();
            return oneColumnOnly;
        }

        private static double[] GetDataForQualities(string prefix, string set, double[] qualityValues, double costs, double feeShifting, string filterOfRowsToGet, string columnToGet) => qualityValues.Select(x => GetDataForSpecificSettings(prefix, set, x, costs, feeShifting, filterOfRowsToGet, columnToGet) ?? 0.0).ToArray();

        private static double? GetDataForSpecificSettings(string prefix, string set, double quality, double costs, double feeShifting, string filterOfRowsToGet, string columnToGet)
        {
            var singleRowResults = GetDataForSpecificSettings(prefix, set, quality, costs, feeShifting, new string[] { filterOfRowsToGet }, new string[] { columnToGet });
            return singleRowResults[0, 0];
        }

        private static double?[,] GetDataForSpecificSettings_AggregatedAcrossQualityValues(string prefix, string set, double[] originalQualityValues, double costs, double feeShifting, string[] filtersOfRowsToGet, string[] columnsToGet)
        {
            // Here, we have multiple files with original quality values, but we care about QualitySum. So we need to aggregate the QualitySum values, weighted by the All column. 
            List<string> columnsToGetRevisedList = new List<string>() { "All" };
            columnsToGetRevisedList.AddRange(columnsToGet);
            string[] columnsToGetRevisedArray = columnsToGetRevisedList.ToArray();
            double?[,] numerators = new double?[filtersOfRowsToGet.Length, columnsToGet.Length];
            double[] denominators = new double[filtersOfRowsToGet.Length];
            for (int f = 0; f < filtersOfRowsToGet.Length; f++)
            {
                denominators[f] = 0;
                for (int c = 0; c < columnsToGet.Length; c++)
                {
                    numerators[f, c] = 0;
                }
            }
            foreach (double originalQualityValue in originalQualityValues)
            {
                double?[,] results = GetDataForSpecificSettings(prefix, set, originalQualityValue, costs, feeShifting, filtersOfRowsToGet, columnsToGetRevisedArray);
                for (int f = 0; f < filtersOfRowsToGet.Length; f++)
                {
                    for (int c = 0; c < columnsToGet.Length; c++)
                        numerators[f, c] += (results[f, 0] ?? 0) * (results[f, 1 + c] ?? 0);
                    denominators[f] += results[f, 0] ?? 0;
                }
            }
            for (int f = 0; f < filtersOfRowsToGet.Length; f++)
                for (int c = 0; c < columnsToGet.Length; c++)
                    numerators[f, c] /= denominators[f];
            return numerators;
        }

        private static double?[,] GetDataForSpecificSettings(string prefix, string set, double quality, double costs, double feeShifting, string[] filtersOfRowsToGet, string[] columnsToGet)
        {
            string settingString(string set, double quality, double costs, double feeShiftingThreshold) => $"{set}q{(int)(quality * 100)}c{(int)(costs * 100)}t{(int)(feeShiftingThreshold * 100)}";
            string path = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\DMS nonlinear results 1";
            string setting = settingString(set, quality, costs, feeShifting);
            string filename = prefix + " " + setting + ".csv";
            string combined = Path.Combine(path, filename);
            (string columnName, string expectedText)[][] rowsToFind = new (string columnName, string expectedText)[filtersOfRowsToGet.Length][];
            for (int f = 0; f < filtersOfRowsToGet.Length; f++)
            {
                rowsToFind[f] = new (string columnName, string expectedText)[2];
                rowsToFind[f][0] = ("OptionSet", setting);
                rowsToFind[f][1] = ("Filter", filtersOfRowsToGet[f]);
            }
            // string[] columnsToGet = new string[] { "Trial", "AccSq", "POffer", "DOffer" };
            var results = CSVData.GetCSVData(combined, rowsToFind, columnsToGet);
            return results;
        }

        //private static void RepeatedLineChart()
        //{
        //    string path = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\DMS nonlinear results 1";
        //    string filename = "R094 AllCombined.csv";

        //    var qualities = new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 };
        //    var costs = new double[] { 0, 0.15, 0.30, 0.45, 0.60 };
        //    var feeShiftingThresholds = new double?[] { 0, 0.25, 0.50, 0.75, 1.0 };

        //}

        private static void InformationSetCharts()
        {
            string path = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\bluffing results";

            //todo; // check the proportion that settle in round 1 etc. Then, if proportion is sufficiently low for next round, leave it out altogether. (relevant for hicost).

            //todo; // consider adding settlement percentages in Round header -- but then we'll need to repeat the header) -- we might have a big header "Round (Settlement %)" and then entries like 1 (76%).

            string[] filenames = new[] { "R079 baseline", "R079 cfrplus", "R079 locost", "R079 hicost", "R079 pra", "R079 dra", "R079 bra", "R079 goodinf", "R079 badinf", "R079 pgdbinf", "R079 pbdginf" };
            filenames = CreateInformationSetCharts(path, filenames);
        }

        private static string[] CreateInformationSetCharts(string path, string[] filenames)
        {
            foreach (string filename in filenames)
                ACESimBase.Util.InformationSetCharts.PlotPAndD_WithHidden(path, filename, numRounds: 3, numSignals: 5, numOffers: 5);

            filenames = new[] { "R079 twobr" };
            foreach (string filename in filenames)
                ACESimBase.Util.InformationSetCharts.PlotPAndD_WithHidden(path, filename, numRounds: 2, numSignals: 5, numOffers: 5);
            return filenames;
        }
    }
}
