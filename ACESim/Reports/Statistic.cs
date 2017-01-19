using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public enum Statistic
    {
        none, 
        count, 
        percentOfAllCases, // the number of cases in which all filters are true divided by the number of cases in which all filters are not null. We are thus figuring out the percentage of all cases (other than those where the filters are null) where the criteria opposite is met and, optionally, the criteria here are met.
        percentOfCasesFilteredOpposite, // the number of cases in which all filters are true divided by the number of cases in which the opposite filters are true and these filters are not null. We are thus figuring out the percentage of the cases in which the criteria opposite is met that the criteria here are met.
        mean, 
        median, 
        sum, 
        stdev
    }
}
