using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ACESim
{
    public class ReportCollection
    {
        public string standardReport;
        public List<string> csvReports;

        public ReportCollection()
        {
            standardReport = "";
            csvReports = new List<string>();
        }

        public ReportCollection(string standard, string csv)
        {
            standardReport = standard;
            csvReports = new List<string>() { csv };
        }

        public ReportCollection(string standard, List<string> csvs)
        {
            standardReport = standard;
            csvReports = csvs;
        }

        public void Add(string standard, string csv)
        {
            standardReport += standard;
            AddCSV(csv);
        }

        public void Add(ReportCollection other)
        {
            standardReport += other.standardReport;
            foreach (string csv in other.csvReports)
                AddCSV(csv);
            SaveLatest();
        }

        public void SaveLatest()
        {
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            TextFileCreate.CreateTextFile(Path.Combine(folder.FullName, "standardreport"), standardReport);
            int i = 0;
            foreach (string csv in csvReports)
                TextFileCreate.CreateTextFile(Path.Combine(folder.FullName, "csvreport-" + i++.ToString() + ".csv"), csv);
        }

        private void AddCSV(string csv)
        {
            if (csv == null || csv == "")
                return;
            string firstLineNew = FirstLine(csv);
            int? match = null;
            for (int i = 0; i < csvReports.Count; i++)
            {
                string existingReport = (string)csvReports[i];
                string firstLineExisting = FirstLine(existingReport);
                if (firstLineNew == firstLineExisting)
                {
                    match = i;
                    break;
                }
            }
            if (match != null)
            {
                csvReports[(int)match] += csv.Substring(firstLineNew.Length);
                csvReports[(int)match] = csvReports[(int)match].Replace("\r\n\r\n", "\r\n");
            }
            else
                csvReports.Add(csv);
        }

        private static string FirstLine(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return str;
            var newLinePos = str.IndexOf(Environment.NewLine, StringComparison.CurrentCulture);
            return newLinePos > 0 ? str.Substring(0, newLinePos) : str;
        }
    }
}
