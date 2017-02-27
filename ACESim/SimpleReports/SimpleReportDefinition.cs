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

        public int TotalCells => MetaFilters.Count() * RowFilters.Count() * ColumnItems.Count();
    }
}
