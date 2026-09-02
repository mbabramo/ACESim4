using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.DiscreteProbabilities
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

        /// <summary>
        /// Returns signal-bin probabilities for an arbitrary continuous source location in [0, 1].
        /// This is the continuous-location counterpart to the discrete-source overload and uses
        /// the same truncated-normal signal model and boundary convention.
        /// </summary>
        public static double[] GetProbabilitiesOfDiscreteSignals(double sourceLocation, DiscreteValueSignalParameters dsParams)
        {
            if (sourceLocation < 0.0 || sourceLocation > 1.0 || double.IsNaN(sourceLocation))
                throw new ArgumentOutOfRangeException(nameof(sourceLocation), "Source location must be in [0, 1].");
            if (dsParams.NumSignals <= 0)
                throw new ArgumentOutOfRangeException(nameof(dsParams.NumSignals));

            if (dsParams.StdevOfNormalDistribution == 0.0)
            {
                int signalIndex = DiscreteSignalBoundaries.MapLocationIn0To1ToZeroBasedSignalIndex(
                    sourceLocation,
                    dsParams.NumSignals,
                    dsParams.SignalBoundaryMode);
                double[] deterministic = new double[dsParams.NumSignals];
                deterministic[signalIndex] = 1.0;
                return deterministic;
            }

            if (dsParams.StdevOfNormalDistribution < 0.0 || double.IsNaN(dsParams.StdevOfNormalDistribution))
                throw new ArgumentOutOfRangeException(nameof(dsParams.StdevOfNormalDistribution));

            double[] density = Enumerable.Range(1, dsParams.NumSignals)
                .Select(signal => GetDensity(
                    sourceLocation,
                    dsParams.MapSignalToRangeIn0To1(signal),
                    dsParams.StdevOfNormalDistribution))
                .ToArray();
            double densitySum = density.Sum();
            if (!(densitySum > 0.0) || double.IsNaN(densitySum) || double.IsInfinity(densitySum))
                throw new InvalidOperationException("Continuous signal probabilities could not be normalized.");
            return density.Select(d => d / densitySum).ToArray();
        }

        static Dictionary<DiscreteValueSignalParameters, double[][]> Remembered = new Dictionary<DiscreteValueSignalParameters, double[][]>();

        private static double[] CalculateProbabilitiesOfDiscreteSignals(int sourceValue, DiscreteValueSignalParameters dsParams)
        {
            double[] density = Enumerable.Range(1, dsParams.NumSignals).Select(signal => GetDensity(dsParams.MapSourceTo0To1(sourceValue), dsParams.MapSignalToRangeIn0To1(signal), Math.Max(dsParams.StdevOfNormalDistribution, 1E-50))).ToArray();
            double densitySum = density.Sum();
            double[] relativeDensity = density.Select(d => d / densitySum).ToArray();
            return relativeDensity;
        }

        private static double GetDensity(double trueValue, (double bottomOfRange, double topOfRange) range, double stdev)
        {
            double lowStdev = (range.bottomOfRange - trueValue) / stdev;
            double highStdev = (range.topOfRange - trueValue) / stdev;
            double result = NormalDistributionCalculation.PortionOfNormalDistributionBetween(lowStdev, highStdev);
            //if (result < 1E-50)
            //    result = 1E-50; // make sure it's not quite zero
            return result;
        }
    }
}
