using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Builds the data needed for a plaintiff–defendant signal heat map where:
    ///  • Row height in each defendant-signal column is Pr(p | d, Accident, Strategy)
    ///  • Cell shading/value is      Pr(Liable | p, d, Accident, Strategy)
    /// Also provides:
    ///  • Pr(d, Accident | Strategy)    (column accident mass)
    ///  • Pr(d | Accident, Strategy)    (accident-conditioned defendant-signal probability)
    ///  • Pr(d)                         (unconditional defendant-signal probability under uniform hidden prior)
    ///  • S(k | h)                      (defendant’s mixed strategy over precaution by hidden state)
    ///  • Pr(k | d, Accident, Strategy) (accident-conditioned precaution mix per defendant signal)
    ///
    /// The strategy S(h) → dist over precaution levels k is supplied via the constructor,
    /// or derived from weighted game progresses.
    /// </summary>
    public sealed class PrecautionWinHeatmapDataBuilder
    {
        // Dependencies
        readonly PrecautionImpactModel impact;
        readonly PrecautionSignalModel signal;
        readonly PrecautionCourtDecisionModel court;

        // Dimensions
        readonly int H; // hidden states
        readonly int P; // plaintiff signals
        readonly int D; // defendant signals
        readonly int K; // precaution levels
        readonly int C; // court signals

        // Strategy: for hidden state h (0-based), return a length-K distribution over precaution levels.
        readonly Func<int, double[]> strategy;

        // ------------------------------ Constructors ------------------------------

        /// <summary>
        /// Provide an explicit mixed strategy S(h): returns a length-K nonnegative vector that sums to 1.
        /// </summary>
        public PrecautionWinHeatmapDataBuilder(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            PrecautionCourtDecisionModel courtModel,
            Func<int, double[]> mixedStrategyOverPrecautionLevelsGivenHidden)
        {
            impact = impactModel ?? throw new ArgumentNullException(nameof(impactModel));
            signal = signalModel ?? throw new ArgumentNullException(nameof(signalModel));
            court  = courtModel  ?? throw new ArgumentNullException(nameof(courtModel));

            if (impact.HiddenCount != signal.HiddenStatesCount)
                throw new ArgumentException("Hidden-state count mismatch between impact and signal models.");

            H = signal.HiddenStatesCount;
            P = signal.NumPSignals;
            D = signal.NumDSignals;
            K = impact.PrecautionLevels;
            C = signal.NumCSignals;

            strategy = mixedStrategyOverPrecautionLevelsGivenHidden
                       ?? throw new ArgumentNullException(nameof(mixedStrategyOverPrecautionLevelsGivenHidden));
        }

        /// <summary>
        /// Deterministic strategy convenience: choose a single precaution level k per hidden state h.
        /// </summary>
        public PrecautionWinHeatmapDataBuilder(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            PrecautionCourtDecisionModel courtModel,
            Func<int, int> choosePrecautionLevelGivenHidden)
            : this(
                impactModel, signalModel, courtModel,
                h =>
                {
                    if (choosePrecautionLevelGivenHidden == null)
                        throw new ArgumentNullException(nameof(choosePrecautionLevelGivenHidden));
                    var arr = new double[impactModel.PrecautionLevels];
                    int k = choosePrecautionLevelGivenHidden(h);
                    if ((uint)k >= impactModel.PrecautionLevels)
                        throw new ArgumentOutOfRangeException(nameof(choosePrecautionLevelGivenHidden), "Chosen k is out of range.");
                    arr[k] = 1.0;
                    return arr;
                })
        { }

        /// <summary>
        /// Convenience wrapper: directly build heat-map data from an explicit mixed strategy S(k | h).
        /// Supply a delegate that, given hidden state index h (0-based), returns a length-K array
        /// of nonnegative probabilities over precaution levels k that sums to 1.
        /// </summary>
        public static PrecautionWinHeatmapData BuildFromStrategy(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            PrecautionCourtDecisionModel courtModel,
            Func<int, double[]> mixedStrategyOverPrecautionLevelsGivenHidden)
        {
            var builder = new PrecautionWinHeatmapDataBuilder(
                impactModel,
                signalModel,
                courtModel,
                mixedStrategyOverPrecautionLevelsGivenHidden);
            return builder.Build();
        }

        /// <summary>
        /// Convenience wrapper: directly build heatmap data from weighted game progresses.
        /// This is equivalent to using the constructor that accepts progresses and then
        /// calling Build().
        /// </summary>
        public static PrecautionWinHeatmapData BuildFromProgresses(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            PrecautionCourtDecisionModel courtModel,
            List<(GameProgress theProgress, double weight)> gameProgresses)
        {
            var builder = new PrecautionWinHeatmapDataBuilder(
                impactModel,
                signalModel,
                courtModel,
                gameProgresses);
            return builder.Build();
        }

        /// <summary>
        /// Derive a mixed strategy S from weighted simulation outputs.
        /// For each progress record, we read:
        ///   - Hidden state h := LiabilityStrengthDiscrete - 1 (1-based → 0-based)
        ///   - Chosen precaution level k := RelativePrecautionLevel (assumed 0-based)
        /// We aggregate weights into S(h)[k] ∝ Σ weights over records with that (h,k).
        /// If a hidden state h has no mass, we fall back to uniform over k for that h.
        /// </summary>
        public PrecautionWinHeatmapDataBuilder(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            PrecautionCourtDecisionModel courtModel,
            List<(GameProgress theProgress, double weight)> gameProgresses)
            : this(
                impactModel, signalModel, courtModel,
                BuildStrategyFromProgresses(impactModel, signalModel, gameProgresses))
        { }

        static Func<int, double[]> BuildStrategyFromProgresses(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel,
            List<(GameProgress theProgress, double weight)> gameProgresses)
        {
            if (gameProgresses == null || gameProgresses.Count == 0)
            {
                // Uniform fallback across all h: S(h) = Uniform over K
                return h => Enumerable.Repeat(1.0 / impactModel.PrecautionLevels, impactModel.PrecautionLevels).ToArray();
            }

            int H = signalModel.HiddenStatesCount;
            int K = impactModel.PrecautionLevels;

            var mass = new double[H][];
            for (int h = 0; h < H; h++) mass[h] = new double[K];

            foreach (var (gp, w) in gameProgresses)
            {
                if (gp is not PrecautionNegligenceProgress p || w <= 0.0)
                    continue;

                int h = p.LiabilityStrengthDiscrete - 1; // stored as 1-based externally
                int k = p.RelativePrecautionLevel;       // already 0-based in progress
                if ((uint)h >= (uint)H || (uint)k >= (uint)K)
                    continue;

                mass[h][k] += w;
            }

            return h =>
            {
                if ((uint)h >= (uint)H)
                    throw new ArgumentOutOfRangeException(nameof(h));

                double sum = 0.0;
                for (int k = 0; k < K; k++) sum += mass[h][k];

                if (sum <= 0.0)
                {
                    // No observed mass for this hidden state → uniform over k
                    return Enumerable.Repeat(1.0 / K, K).ToArray();
                }

                var row = new double[K];
                for (int k = 0; k < K; k++) row[k] = mass[h][k] / sum;
                return row;
            };
        }

        // ------------------------------ Public API ------------------------------

        /// <summary>
        /// Builds all heat-map arrays from the strategy supplied in the constructor.
        /// </summary>
        public PrecautionWinHeatmapData Build()
        {
            // ------------------------------ Precompute signal/court likelihoods given hidden
            var pGivenH = new double[H][];
            var dGivenH = new double[H][];
            var cGivenH = new double[H][];
            for (int h = 0; h < H; h++)
            {
                pGivenH[h] = signal.GetPlaintiffSignalDistributionGivenHidden(h);
                dGivenH[h] = signal.GetDefendantSignalDistributionGivenHidden(h);
                cGivenH[h] = signal.GetCourtSignalDistributionGivenHidden(h);
            }

            // ------------------------------ Liability probability per (h,k):
            // L(h,k) = sum_c 1{liable(c,k)} * Pr(c | h)
            var liableGivenHAndK = new double[H][];
            for (int h = 0; h < H; h++)
            {
                var arr = new double[K];
                var courtDist = cGivenH[h];
                for (int k = 0; k < K; k++)
                {
                    double sum = 0.0;
                    for (int cSig = 0; cSig < C; cSig++)
                        if (court.IsLiable(cSig, k))
                            sum += courtDist[cSig];
                    arr[k] = sum;
                }
                liableGivenHAndK[h] = arr;
            }

            // ------------------------------ Strategy-weighted accident and liable masses per hidden:
            // α_h = Σ_k S(h)[k] * Pr(Accident | h,k)
            // β_h = Σ_k S(h)[k] * Pr(Accident | h,k) * L(h,k)
            var accidentMassGivenHidden = new double[H];
            var liableAccidentMassGivenHidden = new double[H];

            // Also collect a normalized snapshot of S(k | h) to include in the output.
            var strategyOverPrecautionGivenHidden = new double[H][];

            for (int h = 0; h < H; h++)
            {
                double[] s = strategy(h);
                if (s == null || s.Length != K)
                    throw new InvalidOperationException("Strategy must return a length-K array.");

                double sum = 0.0;
                for (int k = 0; k < K; k++)
                {
                    if (s[k] < 0.0)
                        throw new InvalidOperationException("Strategy probabilities must be nonnegative.");
                    sum += s[k];
                }
                if (sum <= 0.0)
                    throw new InvalidOperationException("Strategy must allocate positive total probability mass.");

                // normalise and store
                var S = new double[K];
                for (int k = 0; k < K; k++) S[k] = s[k] / sum;
                strategyOverPrecautionGivenHidden[h] = S;

                double alphaH = 0.0;
                double betaH  = 0.0;

                for (int k = 0; k < K; k++)
                {
                    double pAcc = impact.GetAccidentProbability(h, k);
                    double Lhk  = liableGivenHAndK[h][k];
                    alphaH += S[k] * pAcc;
                    betaH  += S[k] * pAcc * Lhk;
                }

                accidentMassGivenHidden[h] = alphaH;
                liableAccidentMassGivenHidden[h] = betaH;
            }

            // ------------------------------ Build D[p][d] and N[p][d]
            // D[p,d] = Σ_h prior * Pr(p|h) * Pr(d|h) * α_h
            // N[p,d] = Σ_h prior * Pr(p|h) * Pr(d|h) * β_h
            double priorHidden = 1.0 / H;

            var Dtable = new double[P][];
            var Ntable = new double[P][];
            for (int p = 0; p < P; p++)
            {
                Dtable[p] = new double[D];
                Ntable[p] = new double[D];
            }

            for (int h = 0; h < H; h++)
            {
                double alphaH = accidentMassGivenHidden[h];
                double betaH  = liableAccidentMassGivenHidden[h];
                if (alphaH == 0.0 && betaH == 0.0)
                    continue;

                var pRow = pGivenH[h];
                var dRow = dGivenH[h];

                var dFactors = new double[D];
                for (int dSig = 0; dSig < D; dSig++)
                    dFactors[dSig] = priorHidden * dRow[dSig];

                for (int pSig = 0; pSig < P; pSig++)
                {
                    double pWeight = pRow[pSig];
                    if (pWeight == 0.0) continue;

                    double[] Drow = Dtable[pSig];
                    double[] Nrow = Ntable[pSig];

                    for (int dSig = 0; dSig < D; dSig++)
                    {
                        double baseFactor = pWeight * dFactors[dSig];
                        Drow[dSig] += baseFactor * alphaH;
                        Nrow[dSig] += baseFactor * betaH;
                    }
                }
            }

            // ------------------------------ Convert to row heights and win probabilities
            var rowHeight = new double[P][];
            var winProb   = new double[P][];
            for (int p = 0; p < P; p++)
            {
                rowHeight[p] = new double[D];
                winProb[p]   = new double[D];
            }

            var columnAccidentMass = new double[D];
            var columnWinProb      = new double[D];

            for (int dSig = 0; dSig < D; dSig++)
            {
                double colD = 0.0, colN = 0.0;
                for (int pSig = 0; pSig < P; pSig++)
                {
                    colD += Dtable[pSig][dSig];
                    colN += Ntable[pSig][dSig];
                }
                columnAccidentMass[dSig] = colD;
                columnWinProb[dSig] = colD > 0.0 ? colN / colD : 0.0;

                for (int pSig = 0; pSig < P; pSig++)
                {
                    double Dpd = Dtable[pSig][dSig];
                    rowHeight[pSig][dSig] = colD > 0.0 ? Dpd / colD : 0.0;
                    winProb[pSig][dSig]   = Dpd > 0.0 ? (Ntable[pSig][dSig] / Dpd) : 0.0;
                }
            }

            double totalAccidentMassAcrossAllDefendantSignals = 0.0;
            double totalLiableAccidentMassAcrossAllDefendantSignals = 0.0;
            for (int dSig = 0; dSig < D; dSig++)
            {
                totalAccidentMassAcrossAllDefendantSignals += columnAccidentMass[dSig];
                for (int pSig = 0; pSig < P; pSig++)
                    totalLiableAccidentMassAcrossAllDefendantSignals += Ntable[pSig][dSig];
            }

            double overallWinGivenAccident = totalAccidentMassAcrossAllDefendantSignals > 0.0
                ? totalLiableAccidentMassAcrossAllDefendantSignals / totalAccidentMassAcrossAllDefendantSignals
                : 0.0;

            // ------------------------------ Defendant-signal probabilities

            // Accident-conditioned: Pr(d | Accident, Strategy) = ColumnAccidentMass[d] / Sum_d ColumnAccidentMass[d]
            var defendantSignalProbabilityGivenAccident = new double[D];
            if (totalAccidentMassAcrossAllDefendantSignals > 0.0)
            {
                for (int dSig = 0; dSig < D; dSig++)
                    defendantSignalProbabilityGivenAccident[dSig] = columnAccidentMass[dSig] / totalAccidentMassAcrossAllDefendantSignals;
            }
            else
            {
                for (int dSig = 0; dSig < D; dSig++)
                    defendantSignalProbabilityGivenAccident[dSig] = 0.0;
            }

            // Unconditional (strategy-independent): Pr(d) = Σ_h (1/H) * Pr(d | h)
            var defendantSignalUnconditionalProbability = new double[D];
            for (int dSig = 0; dSig < D; dSig++)
            {
                double sum = 0.0;
                for (int h = 0; h < H; h++)
                    sum += (1.0 / H) * dGivenH[h][dSig];
                defendantSignalUnconditionalProbability[dSig] = sum;
            }

            // ------------------------------ Accident-conditioned precaution mix per defendant signal
            // Pr(k | d, Accident, S) ∝ Σ_h (1/H) * Pr(d | h) * S(k | h) * Pr(Accident | h,k)
            var precautionLevelProbabilityGivenDefSignal_Accident = new double[D][];
            for (int dSig = 0; dSig < D; dSig++)
            {
                var numer = new double[K];
                for (int k = 0; k < K; k++)
                {
                    double sum = 0.0;
                    for (int h = 0; h < H; h++)
                    {
                        double priorH = 1.0 / H;
                        double dLike = dGivenH[h][dSig];
                        double Skh = strategyOverPrecautionGivenHidden[h][k];
                        double pAcc = impact.GetAccidentProbability(h, k);
                        sum += priorH * dLike * Skh * pAcc;
                    }
                    numer[k] = sum;
                }
                double denom = numer.Sum();
                if (denom > 0.0)
                {
                    precautionLevelProbabilityGivenDefSignal_Accident[dSig] = numer.Select(x => x / denom).ToArray();
                }
                else
                {
                    precautionLevelProbabilityGivenDefSignal_Accident[dSig] = new double[K];
                }
            }

            return new PrecautionWinHeatmapData(
                rowHeight,
                winProb,
                columnAccidentMass,
                columnWinProb,
                overallWinGivenAccident,
                Dtable,
                Ntable,
                defendantSignalProbabilityGivenAccident,
                defendantSignalUnconditionalProbability,
                strategyOverPrecautionGivenHidden,
                precautionLevelProbabilityGivenDefSignal_Accident);
        }
    }

    /// <summary>
    /// Output container for heat-map data and helpful aggregates.
    /// Arrays are sized as:
    ///   • [P][D] for per-cell quantities (P rows = plaintiff signals, D columns = defendant signals)
    ///   • [D]    for per-column aggregates (defendant signals)
    ///   • [H] or [K] for hidden/precaution strategy tables as noted.
    /// </summary>
    public sealed class PrecautionWinHeatmapData
    {
        public double[][] ProbabilityPlaintiffSignalGivenDefendantSignal_Accident_RowHeight { get; }
        public double[][] ProbabilityOfLiabilityGivenSignals_Accident_CellValue { get; }

        public double[] ColumnAccidentMass { get; }
        public double[] ColumnWinProbabilityGivenAccident { get; }
        public double   OverallWinProbabilityGivenAccident { get; }

        // Defendant-signal probabilities
        public double[] DefendantSignalProbabilityGivenAccident { get; }
        public double[] DefendantSignalUnconditionalProbability { get; }

        // Strategy snapshots
        public double[][] StrategyOverPrecautionGivenHidden { get; }                 // [H][K] = S(k | h)
        public double[][] PrecautionLevelProbabilityGivenDefendantSignal_Accident { get; } // [D][K] = Pr(k | d, Accident, S)

        // Raw joint masses (useful for tooltips or alternative normalisations)
        public double[][] RawJointAccidentMass_D { get; }
        public double[][] RawJointLiableAccidentMass_N { get; }

        public PrecautionWinHeatmapData(
            double[][] rowHeightPGivenD_Accident,
            double[][] winProbGivenSignals_Accident,
            double[]   columnAccidentMass,
            double[]   columnWinProbabilityGivenAccident,
            double     overallWinProbabilityGivenAccident,
            double[][] rawJointAccidentMass_D,
            double[][] rawJointLiableAccidentMass_N,
            double[]   defendantSignalProbabilityGivenAccident,
            double[]   defendantSignalUnconditionalProbability,
            double[][] strategyOverPrecautionGivenHidden,
            double[][] precautionLevelProbabilityGivenDefendantSignal_Accident)
        {
            ProbabilityPlaintiffSignalGivenDefendantSignal_Accident_RowHeight = rowHeightPGivenD_Accident;
            ProbabilityOfLiabilityGivenSignals_Accident_CellValue = winProbGivenSignals_Accident;

            ColumnAccidentMass = columnAccidentMass;
            ColumnWinProbabilityGivenAccident = columnWinProbabilityGivenAccident;
            OverallWinProbabilityGivenAccident = overallWinProbabilityGivenAccident;

            RawJointAccidentMass_D = rawJointAccidentMass_D;
            RawJointLiableAccidentMass_N = rawJointLiableAccidentMass_N;

            DefendantSignalProbabilityGivenAccident = defendantSignalProbabilityGivenAccident;
            DefendantSignalUnconditionalProbability = defendantSignalUnconditionalProbability;

            StrategyOverPrecautionGivenHidden = strategyOverPrecautionGivenHidden;
            PrecautionLevelProbabilityGivenDefendantSignal_Accident = precautionLevelProbabilityGivenDefendantSignal_Accident;
        }
    }
}
