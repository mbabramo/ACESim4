using ACESim;
using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public static class DiscreteValueSignal
    {
        /// <summary>
        /// Given a draw of a band from a uniform distribution, returns the probabilities that a signal consisting of the sum of the midpoint of this band and a value drawn from a truncated normal distribution would end up in each discrete band of signals, where each signal band is of equal size.
        /// </summary>
        /// <param name="sourceValue">The discrete band of the uniform distribution (numbered 1 .. dsParams.NumPointsInSourceUniformDistribution inclusive)</param>
        /// <param name="dsParams">The parameters specifying the noise and the number of signals</param>
        /// <returns></returns>
        public static double[] GetProbabilitiesOfDiscreteSignals(int sourceValue, DiscreteValueSignalParameters dsParams)
        {
            if (!Remembered.ContainsKey(dsParams))
            {
                lock (Remembered)
                {
                    double[][] discreteSignalsForSource = new double[dsParams.NumPointsInSourceUniformDistribution][];
                    for (int i = 1; i <= dsParams.NumPointsInSourceUniformDistribution; i++)
                        discreteSignalsForSource[i - 1] = CalculateProbabilitiesOfDiscreteSignals(i, dsParams);
                    Remembered[dsParams] = discreteSignalsForSource;
                }
            }
            return Remembered[dsParams][sourceValue - 1];
        }

        static Dictionary<DiscreteValueSignalParameters, double[][]> Remembered = new Dictionary<DiscreteValueSignalParameters, double[][]>();

        private static double[] CalculateProbabilitiesOfDiscreteSignals(int sourceValue, DiscreteValueSignalParameters dsParams)
        {
            double[] density = Enumerable.Range(1, dsParams.NumSignals).Select(signal => GetDensity(dsParams.MapSourceTo0To1(sourceValue), dsParams.MapSignalToRangeIn0To1(signal), dsParams.StdevOfNormalDistribution)).ToArray();
            double densitySum = density.Sum();
            double[] relativeDensity = density.Select(d => d / densitySum).ToArray();
            return relativeDensity;
        }

        private static double GetDensity(double trueValue, (double bottomOfRange, double topOfRange) range, double stdev)
        {
            double lowStdev = (range.bottomOfRange - trueValue) / stdev;
            double highStdev = (range.topOfRange - trueValue) / stdev;
            double result = NormalDistributionCalculation.PortionOfNormalDistributionBetween(lowStdev, highStdev);
            return result;
        }
    }
}
