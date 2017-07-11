using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SimpleReportFilter
    {
        public string Name;
        public Func<GameProgress, bool> IsInFilter;

        public SimpleReportFilter(string name, Func<GameProgress, bool> isInFilter)
        {
            Name = name;
            IsInFilter = isInFilter;
        }
    }
}
