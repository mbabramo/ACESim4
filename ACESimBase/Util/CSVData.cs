﻿using CsvHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class CSVData
    {
        public static double? GetCSVData(string fullFilename, (string columnName, string expectedText)[] rowToFind, string columnToGet, bool cacheFile=false) => GetCSVData(fullFilename, new (string columnName, string expectedText)[][] { rowToFind }, new string[] { columnToGet }, cacheFile)[0, 0];

        private static Dictionary<string, byte[]> CachedFilesDictionary = new Dictionary<string, byte[]>();

        public static double?[,] GetCSVData(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, bool cacheFile=false)
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
            using (var reader = new StreamReader(m))
                return GetCSVData(rowsToFind, columnsToGet, reader);
        }

        public static double?[,] GetCSVData((string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet, StreamReader reader)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];
            using (var csv = new CsvReader(reader))
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
            return csv.GetField<string>(columnName) == expectedText;
        }
        private static bool MeetsRowRequirements(CsvReader csv, (string columnName, string expectedText)[] requirements) => requirements.All(r => MeetsRowRequirement(csv, r.columnName, r.expectedText));

    }
}
