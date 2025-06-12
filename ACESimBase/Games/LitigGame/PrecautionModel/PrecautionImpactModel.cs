using System;
using System.Linq;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Models accident risk as a function of a hidden precaution power level and a defendant's chosen precaution level.
    /// Provides ground‑truth liability evaluation based on benefit / cost analysis of the first untaken precaution.
    /// All indexing is zero‑based.
    /// </summary>
    public sealed class PrecautionImpactModel
    {
        // -------------------- Public configuration --------------------
        public int HiddenCount { get; }
        public int PrecautionLevels { get; }           // Levels defendant may choose: 0 .. (PrecautionLevels‑1)
        public int TotalLevelsForRisk { get; }         // = PrecautionLevels + 1 (includes hypothetical next level)
        public double PAccidentNoPrecaution { get; }
        public double PAccidentWrongfulAttribution { get; }
        public double MarginalPrecautionCost { get; }
        public double HarmCost { get; }
        public double LiabilityThreshold { get; }

        // -------------------- Internal data ---------------------------
        readonly double[] precautionPowerFactors;               // length HiddenCount, each in (0,1]
        readonly double[][] accidentProb;                       // [hidden][level] for level 0..TotalLevelsForRisk‑1
        readonly double[][] riskReduction;                      // [hidden][level] = accidentProb[h][k] - accidentProb[h][k+1]
        readonly bool[][] trueLiability;                        // ground‑truth negligence flags
        readonly double[] accidentProbMarginal;                 // [level] marginal over hidden
        readonly double[] trueLiabilityProb;                    // [level] probability of negligence over hidden

        readonly Func<int, int, double> accidentFuncOverride;   // optional custom probability function

        //-----------------------------------------------------------------------
        // Constructor
        //-----------------------------------------------------------------------
        public PrecautionImpactModel(
            int precautionPowerLevels,
            int precautionLevels,
            double pAccidentNoPrecaution,
            double marginalPrecautionCost,
            double harmCost,
            double[] precautionPowerFactors = null,
            double precautionPowerFactorLeastEffective = 0.9,
            double precautionPowerFactorMostEffective = 0.5,
            double liabilityThreshold = 1.0,
            double pAccidentWrongfulAttribution = 0.0,
            Func<int, int, double> accidentProbabilityOverride = null)
        {
            if (precautionPowerLevels <= 0) throw new ArgumentException(nameof(precautionPowerLevels));
            if (precautionLevels <= 0) throw new ArgumentException(nameof(precautionLevels));
            if (pAccidentNoPrecaution > 1) throw new ArgumentException(nameof(pAccidentNoPrecaution));
            if (marginalPrecautionCost <= 0) throw new ArgumentException(nameof(marginalPrecautionCost));
            if (harmCost <= 0) throw new ArgumentException(nameof(harmCost));
            if (liabilityThreshold <= 0) throw new ArgumentException(nameof(liabilityThreshold));
            if (pAccidentWrongfulAttribution < 0 || pAccidentWrongfulAttribution > 1)
                throw new ArgumentException(nameof(pAccidentWrongfulAttribution));

            HiddenCount = precautionPowerLevels;
            PrecautionLevels = precautionLevels;
            TotalLevelsForRisk = precautionLevels + 1; // include hypothetical next level
            PAccidentNoPrecaution = pAccidentNoPrecaution;
            MarginalPrecautionCost = marginalPrecautionCost;
            HarmCost = harmCost;
            LiabilityThreshold = liabilityThreshold;
            PAccidentWrongfulAttribution = pAccidentWrongfulAttribution;

            // Set up precaution‑power factors (effectiveness multipliers per hidden state)
            if (precautionPowerFactors != null)
            {
                if (precautionPowerFactors.Length != precautionPowerLevels)
                    throw new ArgumentException("precautionPowerFactors length must equal hiddenCount");
                if (precautionPowerFactors.Any(f => f <= 0 || f > 1))
                    throw new ArgumentException("All precaution power factors must be in (0,1]");
                this.precautionPowerFactors = precautionPowerFactors.ToArray();
            }
            else
            {
                this.precautionPowerFactors = new double[precautionPowerLevels];
                double start = precautionPowerFactorLeastEffective, end = precautionPowerFactorMostEffective;
                for (int h = 0; h < precautionPowerLevels; h++)
                    this.precautionPowerFactors[h] = start - (start - end) * h / Math.Max(1, precautionPowerLevels - 1);
            }

            accidentFuncOverride = accidentProbabilityOverride;

            // ---- Build core tables (each build followed immediately by its accessor) ----
            accidentProb = BuildAccidentProbTable();
            riskReduction = BuildRiskReductionTable();
            trueLiability = BuildTrueLiabilityTable();
            accidentProbMarginal = BuildAccidentProbMarginalTable();
            trueLiabilityProb = BuildTrueLiabilityProbTable();
        }

        // ------------- Public API ---------------------------------------------------

        public double GetAccidentProbability(int hiddenIndex, int precautionLevel)
        {
            ValidateIndices(hiddenIndex, precautionLevel, allowHypothetical: true);
            return accidentProb[hiddenIndex][precautionLevel];
        }

        public double GetAccidentProbabilityMarginal(int precautionLevel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            return accidentProbMarginal[precautionLevel];
        }

        public double GetRiskReduction(int hiddenIndex, int precautionLevel)
        {
            ValidateIndices(hiddenIndex, precautionLevel, allowHypothetical: false);
            return riskReduction[hiddenIndex][precautionLevel];
        }

        public bool IsTrulyLiable(int hiddenIndex, int precautionLevel)
        {
            ValidateIndices(hiddenIndex, precautionLevel, allowHypothetical: false);
            return trueLiability[hiddenIndex][precautionLevel];
        }

        public double GetTrueLiabilityProbability(int precautionLevel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            return trueLiabilityProb[precautionLevel];
        }

        public double GetWrongfulAttributionProbabilityMarginal(int precautionLevel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);

            double uniformPrior = 1.0 / HiddenCount;
            double sumWrongful = 0.0, sumTotal = 0.0;

            for (int h = 0; h < HiddenCount; h++)
            {
                double pCaused = accidentFuncOverride != null
                    ? accidentFuncOverride(h, precautionLevel)
                    : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], precautionLevel);

                pCaused = Math.Max(0, Math.Min(1, pCaused));

                double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
                double pTotal = accidentProb[h][precautionLevel];

                sumWrongful += uniformPrior * pWrongful;
                sumTotal += uniformPrior * pTotal;
            }

            return sumTotal > 0 ? sumWrongful / sumTotal : 0.0;
        }

        public double GetWrongfulAttributionProbabilityGivenHiddenState(int hiddenState, int precautionLevel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            if (hiddenState < 0 || hiddenState >= HiddenCount)
                throw new ArgumentOutOfRangeException(nameof(hiddenState));

            double pCaused = accidentFuncOverride != null
                ? accidentFuncOverride(hiddenState, precautionLevel)
                : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[hiddenState], precautionLevel);

            pCaused = Math.Clamp(pCaused, 0.0, 1.0);
            double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
            double pAccident = pCaused + pWrongful;

            return pAccident == 0.0 ? 0.0 : pWrongful / pAccident;
        }

        // ---------------------- Internal construction -----------------------------

        double[][] BuildAccidentProbTable()
        {
            var arr = new double[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new double[TotalLevelsForRisk];
                for (int k = 0; k < TotalLevelsForRisk; k++)
                {
                    double pCaused = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k);

                    pCaused = Math.Max(0, Math.Min(1, pCaused));
                    double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
                    arr[h][k] = Math.Max(0, Math.Min(1, pCaused + pWrongful));
                }
            }
            return arr;
        }

        double[][] BuildRiskReductionTable()
        {
            var arr = new double[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new double[PrecautionLevels];
                for (int k = 0; k < PrecautionLevels; k++)
                {
                    double pCaused_k = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k);

                    double pCaused_k1 = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k + 1)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k + 1);

                    double delta = pCaused_k - pCaused_k1;
                    if (delta < 0) delta = 0;
                    arr[h][k] = delta;
                }
            }
            return arr;
        }

        bool[][] BuildTrueLiabilityTable()
        {
            var arr = new bool[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new bool[PrecautionLevels];
                for (int k = 0; k < PrecautionLevels; k++)
                {
                    double benefit = riskReduction[h][k] * HarmCost;
                    double ratio = benefit / MarginalPrecautionCost;
                    arr[h][k] = ratio > LiabilityThreshold;
                }
            }
            return arr;
        }

        double[] BuildAccidentProbMarginalTable()
        {
            var arr = new double[PrecautionLevels];
            double uniformPrior = 1.0 / HiddenCount;
            for (int k = 0; k < PrecautionLevels; k++)
            {
                double sum = 0;
                for (int h = 0; h < HiddenCount; h++)
                    sum += accidentProb[h][k] * uniformPrior;
                arr[k] = sum;
            }
            return arr;
        }

        double[] BuildTrueLiabilityProbTable()
        {
            var arr = new double[PrecautionLevels];
            double uniformPrior = 1.0 / HiddenCount;
            for (int k = 0; k < PrecautionLevels; k++)
            {
                double sum = 0;
                for (int h = 0; h < HiddenCount; h++)
                    if (trueLiability[h][k])
                        sum += uniformPrior;
                arr[k] = sum;
            }
            return arr;
        }

        // ------------------- Validation helpers -----------------------------------
        void ValidateIndices(int hiddenIndex, int precautionLevel, bool allowHypothetical)
        {
            if ((uint)hiddenIndex >= HiddenCount) throw new ArgumentOutOfRangeException(nameof(hiddenIndex));
            ValidatePrecautionLevel(precautionLevel, allowHypothetical);
        }

        void ValidatePrecautionLevel(int precautionLevel, bool allowHypothetical)
        {
            int maxAllowed = allowHypothetical ? TotalLevelsForRisk - 1 : PrecautionLevels - 1;
            if ((uint)precautionLevel > maxAllowed) throw new ArgumentOutOfRangeException(nameof(precautionLevel));
        }
    }
}
