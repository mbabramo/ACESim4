using System;
using System.Diagnostics;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Domain-specific wrapper around <see cref="ThreePartyDiscreteSignals"/>.
    /// After refactor, *all* heavy table-building is executed in the constructor; public
    /// accessors merely retrieve cached results.  Cross-model computations that also
    /// require <see cref="PrecautionImpactModel"/> will migrate to PrecautionRiskModel
    /// and are therefore removed here.
    /// </summary>
    public sealed class PrecautionSignalModel
    {
        // Party indices for readability
        public const int PlaintiffIndex = 0;
        public const int DefendantIndex = 1;
        public const int CourtIndex = 2;

        readonly ThreePartyDiscreteSignals model;

        // ---------------- Pre-computed tables ----------------
        readonly double[][] hiddenPosteriorFromP;       // [pSignal][hidden]
        readonly double[][] hiddenPosteriorFromD;       // [dSignal][hidden]
        readonly double[][] hiddenPosteriorFromC;       // [cSignal][hidden]
        readonly double[][][] hiddenPosteriorFromPD;     // [p][d][hidden]

        // Conditional signal distributions (single-party given one other party)
        readonly double[][] plaintiffGivenDefendant;     // [d][p]
        readonly double[][] plaintiffGivenCourt;         // [c][p]
        readonly double[][] defendantGivenPlaintiff;     // [p][d]
        readonly double[][] defendantGivenCourt;         // [c][d]
        readonly double[][] courtGivenPlaintiff;         // [p][c]
        readonly double[][] courtGivenDefendant;         // [d][c]
        readonly double[][][] courtGivenPD;              // [p][d][c]
        readonly double[][] courtGivenHidden;            // [hidden][c]

        // ---------------- Public dimensions ------------------
        public int HiddenStatesCount => model.hiddenCount;
        public int NumPSignals => model.signalCounts[PlaintiffIndex];
        public int NumDSignals => model.signalCounts[DefendantIndex];
        public int NumCSignals => model.signalCounts[CourtIndex];

        //-----------------------------------------------------------------------
        // Constructor – does *all* heavy table building
        //-----------------------------------------------------------------------
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

            // ---- Build posterior-over-hidden tables ----
            hiddenPosteriorFromP = BuildHiddenPosteriorFromSignalTable(PlaintiffIndex, NumPSignals);
            hiddenPosteriorFromD = BuildHiddenPosteriorFromSignalTable(DefendantIndex, NumDSignals);
            hiddenPosteriorFromC = BuildHiddenPosteriorFromSignalTable(CourtIndex, NumCSignals);
            hiddenPosteriorFromPD = BuildHiddenPosteriorFromPDTable();

            // ---- Build conditional signal distributions ----
            (plaintiffGivenDefendant, plaintiffGivenCourt) = BuildPlaintiffSignalConditionalTables();
            (defendantGivenPlaintiff, defendantGivenCourt) = BuildDefendantSignalConditionalTables();
            (courtGivenPlaintiff, courtGivenDefendant, courtGivenPD, courtGivenHidden) = BuildCourtSignalConditionalTables();
        }

        //-----------------------------------------------------------------------
        // Public API – Posteriors over hidden states
        //-----------------------------------------------------------------------
        public double[] GetHiddenPosteriorFromPlaintiffSignal(int plaintiffSignal)
        {
            ValidateSignalIndex(plaintiffSignal, NumPSignals, nameof(plaintiffSignal));
            return hiddenPosteriorFromP[plaintiffSignal];
        }

        public double[] GetHiddenPosteriorFromDefendantSignal(int defendantSignal)
        {
            ValidateSignalIndex(defendantSignal, NumDSignals, nameof(defendantSignal));
            return hiddenPosteriorFromD[defendantSignal];
        }

        public double[] GetHiddenPosteriorFromCourtSignal(int courtSignal)
        {
            ValidateSignalIndex(courtSignal, NumCSignals, nameof(courtSignal));
            return hiddenPosteriorFromC[courtSignal];
        }

        public double[] GetHiddenPosteriorFromPlaintiffAndDefendantSignals(int plaintiffSignal, int defendantSignal)
        {
            ValidateSignalIndex(plaintiffSignal, NumPSignals, nameof(plaintiffSignal));
            ValidateSignalIndex(defendantSignal, NumDSignals, nameof(defendantSignal));
            return hiddenPosteriorFromPD[plaintiffSignal][defendantSignal];
        }

        //-----------------------------------------------------------------------
        // Public API – Conditional signal distributions (single slice access)
        //-----------------------------------------------------------------------
        public double[] GetPlaintiffSignalDistributionGivenDefendantSignal(int defendantSignal)
        {
            ValidateSignalIndex(defendantSignal, NumDSignals, nameof(defendantSignal));
            return plaintiffGivenDefendant[defendantSignal];
        }

        public double[] GetPlaintiffSignalDistributionGivenCourtSignal(int courtSignal)
        {
            ValidateSignalIndex(courtSignal, NumCSignals, nameof(courtSignal));
            return plaintiffGivenCourt[courtSignal];
        }

        public double[] GetDefendantSignalDistributionGivenPlaintiffSignal(int plaintiffSignal)
        {
            ValidateSignalIndex(plaintiffSignal, NumPSignals, nameof(plaintiffSignal));
            return defendantGivenPlaintiff[plaintiffSignal];
        }

        public double[] GetDefendantSignalDistributionGivenCourtSignal(int courtSignal)
        {
            ValidateSignalIndex(courtSignal, NumCSignals, nameof(courtSignal));
            return defendantGivenCourt[courtSignal];
        }

        public double[] GetCourtSignalDistributionGivenPlaintiffSignal(int plaintiffSignal)
        {
            ValidateSignalIndex(plaintiffSignal, NumPSignals, nameof(plaintiffSignal));
            return courtGivenPlaintiff[plaintiffSignal];
        }

        public double[] GetCourtSignalDistributionGivenDefendantSignal(int defendantSignal)
        {
            ValidateSignalIndex(defendantSignal, NumDSignals, nameof(defendantSignal));
            return courtGivenDefendant[defendantSignal];
        }

        public double[] GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(int plaintiffSignal, int defendantSignal)
        {
            ValidateSignalIndex(plaintiffSignal, NumPSignals, nameof(plaintiffSignal));
            ValidateSignalIndex(defendantSignal, NumDSignals, nameof(defendantSignal));
            return courtGivenPD[plaintiffSignal][defendantSignal];
        }

        public double[] GetCourtSignalDistributionGivenHidden(int hiddenIndex)
        {
            if ((uint)hiddenIndex >= HiddenStatesCount) throw new ArgumentOutOfRangeException(nameof(hiddenIndex));
            return courtGivenHidden[hiddenIndex];
        }
        public double[] GetPlaintiffSignalDistributionGivenHidden(int hiddenIndex)
        {
            if ((uint)hiddenIndex >= HiddenStatesCount)
                throw new ArgumentOutOfRangeException(nameof(hiddenIndex));
            return model.GetSignalDistributionGivenHidden(PlaintiffIndex, hiddenIndex);
        }

        public double[] GetDefendantSignalDistributionGivenHidden(int hiddenIndex)
        {
            if ((uint)hiddenIndex >= HiddenStatesCount)
                throw new ArgumentOutOfRangeException(nameof(hiddenIndex));
            return model.GetSignalDistributionGivenHidden(DefendantIndex, hiddenIndex);
        }


        //-----------------------------------------------------------------------
        // Public helpers – unconditional distributions & sampling
        //-----------------------------------------------------------------------
        public double[] GetUnconditionalSignalDistribution(int partyIndex) => model.GetUnconditionalSignalDistribution(partyIndex);

        public (int plaintiffSignal, int defendantSignal, int courtSignal) GenerateSignals(int hiddenValue, Random rng = null) => model.GenerateSignalsFromHidden(hiddenValue, rng);

        public double GetPlaintiffSignalProbability(int hiddenIndex, int plaintiffSignal) => model.GetSignalDistributionGivenHidden(PlaintiffIndex, hiddenIndex)[plaintiffSignal];
        public double GetDefendantSignalProbability(int hiddenIndex, int defendantSignal) => model.GetSignalDistributionGivenHidden(DefendantIndex, hiddenIndex)[defendantSignal];

        //-----------------------------------------------------------------------
        // ---------  Builders (private) – executed once in constructor ----------
        //-----------------------------------------------------------------------

        double[][] BuildHiddenPosteriorFromSignalTable(int partyIdx, int numSignals)
        {
            var table = new double[numSignals][];
            for (int s = 0; s < numSignals; s++)
                table[s] = model.GetHiddenDistributionGivenSignal(partyIdx, s);
            return table;
        }

        double[][][] BuildHiddenPosteriorFromPDTable()
        {
            var table = new double[NumPSignals][][];
            for (int p = 0; p < NumPSignals; p++)
            {
                table[p] = new double[NumDSignals][];
                for (int d = 0; d < NumDSignals; d++)
                    table[p][d] = model.GetHiddenDistributionGivenTwoSignals(PlaintiffIndex, p, DefendantIndex, d);
            }
            return table;
        }

        (double[][] plaintiffGivenDefendant, double[][] plaintiffGivenCourt) BuildPlaintiffSignalConditionalTables()
        {
            var pgd = new double[NumDSignals][];
            var pgc = new double[NumCSignals][];
            for (int d = 0; d < NumDSignals; d++)
                pgd[d] = model.GetSignalDistributionGivenSignal(PlaintiffIndex, DefendantIndex, d);
            for (int c = 0; c < NumCSignals; c++)
                pgc[c] = model.GetSignalDistributionGivenSignal(PlaintiffIndex, CourtIndex, c);
            return (pgd, pgc);
        }

        (double[][] defendantGivenPlaintiff, double[][] defendantGivenCourt) BuildDefendantSignalConditionalTables()
        {
            var dgp = new double[NumPSignals][];
            var dgc = new double[NumCSignals][];
            for (int p = 0; p < NumPSignals; p++)
                dgp[p] = model.GetSignalDistributionGivenSignal(DefendantIndex, PlaintiffIndex, p);
            for (int c = 0; c < NumCSignals; c++)
                dgc[c] = model.GetSignalDistributionGivenSignal(DefendantIndex, CourtIndex, c);
            return (dgp, dgc);
        }

        (double[][] courtGivenPlaintiff, double[][] courtGivenDefendant, double[][][] courtGivenPD, double[][] courtGivenHidden)
            BuildCourtSignalConditionalTables()
        {
            var cgp = new double[NumPSignals][];
            var cgd = new double[NumDSignals][];
            var cgpD = new double[NumPSignals][][];
            var cgh = new double[HiddenStatesCount][];

            // single-condition tables
            for (int p = 0; p < NumPSignals; p++)
                cgp[p] = model.GetSignalDistributionGivenSignal(CourtIndex, PlaintiffIndex, p);
            for (int d = 0; d < NumDSignals; d++)
                cgd[d] = model.GetSignalDistributionGivenSignal(CourtIndex, DefendantIndex, d);
            // double-condition table
            for (int p = 0; p < NumPSignals; p++)
            {
                cgpD[p] = new double[NumDSignals][];
                for (int d = 0; d < NumDSignals; d++)
                    cgpD[p][d] = model.GetSignalDistributionGivenTwoSignals(
                        targetPartyIndex: CourtIndex,
                        givenPartyIndex1: PlaintiffIndex, givenSignalValue1: p,
                        givenPartyIndex2: DefendantIndex, givenSignalValue2: d);
            }
            // hidden-condition table
            for (int h = 0; h < HiddenStatesCount; h++)
                cgh[h] = model.GetSignalDistributionGivenHidden(CourtIndex, h);

            return (cgp, cgd, cgpD, cgh);
        }

        //-----------------------------------------------------------------------
        // Utilities
        //-----------------------------------------------------------------------
        static void ValidateSignalIndex(int idx, int max, string paramName)
        {
            if ((uint)idx >= max) throw new ArgumentOutOfRangeException(paramName);
        }
    }
}