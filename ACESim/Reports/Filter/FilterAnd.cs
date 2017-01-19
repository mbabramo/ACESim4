using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// This filter returns true only if all contained filters contain true. This is normally not necessary because
    /// this is the default behavior when there are multiple filters for a table cell. However, it may be useful
    /// as a subset of FilterOr.
    /// </summary>
    /// 
    [Serializable]
    public class FilterAnd : Filter
    {
        public List<Filter> ContainedFilters;

        public FilterAnd(List<Filter> containedFilters)
            : base("", "AND")
        {
            ContainedFilters = containedFilters;
        }

        public bool DoFilterAnd(GameProgressReportable theOutput, bool trueIfNonNull = false)
        {
            return ContainedFilters.All(x => x.DoFilter(theOutput, trueIfNonNull));
        }

        //public override string GetFilterName(string prefix)
        //{
        //    prefix = base.GetFilterName(prefix);
        //    return prefix + value.ToString();
        //}
    }
}
