using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ACESim
{
    public class ReportCollection
    {
        public string standardReport;
        public List<string> csvReports;
        public bool MultipleCSV => csvReports.Count() > 1;
        public List<string> IDColumnNames = new List<string>() { "OptionSet", "Filter", "Repetition", "Simulation" };
        public List<string> ReportSuffixes = new List<string>();

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

        public void AddReportSuffix(string name)
        {
            ReportSuffixes.Add(name);
        }

        public void Add(string standard, string csv, bool integrateCSVReportsIfPossible = true, bool ifNotIntegratingAlwaysMakeSeparateReport = false)
        {
            standardReport += standard;
            if (integrateCSVReportsIfPossible)
            {
                string thisReport = csvReports.SingleOrDefault();
                IntegrateCSVReports(thisReport, csv);
            }
            else
                AddCSVIntoExistingMatchOrAsSeparateReport(csv, ifNotIntegratingAlwaysMakeSeparateReport);
        }

        public void Add(ReportCollection other, bool integrateCSVReportsIfPossible = true, bool ifNotIntegratingAlwaysMakeSeparateReport = false)
        {
            standardReport += other.standardReport;
            if (integrateCSVReportsIfPossible && !MultipleCSV && !other.MultipleCSV)
            {
                string thisReport = csvReports.SingleOrDefault();
                string otherReport = other.csvReports.SingleOrDefault();
                IntegrateCSVReports(thisReport, otherReport);
            }
            else
            {
                foreach (string csv in other.csvReports)
                    AddCSVIntoExistingMatchOrAsSeparateReport(csv, ifNotIntegratingAlwaysMakeSeparateReport);
                foreach (string reportSuffix in other.ReportSuffixes)
                    AddReportSuffix(reportSuffix);
            }
        }

        private void IntegrateCSVReports(string thisReport, string otherReport)
        {
            if (thisReport == null || otherReport == null)
            {
                if (thisReport != null || otherReport != null)
                    csvReports = new List<string>() { (thisReport ?? otherReport) };
            }
            else
            {
                csvReports = new List<string>() { DynamicUtilities.MergeCSV(thisReport, otherReport, IDColumnNames) };
            }
        }

        public void SaveLatestLocally()
        {
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            TextFileManage.CreateTextFile(Path.Combine(folder.FullName, "standardreport"), standardReport);
            if (MultipleCSV)
            {
                int i = 0;
                foreach (string csv in csvReports)
                    TextFileManage.CreateTextFile(Path.Combine(folder.FullName, "csvreport-" + i++.ToString() + ".csv"), csv);
            }
            else
            {
                if (csvReports.Any())
                    TextFileManage.CreateTextFile(Path.Combine(folder.FullName, "csvreport.csv"), csvReports.Single());
            }
        }

        private void AddCSVIntoExistingMatchOrAsSeparateReport(string csv, bool alwaysSeparateReport)
        {
            if (alwaysSeparateReport)
            {
                csvReports.Add(csv);
                return;
            }
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
