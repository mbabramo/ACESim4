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
        public static CsvReader GetCSVReader(string fullFilename, bool cacheFile)
        {
            StreamReader reader = GetStreamReader(fullFilename, cacheFile);
            CsvReader csv = GetCSVReader(reader);
            return csv;
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
            StreamReader reader = new StreamReader(m);
            return reader;
        }

        private static CsvReader GetCSVReader(StreamReader reader)
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture);
            var csv = new CsvReader(reader, config);
            return csv;
        }


        public static double? GetCSVData(CsvReader reader, (string columnName, string expectedText)[] rowToFind, string columnToGet, bool cacheFile = false) => GetCSVData(reader, new (string columnName, string expectedText)[][] { rowToFind }, new string[] { columnToGet })[0, 0];

        public static double?[] GetCSVData(CsvReader reader, (string columnName, string expectedText)[] rowToFind, string[] columnsToGet, bool cacheFile = false)
        {
            var resultMatrix = GetCSVData(reader, new (string columnName, string expectedText)[][] { rowToFind }, columnsToGet);
            int columnsCount = columnsToGet.Count();
            double?[] result = new double?[columnsCount];
            for (int i = 0; i < columnsCount; i++)
                result[i] = resultMatrix[0, i];
            return result;
        }

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
            CsvReader reader = GetCSVReader(fullFilename, cacheFile);
            using (reader)
                return GetCSVData(reader, rowsToFind, columnsToGet);
        }

        public static double?[,] GetCSVData(CsvReader csv, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];
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
            return results;
        }

        private static bool MeetsRowRequirement(CsvReader csv, string columnName, string expectedText)
        {
            return csv.GetField<string>(columnName) == expectedText;
        }
        private static bool MeetsRowRequirements(CsvReader csv, (string columnName, string expectedText)[] requirements) => requirements.All(r => MeetsRowRequirement(csv, r.columnName, r.expectedText));

    }
}
