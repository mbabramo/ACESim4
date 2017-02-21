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
                _PointsInInverseNormalDistribution = Enumerable.Range(0, NumInInverseNormalDistribution)
                    .Select(x => x / (NumInInverseNormalDistribution + 1.0))
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
            // Midpoints from uniform distribution: Suppose that there are two points in the uniform distribution --> they will be (0.25, 0.75), because these are the midpoints of (0, 0.5) and (0.5, 1.0). If we have 10 points, we want (0.05, 0.15, ..., 0.95). So the formula for point i (starting with zero) is (i + 0.5) / 10.
            double[] midpoints = 
                Enumerable.Range(0, nsParams.NumPointsInSourceUniformDistribution)
                .Select(x => (double) (x + 0.5) / (double) nsParams.NumPointsInSourceUniformDistribution)
                .ToArray();
            double[] drawsFromNormalDistribution = PointsInInverseNormalDistribution.Select(x => x * nsParams.StdevOfNormalDistribution).ToArray();
            // Now, we make every combination of uniform and normal distribution draws, and add them together
            var crossProduct = midpoints
                .SelectMany(x => drawsFromNormalDistribution, (x, y) => new { uniformDistPoint = x, normDistValue = y });
            var distinctPointsOrdered = crossProduct.Select(x => x.uniformDistPoint + x.normDistValue).OrderBy(x => x).ToArray();
            // Now we want the cutoffs for the signals, making each cutoff equally likely. Note that if we want 2 signals, then we want 1 cutoff. (More generally, n signals -> n - 1 cutoffs). 
            double[] percentilePoints = Enumerable.Range(1, nsParams.NumSignals - 1).Select(x => (double)x / (double)nsParams.NumSignals).ToArray();
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
