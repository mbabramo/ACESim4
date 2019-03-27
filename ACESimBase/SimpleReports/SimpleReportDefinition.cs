using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SimpleReportDefinition
    {
        public string Name;
        public List<SimpleReportFilter> MetaFilters;
        public List<SimpleReportFilter> RowFilters;
        public List<SimpleReportColumnItem> ColumnItems;
        public bool DivideColumnFiltersByImmediatelyEarlierReport;
        public Func<Decision, GameProgress, byte> ActionsOverride;

        public SimpleReportDefinition(string name, List<SimpleReportFilter> metaFilters, List<SimpleReportFilter> rowFilters, List<SimpleReportColumnItem> columnItems, bool divideColumnFiltersByImmediatelyEarlierReport = false)
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
            DivideColumnFiltersByImmediatelyEarlierReport = divideColumnFiltersByImmediatelyEarlierReport;
        }

        public int TotalCells => MetaFilters.Count() * RowFilters.Count() * ColumnItems.Count();
    }
}
