﻿using ACESim;
using ACESimBase.Util;
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
        public static List<List<string>> GetCSVLines(List<GameOptions> gameOptionsSets, Dictionary<string, string> map, List<string> rowsToGet, List<string> replacementRowNames, string filePrefix, string fileSuffix, string altFileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames)
        {

            // Set the following on opening the first file
            List<List<string>> outputLines = null;

            var matchingSets = gameOptionsSets.Where(x => map[x.Name] == x.Name).ToList();
            foreach (var gameOptionsSet in matchingSets) // for this aggregation, we want only one copy of each report, so we exclude the redundant names that include the baseline values for a noncritical set
            {
                bool keepGoingOnException = false;
                try
                {
                    GetCSVLinesForGameOptionsSet(map, rowsToGet, replacementRowNames, filePrefix, ref fileSuffix, altFileSuffix, path, includeHeader, columnsToGet, replacementColumnNames, ref outputLines, gameOptionsSet);
                }
                catch
                {
                    if (keepGoingOnException == false)
                        throw;
                }
            }
            return outputLines;
        }

        private static void GetCSVLinesForGameOptionsSet(Dictionary<string, string> map, List<string> rowsToGet, List<string> replacementRowNames, string filePrefix, ref string fileSuffix, string altFileSuffix, string path, bool includeHeader, List<string> columnsToGet, List<string> replacementColumnNames, ref List<List<string>> outputLines, GameOptions gameOptionsSet)
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
            GetFileInfo(map, filePrefix, ".csv", altFileSuffix, ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath);
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

        public static void GetFileInfo(Dictionary<string, string> map, string filePrefix, string fileExtensionIncludingPeriod, string altFileSuffix, ref string fileSuffix, string path, GameOptions gameOptionsSet, out string filenameCore, out string combinedPath)
        {
            filenameCore = map[gameOptionsSet.Name];
            string filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
            combinedPath = Path.Combine(path, filename);
            if (!File.Exists(combinedPath))
            {
                fileSuffix = altFileSuffix;
                filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
                combinedPath = Path.Combine(path, filename);
                if (!File.Exists(combinedPath))
                {
                    fileSuffix = "";
                    filename = filePrefix + filenameCore + fileSuffix + fileExtensionIncludingPeriod;
                    combinedPath = Path.Combine(path, filename);
                }
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
