using System;
using System.Linq;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Determines liability and related probabilities.  All heavy work is done
    /// once in the constructor; public members read from cached tables.
    /// </summary>
    public sealed class PrecautionCourtDecisionModel
    {
        // ------------------------------------------------------------------  refs
        readonly PrecautionImpactModel impact;
        readonly PrecautionSignalModel signal;

        // ------------------------------------------------------------------  dims
        readonly int C;   // court-signal count
        readonly int K;   // precaution levels
        readonly int P;   // plaintiff signals
        readonly int D;   // defendant signals
        readonly int H;   // hidden states

        // ------------------------------------------------------------------  core tables
        readonly double[][] expRiskReduction;   // [c][k]
        readonly double[][] expBenefit;         // [c][k]
        readonly double[][] benefitCostRatio;   // [c][k]
        readonly bool[][] liable;             // [c][k]
        readonly double[][] liableProbGivenHidden; // [h][k]

        // ------------------------------------------------------------------  extended tables
        readonly double[][][][] courtDistLiable;        // [p][d][k][c]
        readonly double[][][][] courtDistNoLiable;      // [p][d][k][c]

        readonly double[][][][] hiddenPostAccLiable;    // [p][d][k][h]
        readonly double[][][][] hiddenPostAccNoLiable;  // [p][d][k][h]
        readonly double[][][] hiddenPostNoAccident;   // [d][k][h]
        readonly double[][] hiddenPostDefSignal;    // [d][h]

        // Exposed read-only properties (needed by generator)
        public double[][][][] CourtSignalDistGivenLiabilityTable => courtDistLiable;
        public double[][][][] CourtSignalDistGivenNoLiabilityTable => courtDistNoLiable;
        public double[][][][] HiddenPosteriorAccidentLiabilityTable => hiddenPostAccLiable;
        public double[][][][] HiddenPosteriorAccidentNoLiabilityTable => hiddenPostAccNoLiable;
        public double[][][] HiddenPosteriorNoAccidentTable => hiddenPostNoAccident;
        public double[][] HiddenPosteriorDefendantSignalTable => hiddenPostDefSignal;

        // ==================================================================  ctor
        public PrecautionCourtDecisionModel(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel)
        {
            impact = impactModel ?? throw new ArgumentNullException(nameof(impactModel));
            signal = signalModel ?? throw new ArgumentNullException(nameof(signalModel));

            C = signal.NumCSignals;
            K = impact.PrecautionLevels;
            P = signal.NumPSignals;
            D = signal.NumDSignals;
            H = signal.HiddenStatesCount;

            expRiskReduction = new double[C][];
            expBenefit = new double[C][];
            benefitCostRatio = new double[C][];
            liable = new bool[C][];

            BuildBenefitAndLiabilityTables();
            liableProbGivenHidden = BuildLiableProbTable();

            courtDistLiable = BuildCourtSignalConditionalTables(liableWanted: true);
            courtDistNoLiable = BuildCourtSignalConditionalTables(liableWanted: false);

            hiddenPostAccLiable = BuildHiddenPosteriorFromCourtDistTable(courtDistLiable);
            hiddenPostAccNoLiable = BuildHiddenPosteriorFromCourtDistTable(courtDistNoLiable);
            hiddenPostNoAccident = BuildHiddenPosteriorNoAccidentTable();
            hiddenPostDefSignal = BuildHiddenPosteriorDefSignalTable();
        }

        // ==================================================================  public deterministic metrics
        public double GetExpectedBenefit(int courtSignal, int precautionLevel)
        {
            Validate(courtSignal, precautionLevel);
            return expBenefit[courtSignal][precautionLevel];
        }

        public double GetBenefitCostRatio(int courtSignal, int precautionLevel)
        {
            Validate(courtSignal, precautionLevel);
            return benefitCostRatio[courtSignal][precautionLevel];
        }

        public bool IsLiable(int courtSignal, int precautionLevel)
        {
            Validate(courtSignal, precautionLevel);
            return liable[courtSignal][precautionLevel];
        }

        // ==================================================================  public conditional court-signal dists
        public double[] GetCourtSignalDistributionGivenSignalsAndLiability(int pSig, int dSig, int precautionLevel)
        {
            ValidatePdK(pSig, dSig, precautionLevel);
            return courtDistLiable[pSig][dSig][precautionLevel];
        }

        public double[] GetCourtSignalDistributionGivenSignalsAndNoLiability(int pSig, int dSig, int precautionLevel)
        {
            ValidatePdK(pSig, dSig, precautionLevel);
            return courtDistNoLiable[pSig][dSig][precautionLevel];
        }

        // ==================================================================  public outcome probs
        public double[] GetLiabilityOutcomeProbabilities(int hiddenState, int precautionLevel)
            => new[] { 1.0 - liableProbGivenHidden[hiddenState][precautionLevel],
                        liableProbGivenHidden[hiddenState][precautionLevel] };

        public double[] GetLiabilityOutcomeProbabilities(int pSig, int dSig, bool accident, int precautionLevel)
        {
            ValidatePdK(pSig, dSig, precautionLevel);

            double uniformPrior = 1.0 / H;
            double[] posterior = new double[H];
            double total = 0.0;

            for (int h = 0; h < H; h++)
            {
                double w = uniformPrior *
                           signal.GetPlaintiffSignalProbability(h, pSig) *
                           signal.GetDefendantSignalProbability(h, dSig);

                double pAcc = impact.GetAccidentProbability(h, precautionLevel);
                w *= accident ? pAcc : 1.0 - pAcc;

                posterior[h] = w;
                total += w;
            }
            if (total == 0.0) return new[] { 0.5, 0.5 };
            for (int h = 0; h < H; h++) posterior[h] /= total;

            double liableProb = 0.0;
            for (int h = 0; h < H; h++)
                liableProb += posterior[h] * liableProbGivenHidden[h][precautionLevel];

            return new[] { 1.0 - liableProb, liableProb };
        }

        /// <summary>
        /// Posterior P(hidden | signals & path).
        /// If <paramref name="accidentOccurred"/> is false the path skipped court;
        /// if true and <paramref name="wasLiable"/> is null, the court verdict is unknown
        /// (e.g., settlement), so we mix the liable/no-liable posteriors by their
        /// probability.  All cases are served from cached tables—no new integration.
        /// </summary>
        public double[] GetHiddenPosteriorFromPath(
            int plaintiffSignal,
            int defendantSignal,
            bool accidentOccurred,
            int precautionLevel,
            bool? wasLiable)
        {
            ValidatePdK(plaintiffSignal, defendantSignal, precautionLevel);

            // ---------------- no accident occurred ----------------------------------
            if (!accidentOccurred)
                return hiddenPostNoAccident[defendantSignal][precautionLevel];

            // ---------------- accident + verdict known ------------------------------
            if (wasLiable.HasValue)
                return wasLiable.Value
                    ? hiddenPostAccLiable[plaintiffSignal][defendantSignal][precautionLevel]
                    : hiddenPostAccNoLiable[plaintiffSignal][defendantSignal][precautionLevel];

            // ---------------- accident + verdict unknown (e.g., settlement) ---------
            double[] liabPost = hiddenPostAccLiable[plaintiffSignal][defendantSignal][precautionLevel];
            double[] nLiabPost = hiddenPostAccNoLiable[plaintiffSignal][defendantSignal][precautionLevel];

            double liableProb = GetLiabilityOutcomeProbabilities(
                                    plaintiffSignal, defendantSignal, true, precautionLevel)[1];

            var mix = new double[liabPost.Length];
            for (int h = 0; h < mix.Length; h++)
                mix[h] = liableProb * liabPost[h] + (1.0 - liableProb) * nLiabPost[h];
            return mix;
        }


        // ==================================================================  internal core builders
        void BuildBenefitAndLiabilityTables()
        {
            for (int cSig = 0; cSig < C; cSig++)
            {
                double[] postH = signal.GetHiddenPosteriorFromCourtSignal(cSig);
                expRiskReduction[cSig] = new double[K];
                expBenefit[cSig] = new double[K];
                benefitCostRatio[cSig] = new double[K];
                liable[cSig] = new bool[K];

                for (int k = 0; k < K; k++)
                {
                    double delta = 0.0;
                    for (int h = 0; h < H; h++)
                        delta += impact.GetRiskReduction(h, k) * postH[h];

                    expRiskReduction[cSig][k] = delta;
                    double benefit = delta * impact.HarmCost;
                    expBenefit[cSig][k] = benefit;

                    double ratio = impact.MarginalPrecautionCost == 0
                                   ? double.PositiveInfinity
                                   : benefit / impact.MarginalPrecautionCost;

                    benefitCostRatio[cSig][k] = ratio;
                    liable[cSig][k] = ratio >= impact.LiabilityThreshold;
                }
            }
        }

        double[][] BuildLiableProbTable()
        {
            var table = new double[H][];
            for (int h = 0; h < H; h++)
            {
                table[h] = new double[K];
                double[] courtGivenH = signal.GetCourtSignalDistributionGivenHidden(h);

                for (int k = 0; k < K; k++)
                    for (int cSig = 0; cSig < C; cSig++)
                        if (liable[cSig][k])
                            table[h][k] += courtGivenH[cSig];
            }
            return table;
        }

        double[][][][] BuildCourtSignalConditionalTables(bool liableWanted)
        {
            var table = new double[P][][][];
            for (int p = 0; p < P; p++)
            {
                table[p] = new double[D][][];
                for (int d = 0; d < D; d++)
                {
                    table[p][d] = new double[K][];
                    double[] baseDist = signal.GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(p, d);

                    for (int k = 0; k < K; k++)
                    {
                        double[] slice = new double[C];
                        double tot = 0.0;
                        for (int cSig = 0; cSig < C; cSig++)
                        {
                            bool cond = liable[cSig][k];
                            if (cond == liableWanted)
                            {
                                slice[cSig] = baseDist[cSig];
                                tot += slice[cSig];
                            }
                        }
                        if (tot == 0.0) table[p][d][k] = slice;          // impossible path
                        else
                        {
                            for (int cSig = 0; cSig < C; cSig++)
                                slice[cSig] /= tot;
                            table[p][d][k] = slice;
                        }
                    }
                }
            }
            return table;
        }

        double[][][][] BuildHiddenPosteriorFromCourtDistTable(double[][][][] courtDistTable)
        {
            var table = new double[P][][][];
            for (int p = 0; p < P; p++)
            {
                table[p] = new double[D][][];
                for (int d = 0; d < D; d++)
                {
                    table[p][d] = new double[K][];
                    for (int k = 0; k < K; k++)
                    {
                        double[] courtDist = courtDistTable[p][d][k];
                        table[p][d][k] = GetHiddenPosteriorFromSignalsAndCourtDistribution(p, d, courtDist);
                    }
                }
            }
            return table;
        }

        public double[] GetHiddenPosteriorFromSignalsAndCourtDistribution(int pSig, int dSig, double[] courtDist)
        {
            double uniformPrior = 1.0 / H;
            double[] unnorm = new double[H];
            double total = 0.0;

            for (int h = 0; h < H; h++)
            {
                double w = uniformPrior *
                           signal.GetPlaintiffSignalProbability(h, pSig) *
                           signal.GetDefendantSignalProbability(h, dSig);

                double likelihood = 0.0;
                double[] courtGivenH = signal.GetCourtSignalDistributionGivenHidden(h);
                for (int c = 0; c < C; c++)
                    likelihood += courtGivenH[c] * courtDist[c];

                unnorm[h] = w * likelihood;
                total += unnorm[h];
            }
            if (total == 0.0) return unnorm;
            for (int h = 0; h < H; h++) unnorm[h] /= total;
            return unnorm;
        }

        double[][][] BuildHiddenPosteriorNoAccidentTable()
        {
            var table = new double[D][][];   // [d][k][h]
            for (int d = 0; d < D; d++)
            {
                table[d] = new double[K][];
                for (int k = 0; k < K; k++)
                    table[d][k] = GetHiddenPosteriorFromNoAccidentScenario(d, k);
            }
            return table;
        }

        public double[] GetHiddenPosteriorFromNoAccidentScenario(int dSig, int precautionLevel)
        {
            double[] posterior = new double[H];
            double total = 0.0;
            double uniformPrior = 1.0 / H;

            for (int h = 0; h < H; h++)
            {
                double w = uniformPrior *
                           signal.GetDefendantSignalProbability(h, dSig) *
                           (1.0 - impact.GetAccidentProbability(h, precautionLevel));

                posterior[h] = w;
                total += w;
            }
            if (total == 0.0) return posterior;
            for (int h = 0; h < H; h++) posterior[h] /= total;
            return posterior;
        }

        double[][] BuildHiddenPosteriorDefSignalTable()
        {
            var table = new double[D][];
            for (int d = 0; d < D; d++)
                table[d] = GetHiddenPosteriorFromDefendantSignal(d);
            return table;
        }

        public double[] GetHiddenPosteriorFromDefendantSignal(int dSig)
        {
            double[] posterior = new double[H];
            double total = 0.0;
            double uniformPrior = 1.0 / H;

            for (int h = 0; h < H; h++)
            {
                double w = uniformPrior * signal.GetDefendantSignalProbability(h, dSig);
                posterior[h] = w;
                total += w;
            }
            if (total == 0.0) return posterior;
            for (int h = 0; h < H; h++) posterior[h] /= total;
            return posterior;
        }

        // ==================================================================  validation helpers
        void Validate(int cSig, int k)
        {
            if ((uint)cSig >= C) throw new ArgumentOutOfRangeException(nameof(cSig));
            if ((uint)k >= K) throw new ArgumentOutOfRangeException(nameof(k));
        }
        void ValidatePdK(int p, int d, int k)
        {
            if ((uint)p >= P) throw new ArgumentOutOfRangeException(nameof(p));
            if ((uint)d >= D) throw new ArgumentOutOfRangeException(nameof(d));
            if ((uint)k >= K) throw new ArgumentOutOfRangeException(nameof(k));
        }
    }
}
