// ACESimBase.Util.DiscreteProbabilities.ThreePartyDiscreteSignalsInferNet.cs
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using Microsoft.ML.Probabilistic.Models;
using Microsoft.ML.Probabilistic.Distributions;
using Microsoft.ML.Probabilistic.Math;
using System;
using System.Linq;
using Range = Microsoft.ML.Probabilistic.Models.Range;


namespace ACESimBase.Util.DiscreteProbabilities
{
    /// <summary>
    /// Infer.NET-backed re-implementation of ThreePartyDiscreteSignals.
    /// All indexing is zero-based.
    /// </summary>
    public sealed class ThreePartyDiscreteSignalsInferNet
    {
        public const int PartyCount = 3;

        readonly int hiddenCount;
        readonly int[] signalCounts;                                  // [party]
        readonly DiscreteValueSignalParameters[] partyParams;         // [party]
        readonly double[][][] probSignalGivenHidden;                  // [party][hidden][signal]
        readonly Vector[][] cptVectors;                               // [party][hidden]

        public ThreePartyDiscreteSignalsInferNet(int hiddenCount,
                                                 int[] signalCounts,
                                                 double[] sigmas,
                                                 bool sourceIncludesExtremes = true)
        {
            if (hiddenCount <= 0) throw new ArgumentException(nameof(hiddenCount));
            if (signalCounts == null || signalCounts.Length != PartyCount) throw new ArgumentException(nameof(signalCounts));
            if (sigmas == null || sigmas.Length != PartyCount) throw new ArgumentException(nameof(sigmas));
            if (signalCounts.Any(c => c <= 0)) throw new ArgumentException("All signal counts must be positive.");

            this.hiddenCount = hiddenCount;
            this.signalCounts = signalCounts.ToArray();

            // Build per-party Discrete-Signal parameters.
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
            cptVectors = BuildCptVectors();                 // Vector-form for Infer.NET
        }

        #region Public API
        public (int sig0, int sig1, int sig2) GenerateSignalsFromHidden(int hiddenValue, Random rng = null)
        {
            if ((uint)hiddenValue >= hiddenCount) throw new ArgumentOutOfRangeException(nameof(hiddenValue));
            rng ??= Random.Shared;

            int Draw(int party)
            {
                double[] p = probSignalGivenHidden[party][hiddenValue];
                double r = rng.NextDouble();
                double cum = 0.0;
                for (int i = 0; i < p.Length; i++)
                {
                    cum += p[i];
                    if (r <= cum) return i;
                }
                return p.Length - 1; // should not happen except for rounding
            }

            return (Draw(0), Draw(1), Draw(2));
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

            // “-1” marks “no second party”.
            Discrete post = InferHidden(signalValue, null, partyIndex, -1);
            return post.GetProbs().ToArray();
        }

        public double[] GetHiddenDistributionGivenTwoSignals(
            int partyIndex1, int signalValue1,
            int partyIndex2, int signalValue2)
        {
            ValidatePartyPair(partyIndex1, partyIndex2);
            if ((uint)signalValue1 >= signalCounts[partyIndex1]) throw new ArgumentOutOfRangeException(nameof(signalValue1));
            if ((uint)signalValue2 >= signalCounts[partyIndex2]) throw new ArgumentOutOfRangeException(nameof(signalValue2));

            Discrete posterior = InferHidden(signalValue1, signalValue2, partyIndex1, partyIndex2);
            return posterior.GetProbs().ToArray();
        }

        public double[] GetSignalDistributionGivenSignal(int targetPartyIndex,
                                                         int givenPartyIndex,
                                                         int givenSignalValue)
        {
            ValidatePartyPair(targetPartyIndex, givenPartyIndex);
            if ((uint)givenSignalValue >= signalCounts[givenPartyIndex]) throw new ArgumentOutOfRangeException(nameof(givenSignalValue));

            // Only one observed party, so pass -1 for the second.
            Discrete dist = InferSignal(targetPartyIndex, givenSignalValue, null,
                                        givenPartyIndex, -1);
            return dist.GetProbs().ToArray();
        }

        public double[] GetSignalDistributionGivenTwoSignals(
            int targetPartyIndex,
            int givenPartyIndex1, int givenSignalValue1,
            int givenPartyIndex2, int givenSignalValue2)
        {
            if (givenPartyIndex1 == givenPartyIndex2)
                throw new ArgumentException("Given parties must differ.");
            ValidatePartyTriple(targetPartyIndex, givenPartyIndex1, givenPartyIndex2);

            if ((uint)givenSignalValue1 >= signalCounts[givenPartyIndex1]) throw new ArgumentOutOfRangeException(nameof(givenSignalValue1));
            if ((uint)givenSignalValue2 >= signalCounts[givenPartyIndex2]) throw new ArgumentOutOfRangeException(nameof(givenSignalValue2));

            Discrete dist = InferSignal(targetPartyIndex, givenSignalValue1, givenSignalValue2, givenPartyIndex1, givenPartyIndex2);
            return dist.GetProbs().ToArray();
        }
        #endregion

        #region Infer.NET helpers
        Discrete InferHidden(int? sigA, int? sigB, int partyA, int partyB)
        {
            var h = new Range(hiddenCount);
            var hidden = Variable.DiscreteUniform(h).Named("H");

            var sigVars = BuildSignalVariables(h, hidden);

            if (partyA >= 0 && sigA.HasValue)
                sigVars[partyA].ObservedValue = sigA.Value;

            if (partyB >= 0 && sigB.HasValue)
                sigVars[partyB].ObservedValue = sigB.Value;

            var ie = new InferenceEngine(); 
            ie.ModelName = $"TPS_{Guid.NewGuid():N}";   // ← force a fresh compile every call
            ie.ModelNamespace = "MyInferModel_" + Guid.NewGuid();


            return ie.Infer<Discrete>(hidden);
        }

        Discrete InferSignal(int targetParty,
                             int? sigA, int? sigB,
                             int partyA, int partyB)
        {
            var h = new Range(hiddenCount);
            var hidden = Variable.DiscreteUniform(h).Named("H");

            var sigVars = BuildSignalVariables(h, hidden);

            if (partyA >= 0 && sigA.HasValue)
                sigVars[partyA].ObservedValue = sigA.Value;

            if (partyB >= 0 && sigB.HasValue)
                sigVars[partyB].ObservedValue = sigB.Value;

            var ie = new InferenceEngine();
            ie.ModelName = $"TPS_{Guid.NewGuid():N}";   // ← force a fresh compile every call
            ie.ModelNamespace = "MyInferModel_" + Guid.NewGuid();

            return ie.Infer<Discrete>(sigVars[targetParty]);
        }


        /// <summary>
        /// Creates S0, S1, S2 with explicit value ranges and connects each
        /// to its conditional-probability table  CPT[p][hidden].
        /// </summary>
        Variable<int>[] BuildSignalVariables(Range h, Variable<int> hidden)
        {
            var sigVars = new Variable<int>[PartyCount];

            for (int p = 0; p < PartyCount; p++)
            {
                // Value domain for this party’s signals: {0, …, signalCounts[p]-1}
                Range sRange = new Range(signalCounts[p]).Named($"S{p}Range");

                // CPT[p] is a Vector[hidden] that we already pre-computed
                var cpt = Variable.Array<Vector>(h).Named($"cpt{p}");
                cpt.ObservedValue = cptVectors[p];

                // Build Sₚ in one line so the Range information is baked in
                using (Variable.Switch(hidden))                 // pick row after H is realised
                {
                    sigVars[p] = Variable
                        .Discrete(sRange, cpt[hidden])          // <-- range attached here
                        .Named($"S{p}");
                }
            }

            return sigVars;
        }



        #endregion

        #region Internal builders
        double[][][] BuildSignalGivenHidden()
        {
            var result = new double[PartyCount][][];                // [party][hidden][signal]
            for (int p = 0; p < PartyCount; p++)
            {
                result[p] = new double[hiddenCount][];
                for (int h = 0; h < hiddenCount; h++)
                {
                    // NOTE: DiscreteValueSignal expects 1-based sourceValue
                    result[p][h] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h + 1, partyParams[p]);
                }
            }
            return result;
        }


        Vector[][] BuildCptVectors()
        {
            var vectors = new Vector[PartyCount][];
            for (int p = 0; p < PartyCount; p++)
            {
                vectors[p] = new Vector[hiddenCount];
                for (int h = 0; h < hiddenCount; h++)
                    vectors[p][h] = Vector.FromArray(probSignalGivenHidden[p][h]);
            }
            return vectors;
        }
        #endregion

        #region Validation helpers
        static void ValidateParty(int partyIndex)
        {
            if ((uint)partyIndex >= PartyCount) throw new ArgumentOutOfRangeException(nameof(partyIndex));
        }

        static void ValidatePartyPair(int p1, int p2)
        {
            ValidateParty(p1); ValidateParty(p2);
            if (p1 == p2) throw new ArgumentException("Parties must differ.");
        }

        static void ValidatePartyTriple(int target, int p1, int p2)
        {
            ValidateParty(target); ValidatePartyPair(p1, p2);
            if (target == p1 || target == p2) throw new ArgumentException("Target party must differ from given parties.");
        }
        #endregion
    }
}
