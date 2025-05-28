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
        public int TotalLevelsForRisk { get; }           // = PrecautionLevels + 1 (includes hypothetical next level)
        public double PAccidentNoActivity { get; }
        public double PAccidentNoPrecaution { get; }
        public double PAccidentWrongfulAttribution { get; }
        public double PrecautionCost { get; }
        public double HarmCost { get; }
        public double LiabilityThreshold { get; }

        // -------------------- Internal data ---------------------------
        readonly double[] precautionPowerFactors;               // length HiddenCount, each in (0,1]
        readonly double[][] accidentProb;                       // [hidden][level] for level 0..TotalLevelsForRisk‑1
        readonly double[][] riskReduction;                      // [hidden][level] = accidentProb[h][k] - accidentProb[h][k+1]
        readonly bool[][] trueLiability;                      // ground‑truth negligence flags
        readonly double[] accidentProbMarginal;               // [level] marginal over hidden
        readonly double[] trueLiabilityProb;                  // [level] probability of negligence over hidden

        readonly Func<int, int, double> accidentFuncOverride;     // optional custom probability function

        /// <summary>
        /// Constructs the model.
        /// </summary>
        /// <param name="hiddenCount">Number of hidden precaution‑power states &gt; 0.</param>
        /// <param name="precautionLevels">Number of precaution levels the defendant may choose (>=1).</param>
        /// <param name="pAccidentNoActivity">Baseline accident risk with no activity (0≤p≤1).</param>
        /// <param name="pAccidentNoPrecaution">Accident risk when activity undertaken with zero precaution (≥ pAccidentNoActivity).</param>
        /// <param name="precautionCost">Cost of one unit of precaution (&gt;0).</param>
        /// <param name="harmCost">Loss from an accident (&gt;0).</param>
        /// <param name="precautionPowerFactors">Optional array length = hiddenCount with entries in (0,1]. If null, evenly spaced factors between 0.9 and 0.5 are used.</param>
        /// <param name="liabilityThreshold">Benefit / cost threshold for negligence (default 1.0).</param>
        /// <param name="accidentProbabilityOverride">Optional custom function P(accident | hiddenIndex, precautionLevel).</param>
        public PrecautionImpactModel(
            int hiddenCount,
            int precautionLevels,
            double pAccidentNoActivity,
            double pAccidentNoPrecaution,
            double precautionCost,
            double harmCost,
            double[] precautionPowerFactors = null,
            double precautionPowerFactorLeastEffective = 0.9,
            double precautionPowerFactorMostEffective = 0.5,
            double liabilityThreshold = 1.0,
            double pAccidentWrongfulAttribution = 0.0,
            Func<int, int, double> accidentProbabilityOverride = null)
        {
            if (hiddenCount <= 0) throw new ArgumentException(nameof(hiddenCount));
            if (precautionLevels <= 0) throw new ArgumentException(nameof(precautionLevels));
            if (pAccidentNoActivity < 0 || pAccidentNoActivity > 1) throw new ArgumentException(nameof(pAccidentNoActivity));
            if (pAccidentNoPrecaution < pAccidentNoActivity || pAccidentNoPrecaution > 1) throw new ArgumentException(nameof(pAccidentNoPrecaution));
            if (precautionCost <= 0) throw new ArgumentException(nameof(precautionCost));
            if (harmCost <= 0) throw new ArgumentException(nameof(harmCost));
            if (liabilityThreshold <= 0) throw new ArgumentException(nameof(liabilityThreshold));
            if (pAccidentWrongfulAttribution < 0 || pAccidentWrongfulAttribution > 1)
                throw new ArgumentException(nameof(pAccidentWrongfulAttribution));

            HiddenCount = hiddenCount;
            PrecautionLevels = precautionLevels;
            TotalLevelsForRisk = precautionLevels + 1; // include hypothetical next level
            PAccidentNoActivity = pAccidentNoActivity;
            PAccidentNoPrecaution = pAccidentNoPrecaution;
            PrecautionCost = precautionCost;
            HarmCost = harmCost;
            LiabilityThreshold = liabilityThreshold;
            PAccidentWrongfulAttribution = pAccidentWrongfulAttribution;

            // Set up precaution‑power factors (effectiveness multipliers per hidden state)
            if (precautionPowerFactors != null)
            {
                if (precautionPowerFactors.Length != hiddenCount)
                    throw new ArgumentException("precautionPowerFactors length must equal hiddenCount");
                if (precautionPowerFactors.Any(f => f <= 0 || f > 1))
                    throw new ArgumentException("All precaution power factors must be in (0,1]");
                this.precautionPowerFactors = precautionPowerFactors.ToArray();
            }
            else
            {
                // Default: evenly spaced factors from 0.9 (least effective) down to 0.5 (most effective)
                this.precautionPowerFactors = new double[hiddenCount];
                double start = precautionPowerFactorLeastEffective, end = precautionPowerFactorMostEffective;
                for (int h = 0; h < hiddenCount; h++)
                    this.precautionPowerFactors[h] = start - (start - end) * h / Math.Max(1, hiddenCount - 1);
            }

            accidentFuncOverride = accidentProbabilityOverride;

            accidentProb = BuildAccidentProb();
            riskReduction = BuildRiskReduction();
            trueLiability = BuildTrueLiability();
            accidentProbMarginal = BuildAccidentProbMarginal();
            trueLiabilityProb = BuildTrueLiabilityProb();
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

        /// <summary>
        /// P(accident | defendantSignal, plaintiffSignal, precautionLevel).
        /// Requires a configured PrecautionSignalModel (same hidden count & ordering).
        /// </summary>
        public double GetAccidentProbabilityGivenSignals(
            int defendantSignal,
            int plaintiffSignal,
            int precautionLevel,
            PrecautionSignalModel signalModel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            if (signalModel == null) throw new ArgumentNullException(nameof(signalModel));

            double numer = 0.0, denom = 0.0;
            double uniformPrior = 1.0 / HiddenCount;

            for (int h = 0; h < HiddenCount; h++)
            {
                double pDefSig = signalModel.GetDefendantSignalProbability(h, defendantSignal);
                double pPlSig = signalModel.GetPlaintiffSignalProbability(h, plaintiffSignal);
                double joint = uniformPrior * pDefSig * pPlSig;          // P(h,i,j)

                double pCaused = accidentFuncOverride != null
                    ? accidentFuncOverride(h, precautionLevel)
                    : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], precautionLevel);

                pCaused = Math.Max(0.0, Math.Min(1.0, pCaused));
                double pAccident = pCaused + (1.0 - pCaused) * PAccidentWrongfulAttribution;

                numer += joint * pAccident;
                denom += joint;
            }
            return denom == 0.0 ? 0.0 : numer / denom;
        }

        /// <summary>
        /// P(wrongful attribution | defendantSignal, plaintiffSignal, precautionLevel).
        /// Returns the probability that an accident is wrongfully attributed
        /// (conditioned on an accident having occurred, not on the accident node outcome).
        /// </summary>
        public double GetWrongfulAttributionProbabilityGivenSignals(
            int defendantSignal,
            int plaintiffSignal,
            int precautionLevel,
            PrecautionSignalModel signalModel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            if (signalModel == null) throw new ArgumentNullException(nameof(signalModel));

            double numer = 0.0, denom = 0.0;
            double uniformPrior = 1.0 / HiddenCount;

            for (int h = 0; h < HiddenCount; h++)
            {
                double pDefSig = signalModel.GetDefendantSignalProbability(h, defendantSignal);
                double pPlSig = signalModel.GetPlaintiffSignalProbability(h, plaintiffSignal);
                double joint = uniformPrior * pDefSig * pPlSig;

                double pCaused = accidentFuncOverride != null
                    ? accidentFuncOverride(h, precautionLevel)
                    : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], precautionLevel);

                pCaused = Math.Max(0.0, Math.Min(1.0, pCaused));

                double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
                double pAccident = pCaused + pWrongful;           // same as in BuildAccidentProb

                numer += joint * pWrongful;
                denom += joint * pAccident;                       // condition on accident happening
            }
            return denom == 0.0 ? 0.0 : numer / denom;
        }


        // ---------------------- Internal construction -----------------------------

        double[][] BuildAccidentProb()
        {
            var arr = new double[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new double[TotalLevelsForRisk];
                for (int k = 0; k < TotalLevelsForRisk; k++)
                {
                    // Baseline probability the defendant’s conduct actually causes an accident
                    double pCaused = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k);

                    pCaused = Math.Max(0, Math.Min(1, pCaused));          // clamp

                    // Wrongful attribution: applies only when no accident was caused
                    double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;

                    arr[h][k] = Math.Max(0, Math.Min(1, pCaused + pWrongful));
                }
            }
            return arr;
        }


        double[][] BuildRiskReduction()
        {
            var arr = new double[HiddenCount][];

            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new double[PrecautionLevels];

                for (int k = 0; k < PrecautionLevels; k++)
                {
                    // Accident probability *caused* by the defendant at level k
                    double pCaused_k = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k);

                    // …and at the next higher precaution level
                    double pCaused_k1 = accidentFuncOverride != null
                        ? accidentFuncOverride(h, k + 1)
                        : PAccidentNoPrecaution * Math.Pow(precautionPowerFactors[h], k + 1);

                    double delta = pCaused_k - pCaused_k1;
                    if (delta < 0) delta = 0;        // shouldn’t happen, but keep safeguard

                    arr[h][k] = delta;
                }
            }
            return arr;
        }

        bool[][] BuildTrueLiability()
        {
            var arr = new bool[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new bool[PrecautionLevels];
                for (int k = 0; k < PrecautionLevels; k++)
                {
                    double benefit = riskReduction[h][k] * HarmCost;
                    double ratio = benefit / PrecautionCost;
                    arr[h][k] = ratio > LiabilityThreshold; // negligent if benefit exceeds cost threshold
                }
            }
            return arr;
        }

        double[] BuildAccidentProbMarginal()
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

        double[] BuildTrueLiabilityProb()
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
