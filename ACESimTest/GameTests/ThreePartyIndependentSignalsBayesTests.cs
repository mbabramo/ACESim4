using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    /// <summary>
    /// Thorough MSTest + FluentAssertions tests for <see cref="ThreePartyIndependentSignalsBayes"/>.
    /// Uses 2 hidden states and 3 signal levels for each party, with mild noise.
    /// Verifies normalization, Bayes posteriors, sequential conditionals, and guard clauses.
    /// </summary>
    [TestClass]
    public sealed class ThreePartyIndependentSignalsBayesTests
    {
        const int HiddenCount = 2;
        const int SignalLevels = 3;

        private static DiscreteValueSignalParameters MakeParameters(int hiddenCount, int signalLevels, double sigma, bool includeExtremes)
            => new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = hiddenCount,
                NumSignals = signalLevels,
                StdevOfNormalDistribution = sigma,
                SourcePointsIncludeExtremes = includeExtremes
            };

        private static double[] GetConditionalSignalGivenHiddenOneBased(byte hiddenOneBased, DiscreteValueSignalParameters parameters)
            => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(hiddenOneBased, parameters);

        private static double[] Normalize(double[] v)
        {
            double sum = v.Sum();
            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
                return Enumerable.Range(0, v.Length).Select(_ => 1.0 / v.Length).ToArray();
            return v.Select(x => x / sum).ToArray();
        }

        private static double[] PosteriorHiddenGivenTwoSignals(
            double[] priorHiddenValues,
            double[] signal0GivenHiddenForHidden0,
            double[] signal0GivenHiddenForHidden1,
            int signal0IndexZeroBased,
            double[] signal1GivenHiddenForHidden0,
            double[] signal1GivenHiddenForHidden1,
            int signal1IndexZeroBased)
        {
            double[] posterior = new double[HiddenCount];

            double like0 = signal0GivenHiddenForHidden0[signal0IndexZeroBased] * signal1GivenHiddenForHidden0[signal1IndexZeroBased];
            double like1 = signal0GivenHiddenForHidden1[signal0IndexZeroBased] * signal1GivenHiddenForHidden1[signal1IndexZeroBased];

            posterior[0] = priorHiddenValues[0] * like0;
            posterior[1] = priorHiddenValues[1] * like1;

            return Normalize(posterior);
        }

        // ---------------------------------------------------------------
        // 1) Likelihood rows normalize to 1 (validated via the parameters
        //    used to build the model).
        // ---------------------------------------------------------------
        [TestMethod]
        public void LikelihoodRowsNormalizeToOne()
        {
            var prior = new[] { 0.5, 0.5 };

            var pParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var dParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var cParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);

            for (byte h = 1; h <= HiddenCount; h++)
            {
                GetConditionalSignalGivenHiddenOneBased(h, pParams).Sum().Should().BeApproximately(1.0, 1e-8);
                GetConditionalSignalGivenHiddenOneBased(h, dParams).Sum().Should().BeApproximately(1.0, 1e-8);
                GetConditionalSignalGivenHiddenOneBased(h, cParams).Sum().Should().BeApproximately(1.0, 1e-8);
            }

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            model.GetParty0SignalProbabilitiesUnconditional().Sum().Should().BeApproximately(1.0, 1e-8);
        }

        // ---------------------------------------------------------------
        // 2) Posterior P(H | S0, S1) matches manual Bayes (with non-uniform prior).
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(1, 1)]
        [DataRow(2, 1)]
        [DataRow(3, 2)]
        public void PosteriorGivenTwoSignalsMatchesBayes(int s0OneBasedInt, int s1OneBasedInt)
        {
            byte s0OneBased = checked((byte)s0OneBasedInt);
            byte s1OneBased = checked((byte)s1OneBasedInt);

            var prior = new[] { 0.25, 0.75 };

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            var pParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var dParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);

            double[] s0GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, pParams);
            double[] s0GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, pParams);
            double[] s1GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, dParams);
            double[] s1GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, dParams);

            double[] manual = PosteriorHiddenGivenTwoSignals(
                priorHiddenValues: prior,
                signal0GivenHiddenForHidden0: s0GivenH1,
                signal0GivenHiddenForHidden1: s0GivenH2,
                signal0IndexZeroBased: s0OneBased - 1,
                signal1GivenHiddenForHidden0: s1GivenH1,
                signal1GivenHiddenForHidden1: s1GivenH2,
                signal1IndexZeroBased: s1OneBased - 1);

            double[] table = model.GetPosteriorHiddenProbabilitiesGivenSignals(s0OneBased, s1OneBased, party2Signal: null);

            table.Length.Should().Be(HiddenCount);
            table.Sum().Should().BeApproximately(1.0, 1e-8);

            table[0].Should().BeApproximately(manual[0], 1e-6);
            table[1].Should().BeApproximately(manual[1], 1e-6);
        }

        // ---------------------------------------------------------------
        // 3) P(S1 | S0) matches manual mixture over posterior P(H | S0)
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void Party1GivenParty0MatchesMixture(int s0OneBasedInt)
        {
            byte s0OneBased = checked((byte)s0OneBasedInt);

            var prior = new[] { 0.4, 0.6 };

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            var pParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var dParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);

            double[] s0GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, pParams);
            double[] s0GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, pParams);
            double[] s1GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, dParams);
            double[] s1GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, dParams);

            double[] posteriorGivenS0 = new double[HiddenCount];
            posteriorGivenS0[0] = prior[0] * s0GivenH1[s0OneBased - 1];
            posteriorGivenS0[1] = prior[1] * s0GivenH2[s0OneBased - 1];
            posteriorGivenS0 = Normalize(posteriorGivenS0);

            double[] manual = new double[SignalLevels];
            for (int s1 = 0; s1 < SignalLevels; s1++)
                manual[s1] = posteriorGivenS0[0] * s1GivenH1[s1] + posteriorGivenS0[1] * s1GivenH2[s1];
            manual = Normalize(manual);

            double[] table = model.GetParty1SignalProbabilitiesGivenParty0Signal(s0OneBased);

            table.Sum().Should().BeApproximately(1.0, 1e-8);
            for (int s = 0; s < SignalLevels; s++)
                table[s].Should().BeApproximately(manual[s], 1e-6);
        }

        // ---------------------------------------------------------------
        // 4) P(S2 | S0, S1) matches manual mixture over posterior P(H | S0, S1)
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(1, 1)]
        [DataRow(2, 3)]
        [DataRow(3, 2)]
        public void Party2GivenParty0AndParty1MatchesMixture(int s0OneBasedInt, int s1OneBasedInt)
        {
            byte s0OneBased = checked((byte)s0OneBasedInt);
            byte s1OneBased = checked((byte)s1OneBasedInt);

            var prior = new[] { 0.5, 0.5 };

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            var pParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var dParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var cParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);

            double[] s0GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, pParams);
            double[] s0GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, pParams);

            double[] s1GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, dParams);
            double[] s1GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, dParams);

            double[] s2GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, cParams);
            double[] s2GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, cParams);

            double[] posterior = PosteriorHiddenGivenTwoSignals(
                priorHiddenValues: prior,
                signal0GivenHiddenForHidden0: s0GivenH1,
                signal0GivenHiddenForHidden1: s0GivenH2,
                signal0IndexZeroBased: s0OneBased - 1,
                signal1GivenHiddenForHidden0: s1GivenH1,
                signal1GivenHiddenForHidden1: s1GivenH2,
                signal1IndexZeroBased: s1OneBased - 1);

            double[] manual = new double[SignalLevels];
            for (int s2 = 0; s2 < SignalLevels; s2++)
                manual[s2] = posterior[0] * s2GivenH1[s2] + posterior[1] * s2GivenH2[s2];
            manual = Normalize(manual);

            double[] table = model.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(s0OneBased, s1OneBased);

            table.Sum().Should().BeApproximately(1.0, 1e-8);
            for (int s = 0; s < SignalLevels; s++)
                table[s].Should().BeApproximately(manual[s], 1e-6);
        }

        // ---------------------------------------------------------------
        // 5) P(H | S0, S1, S2) matches manual Bayes
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(1, 1, 1)]
        [DataRow(2, 3, 2)]
        [DataRow(3, 2, 3)]
        public void PosteriorGivenThreeSignalsMatchesBayes(int s0OneBasedInt, int s1OneBasedInt, int s2OneBasedInt)
        {
            byte s0OneBased = checked((byte)s0OneBasedInt);
            byte s1OneBased = checked((byte)s1OneBasedInt);
            byte s2OneBased = checked((byte)s2OneBasedInt);

            var prior = new[] { 0.2, 0.8 };

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            var pParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var dParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);
            var cParams = MakeParameters(HiddenCount, SignalLevels, 0.12, includeExtremes: true);

            double[] s0GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, pParams);
            double[] s0GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, pParams);

            double[] s1GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, dParams);
            double[] s1GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, dParams);

            double[] s2GivenH1 = GetConditionalSignalGivenHiddenOneBased(1, cParams);
            double[] s2GivenH2 = GetConditionalSignalGivenHiddenOneBased(2, cParams);

            double[] manual = new double[HiddenCount];
            manual[0] = prior[0] * s0GivenH1[s0OneBased - 1] * s1GivenH1[s1OneBased - 1] * s2GivenH1[s2OneBased - 1];
            manual[1] = prior[1] * s0GivenH2[s0OneBased - 1] * s1GivenH2[s1OneBased - 1] * s2GivenH2[s2OneBased - 1];
            manual = Normalize(manual);

            double[] table = model.GetPosteriorHiddenProbabilitiesGivenSignals(s0OneBased, s1OneBased, s2OneBased);

            table.Sum().Should().BeApproximately(1.0, 1e-8);
            table[0].Should().BeApproximately(manual[0], 1e-6);
            table[1].Should().BeApproximately(manual[1], 1e-6);
        }

        // ---------------------------------------------------------------
        // 6) Deterministic case: sigma=0 -> signals are perfectly separating for 2 hidden / 2 signals
        // ---------------------------------------------------------------
        [TestMethod]
        public void DeterministicSigmaProducesPointPosterior()
        {
            var prior = new[] { 0.5, 0.5 };

            var deterministic = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: 2, party0NoiseStdev: 0.0,
                party1SignalCount: 2, party1NoiseStdev: 0.0,
                party2SignalCount: 2, party2NoiseStdev: 0.0,
                sourcePointsIncludeExtremes: true);

            double[] posterior = deterministic.GetPosteriorHiddenProbabilitiesGivenSignals(2, 2, party2Signal: null);

            posterior[0].Should().BeApproximately(0.0, 1e-10);
            posterior[1].Should().BeApproximately(1.0, 1e-10);
            posterior.Sum().Should().BeApproximately(1.0, 1e-10);

            double[] s2 = deterministic.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(2, 2);
            s2[0].Should().BeApproximately(0.0, 1e-10);
            s2[1].Should().BeApproximately(1.0, 1e-10);
            s2.Sum().Should().BeApproximately(1.0, 1e-10);
        }

        // ---------------------------------------------------------------
        // 7) Guard-clause: out-of-bounds signal throws
        // ---------------------------------------------------------------
        [TestMethod]
        public void InvalidSignalIndexThrows()
        {
            var prior = new[] { 0.5, 0.5 };

            var model = ThreePartyIndependentSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                priorHiddenValues: prior,
                party0SignalCount: SignalLevels, party0NoiseStdev: 0.12,
                party1SignalCount: SignalLevels, party1NoiseStdev: 0.12,
                party2SignalCount: SignalLevels, party2NoiseStdev: 0.12,
                sourcePointsIncludeExtremes: true);

            Action a = () => model.GetParty1SignalProbabilitiesGivenParty0Signal(0);
            a.Should().Throw<ArgumentOutOfRangeException>();

            Action b = () => model.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(1, 99);
            b.Should().Throw<ArgumentOutOfRangeException>();

            Action c = () => model.GetPosteriorHiddenProbabilitiesGivenSignals(99, 1, party2Signal: null);
            c.Should().Throw<ArgumentOutOfRangeException>();
        }

        // ---------------------------------------------------------------
        // 8) Guard-clause: constructor rejects dimension mismatch
        // ---------------------------------------------------------------
        [TestMethod]
        public void DimensionMismatchThrows()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] badParty0 = new[]
            {
                new[] { 0.5, 0.5 },
                new[] { 0.25, 0.25, 0.5 }
            };

            double[][] party1 = new[]
            {
                new[] { 0.2, 0.3, 0.5 },
                new[] { 0.1, 0.2, 0.7 }
            };

            double[][] party2 = new[]
            {
                new[] { 0.3, 0.3, 0.4 },
                new[] { 0.2, 0.2, 0.6 }
            };

            Action act = () => new ThreePartyIndependentSignalsBayes(prior, badParty0, party1, party2);
            act.Should().Throw<ArgumentException>();
        }
    }
}
