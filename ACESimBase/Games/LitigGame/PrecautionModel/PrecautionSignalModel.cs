using System;
using System.Diagnostics;
using System.Linq;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Domain-specific wrapper around <see cref="ThreePartyDiscreteSignals"/> that labels the three parties
    /// as Plaintiff (0), Defendant (1), and Court (2) for use in litigation simulations.
    /// </summary>
    public sealed class PrecautionSignalModel
    {
        // Party indices for readability
        public const int PlaintiffIndex = 0;
        public const int DefendantIndex = 1;
        public const int CourtIndex = 2;

        public readonly ThreePartyDiscreteSignals model;

        public int HiddenStatesCount => model.hiddenCount;

        public int NumPSignals => model.signalCounts[PlaintiffIndex];
        public int NumDSignals => model.signalCounts[DefendantIndex];
        public int NumCSignals => model.signalCounts[CourtIndex];

        public PrecautionSignalModel(
            int numPrecautionPowerLevels,
            int numPlaintiffSignals,
            int numDefendantSignals,
            int numCourtSignals,
            double sigmaPlaintiff,
            double sigmaDefendant,
            double sigmaCourt,
            bool includeExtremes = true)
        {
            int[] signalCounts = { numPlaintiffSignals, numDefendantSignals, numCourtSignals };
            double[] sigmas = { sigmaPlaintiff, sigmaDefendant, sigmaCourt };
            model = new ThreePartyDiscreteSignals(numPrecautionPowerLevels, signalCounts, sigmas, includeExtremes);
        }

        // === Generation ====================================================================

        public (int plaintiffSignal, int defendantSignal, int courtSignal) GenerateSignals(
            int hiddenValue,
            Random rng = null)
        {
            var (s0, s1, s2) = model.GenerateSignalsFromHidden(hiddenValue, rng);
            return (s0, s1, s2);
        }

        // === Hidden posteriors – scalar variants ===========================================

        public double[] GetHiddenPosteriorFromPlaintiffSignal(int plaintiffSignal) =>
            model.GetHiddenDistributionGivenSignal(PlaintiffIndex, plaintiffSignal);

        public double[] GetHiddenPosteriorFromDefendantSignal(int defendantSignal) =>
            model.GetHiddenDistributionGivenSignal(DefendantIndex, defendantSignal);

        public double[] GetHiddenPosteriorFromCourtSignal(int courtSignal) =>
            model.GetHiddenDistributionGivenSignal(CourtIndex, courtSignal);

        public double[] GetHiddenPosteriorFromPlaintiffAndDefendantSignals(
            int plaintiffSignal,
            int defendantSignal) =>
            model.GetHiddenDistributionGivenTwoSignals(
                PlaintiffIndex, plaintiffSignal,
                DefendantIndex, defendantSignal);

        // === Hidden posteriors – lookup-table overloads =====================================

        public double[][] GetHiddenPosteriorFromPlaintiffSignal()
        {
            var table = new double[NumPSignals][];
            for (int p = 0; p < NumPSignals; p++)
                table[p] = GetHiddenPosteriorFromPlaintiffSignal(p);
            return table;
        }

        public double[][] GetHiddenPosteriorFromDefendantSignal()
        {
            var table = new double[NumDSignals][];
            for (int d = 0; d < NumDSignals; d++)
                table[d] = GetHiddenPosteriorFromDefendantSignal(d);
            return table;
        }

        public double[][] GetHiddenPosteriorFromCourtSignal()
        {
            var table = new double[NumCSignals][];
            for (int c = 0; c < NumCSignals; c++)
                table[c] = GetHiddenPosteriorFromCourtSignal(c);
            return table;
        }

        /// <summary>
        /// Lookup-table for P(hidden | plaintiffSignal, defendantSignal) over all combinations.
        /// First index → plaintiffSignal, second → defendantSignal.
        /// </summary>
        public double[][][] GetHiddenPosteriorFromPlaintiffAndDefendantSignals()
        {
            var table = new double[NumPSignals][][];
            for (int p = 0; p < NumPSignals; p++)
            {
                table[p] = new double[NumDSignals][];
                for (int d = 0; d < NumDSignals; d++)
                    table[p][d] = GetHiddenPosteriorFromPlaintiffAndDefendantSignals(p, d);
            }
            return table;
        }



        // === Conditional signal distributions – scalar variants ============================

        public double[] GetPlaintiffSignalDistributionGivenDefendantSignal(int defendantSignal) =>
            model.GetSignalDistributionGivenSignal(PlaintiffIndex, DefendantIndex, defendantSignal);

        public double[] GetPlaintiffSignalDistributionGivenCourtSignal(int courtSignal) =>
            model.GetSignalDistributionGivenSignal(PlaintiffIndex, CourtIndex, courtSignal);

        public double[] GetDefendantSignalDistributionGivenPlaintiffSignal(int plaintiffSignal) =>
            model.GetSignalDistributionGivenSignal(DefendantIndex, PlaintiffIndex, plaintiffSignal);

        public double[] GetDefendantSignalDistributionGivenCourtSignal(int courtSignal) =>
            model.GetSignalDistributionGivenSignal(DefendantIndex, CourtIndex, courtSignal);

        public double[] GetCourtSignalDistributionGivenPlaintiffSignal(int plaintiffSignal) =>
            model.GetSignalDistributionGivenSignal(CourtIndex, PlaintiffIndex, plaintiffSignal);

        public double[] GetCourtSignalDistributionGivenDefendantSignal(int defendantSignal) =>
            model.GetSignalDistributionGivenSignal(CourtIndex, DefendantIndex, defendantSignal);

        public double[] GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(
            int plaintiffSignal,
            int defendantSignal) =>
            model.GetSignalDistributionGivenTwoSignals(
                targetPartyIndex: CourtIndex,
                givenPartyIndex1: PlaintiffIndex, givenSignalValue1: plaintiffSignal,
                givenPartyIndex2: DefendantIndex, givenSignalValue2: defendantSignal);

        public double[] GetCourtSignalDistributionGivenHidden(int hiddenIndex) =>
            model.GetSignalDistributionGivenHidden(CourtIndex, hiddenIndex);

        // === Conditional signal distributions – lookup-table overloads ======================

        public double[][] BuildPlaintiffSignalDistributionGivenDefendantSignal()
        {
            var table = new double[NumDSignals][];
            for (int d = 0; d < NumDSignals; d++)
                table[d] = GetPlaintiffSignalDistributionGivenDefendantSignal(d);
            return table;
        }

        public double[][] GetPlaintiffSignalDistributionGivenCourtSignal()
        {
            var table = new double[NumCSignals][];
            for (int c = 0; c < NumCSignals; c++)
                table[c] = GetPlaintiffSignalDistributionGivenCourtSignal(c);
            return table;
        }

        public double[][] GetDefendantSignalDistributionGivenPlaintiffSignal()
        {
            var table = new double[NumPSignals][];
            for (int p = 0; p < NumPSignals; p++)
                table[p] = GetDefendantSignalDistributionGivenPlaintiffSignal(p);
            return table;
        }

        public double[][] GetDefendantSignalDistributionGivenCourtSignal()
        {
            var table = new double[NumCSignals][];
            for (int c = 0; c < NumCSignals; c++)
                table[c] = GetDefendantSignalDistributionGivenCourtSignal(c);
            return table;
        }

        public double[][] GetCourtSignalDistributionGivenPlaintiffSignal()
        {
            var table = new double[NumPSignals][];
            for (int p = 0; p < NumPSignals; p++)
                table[p] = GetCourtSignalDistributionGivenPlaintiffSignal(p);
            return table;
        }

        public double[][] GetCourtSignalDistributionGivenDefendantSignal()
        {
            var table = new double[NumDSignals][];
            for (int d = 0; d < NumDSignals; d++)
                table[d] = GetCourtSignalDistributionGivenDefendantSignal(d);
            return table;
        }

        /// <summary>
        /// Lookup-table for P(courtSignal | plaintiffSignal, defendantSignal) over all combinations.
        /// First index → plaintiffSignal, second → defendantSignal.
        /// </summary>
        public double[][][] GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals()
        {
            var table = new double[NumPSignals][][];
            for (int p = 0; p < NumPSignals; p++)
            {
                table[p] = new double[NumDSignals][];
                for (int d = 0; d < NumDSignals; d++)
                    table[p][d] = GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(p, d);
            }
            return table;
        }

        public double[][] GetCourtSignalDistributionGivenHidden()
        {
            var table = new double[HiddenStatesCount][];
            for (int h = 0; h < HiddenStatesCount; h++)
                table[h] = GetCourtSignalDistributionGivenHidden(h);
            return table;
        }

        // === Unconditional signal distributions – scalar variants ============================

        /// <summary>
        /// P(plaintiffSignal), P(defendantSignal), or P(courtSignal) – unconditional
        /// on hidden state. Wrapper around ThreePartyDiscreteSignals.
        /// </summary>
        public double[] GetUnconditionalSignalDistribution(int partyIndex) =>
            model.GetUnconditionalSignalDistribution(partyIndex);

        // Convenience shortcuts
        public double[] GetUnconditionalPlaintiffSignalDistribution() =>
            GetUnconditionalSignalDistribution(PlaintiffIndex);

        public double[] GetUnconditionalDefendantSignalDistribution() =>
            GetUnconditionalSignalDistribution(DefendantIndex);

        public double[] GetUnconditionalCourtSignalDistribution() =>
            GetUnconditionalSignalDistribution(CourtIndex);


        // === Unconditional calculation of plaintiff signal probability

        /// <summary>
        /// Fully enumerated distribution P(plaintiffSignal | hidden state).
        /// Entry <c>p</c> is GetPlaintiffSignalProbability(hidden, p).
        /// </summary>
        public double[] GetPlaintiffSignalDistributionGivenHidden(int hidden)
        {
            if (hidden < 0 || hidden >= HiddenStatesCount)
                throw new ArgumentOutOfRangeException(nameof(hidden));

            var dist = new double[NumPSignals];
            for (int p = 0; p < NumPSignals; p++)
                dist[p] = GetPlaintiffSignalProbability(hidden, p);
            return dist;
        }

        /// <summary>
        /// Build and return the entire lookup table
        ///     table[h][p] = P(plaintiffSignal = p | hidden = h).
        /// Each row is produced by <see cref="GetPlaintiffSignalDistributionGivenHidden"/>.
        /// </summary>
        public double[][] BuildPlaintiffSignalGivenHiddenTable()
        {
            var table = new double[HiddenStatesCount][];

            for (int h = 0; h < HiddenStatesCount; h++)
                table[h] = GetPlaintiffSignalDistributionGivenHidden(h);

            return table;
        }


        /// <summary>
        /// Mixture distribution P(plaintiffSignal | caller’s posterior over hidden states).
        /// The posterior must be length = HiddenStatesCount and sum to 1.
        /// </summary>
        public double[] GetPlaintiffSignalDistributionGivenPosterior(double[] hiddenPosterior)
        {
            if (hiddenPosterior == null)
                throw new ArgumentNullException(nameof(hiddenPosterior));
            if (hiddenPosterior.Length != HiddenStatesCount)
                throw new ArgumentException("Posterior length must equal HiddenStatesCount.", nameof(hiddenPosterior));

            var dist = new double[NumPSignals];

            for (int h = 0; h < HiddenStatesCount; h++)
            {
                double w = hiddenPosterior[h];
                if (w == 0.0) continue;

                for (int p = 0; p < NumPSignals; p++)
                    dist[p] += w * GetPlaintiffSignalProbability(h, p);
            }

            // numerical guard: renormalise
            double sum = dist.Sum();
            if (sum == 0.0) return dist;          // impossible evidence path
            for (int p = 0; p < NumPSignals; p++)
                dist[p] /= sum;
            return dist;
        }


        // === Model taking into account accident occurrence ============================

        /// P(plaintiffSignal | defendantSignal, accident, precautionLevel)
        public double[] GetPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelAfterAccident(
            int defendantSignal,
            int precautionLevel,
            PrecautionImpactModel impactModel)
        {
            if (impactModel == null) throw new ArgumentNullException(nameof(impactModel));
            if ((uint)defendantSignal >= NumDSignals) throw new ArgumentOutOfRangeException(nameof(defendantSignal));
            if ((uint)precautionLevel >= impactModel.PrecautionLevels) throw new ArgumentOutOfRangeException(nameof(precautionLevel));

            int hCount = HiddenStatesCount;
            int pCount = NumPSignals;
            double uniformPrior = 1.0 / hCount;

            double[] numerators = new double[pCount];
            double denominator = 0.0;

            for (int h = 0; h < hCount; h++)
            {
                double weight =
                    uniformPrior *
                    GetDefendantSignalProbability(h, defendantSignal) *
                    impactModel.GetAccidentProbability(h, precautionLevel);

                if (weight == 0.0) continue;

                double[] pSigGivenH = model.GetSignalDistributionGivenHidden(PlaintiffIndex, h); // length pCount
                for (int p = 0; p < pCount; p++)
                    numerators[p] += weight * pSigGivenH[p];

                denominator += weight;
            }

            if (denominator == 0.0) return numerators;           // unreachable combination ⇒ all zeros

            for (int p = 0; p < pCount; p++)                     // normalise
                numerators[p] /= denominator;

            return numerators;
        }

        public double[][][] BuildPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelAfterAccidentTable(PrecautionImpactModel impactModel)
        {
            if (impactModel is null) throw new ArgumentNullException(nameof(impactModel));
            int precautionLevels = impactModel.PrecautionLevels;
            var table = new double[NumDSignals][][];
            for (int d = 0; d < NumDSignals; d++)
            {
                table[d] = new double[precautionLevels][];
                for (int k = 0; k < precautionLevels; k++)
                    table[d][k] = GetPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelAfterAccident(
                        d, k, impactModel);
            }
            return table;
        }

        /// Returns P(plaintiffSignal | defendantSignal, precautionLevel, NO accident).
        public double[] GetPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelNoAccident(
            int defendantSignal,
            int precautionLevel,
            PrecautionImpactModel impactModel)
        {
            if (impactModel == null) throw new ArgumentNullException(nameof(impactModel));
            if ((uint)defendantSignal >= NumDSignals) throw new ArgumentOutOfRangeException(nameof(defendantSignal));
            if ((uint)precautionLevel >= impactModel.PrecautionLevels) throw new ArgumentOutOfRangeException(nameof(precautionLevel));

            int hCount = HiddenStatesCount;
            int pCount = NumPSignals;
            double uniformPrior = 1.0 / hCount;

            double[] numerators = new double[pCount];
            double denominator = 0.0;

            for (int h = 0; h < hCount; h++)
            {
                // posterior weight ∝ P(h) · P(DLS | h) · P(NO accident | h,k)
                double pAcc = impactModel.GetAccidentProbability(h, precautionLevel);
                double weight =
                    uniformPrior *
                    GetDefendantSignalProbability(h, defendantSignal) *
                    (1.0 - pAcc);

                if (weight == 0.0) continue;

                double[] pSigGivenH = model.GetSignalDistributionGivenHidden(PlaintiffIndex, h);
                for (int p = 0; p < pCount; p++)
                    numerators[p] += weight * pSigGivenH[p];

                denominator += weight;
            }

            if (denominator == 0.0) return numerators;   // unreachable evidence ⇒ all zeros

            for (int p = 0; p < pCount; p++)
                numerators[p] /= denominator;

            return numerators;
        }

        /// Lookup-table version:
        ///     table[d][k][p] = P(PLS = p | DLS = d, precaution = k, NO accident)
        public double[][][] BuildPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelNoAccidentTable(
            PrecautionImpactModel impactModel)
        {
            if (impactModel == null) throw new ArgumentNullException(nameof(impactModel));

            int precautionLevels = impactModel.PrecautionLevels;
            var table = new double[NumDSignals][][];

            for (int d = 0; d < NumDSignals; d++)
            {
                table[d] = new double[precautionLevels][];
                for (int k = 0; k < precautionLevels; k++)
                    table[d][k] = GetPlaintiffSignalDistributionGivenDefendantSignalAndPrecautionLevelNoAccident(
                        d, k, impactModel);
            }
            return table;
        }


        // === Utilities =====================================================================

        public double GetPlaintiffSignalProbability(int hiddenIndex, int plaintiffSignal) =>
            model.GetSignalDistributionGivenHidden(PlaintiffIndex, hiddenIndex)[plaintiffSignal];

        public double GetDefendantSignalProbability(int hiddenIndex, int defendantSignal) =>
            model.GetSignalDistributionGivenHidden(DefendantIndex, hiddenIndex)[defendantSignal];
    }
}
