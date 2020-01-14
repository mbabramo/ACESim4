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
        /// Calculates a signal by adding noise drawn from a normal distribution to a true value. If the sum is less than 0, the signal returned is 1. If it is greater than 1, the signal returned is numLiabilitySignalValues. Otherwise, the signal is in between.
        /// </summary>
        /// <param name="trueValueFrom0To1"></param>
        /// <param name="oneBasedNoiseValue"></param>
        /// <param name="numNoiseValues"></param>
        /// <param name="standardDeviationOfNoise"></param>
        /// <param name="numLiabilitySignalValues"></param>
        /// <returns></returns>
        public static byte ConvertNoiseToLiabilitySignal(double trueValueFrom0To1, byte oneBasedNoiseValue, byte numNoiseValues, double standardDeviationOfNoise, byte numLiabilitySignalValues)
        {
            double noiseUniformDistribution = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (oneBasedNoiseValue - 1), numNoiseValues, false);
            double noiseNormalDistributionDraw = InvNormal.Calculate(noiseUniformDistribution) * standardDeviationOfNoise;
            double obfuscatedValue = trueValueFrom0To1 + noiseNormalDistributionDraw;
            if (obfuscatedValue < 0)
                return 1;
            else if (obfuscatedValue > 1)
                return numLiabilitySignalValues;
            else
                return (byte) (2 + (int) Math.Floor(obfuscatedValue * (numLiabilitySignalValues - 2)));
        }

        /// <summary>
        /// Given a signal (in the form of the sum of values taken from a uniform distribution and from a normal distribution), returns a discrete signal, such that each signal will ex ante be equally likely to obtain. The lowest signal is equal to 1. 
        /// For example, if NumLiabilitySignals is 10, then one-tenth of the time, we will draw from the two distributions in a way that will produce a signal of i, for all i from 1 to 10. 
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="dsParams"></param>
        /// <returns></returns>
        public static int GetDiscreteLiabilitySignal(double signal, DiscreteValueSignalParameters dsParams)
        {
            double[] cutoffs = GetLiabilitySignalCutoffs(dsParams);
            int i = 0;
            while (i < cutoffs.Length && cutoffs[i] < signal)
                i++;
            return i + 1;
        }

        /// <summary>
        /// Given a draw of a band from a uniform distribution, returns the probability that a signal consisting of the sum of the midpoint of this band and a value drawn from a normal distribution would end up in the specified discrete band of signals, where each signal band is of equal size.
        /// </summary>
        /// <param name="actualUniformDistributionValue">The discrete band of the uniform distribution (numbered 1 .. number of bands)</param>
        /// <param name="discreteLiabilitySignal">The discrete band of the signal (numbered 1 .. number of signals)</param>
        /// <param name="dsParams">The parameters specifying the noise and the number of signals and uniform distribution points</param>
        /// <returns></returns>
        public static double GetProbabilityOfDiscreteLiabilitySignal(int actualUniformDistributionValue, int discreteLiabilitySignal, DiscreteValueSignalParameters dsParams)
        {
            return GetProbabilitiesOfDiscreteSignals(actualUniformDistributionValue, dsParams)[discreteLiabilitySignal - 1];
        }


        public static double[] GetProbabilitiesOfDiscreteSignals(int actualUniformDistributionValue, DiscreteValueSignalParameters dsParams)
        {
            double[][] probabilities = GetProbabilitiesOfLiabilitySignalGivenSourceLiabilityStrength(dsParams);
            return probabilities[actualUniformDistributionValue - 1];
        }

        private static Dictionary<DiscreteValueSignalParameters, double[]> CutoffsForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[]>();
        private static Dictionary<DiscreteValueSignalParameters, double[][]> ProbabilitiesOfLiabilitySignalGivenSourceLiabilityStrengthForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[][]>();
        private static object CalcLock = new object();

        // We will calculate a number of discrete points in the inverse normal distribution

        const int NumInInverseNormalDistribution = 100; // DEBUG 10_000;
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
                _PointsInInverseNormalDistribution = EquallySpaced.GetEquallySpacedPoints(NumInInverseNormalDistribution, false)
                    .Select(x => InvNormal.Calculate(x))
                    .ToArray();
            }
        }

        // We are going to store last values returned for a particular parameters to avoid having to go to the dictionary unnecessarily.
        private static bool AllowCaching = false; // Does not work if running multiple simulations in parallel
        private static double[] CachedCutoffsValue;
        private static double[][] CachedProbabilitiesValue;
        private static DiscreteValueSignalParameters CachedParamsValue;

        private static double[] GetLiabilitySignalCutoffs(DiscreteValueSignalParameters nsParams)
        {
            double[] returnVal;
            if (AllowCaching && CachedParamsValue.Equals(nsParams))
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

        private static double[][] GetProbabilitiesOfLiabilitySignalGivenSourceLiabilityStrength(DiscreteValueSignalParameters nsParams)
        {
            double[][] returnVal;
            if (AllowCaching && CachedParamsValue.Equals(nsParams))
            {
                returnVal = CachedProbabilitiesValue;
                if (CachedParamsValue.Equals(nsParams)) // make sure it hasn't changed!
                {
                    return CachedProbabilitiesValue;
                }
            }
            CalculateCutoffsIfNecessary(nsParams);
            returnVal = ProbabilitiesOfLiabilitySignalGivenSourceLiabilityStrengthForStandardDeviation[nsParams];
            CachedParamsValue = nsParams; // must change this first so that we can be sure to detect the change
            CachedProbabilitiesValue = returnVal;
            return returnVal;
        }

        private static void CalculateCutoffs(DiscreteValueSignalParameters nsParams)
        {
            // Midpoints from uniform distribution: 
            var equallySpacedPoints = EquallySpaced.GetEquallySpacedPoints(nsParams.NumPointsInSourceUniformDistribution, nsParams.UseEndpoints);
            double[] sourcePoints = equallySpacedPoints;

            double stdev = nsParams.StdevOfNormalDistribution;
            if (stdev == 0)
                stdev = 1E-10;
            GetCombinedPoints(sourcePoints, stdev, out (double uniformDistPoint, double normDistValue)[] uniformAndNormDistPointsToCombine, out double[] distinctPointsOrdered);
            // Now we want the cutoffs for the signals, making each signal equally likely. Note that if we want 2 signals, then we want 1 cutoff at 0.5 (i.e., 50th percentiles); if there are 10 signals, we want cutoffs at percentiles corresponding to .1, .2, ..., .9. 
            // (More generally, n signals -> n - 1 percentile cutoffs). After we have the percentile cutoffs, we can divide the signals into 
            // corresponding, equally sized groups.
            double[] percentileCutoffs = EquallySpaced.GetCutoffsBetweenRegions(nsParams.NumSignals);
            double[] signalValueCutoffs;
            if (nsParams.StdevOfNormalDistributionForCutoffPoints == null)
                signalValueCutoffs = percentileCutoffs.Select(ple => ValueAtPercentile(distinctPointsOrdered, ple)).ToArray();
            else
            {
                GetCombinedPoints(sourcePoints, (double)nsParams.StdevOfNormalDistributionForCutoffPoints, out (double uniformDistPoint, double normDistValue)[] uniformAndNormDistPointsToCombine2, out double[] distinctPointsOrdered2);
                signalValueCutoffs = percentileCutoffs.Select(ple => ValueAtPercentile(distinctPointsOrdered2, ple)).ToArray();
            }
            // Now, for each of the signal ranges, we must determine the probability that we would end up in this signal range given
            // any actual litigation quality value. 
            double[][] probabilitiesOfLiabilitySignalGivenSourceLiabilityStrength = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(nsParams.NumPointsInSourceUniformDistribution, nsParams.NumSignals);
            for (int u = 0; u < nsParams.NumPointsInSourceUniformDistribution; u++)
            {
                int[] numValuesAtEachLiabilitySignal = new int[nsParams.NumSignals];
                double uniformDistributionPoint = sourcePoints[u];
                var crossProductFromThisUniformDistributionPoint = uniformAndNormDistPointsToCombine.Where(x => x.uniformDistPoint == uniformDistributionPoint).ToArray();
                int totalNumberForUniformDistributionPoint = 0;
                foreach (var point in crossProductFromThisUniformDistributionPoint)
                {
                    int band = GetBandForLiabilitySignalValue(point.uniformDistPoint + point.normDistValue, signalValueCutoffs);
                    numValuesAtEachLiabilitySignal[band]++;
                    totalNumberForUniformDistributionPoint++;
                }
                bool calculateExactValues = false; // DEBUG nsParams.NonUniformWeightingOfPoints != null; // we've calculated the signal bands using an approximation, but we may wish to use exact values so that we can represent small probabilities
                if (calculateExactValues)
                {
                    double sumCumNormal = 0;
                    int indexOfSignalWithinCutoff = -1;
                    for (int s = 0; s < nsParams.NumSignals; s++)
                    {
                        double lowerCutoff = (s == 0) ? double.NegativeInfinity : signalValueCutoffs[s - 1];
                        double upperCutoff = (s == nsParams.NumSignals - 1) ? double.PositiveInfinity : signalValueCutoffs[s];
                        if (lowerCutoff < uniformDistributionPoint && uniformDistributionPoint <= upperCutoff)
                            indexOfSignalWithinCutoff = s;
                        else
                        {
                            double distance = Math.Min(Math.Abs(lowerCutoff - uniformDistributionPoint), Math.Abs(upperCutoff - uniformDistributionPoint));
                            double numStdDeviations = distance / nsParams.StdevOfNormalDistribution;
                            double cumNormal = NormalDistributionCalculation.CumulativeNormalDistribution(0 - numStdDeviations);
                            sumCumNormal += cumNormal;
                            probabilitiesOfLiabilitySignalGivenSourceLiabilityStrength[u][s] = cumNormal;
                        }
                    }
                    probabilitiesOfLiabilitySignalGivenSourceLiabilityStrength[u][indexOfSignalWithinCutoff] = 1.0 - sumCumNormal;
                }
                else
                {
                    for (int s = 0; s < nsParams.NumSignals; s++)
                    {
                        probabilitiesOfLiabilitySignalGivenSourceLiabilityStrength[u][s] = ((double)numValuesAtEachLiabilitySignal[s]) / ((double)totalNumberForUniformDistributionPoint);
                    }
                }
            }
            // Assign to dictionary.
            CutoffsForStandardDeviation[nsParams] = signalValueCutoffs;
            ProbabilitiesOfLiabilitySignalGivenSourceLiabilityStrengthForStandardDeviation[nsParams] = probabilitiesOfLiabilitySignalGivenSourceLiabilityStrength;
        }

        private static void GetCombinedPoints(double[] sourcePoints, double stdev, out (double uniformDistPoint, double normDistValue)[] uniformAndNormDistPointsToCombine, out double[] distinctPointsOrdered)
        {
            double[] drawsFromNormalDistribution = PointsInInverseNormalDistribution.Select(x => x * stdev).ToArray();
            uniformAndNormDistPointsToCombine = sourcePoints
                    .SelectMany(x => drawsFromNormalDistribution, (x, y) => (x, y)).ToArray();
            distinctPointsOrdered = uniformAndNormDistPointsToCombine.Select(x => x.uniformDistPoint + x.normDistValue).OrderBy(x => x).ToArray();
        }

        private static int GetBandForLiabilitySignalValue(double signalValue, double[] signalValueCutoffs)
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
