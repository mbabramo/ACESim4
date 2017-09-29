using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SimpleReportColumnFilter : SimpleReportColumnItem
    {
        public Func<GameProgress, bool?> Filter;
        public bool ReportAsPercentageOfAll;

        public override double? GetValueToRecord(GameProgress completedGame)
        {
            bool? filterResult = Filter(completedGame);
            if (filterResult == null)
                return null;
            return ((bool) filterResult) ? 1.0 : 0.0;
        }

        public SimpleReportColumnFilter(string name, Func<GameProgress, bool?> filter, bool reportAsPercentageOfAll, int? width = null) : base(name, width)
        {
            Filter = filter;
            ReportAsPercentageOfAll = reportAsPercentageOfAll;
        }
    }
}
