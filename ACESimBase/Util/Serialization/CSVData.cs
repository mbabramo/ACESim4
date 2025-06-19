using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Serialization
{
    public static class CSVData
    {

        public static double?[,] GetCSVData_SinglePass(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, bool cacheFile = false) => GetCSVData_SinglePass(rowsToFind, columnsToGet, GetStreamReader(fullFilename, cacheFile));

        // TODO: Try eliminating some of the non-single pass methods, at least where there are multiple rows to find.

        public static double? GetCSVData(string fullFilename, (string columnName, string expectedText)[] rowToFind, string columnToGet, bool cacheFile = false) => GetCSVData(fullFilename, new (string columnName, string expectedText)[][] { rowToFind }, new string[] { columnToGet }, cacheFile)[0, 0];

        public static double?[] GetCSVData(string fullFilename, (string columnName, string expectedText)[] rowToFind, string[] columnsToGet, bool cacheFile = false)
        {
            var results = GetCSVData(fullFilename, new (string columnName, string expectedText)[][] { rowToFind }, columnsToGet, cacheFile);
            int length = results.GetLength(1);
            double?[] result = new double?[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = results[0, i];
            }
            return result;
        }

        private static Dictionary<string, byte[]> CachedFilesDictionary = new Dictionary<string, byte[]>();

        public static double?[,] GetCSVData(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, bool cacheFile = false)
        {
            StreamReader reader = GetStreamReader(fullFilename, cacheFile);
            using (reader)
                return GetCSVData(rowsToFind, columnsToGet, reader);
        }

        private static StreamReader GetStreamReader(string fullFilename, bool cacheFile)
        {
            byte[] bytes = null;
            if (cacheFile)
            {
                if (CachedFilesDictionary.ContainsKey(fullFilename))
                {
                    bytes = CachedFilesDictionary[fullFilename];
                }
                else
                {
                    bytes = File.ReadAllBytes(fullFilename);
                    CachedFilesDictionary[fullFilename] = bytes;
                }
            }
            else
                bytes = File.ReadAllBytes(fullFilename);
            MemoryStream m = new MemoryStream(bytes);
            var reader = new StreamReader(m);
            return reader;
        }

        public static double?[,] GetCSVData_SinglePass((string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, StreamReader reader)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];

            List<(string columnName, string expectedText)> criteria = rowsToFind.SelectMany(x => x).Distinct().OrderBy(x => x.columnName).ToList();
            var indexedCriteria = criteria.Select((item, index) => (item, index)).ToList();
            int numCriteria = criteria.Count();
            int[][] criteriaIndices = rowsToFind.Select(rowToFind => rowToFind.Select(criterionInRowToFind => indexedCriteria.First(criterion => criterionInRowToFind == criterion.item).index).ToArray()).ToArray();
            int numRowsToFind = criteriaIndices.Length;
            bool[] matchingCriteria = new bool[numCriteria];
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture) { MissingFieldFound = null };
            using (var csv = new CsvReader(reader, config))
            {
                csv.Read();
                csv.ReadHeader();
                int rowInCSV = -1;
                while (csv.Read())
                {
                    rowInCSV++;
                    foreach (var indexedCriterion in indexedCriteria)
                        matchingCriteria[indexedCriterion.index] = MeetsRowRequirement(csv, indexedCriterion.item.columnName, indexedCriterion.item.expectedText);
                    int bestRow = -1;
                    for (int rowToFind = 0; rowToFind < rowsToFind.Length; rowToFind++)
                    {
                        bool meetsRowRequirements = criteriaIndices[rowToFind].All(x => matchingCriteria[x]);
                        if (meetsRowRequirements)
                        {
                            bestRow = rowToFind;
                            for (int c = 0; c < columnsToGet.Length; c++)
                            {
                                string contents = csv.GetField<string>(columnsToGet[c]);
                                if (contents == null || contents == "")
                                    results[rowToFind, c] = null;
                                else
                                    results[rowToFind, c] = csv.GetField<double>(columnsToGet[c]);
                            }
                        }
                    }
                }
            }
            return results;
        }

        public static double?[,] GetCSVData_MultiPassValidated(
            string fullFilename,
            (string columnName, string expectedText)[][] rowsToFind,
            string[] columnsToGet,
            bool cacheFile = false)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];

            for (int rowIndex = 0; rowIndex < rowsToFind.Length; rowIndex++)
            {
                bool perfectRowLocated = false;
                int greatestCriteriaMatchCount = -1;
                Dictionary<string, string> closestRowCellValues = null;

                using (var reader = GetStreamReader(fullFilename, cacheFile))
                {
                    var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        MissingFieldFound = null
                    };

                    using (var csv = new CsvReader(reader, csvConfiguration))
                    {
                        csv.Read();
                        csv.ReadHeader();

                        while (csv.Read())
                        {
                            int matchedCriteriaCount = rowsToFind[rowIndex]
                                .Count(r => MeetsRowRequirement(csv, r.columnName, r.expectedText));

                            if (matchedCriteriaCount > greatestCriteriaMatchCount)
                            {
                                greatestCriteriaMatchCount = matchedCriteriaCount;
                                closestRowCellValues = rowsToFind[rowIndex]
                                    .ToDictionary(r => r.columnName, r => csv.GetField<string>(r.columnName));
                            }

                            if (matchedCriteriaCount == rowsToFind[rowIndex].Length)
                            {
                                for (int c = 0; c < columnsToGet.Length; c++)
                                {
                                    string cellText = csv.GetField<string>(columnsToGet[c]);

                                    results[rowIndex, c] = string.IsNullOrWhiteSpace(cellText)
                                        ? null
                                        : csv.GetField<double>(columnsToGet[c]);
                                }

                                perfectRowLocated = true;
                                break;
                            }
                        }
                    }
                }

                if (!perfectRowLocated)
                {
                    var exceptionMessageBuilder = new StringBuilder();
                    exceptionMessageBuilder.AppendLine("Unable to locate a CSV row matching every specified criterion.");
                    exceptionMessageBuilder.AppendLine("Criteria:");
                    foreach (var (columnName, expectedText) in rowsToFind[rowIndex])
                        exceptionMessageBuilder.AppendLine($"    {columnName}: \"{expectedText}\"");

                    if (closestRowCellValues != null)
                    {
                        exceptionMessageBuilder.AppendLine("Closest row:");
                        foreach (var (columnName, expectedText) in rowsToFind[rowIndex])
                        {
                            string actual = closestRowCellValues.TryGetValue(columnName, out var value) ? value : "[missing]";
                            string indicator = string.Equals(actual, expectedText, StringComparison.OrdinalIgnoreCase) ? "✓" : "✗";
                            exceptionMessageBuilder.AppendLine($"    {columnName}: \"{actual}\" {indicator}");
                        }
                    }

                    throw new Exception(exceptionMessageBuilder.ToString());
                }
            }

            return results;
        }


        public static double?[,] GetCSVData((string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, StreamReader reader)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];

            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture) { MissingFieldFound = null };
            using (var csv = new CsvReader(reader, config))
            {
                csv.Read();
                csv.ReadHeader();
                int rowInCSV = -1;
                while (csv.Read())
                {
                    rowInCSV++;
                    for (int rowToFind = 0; rowToFind < rowsToFind.Length; rowToFind++)
                    {
                        if (MeetsRowRequirements(csv, rowsToFind[rowToFind]))
                        {
                            for (int c = 0; c < columnsToGet.Length; c++)
                            {
                                string contents = csv.GetField<string>(columnsToGet[c]);
                                if (contents == null || contents == "")
                                    results[rowToFind, c] = null;
                                else
                                    results[rowToFind, c] = csv.GetField<double>(columnsToGet[c]);
                            }
                        }
                    }
                }
            }
            return results;
        }

        private static bool MeetsRowRequirement(CsvReader csv, string columnName, string expectedText)
        {
            return csv.GetField<string>(columnName).ToUpper() == expectedText.ToUpper();
        }
        private static bool MeetsRowRequirements(CsvReader csv, (string columnName, string expectedText)[] requirements) => requirements.All(r => MeetsRowRequirement(csv, r.columnName, r.expectedText));

    }
}
