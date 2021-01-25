using ACESim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts
{
    public class FeeShiftingData
    {
        public static void Execute()
        {
            var launcher = new LitigGameLauncher();
            var gameOptionsSets = launcher.GetFeeShiftingArticleGamesSets(false, true).SelectMany(x => x).ToList();
            var map = launcher.GetFeeShiftingArticleNameMap(); // name to find (avoids redundancies)
            string[] filtersOfRowsToGet = new string[] { "All" };
            string[] columnsToGet = new string[] { "PFiles", "DAnswers" };
            foreach (var gameOptionsSet in gameOptionsSets)
            {
                string filenameWithoutCSV = map[gameOptionsSet.Name];
                string filename = filenameWithoutCSV + ".csv";
                string path = @"C:\Users\Admin\Documents\GitHub\ACESim4\ReportResults";
                string combined = Path.Combine(path, filenameWithoutCSV);
                (string columnName, string expectedText)[][] rowsToFind = new (string columnName, string expectedText)[filtersOfRowsToGet.Length][];
                for (int f = 0; f < filtersOfRowsToGet.Length; f++)
                {
                    rowsToFind[f] = new (string columnName, string expectedText)[2];
                    rowsToFind[f][0] = ("OptionSet", filenameWithoutCSV);
                    rowsToFind[f][1] = ("Filter", filtersOfRowsToGet[f]);
                }
                // string[] columnsToGet = new string[] { "Trial", "AccSq", "POffer", "DOffer" };
                var results = CSVData.GetCSVData(combined, rowsToFind, columnsToGet, true);
            }
        }
    }
}
