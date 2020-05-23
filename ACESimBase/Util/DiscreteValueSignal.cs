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
        /// <param name="dsParams"></param>
        /// <returns></returns>
        public static int GetDiscreteSignal(double signal, DiscreteValueSignalParameters dsParams)
        {
            double[] cutoffs = GetSignalCutoffs(dsParams);
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
            double[][] probabilities = GetProbabilitiesOfDiscreteSignalGivenSourceStrength(dsParams);
            return probabilities[actualUniformDistributionValue - 1];
        }

        private static Dictionary<DiscreteValueSignalParameters, double[]> CutoffsForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[]>();
        private static Dictionary<DiscreteValueSignalParameters, double[][]> ProbabilitiesOfSignalGivenSourceStrengthForStandardDeviation = new Dictionary<DiscreteValueSignalParameters, double[][]>();
        private static object CalcLock = new object();

        // We will calculate a number of discrete points in the inverse normal distribution

        const int NumInInverseNormalDistribution = 10_000;
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

        private static double[] GetSignalCutoffs(DiscreteValueSignalParameters nsParams)
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

        private static double[][] GetProbabilitiesOfDiscreteSignalGivenSourceStrength(DiscreteValueSignalParameters nsParams)
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
            returnVal = ProbabilitiesOfSignalGivenSourceStrengthForStandardDeviation[nsParams];
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
            // Now we want the cutoffs for the signals 
            double[] percentileCutoffs = EquallySpaced.GetCutoffsBetweenRegions(nsParams.NumSignals);
            double[] signalValueCutoffs;
            if (nsParams.StdevOfNormalDistributionForCutoffPoints == null)
            { 
                // Make each signal equally likely. Note that if we want 2 signals, then we want 1 cutoff at 0.5(i.e., 50th percentiles); if there are 10 signals, we want cutoffs at percentiles corresponding to .1, .2, ..., .9.
               // (More generally, n signals -> n - 1 percentile cutoffs).
               signalValueCutoffs = percentileCutoffs.Select(ple => ValueAtPercentile(distinctPointsOrdered, ple)).ToArray();
            }
            else
            {
                // Some signals may be more likely than others. Get the cross product of the normal distribution for cutoff points and the source points, then take an ordered list of these sums. Thus, the higher the standard deviation we're using for this, the further apart the signal value cutoffs will be (and, if the normal distribution that we are using is lower, the less likely it will be that we hit extreme values). If the number of signals is even, then the number of cutoffs will be odd, and the middle cutoff will be 0.5, assuming that the source points are 0.0 and 1.0
                GetCombinedPoints(sourcePoints, (double)nsParams.StdevOfNormalDistributionForCutoffPoints, out (double uniformDistPoint, double normDistValue)[] uniformAndNormDistPointsToCombine2, out double[] distinctPointsOrdered2);
                signalValueCutoffs = percentileCutoffs.Select(ple => ValueAtPercentile(distinctPointsOrdered2, ple)).ToArray();
            }
            // Now, for each of the signal ranges, we must determine the probability that we would end up in this signal range given
            // any actual litigation quality value. We're now using the basic standard deviation of normal distribution -- not the one for cutoff points. 
            double[][] probabilitiesOfSignalGivenSourceStrength = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(nsParams.NumPointsInSourceUniformDistribution, nsParams.NumSignals);
            for (int u = 0; u < nsParams.NumPointsInSourceUniformDistribution; u++)
            {
                int[] numValuesAtEachSignal = new int[nsParams.NumSignals];
                double uniformDistributionPoint = sourcePoints[u];
                var crossProductFromThisUniformDistributionPoint = uniformAndNormDistPointsToCombine.Where(x => x.uniformDistPoint == uniformDistributionPoint).ToArray();
                int totalNumberForUniformDistributionPoint = 0;
                foreach (var point in crossProductFromThisUniformDistributionPoint)
                {
                    int band = GetBandForSignalValue(point.uniformDistPoint + point.normDistValue, signalValueCutoffs);
                    numValuesAtEachSignal[band]++;
                    totalNumberForUniformDistributionPoint++;
                }
                bool calculateExactValues = true; // if false, we use an approximation
                if (calculateExactValues)
                {
                    double sumCumNormal = 0;
                    int indexOfSignalWithinCutoff = -1;
                    bool subtractOneResultFromOthers = false; // can't use now that we're not bucketing liability values evenly
                    for (int s = 0; s < nsParams.NumSignals; s++)
                    {
                        double lowerCutoff = (s == 0) ? double.NegativeInfinity : signalValueCutoffs[s - 1];
                        double upperCutoff = (s == nsParams.NumSignals - 1) ? double.PositiveInfinity : signalValueCutoffs[s];
                        bool signalWithinCutoffs = (lowerCutoff < uniformDistributionPoint && uniformDistributionPoint <= upperCutoff);
                        if (signalWithinCutoffs)
                            indexOfSignalWithinCutoff = s;
                        double distance = Math.Min(Math.Abs(lowerCutoff - uniformDistributionPoint), Math.Abs(upperCutoff - uniformDistributionPoint));
                        double numStdDeviations = distance / nsParams.StdevOfNormalDistribution;
                        double cumNormal = NormalDistributionCalculation.CumulativeNormalDistribution(0 - numStdDeviations);
                        sumCumNormal += cumNormal;
                        if (!signalWithinCutoffs || !subtractOneResultFromOthers)
                            probabilitiesOfSignalGivenSourceStrength[u][s] = cumNormal; // the s values won't add up to 1.0 yet (we have to divide below)
                    }
                    if (subtractOneResultFromOthers)
                        probabilitiesOfSignalGivenSourceStrength[u][indexOfSignalWithinCutoff] = 1.0 - sumCumNormal;
                    else
                    {
                        if (sumCumNormal == 0)
                        {
                            // standard deviation size is so low that even using double precision, all cumulative normal distribution values
                            // round off to 0. So, let's just set the closest one to 1.
                            probabilitiesOfSignalGivenSourceStrength[u][indexOfSignalWithinCutoff] = 1.0;
                        }
                        else
                        {
                            if (sumCumNormal != 1)
                                for (int s = 0; s < nsParams.NumSignals; s++)
                                    probabilitiesOfSignalGivenSourceStrength[u][s] /= sumCumNormal;
                        }
                    }

                }
                else
                {
                    for (int s = 0; s < nsParams.NumSignals; s++)
                    {
                        probabilitiesOfSignalGivenSourceStrength[u][s] = ((double)numValuesAtEachSignal[s]) / ((double)totalNumberForUniformDistributionPoint);
                    }
                }
            }
            // Assign to dictionary.
            CutoffsForStandardDeviation[nsParams] = signalValueCutoffs;
            ProbabilitiesOfSignalGivenSourceStrengthForStandardDeviation[nsParams] = probabilitiesOfSignalGivenSourceStrength;
        }

        private static void GetCombinedPoints(double[] sourcePoints, double stdev, out (double uniformDistPoint, double normDistValue)[] uniformAndNormDistPointsToCombine, out double[] distinctPointsOrdered)
        {
            double[] drawsFromNormalDistribution = PointsInInverseNormalDistribution.Select(x => x * stdev).ToArray();
            uniformAndNormDistPointsToCombine = sourcePoints
                    .SelectMany(x => drawsFromNormalDistribution, (x, y) => (x, y)).ToArray();
            distinctPointsOrdered = uniformAndNormDistPointsToCombine.Select(x => x.uniformDistPoint + x.normDistValue).OrderBy(x => x).ToArray();
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
