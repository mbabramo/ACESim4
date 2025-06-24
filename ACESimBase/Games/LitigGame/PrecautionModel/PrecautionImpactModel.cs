using System;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Selects the rule used to compute the marginal accident-risk reduction of a
    /// chosen precaution level.
    /// </summary>
    public enum MarginalBenefitRule
    {
        AtChosenPrecautionLevel,        // default: P(h,k) – P(h,k+ε)
        RelativeToNextDiscreteLevel     //        P(h,k) – P(h,k+1)
    }

    /// <summary>
    /// Models accident risk as a function of a hidden precaution power level and a
    /// defendant's chosen precaution level.  Provides ground-truth liability
    /// evaluation based on benefit / cost analysis of the first untaken precaution.
    /// All indexing is zero-based.
    /// </summary>
    public sealed class PrecautionImpactModel
    {
        // -------------------- Public configuration --------------------
        public int HiddenCount { get; }
        public int PrecautionLevels { get; }
        public int TotalLevelsForRisk { get; }
        public double PAccidentNoPrecaution { get; }
        public double PAccidentWrongfulAttribution { get; }
        public double UnitPrecautionCost { get; }
        public double HarmCost { get; }
        public double LiabilityThreshold { get; }

        // -------------------- Internal data ---------------------------
        readonly double _pMinLow;
        readonly double _pMinHigh;
        readonly double _alphaLow;
        readonly double _alphaHigh;

        readonly double[][] accidentProb;
        readonly double[][] riskReduction;
        readonly double[][] benefitCostRatio;
        readonly bool[][] trueLiability;
        readonly double[] accidentProbMarginal;
        readonly double[] trueLiabilityProb;

        readonly Func<int, int, double> accidentFuncOverride;

        readonly MarginalBenefitRule _benefitRule;
        readonly double _epsilon;

        //-----------------------------------------------------------------------
        // Constructor
        //-----------------------------------------------------------------------
        public PrecautionImpactModel(
            int precautionPowerLevels,
            int precautionLevels,
            double pAccidentNoPrecaution,
            double pMinLow,
            double pMinHigh,
            double alphaLow,
            double alphaHigh,
            double marginalPrecautionCost,
            double harmCost,
            double liabilityThreshold = 1.0,
            double pAccidentWrongfulAttribution = 0.0,
            MarginalBenefitRule benefitRule = MarginalBenefitRule.AtChosenPrecautionLevel,
            double epsilonForBenefit = 1e-6)
        {
            if (precautionPowerLevels <= 0) throw new ArgumentException(nameof(precautionPowerLevels));
            if (precautionLevels <= 0) throw new ArgumentException(nameof(precautionLevels));
            if (pAccidentNoPrecaution <= 0 || pAccidentNoPrecaution > 1) throw new ArgumentException(nameof(pAccidentNoPrecaution));
            if (pMinLow < 0 || pMinHigh < 0 || pMinHigh > pMinLow) throw new ArgumentException("pMinHigh must be ≤ pMinLow and both ≥ 0.");
            if (alphaLow <= 0 || alphaHigh <= 0) throw new ArgumentException("α values must be positive.");
            if (marginalPrecautionCost <= 0) throw new ArgumentException(nameof(marginalPrecautionCost));
            if (harmCost <= 0) throw new ArgumentException(nameof(harmCost));
            if (liabilityThreshold < 0) throw new ArgumentException(nameof(liabilityThreshold));
            if (pAccidentWrongfulAttribution < 0 || pAccidentWrongfulAttribution > 1)
                throw new ArgumentException(nameof(pAccidentWrongfulAttribution));
            if (epsilonForBenefit <= 0 || epsilonForBenefit >= 1)
                throw new ArgumentException(nameof(epsilonForBenefit));

            HiddenCount = precautionPowerLevels;
            PrecautionLevels = precautionLevels;
            TotalLevelsForRisk = precautionLevels + 1;

            PAccidentNoPrecaution = pAccidentNoPrecaution;
            _pMinLow = pMinLow;
            _pMinHigh = pMinHigh;
            _alphaLow = alphaLow;
            _alphaHigh = alphaHigh;
            UnitPrecautionCost = marginalPrecautionCost;
            HarmCost = harmCost;
            LiabilityThreshold = liabilityThreshold;
            PAccidentWrongfulAttribution = pAccidentWrongfulAttribution;

            _benefitRule = benefitRule;
            _epsilon = epsilonForBenefit;

            accidentProb = BuildAccidentProbTable();
            riskReduction = BuildRiskReductionTable();
            benefitCostRatio = BuildBenefitCostRatioTable();
            trueLiability = BuildTrueLiabilityTable();
            accidentProbMarginal = BuildAccidentProbMarginalTable();
            trueLiabilityProb = BuildTrueLiabilityProbTable();
        }

        // ------------- Public API ---------------------------------------------------

        double PMin(double p) => _pMinLow + (_pMinHigh - _pMinLow) * p;
        double Alpha(double p) => _alphaLow + (_alphaHigh - _alphaLow) * p;

        double CausedAccidentProbability(int h, double kContinuous)
        {
            double p = (h + 1.0) / (HiddenCount + 1.0);
            double t = kContinuous / PrecautionLevels;
            double pMin = PMin(p);
            double alpha = Alpha(p);
            return pMin + (PAccidentNoPrecaution - pMin) * Math.Pow(1.0 - t, alpha);
        }

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

        public double GetBenefitCostRatio(int hiddenIndex, int precautionLevel)
        {
            ValidateIndices(hiddenIndex, precautionLevel, allowHypothetical: false);
            return benefitCostRatio[hiddenIndex][precautionLevel];
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
            double sumWrongful = 0.0;
            double sumTotal = 0.0;

            for (int h = 0; h < HiddenCount; h++)
            {
                double pCaused = CausedAccidentProbability(h, precautionLevel);
                double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
                double pTotal = pCaused + pWrongful;

                sumWrongful += uniformPrior * pWrongful;
                sumTotal += uniformPrior * pTotal;
            }

            return sumTotal == 0.0 ? 0.0 : sumWrongful / sumTotal;
        }

        public double GetWrongfulAttributionProbabilityGivenHiddenState(int hiddenState, int precautionLevel)
        {
            ValidatePrecautionLevel(precautionLevel, allowHypothetical: false);
            if (hiddenState < 0 || hiddenState >= HiddenCount) throw new ArgumentOutOfRangeException(nameof(hiddenState));

            double pCaused = CausedAccidentProbability(hiddenState, precautionLevel);
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
                    double pCaused = CausedAccidentProbability(h, k);
                    double pWrongful = (1.0 - pCaused) * PAccidentWrongfulAttribution;
                    arr[h][k] = Math.Clamp(pCaused + pWrongful, 0.0, 1.0);
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
                    double pCurrent = accidentProb[h][k];
                    double delta;

                    if (_benefitRule == MarginalBenefitRule.AtChosenPrecautionLevel)
                    {
                        // forward ε step on the same continuous curve
                        double pPlusE = CausedAccidentProbability(h, k + _epsilon);
                        double pWrongful = (1.0 - pPlusE) * PAccidentWrongfulAttribution;
                        double pNext = Math.Clamp(pPlusE + pWrongful, 0.0, 1.0);

                        // scale by 1/ε ⇒ benefit of a *unit* of precaution, not an ε-sliver
                        delta = (pCurrent - pNext) / _epsilon;
                    }
                    else
                    {
                        // discrete forward one-level; top level ⇒ ΔP = 0
                        if (k == PrecautionLevels - 1)
                            delta = 0.0;
                        else
                            delta = pCurrent - accidentProb[h][k + 1];
                    }

                    arr[h][k] = delta < 0.0 ? 0.0 : delta;
                }
            }

            return arr;
        }


        double[][] BuildBenefitCostRatioTable()
        {
            var arr = new double[HiddenCount][];
            for (int h = 0; h < HiddenCount; h++)
            {
                arr[h] = new double[PrecautionLevels];
                for (int k = 0; k < PrecautionLevels; k++)
                {
                    double benefit = riskReduction[h][k] * HarmCost;
                    double ratio = benefit / UnitPrecautionCost;
                    arr[h][k] = ratio;
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
                    double ratio = benefit / UnitPrecautionCost;
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
