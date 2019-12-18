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
        public static double? GetCSVData(string fullFilename, (string columnName, string expectedText)[] rowToFind, string columnToGet) => GetCSVData(fullFilename, new (string columnName, string expectedText)[][] { rowToFind }, new string[] { columnToGet })[0, 0];

        public static double?[,] GetCSVData(string fullFilename, (string columnName, string expectedText)[][] rowsToFind, string[] columnsToGet)
        {
            double?[,] results = new double?[rowsToFind.Length, columnsToGet.Length];
            using (var reader = new StreamReader(fullFilename))
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
