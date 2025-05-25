using System;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Domain‑specific wrapper around <see cref="ThreePartyDiscreteSignals"/> that labels the three parties
    /// as Plaintiff (0), Defendant (1), and Court (2) for use in litigation simulations.
    /// </summary>
    public sealed class PrecautionSignalModel
    {
        // Party indices for readability
        public const int PlaintiffIndex = 0;
        public const int DefendantIndex = 1;
        public const int CourtIndex = 2;

        readonly ThreePartyDiscreteSignals model;

        /// <summary>
        /// Constructs a new signal model.
        /// </summary>
        /// <param name="hiddenCount">Number of discrete precaution‑power values.</param>
        /// <param name="numPlaintiffSignals">Signal levels for plaintiff.</param>
        /// <param name="numDefendantSignals">Signal levels for defendant.</param>
        /// <param name="numCourtSignals">Signal levels for court.</param>
        /// <param name="sigmaPlaintiff">Noise stdev for plaintiff.</param>
        /// <param name="sigmaDefendant">Noise stdev for defendant.</param>
        /// <param name="sigmaCourt">Noise stdev for court.</param>
        /// <param name="includeExtremes">Map hidden extremes to [0,1] extremes when true.</param>
        public PrecautionSignalModel(
            int hiddenCount,
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
            model = new ThreePartyDiscreteSignals(hiddenCount, signalCounts, sigmas, includeExtremes);
        }

        /// <summary>
        /// Generates (plaintiff, defendant, court) signals for a given hidden precaution‑power value.
        /// </summary>
        public (int plaintiffSignal, int defendantSignal, int courtSignal) GenerateSignals(int hiddenValue, Random rng = null)
        {
            var (s0, s1, s2) = model.GenerateSignalsFromHidden(hiddenValue, rng);
            return (s0, s1, s2);
        }

        // === Hidden posteriors ==========================================================
        public double[] GetHiddenPosteriorFromPlaintiffSignal(int plaintiffSignal) =>
            model.GetHiddenDistributionGivenSignal(PlaintiffIndex, plaintiffSignal);

        public double[] GetHiddenPosteriorFromDefendantSignal(int defendantSignal) =>
            model.GetHiddenDistributionGivenSignal(DefendantIndex, defendantSignal);

        public double[] GetHiddenPosteriorFromCourtSignal(int courtSignal) =>
            model.GetHiddenDistributionGivenSignal(CourtIndex, courtSignal);

        // === Conditional signal distributions ==========================================
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
    }
}
