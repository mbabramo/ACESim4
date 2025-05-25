using System;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Determines court liability outcomes using expected benefit / cost of the first untaken precaution.
    /// Requires a PrecautionImpactModel (true accident dynamics) and a PrecautionSignalModel (court's information).
    /// All indices are zero‑based.
    /// </summary>
    public sealed class CourtDecisionModel
    {
        // References
        readonly PrecautionImpactModel impact;
        readonly PrecautionSignalModel signal;

        readonly double precautionCost;
        readonly double harmCost;
        readonly double liabilityThreshold;

        // Dimensions
        readonly int numCourtSignals;
        readonly int numPrecautionLevels;

        // Precomputed tables
        readonly double[][] expRiskReduction;  // [signal][precaution]
        readonly double[][] expBenefit;        // [signal][precaution]
        readonly double[][] benefitCostRatio;  // [signal][precaution]
        readonly bool[][] liable;            // [signal][precaution]

        /// <summary>
        /// Build a court‑decision model.
        /// </summary>
        public CourtDecisionModel(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            double precautionCost,
            double harmCost,
            double liabilityThreshold = 1.0)
        {
            impact = impactModel ?? throw new ArgumentNullException(nameof(impactModel));
            signal = signalModel ?? throw new ArgumentNullException(nameof(signalModel));
            this.precautionCost = precautionCost > 0 ? precautionCost : throw new ArgumentException(nameof(precautionCost));
            this.harmCost = harmCost > 0 ? harmCost : throw new ArgumentException(nameof(harmCost));
            this.liabilityThreshold = liabilityThreshold > 0 ? liabilityThreshold : throw new ArgumentException(nameof(liabilityThreshold));

            numPrecautionLevels = impactModel.PrecautionLevels;
            numCourtSignals = signalModel.GetHiddenPosteriorFromCourtSignal(0).Length == 0
                ? throw new InvalidOperationException("Signal model appears unconfigured for court signals.")
                : signalModel.GetCourtSignalDistributionGivenDefendantSignal(0).Length; // quick way to fetch count

            // Precompute decision metrics
            expRiskReduction = new double[numCourtSignals][];
            expBenefit = new double[numCourtSignals][];
            benefitCostRatio = new double[numCourtSignals][];
            liable = new bool[numCourtSignals][];

            for (int s = 0; s < numCourtSignals; s++)
            {
                var posterior = signal.GetHiddenPosteriorFromCourtSignal(s); // length = hiddenCount, sums to 1
                expRiskReduction[s] = new double[numPrecautionLevels];
                expBenefit[s] = new double[numPrecautionLevels];
                benefitCostRatio[s] = new double[numPrecautionLevels];
                liable[s] = new bool[numPrecautionLevels];

                for (int k = 0; k < numPrecautionLevels; k++)
                {
                    // Expected risk reduction from k -> k+1 (if k is max‑1, still allowed; if k==max, delta=0)
                    double expectedDelta = 0;
                    for (int h = 0; h < posterior.Length; h++)
                    {
                        double delta = impact.GetRiskReduction(h, k);
                        expectedDelta += delta * posterior[h];
                    }
                    expRiskReduction[s][k] = expectedDelta; // already normalized via posterior
                    double benefit = expectedDelta * harmCost;
                    expBenefit[s][k] = benefit;
                    double ratio = precautionCost == 0 ? double.PositiveInfinity : benefit / precautionCost;
                    benefitCostRatio[s][k] = ratio;
                    liable[s][k] = ratio >= liabilityThreshold && k < numPrecautionLevels - 1; // cannot be liable if no further precaution exists
                }
            }
        }

        // ---------------- Public API ------------------------

        /// <summary>
        /// Expected benefit of first untaken precaution given court signal and chosen precaution.
        /// </summary>
        public double GetExpectedBenefit(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return expBenefit[courtSignal][precautionLevel];
        }

        /// <summary>
        /// Benefit/cost ratio for first untaken precaution.
        /// </summary>
        public double GetBenefitCostRatio(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return benefitCostRatio[courtSignal][precautionLevel];
        }

        /// <summary>
        /// Court's liability decision (true = liable) for given court signal and precaution level.
        /// </summary>
        public bool IsLiable(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return liable[courtSignal][precautionLevel];
        }

        // ---------------- Helpers ---------------------------

        void ValidateIndices(int signal, int precautionLevel)
        {
            if ((uint)signal >= numCourtSignals) throw new ArgumentOutOfRangeException(nameof(signal));
            if ((uint)precautionLevel >= numPrecautionLevels) throw new ArgumentOutOfRangeException(nameof(precautionLevel));
        }
    }
}
