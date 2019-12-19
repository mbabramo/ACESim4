using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using System;
using System.IO;
using CsvHelper;
using System.Linq;

namespace LitigCharts
{
    class Program
    {
        static void Main(string[] args)
        {
            //CopyAzureFiles();
            double[] qualityValues = new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 };
            var trialRates = GetDataForQualities("R094", "orig", qualityValues, 0.30, 0.25, "All", "Trial");
            //InformationSetCharts();
        }

        

        private static void CopyAzureFiles(string prefix)
        {
            string path = @"H:\My Drive\Articles, books in progress\Machine learning model of litigation\DMS nonlinear results 1";
            string settingString(string set, double quality, double costs, double feeShiftingThreshold) => $"{set}q{(int)(quality * 100)}c{(int)(costs * 100)}t{(int)(feeShiftingThreshold * 100)}";
            foreach (string set in new string[] { "orig", "bl", "es", "bl_es"})
            foreach (double quality in new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 })
                foreach (double costs in new double[] { 0, 0.15, 0.30, 0.45, 0.60 })
                    foreach (double feeShifting in new double?[] { 0, 0.25, 0.50, 0.75, 1.0 } )
                    {
                            string filename = prefix + " " + settingString(set, quality, costs, feeShifting) + ".csv";
                            TextFileCreate.CopyFileFromAzure("results", filename, path);
                    }
        }

        private static double[] GetDataForQualities(string prefix, string set, double[] qualityValues, double costs, double feeShifting, string filterOfRowsToGet, string columnToGet) => qualityValues.Select(x => GetDataForSpecificSettings(prefix, set, x, costs, feeShifting, filterOfRowsToGet, columnToGet) ?? 0.0).ToArray();

        private static double? GetDataForSpecificSettings(string prefix, string set, double quality, double costs, double feeShifting, string filterOfRowsToGet, string columnToGet)
        {
            var singleRowResults = GetDataForSpecificSettings(prefix, set, quality, costs, feeShifting, new string[] { filterOfRowsToGet }, new string[] { columnToGet });
            return singleRowResults[0, 0];
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
