using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using System;
using System.Linq;

namespace ACESimBase.Util.DiscreteProbabilities
{
    /// <summary>
    /// Models three independent noisy signals derived from a hidden discrete source variable.
    /// All indexing is zero‑based.
    /// </summary>
    public sealed class ThreePartyDiscreteSignals
    {
        public const int PartyCount = 3;

        readonly int hiddenCount;
        readonly int[] signalCounts; // length 3

        readonly DiscreteValueSignalParameters[] partyParams; // length 3

        // Dimensions: [party][hidden][signal]
        readonly double[][][] probSignalGivenHidden;
        // Dimensions: [party][signal][hidden]
        readonly double[][][] probHiddenGivenSignal;
        // Dimensions: [targetParty][givenParty][givenSignal][targetSignal]
        readonly double[][][][] probSignalGivenSignal;

        /// <summary>
        /// Creates a new model with the specified configuration.
        /// </summary>
        /// <param name="hiddenCount">Number of discrete hidden source values &gt; 0.</param>
        /// <param name="signalCounts">Array of length 3 holding the number of signal levels for each party.</param>
        /// <param name="sigmas">Array of length 3 holding the noise stdev for each party.</param>
        /// <param name="sourceIncludesExtremes">If true, hidden values map to the full [0,1] range extremes.</param>
        public ThreePartyDiscreteSignals(int hiddenCount, int[] signalCounts, double[] sigmas, bool sourceIncludesExtremes = true)
        {
            if (hiddenCount <= 0) throw new ArgumentException(nameof(hiddenCount));
            if (signalCounts == null || signalCounts.Length != PartyCount) throw new ArgumentException(nameof(signalCounts));
            if (sigmas == null || sigmas.Length != PartyCount) throw new ArgumentException(nameof(sigmas));
            if (signalCounts.Any(c => c <= 0)) throw new ArgumentException("All signal counts must be positive.");

            this.hiddenCount = hiddenCount;
            this.signalCounts = signalCounts.ToArray();

            partyParams = new DiscreteValueSignalParameters[PartyCount];
            for (int i = 0; i < PartyCount; i++)
            {
                partyParams[i] = new DiscreteValueSignalParameters
                {
                    NumPointsInSourceUniformDistribution = hiddenCount,
                    NumSignals = signalCounts[i],
                    StdevOfNormalDistribution = sigmas[i],
                    SourcePointsIncludeExtremes = sourceIncludesExtremes
                };
            }

            probSignalGivenHidden = BuildSignalGivenHidden();
            probHiddenGivenSignal = BuildHiddenGivenSignal();
            probSignalGivenSignal = BuildSignalGivenSignal();
        }

        #region Public API

        public (int sig0, int sig1, int sig2) GenerateSignalsFromHidden(int hiddenValue, Random rng = null)
        {
            if ((uint)hiddenValue >= hiddenCount) throw new ArgumentOutOfRangeException(nameof(hiddenValue));
            rng ??= RandomShared.Instance;
            int[] result = new int[PartyCount];
            for (int party = 0; party < PartyCount; party++)
                result[party] = DrawIndex(probSignalGivenHidden[party][hiddenValue], rng);
            return (result[0], result[1], result[2]);
        }

        public double[] GetSignalDistributionGivenHidden(int partyIndex, int hiddenValue)
        {
            ValidateParty(partyIndex);
            if ((uint)hiddenValue >= hiddenCount) throw new ArgumentOutOfRangeException(nameof(hiddenValue));
            return probSignalGivenHidden[partyIndex][hiddenValue];
        }

        public double[] GetHiddenDistributionGivenSignal(int partyIndex, int signalValue)
        {
            ValidateParty(partyIndex);
            if ((uint)signalValue >= signalCounts[partyIndex]) throw new ArgumentOutOfRangeException(nameof(signalValue));
            return probHiddenGivenSignal[partyIndex][signalValue];
        }

        public double[] GetSignalDistributionGivenSignal(int targetPartyIndex, int givenPartyIndex, int givenSignalValue)
        {
            ValidateParty(targetPartyIndex);
            ValidateParty(givenPartyIndex);
            if (targetPartyIndex == givenPartyIndex) throw new ArgumentException("Target and given party must differ.");
            if ((uint)givenSignalValue >= signalCounts[givenPartyIndex]) throw new ArgumentOutOfRangeException(nameof(givenSignalValue));
            return probSignalGivenSignal[targetPartyIndex][givenPartyIndex][givenSignalValue];
        }

        #endregion

        #region Precomputation

        double[][][] BuildSignalGivenHidden()
        {
            var result = new double[PartyCount][][];
            for (int party = 0; party < PartyCount; party++)
            {
                result[party] = new double[hiddenCount][];
                for (int h = 0; h < hiddenCount; h++)
                {
                    // DiscreteValueSignal expects 1‑based sourceValue
                    result[party][h] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h + 1, partyParams[party]);
                }
            }
            return result;
        }

        double[][][] BuildHiddenGivenSignal()
        {
            var result = new double[PartyCount][][];
            double priorHidden = 1.0 / hiddenCount;
            for (int party = 0; party < PartyCount; party++)
            {
                int S = signalCounts[party];
                result[party] = new double[S][];
                for (int s = 0; s < S; s++)
                {
                    double[] posterior = new double[hiddenCount];
                    double denom = 0;
                    for (int h = 0; h < hiddenCount; h++)
                    {
                        double val = probSignalGivenHidden[party][h][s] * priorHidden;
                        posterior[h] = val;
                        denom += val;
                    }
                    if (denom == 0) throw new InvalidOperationException("Zero probability encountered in posterior computation.");
                    for (int h = 0; h < hiddenCount; h++)
                        posterior[h] /= denom;
                    result[party][s] = posterior;
                }
            }
            return result;
        }

        double[][][][] BuildSignalGivenSignal()
        {
            var result = new double[PartyCount][][][];
            for (int target = 0; target < PartyCount; target++)
            {
                result[target] = new double[PartyCount][][];
                for (int given = 0; given < PartyCount; given++)
                {
                    if (target == given)
                    {
                        result[target][given] = Array.Empty<double[]>();
                        continue;
                    }
                    int S_given = signalCounts[given];
                    int S_target = signalCounts[target];
                    result[target][given] = new double[S_given][];
                    for (int sGiven = 0; sGiven < S_given; sGiven++)
                    {
                        double[] dist = new double[S_target];
                        // Use posterior of hidden given the observed signal of the given party
                        double[] posteriorH = probHiddenGivenSignal[given][sGiven];
                        for (int sTarget = 0; sTarget < S_target; sTarget++)
                        {
                            double prob = 0;
                            for (int h = 0; h < hiddenCount; h++)
                                prob += probSignalGivenHidden[target][h][sTarget] * posteriorH[h];
                            dist[sTarget] = prob;
                        }
                        // dist is already normalized (posteriorH sums to 1, and for each h the conditional probabilities sum to 1 over sTarget)
                        result[target][given][sGiven] = dist;
                    }
                }
            }
            return result;
        }

        #endregion

        #region Helpers

        static int DrawIndex(double[] probabilities, Random rng)
        {
            double r = rng.NextDouble();
            double cumulative = 0;
            for (int i = 0; i < probabilities.Length; i++)
            {
                cumulative += probabilities[i];
                if (r <= cumulative) return i;
            }
            return probabilities.Length - 1; // Should rarely happen due to rounding.
        }

        static void ValidateParty(int partyIndex)
        {
            if ((uint)partyIndex >= PartyCount) throw new ArgumentOutOfRangeException(nameof(partyIndex));
        }

        #endregion
    }

    static class RandomShared
    {
        internal static readonly Random Instance = new Random();
    }
}
