using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Statistical
{
    public static class InverseCumulativeNormalDistributionPoints
    {
        private const int MaxDictionarySize = 10;
        public static Dictionary<Tuple<int, double>, double[]> NPointsMultipliedByStandardDeviation = new Dictionary<Tuple<int, double>, double[]>();
        public static object lockObj = new object();

        public static double[] GetDistributionPoints(int numberPoints, double standardDeviationToMultiplyBy)
        {
            var key = new Tuple<int, double>(numberPoints, standardDeviationToMultiplyBy);
            if (NPointsMultipliedByStandardDeviation.ContainsKey(key))
                return NPointsMultipliedByStandardDeviation[key];
            double[] returnVal = CalculateDistributionPoints(numberPoints, standardDeviationToMultiplyBy);
            if (NPointsMultipliedByStandardDeviation.Count < MaxDictionarySize)
                lock (lockObj)
                {
                    if (NPointsMultipliedByStandardDeviation.Count < MaxDictionarySize && !NPointsMultipliedByStandardDeviation.ContainsKey(key)) // check both conditions again, in case something happened on another thread
                        NPointsMultipliedByStandardDeviation[key] = returnVal;
                }
            return returnVal;
        }

        public static double[] CalculateDistributionPoints(int numberPoints, double standardDeviationToMultiplyBy)
        {
            double[] points = new double[numberPoints];
            double stepSize = 1.0 / (numberPoints + 1);
            for (int i = 0; i < numberPoints; i++)
                points[i] = InvNormal.Calculate((i + 1) * stepSize) * standardDeviationToMultiplyBy;
            return points;
        }
    }
}
