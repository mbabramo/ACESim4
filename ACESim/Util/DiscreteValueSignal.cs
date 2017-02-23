using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{

    public struct DiscreteValueSignalParameters
    {
        public int NumPointsInSourceUniformDistribution;
        public double StdevOfNormalDistribution;
        public int NumSignals;
    }

    public static class DiscreteValueSignal
    {
        /// <summary>
        /// Given a signal (in the form of the sum of values taken from a uniform distribution and from a normal distribution), returns a discrete signal, such that each signal will ex ante be equally likely to obtain. The lowest signal is equal to 1. 
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="dsParamas"></param>
        /// <returns></returns>
        public static int GetDiscreteSignal(double signal, DiscreteValueSignalParameters dsParamas)
        {
            double[] cutoffs = GetSignalCutoffs(dsParamas);
            int i = 0;
            while (i < cutoffs.Length && cutoffs[i] < signal)
                i++;
            return i + 1;
        }

        private static Dictionary<DiscreteValueSignalParameters, double[]> CutoffsForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[]>();
        private static object CalcLock = new object();

        // We will calculate a number of discrete points in the inverse normal distribution

        const int NumInInverseNormalDistribution = 1000;
        private static double[] _PointsInInverseNormalDistribution;
        private static double[] PointsInInverseNormalDistribution
        {
            get
            {
                if (_PointsInInverseNormalDistribution == null)
                    CalculateInverseNormalDistributionPoints();
                return _PointsInInverseNormalDistribution;
            }
        }

        private static void CalculateInverseNormalDistributionPoints()
        {
            lock (CalcLock)
            {
                _PointsInInverseNormalDistribution = EquallySpaced.GetEquallySpacedPoints(NumInInverseNormalDistribution)
                    .Select(x => InvNormal.Calculate(x))
                    .ToArray();
            }
        }

        //

        private static double[] GetSignalCutoffs(DiscreteValueSignalParameters nsParams)
        {
            if (!CutoffsForStandardDeviation.ContainsKey(nsParams))
            {
                lock (CalcLock)
                {
                    CalculateCutoffs(nsParams);
                }
            }
            return CutoffsForStandardDeviation[nsParams];
        }

        private static void CalculateCutoffs(DiscreteValueSignalParameters nsParams)
        {
            // Midpoints from uniform distribution: 
            double[] midpoints = 
                EquallySpaced.GetMidpointsOfEquallySpacedRegions(nsParams.NumPointsInSourceUniformDistribution);
            double[] drawsFromNormalDistribution = PointsInInverseNormalDistribution.Select(x => x * nsParams.StdevOfNormalDistribution).ToArray();
            // Now, we make every combination of uniform and normal distribution draws, and add them together
            var crossProduct = midpoints
                .SelectMany(x => drawsFromNormalDistribution, (x, y) => new { uniformDistPoint = x, normDistValue = y });
            var distinctPointsOrdered = crossProduct.Select(x => x.uniformDistPoint + x.normDistValue).OrderBy(x => x).ToArray();
            // Now we want the cutoffs for the signals, making each cutoff equally likely. Note that if we want 2 signals, then we want 1 cutoff at 0.5; if there are 10 signals, we want cutoffs at .1, .2, ..., .9. (More generally, n signals -> n - 1 cutoffs). 
            double[] percentilePoints = EquallySpaced.GetCutoffsBetweenRegions(nsParams.NumSignals);
            double[] cutoffs = percentilePoints.Select(x => Percentile(distinctPointsOrdered, x)).ToArray();
            CutoffsForStandardDeviation[nsParams] = cutoffs;
        }

        public static double Percentile(double[] orderedSequence, double percentile)
        {
            int N = orderedSequence.Length;
            double n = (N - 1) * percentile + 1;
            // Another method: double n = (N + 1) * excelPercentile;
            if (n == 1d) return orderedSequence[0];
            else if (n == N) return orderedSequence[N - 1];
            else
            {
                int k = (int)n;
                double d = n - k;
                return orderedSequence[k - 1] + d * (orderedSequence[k] - orderedSequence[k - 1]);
            }
        }
    }
}
