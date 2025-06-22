using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util;
using ACESimBase.Util.ArrayManipulation;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts
{
    public static class DataProcessingUtils
    {
        public static List<List<string>> GetCSVLines(List<GameOptions> gameOptionsSets, List<string> rowsToGet, List<string> replacementRowNames, string filePrefix, string fileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames)
        {

            // Set the following on opening the first file
            List<List<string>> outputLines = null;

            foreach (var gameOptionsSet in gameOptionsSets) // for this aggregation, we want only one copy of each report, so we exclude the redundant names that include the baseline values for a noncritical set
            {
                bool keepGoingOnException = false;
                try
                {
                    GetCSVLinesForGameOptionsSet(rowsToGet, replacementRowNames, filePrefix, ref fileSuffix, path, includeHeader, columnsToGet, replacementColumnNames, ref outputLines, gameOptionsSet);
                }
                catch
                {
                    if (keepGoingOnException == false)
                        throw;
                }
            }
            return outputLines;
        }

        private static void GetCSVLinesForGameOptionsSet(List<string> rowsToGet, List<string> replacementRowNames, string filePrefix, ref string fileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames, ref List<List<string>> outputLines, GameOptions gameOptionsSet)
        {
            if (outputLines == null)
            {
                outputLines = new List<List<string>>();

                if (includeHeader)
                {
                    List<string> headings = gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Key).ToList();
                    headings.Add("Filter");
                    headings.Add("GroupName");
                    headings.Add("OptionSetName");
                    headings.AddRange(replacementColumnNames);
                    outputLines.Add(headings);
                }
            }
            double?[,] resultsAllRows = null;
            string filenameCore, combinedPath;
            bool exists;
            GetFileInfo(filePrefix, ".csv", ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath, out exists);
            if (!exists)
                return;
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
                bodyRow.AddRange(gameOptionsSet.VariableSettings.OrderBy(x => x.Key.ToString()).Select(x => x.Value?.ToString().Replace(",", "-")));
                bodyRow.Add(replacementRowNames[f]?.Replace(",", "-"));
                bodyRow.Add(gameOptionsSet.GroupName?.Replace(",", "-"));
                bodyRow.Add(filenameCore?.Replace(",", "-")); // option set name 
                bodyRow.AddRange(resultsAllRows.GetRow(f).Select(x => x?.ToString()));
                outputLines.Add(bodyRow);
            }
        }

        public static void GetFileInfo(string filePrefix, string fileExtensionIncludingPeriod, ref string fileSuffix, string path, GameOptions gameOptionsSet, out string filenameCore, out string combinedPath, out bool exists)
        {
            exists = true;
            filenameCore = gameOptionsSet.Name;
            combinedPath = Launcher.ReportFullPath(filePrefix, filenameCore, fileSuffix + fileExtensionIncludingPeriod);
            if (!File.Exists(combinedPath))
            {
                exists = false;
            }
        }

        public static string MakeString(List<List<string>> values)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < values.Count(); i++)
                b.AppendLine(String.Join(",", values[i]));
            return b.ToString();
        }
    }
}
