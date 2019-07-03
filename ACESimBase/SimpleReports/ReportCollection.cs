using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    public class ReportCollection
    {
        public string standardReport;
        public string csvReport;

        public ReportCollection()
        {
            standardReport = "";
            csvReport = "";
        }

        public ReportCollection(string standard, string csv)
        {
            standardReport = standard;
            csvReport = csv;
        }

        public void Add(string standard, string csv)
        {
            standardReport += standard;
            csvReport += csv;
        }

        public void Add(ReportCollection other)
        {
            Add(other.standardReport, other.csvReport);
        }
    }
}
