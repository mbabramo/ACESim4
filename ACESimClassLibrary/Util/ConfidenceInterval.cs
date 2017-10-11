using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class ConfidenceInterval
    {
        public static double GetBoundWithLogitIfNeeded(bool lowerBound, bool useLogitIfAllInUnitInterval, bool cutoffIfAllInUnitInterval, List<double> values)
        {
            if (!values.Any())
                return lowerBound ? 0 : 1.0;
            if (values.All(y => y == 0))
                return 0;
            else if (values.All(y => y == 1.0))
                return 1.0;
            bool allInUnitInterval = values.All(y => y >= 0 && y <= 1);
            bool doLogitTransformation = useLogitIfAllInUnitInterval && allInUnitInterval;
            StatCollector statCollector = new StatCollector();
            List<double> transformedList = new List<double>();
            foreach (double d in values)
            {
                double dTransformed = d;
                if (doLogitTransformation)
                {
                    if (dTransformed == 0)
                        dTransformed = 1E-10;
                    else if (dTransformed == 1)
                        dTransformed = 1.0 - 1E-10;
                    dTransformed = 0 - Math.Log(1.0 / dTransformed - 1.0);
                }
                statCollector.Add(dTransformed);
                transformedList.Add(dTransformed);
            }
            double confInterval = statCollector.ConfInterval();
            double bound = lowerBound ? statCollector.Average() - confInterval : statCollector.Average() + confInterval;
            if (doLogitTransformation)
                bound = Math.Exp(bound) / (Math.Exp(bound) + 1.0);
            if (cutoffIfAllInUnitInterval)
            {
                if (bound < 0)
                    bound = 0;
                if (bound > 1.0)
                    bound = 1.0;
            }
            return bound;
        }
    }
}
