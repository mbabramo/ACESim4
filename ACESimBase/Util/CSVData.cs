using CsvHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class CSVData
    {

        public static double?[,] GetCSVData_SinglePass(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, bool cacheFile = false) => GetCSVData_SinglePass(rowsToFind, columnsToGet, GetStreamReader(fullFilename, cacheFile));

        // TODO: Try eliminating some of the non-single pass methods, at least where there are multiple rows to find.

        public static double? GetCSVData(string fullFilename, (string columnName, string expectedText)[] rowToFind, string columnToGet, bool cacheFile=false) => GetCSVData(fullFilename, new (string columnName, string expectedText)[][] { rowToFind }, new string[] { columnToGet }, cacheFile)[0, 0];

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

        public static double?[,] GetCSVData(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, bool cacheFile=false)
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
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
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
                    for (int rowToFind = 0; rowToFind < rowsToFind.Length; rowToFind++)
                    {
                        bool meetsRowRequirements = criteriaIndices[rowToFind].All(x => matchingCriteria[x]);
                        if (meetsRowRequirements)
                        {
                            for (int c = 0; c < columnsToGet.Length; c++)
                            {
                                csv.Configuration.MissingFieldFound = null;
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

        public static double?[,] GetCSVData((string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, StreamReader reader)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];

            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
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
                                csv.Configuration.MissingFieldFound = null;
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
            return csv.GetField<string>(columnName) == expectedText;
        }
        private static bool MeetsRowRequirements(CsvReader csv, (string columnName, string expectedText)[] requirements) => requirements.All(r => MeetsRowRequirement(csv, r.columnName, r.expectedText));

    }
}
