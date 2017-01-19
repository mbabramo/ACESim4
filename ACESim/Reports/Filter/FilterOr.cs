using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class FilterOr : Filter
    {
        public List<Filter> ContainedFilters;

        public FilterOr(List<Filter> containedFilters)
            : base("", "OR")
        {
            ContainedFilters = containedFilters;
        }

        public bool DoFilterOr(GameProgressReportable theOutput, bool trueIfNonNull = false)
        {
            return ContainedFilters.Any(x => x.DoFilter(theOutput, trueIfNonNull));
        }

        //public override string GetFilterName(string prefix)
        //{
        //    prefix = base.GetFilterName(prefix);
        //    return prefix + value.ToString();
        //}
    }
}
