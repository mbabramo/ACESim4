using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class SimpleReportDefinition
    {
        public string Name;
        public List<SimpleReportFilter> MetaFilters;
        public List<SimpleReportFilter> RowFilters;
        public List<SimpleReportColumnItem> ColumnItems;

        public SimpleReportDefinition(string name, List<SimpleReportFilter> metaFilters, List<SimpleReportFilter> rowFilters, List<SimpleReportColumnItem> columnItems)
        {
            Name = name;
            if (metaFilters == null)
                MetaFilters = new List<SimpleReportFilter>() { new SimpleReportFilter("All", (GameProgress gp) => true) };
            else
                MetaFilters = metaFilters;
            if (rowFilters == null)
                RowFilters = new List<SimpleReportFilter>() { new SimpleReportFilter("All", (GameProgress gp) => true) };
            else
                RowFilters = rowFilters;
            ColumnItems = columnItems;
            if (!((ColumnItems.First() as SimpleReportColumnFilter)?.ReportAsPercentageOfAll == false))
                throw new Exception("First column item must be a filter that counts everything in the row.");
        }

        public int TotalCells => MetaFilters.Count() * RowFilters.Count() * ColumnItems.Count();
    }
}
