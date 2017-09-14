using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{

    public static class DiscreteValueSignal
    {
        /// <summary>
        /// Calculates a signal by adding noise drawn from a normal distribution to a true value. If the sum is less than 0, the signal returned is 1. If it is greater than 1, the signal returned is numSignalValues. Otherwise, the signal is in between.
        /// </summary>
        /// <param name="trueValueFrom0To1"></param>
        /// <param name="oneBasedNoiseValue"></param>
        /// <param name="numNoiseValues"></param>
        /// <param name="standardDeviationOfNoise"></param>
        /// <param name="numSignalValues"></param>
        /// <returns></returns>
        public static byte ConvertNoiseToSignal(double trueValueFrom0To1, byte oneBasedNoiseValue, byte numNoiseValues, double standardDeviationOfNoise, byte numSignalValues)
        {
            double noiseUniformDistribution = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (oneBasedNoiseValue - 1), numNoiseValues, false);
            double noiseNormalDistributionDraw = InvNormal.Calculate(noiseUniformDistribution) * standardDeviationOfNoise;
            double obfuscatedValue = trueValueFrom0To1 + noiseNormalDistributionDraw;
            if (obfuscatedValue < 0)
                return 1;
            else if (obfuscatedValue > 1)
                return numSignalValues;
            else
                return (byte) (2 + (int) Math.Floor(obfuscatedValue * (numSignalValues - 2)));
        }

        /// <summary>
        /// Given a signal (in the form of the sum of values taken from a uniform distribution and from a normal distribution), returns a discrete signal, such that each signal will ex ante be equally likely to obtain. The lowest signal is equal to 1. 
        /// For example, if NumSignals is 10, then one-tenth of the time, we will draw from the two distributions in a way that will produce a signal of i, for all i from 1 to 10. 
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

        /// <summary>
        /// Given a draw of a band from a uniform distribution, returns the probability that a signal consisting of the sum of the midpoint of this band and a value drawn from a normal distribution would end up in the specified discrete band of signals, where each signal band is of equal size.
        /// </summary>
        /// <param name="actualUniformDistributionValue">The discrete band of the uniform distribution (numbered 1 .. number of bands)</param>
        /// <param name="discreteSignal">The discrete band of the signal (numbered 1 .. number of signals)</param>
        /// <param name="dsParams">The parameters specifying the noise and the number of signals and uniform distribution points</param>
        /// <returns></returns>
        public static double GetProbabilityOfDiscreteSignal(int actualUniformDistributionValue, int discreteSignal, DiscreteValueSignalParameters dsParams)
        {
            return GetProbabilitiesOfDiscreteSignals(actualUniformDistributionValue, dsParams)[discreteSignal - 1];
        }


        public static double[] GetProbabilitiesOfDiscreteSignals(int actualUniformDistributionValue, DiscreteValueSignalParameters dsParams)
        {
            double[][] probabilities = GetProbabilitiesOfSignalGivenSourceLitigationQuality(dsParams);
            return probabilities[actualUniformDistributionValue - 1];
        }

        private static Dictionary<DiscreteValueSignalParameters, double[]> CutoffsForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[]>();
        private static Dictionary<DiscreteValueSignalParameters, double[][]> ProbabilitiesOfSignalGivenSourceLitigationQualityForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[][]>();
        private static object CalcLock = new object();

        // We will calculate a number of discrete points in the inverse normal distribution

        const int NumInInverseNormalDistribution = 10000;
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

        // We are going to store last values returned for a particular parameters to avoid having to go to the dictionary unnecessarily.
        private static double[] CachedCutoffsValue;
        private static double[][] CachedProbabilitiesValue;
        private static DiscreteValueSignalParameters CachedParamsValue;

        private static double[] GetSignalCutoffs(DiscreteValueSignalParameters nsParams)
        {
            double[] returnVal;
            if (CachedParamsValue.Equals(nsParams))
            {
                returnVal = CachedCutoffsValue;
                if (CachedParamsValue.Equals(nsParams)) // make sure it hasn't changed!
                {
                    return CachedCutoffsValue;
                }
            }
            CalculateCutoffsIfNecessary(nsParams);
            returnVal = CutoffsForStandardDeviation[nsParams];
            CachedParamsValue = nsParams; // must change this first so that we can be sure to detect the change
            CachedCutoffsValue = returnVal;
            return returnVal;
        }

        private static void CalculateCutoffsIfNecessary(DiscreteValueSignalParameters nsParams)
        {
            if (!CutoffsForStandardDeviation.ContainsKey(nsParams))
            {
                lock (CalcLock)
                {
                    if (!CutoffsForStandardDeviation.ContainsKey(nsParams))
                        CalculateCutoffs(nsParams);
                }
            }
        }

        private static double[][] GetProbabilitiesOfSignalGivenSourceLitigationQuality(DiscreteValueSignalParameters nsParams)
        {
            double[][] returnVal;
            if (CachedParamsValue.Equals(nsParams))
            {
                returnVal = CachedProbabilitiesValue;
                if (CachedParamsValue.Equals(nsParams)) // make sure it hasn't changed!
                {
                    return CachedProbabilitiesValue;
                }
            }
            CalculateCutoffsIfNecessary(nsParams);
            returnVal = ProbabilitiesOfSignalGivenSourceLitigationQualityForStandardDeviation[nsParams];
            CachedParamsValue = nsParams; // must change this first so that we can be sure to detect the change
            CachedProbabilitiesValue = returnVal;
            return returnVal;
        }

        private static void CalculateCutoffs(DiscreteValueSignalParameters nsParams)
        {
            // Midpoints from uniform distribution: 
            double[] midpoints = 
                EquallySpaced.GetEquallySpacedPoints(nsParams.NumPointsInSourceUniformDistribution);
            double[] drawsFromNormalDistribution = PointsInInverseNormalDistribution.Select(x => x * nsParams.StdevOfNormalDistribution).ToArray();
            // Now, we make every combination of uniform and normal distribution draws, and add them together
            var crossProduct = midpoints
                .SelectMany(x => drawsFromNormalDistribution, (x, y) => new { uniformDistPoint = x, normDistValue = y }).ToArray();
            var distinctPointsOrdered = crossProduct.Select(x => x.uniformDistPoint + x.normDistValue).OrderBy(x => x).ToArray();
            // Now we want the cutoffs for the signals, making each signal equally likely. Note that if we want 2 signals, then we want 1 cutoff at 0.5 (i.e., 50th percentiles); if there are 10 signals, we want cutoffs at percentiles corresponding to .1, .2, ..., .9. 
            // (More generally, n signals -> n - 1 percentile cutoffs). After we have the percentile cutoffs, we can divide the signals into 
            // corresponding, equally sized groups.
            double[] percentileCutoffs = EquallySpaced.GetCutoffsBetweenRegions(nsParams.NumSignals);
            double[] signalValueCutoffs = percentileCutoffs.Select(ple => ValueAtPercentile(distinctPointsOrdered, ple)).ToArray();
            // Now, for each of the signal ranges, we must determine the probability that we would end up in this signal range given
            // any actual litigation quality value. 
            double[][] probabilitiesOfSignalGivenSourceLitigationQuality = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(nsParams.NumPointsInSourceUniformDistribution,nsParams.NumSignals);
            for (int u = 0; u < nsParams.NumPointsInSourceUniformDistribution; u++)
            {
                int[] numValuesAtEachSignal = new int[nsParams.NumSignals];
                double uniformDistributionPoint = midpoints[u];
                var crossProductFromThisUniformDistributionPoint = crossProduct.Where(x => x.uniformDistPoint == uniformDistributionPoint).ToArray();
                int totalNumberForUniformDistributionPoint = 0;
                foreach (var point in crossProductFromThisUniformDistributionPoint)
                {
                    int band = GetBandForSignalValue(point.uniformDistPoint + point.normDistValue, signalValueCutoffs);
                    numValuesAtEachSignal[band]++;
                    totalNumberForUniformDistributionPoint++;
                }
                for (int s = 0; s < nsParams.NumSignals; s++)
                    probabilitiesOfSignalGivenSourceLitigationQuality[u][s] = ((double)numValuesAtEachSignal[s]) / ((double)totalNumberForUniformDistributionPoint);
            }
            // Assign to dictionary.
            CutoffsForStandardDeviation[nsParams] = signalValueCutoffs;
            ProbabilitiesOfSignalGivenSourceLitigationQualityForStandardDeviation[nsParams] = probabilitiesOfSignalGivenSourceLitigationQuality;
        }

        private static int GetBandForSignalValue(double signalValue, double[] signalValueCutoffs)
        {

            for (int s = 0; s < signalValueCutoffs.Length; s++)
            {
                if (signalValue < signalValueCutoffs[s])
                    return s;
            }
            return signalValueCutoffs.Length; // last band
        }

        public static double ValueAtPercentile(double[] orderedSequence, double percentile)
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
