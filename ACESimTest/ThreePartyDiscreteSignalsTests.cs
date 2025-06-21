using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest
{
    /// <summary>
    /// Thorough MSTest + FluentAssertions tests for <see cref="ThreePartyDiscreteSignals"/>.
    ///  ▸ 2 hidden states       (0,1)
    ///  ▸ 3 signal levels each  (low / mid / high)
    ///  ▸ Mild noise so probabilities are fractional.
    /// </summary>
    [TestClass]
    public sealed class ThreePartyDiscreteSignalsTests
    {
        const int HiddenCount = 2;
        const int SignalLevels = 3;

        ThreePartyDiscreteSignals model;

        [TestInitialize]
        public void Init()
        {
            // Mild noise: σ=0.12 so middle band >0 for either hidden
            model = new ThreePartyDiscreteSignals(
                hiddenCount: HiddenCount,
                signalCounts: new[] { SignalLevels, SignalLevels, SignalLevels },
                sigmas: new[] { 0.12, 0.12, 0.12 },
                sourceIncludesExtremes: true);
        }

        // ---------------------------------------------------------------
        // 1) Every likelihood row sums to 1
        // ---------------------------------------------------------------
        [TestMethod]
        public void LikelihoodRowsNormalizeToOne()
        {
            for (int party = 0; party < 3; party++)
            {
                for (int h = 0; h < HiddenCount; h++)
                {
                    model.GetSignalDistributionGivenHidden(party, h)
                         .Sum()
                         .Should().BeApproximately(1.0, 1e-8);
                }
            }
        }

        // ---------------------------------------------------------------
        // 2) Posterior via Bayes matches table
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void PosteriorMatchesBayes(int courtSignal)
        {
            double prior = 1.0 / HiddenCount;
            double[] manual = new double[HiddenCount];
            double denom = 0;

            for (int h = 0; h < HiddenCount; h++)
            {
                double like = model.GetSignalDistributionGivenHidden(2, h)[courtSignal];
                manual[h] = like * prior;
                denom += manual[h];
            }
            for (int h = 0; h < HiddenCount; h++) manual[h] /= denom;

            double[] table = model.GetHiddenDistributionGivenSignal(2, courtSignal);

            table[0].Should().BeApproximately(manual[0], 1e-6);
            table[1].Should().BeApproximately(manual[1], 1e-6);
        }

        // ---------------------------------------------------------------
        // 3) Monte-Carlo matches likelihood
        // ---------------------------------------------------------------
        [TestMethod]
        public void MonteCarloSampling()
        {
            var rng = new Random(42);
            const int Samples = 100_000;
            int[] counts = new int[SignalLevels];

            for (int i = 0; i < Samples; i++)
                counts[model.GenerateSignalsFromHidden(0, rng).Item3]++;

            double[] empirical = counts.Select(c => (double)c / Samples).ToArray();
            double[] theoretical = model.GetSignalDistributionGivenHidden(2, 0);

            for (int s = 0; s < SignalLevels; s++)
                empirical[s].Should().BeApproximately(theoretical[s], 0.01); // ≤1 % error
        }

        // ---------------------------------------------------------------
        // 4) Cross-signal conditional matches joint calculation
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void PlaintiffGivenCourtConsistent(int courtSignal)
        {
            double prior = 0.5;               // uniform prior
            double[] joint = new double[SignalLevels];
            double pcMarginal = 0;

            for (int pSig = 0; pSig < SignalLevels; pSig++)
            {
                double sum = 0;
                for (int h = 0; h < HiddenCount; h++)
                {
                    double pP = model.GetSignalDistributionGivenHidden(0, h)[pSig];
                    double pC = model.GetSignalDistributionGivenHidden(2, h)[courtSignal];
                    sum += pP * pC * prior;
                }
                joint[pSig] = sum;
                pcMarginal += sum;
            }
            for (int pSig = 0; pSig < SignalLevels; pSig++) joint[pSig] /= pcMarginal;

            double[] table = model.GetSignalDistributionGivenSignal(0, 2, courtSignal);

            for (int pSig = 0; pSig < SignalLevels; pSig++)
                table[pSig].Should().BeApproximately(joint[pSig], 1e-6);
        }

        // ---------------------------------------------------------------
        // 5) Deterministic case: σ ≈ 0 → signal = hidden
        // ---------------------------------------------------------------
        [TestMethod]
        public void DeterministicSignalsMatchHidden()
        {
            var deterministic = new ThreePartyDiscreteSignals(
                hiddenCount: 2,
                signalCounts: new[] { 2, 2, 2 },
                sigmas: new[] { 1e-5, 1e-5, 1e-5 },
                sourceIncludesExtremes: true);

            var r = new Random(123);
            for (int h = 0; h < 2; h++)
            {
                var (p, d, c) = deterministic.GenerateSignalsFromHidden(h, r);
                p.Should().Be(h);
                d.Should().Be(h);
                c.Should().Be(h);
            }
        }

        // ---------------------------------------------------------------
        // 6) Same-party conditional throws
        // ---------------------------------------------------------------
        [TestMethod]
        public void SamePartyConditionalThrows()
        {
            Action act = () => model.GetSignalDistributionGivenSignal(1, 1, 0);
            act.Should().Throw<ArgumentException>();
        }

        // ---------------------------------------------------------------
        // 7) Out-of-bounds signal index throws
        // ---------------------------------------------------------------
        [TestMethod]
        public void InvalidSignalIndexThrows()
        {
            Action bad = () => model.GetHiddenDistributionGivenSignal(0, 99);
            bad.Should().Throw<ArgumentOutOfRangeException>();
        }

        // ---------------------------------------------------------------
        // 8) Hidden = 1 Monte Carlo behaves correctly
        // ---------------------------------------------------------------
        [TestMethod]
        public void HiddenOneMonteCarloSampling()
        {
            var rng = new Random(77);
            const int Samples = 100_000;
            int[] counts = new int[SignalLevels];

            for (int i = 0; i < Samples; i++)
                counts[model.GenerateSignalsFromHidden(1, rng).Item3]++;

            double[] empirical = counts.Select(c => (double)c / Samples).ToArray();
            double[] theoretical = model.GetSignalDistributionGivenHidden(2, 1);

            for (int s = 0; s < SignalLevels; s++)
                empirical[s].Should().BeApproximately(theoretical[s], 0.01);
        }

        // ---------------------------------------------------------------------
        // Conditional distribution given *two* other parties’ signals
        // ---------------------------------------------------------------------

        [DataTestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 1)]
        [DataRow(2, 0)]
        public void TargetGivenOtherTwoConsistent(int signal1, int signal2)
        {
            int targetParty = 2;
            int givenParty1 = 0;
            int givenParty2 = 1;

            // Manual Bayesian computation of P(targetSig | sig1, sig2)
            double prior = 1.0 / HiddenCount;
            double[] manual = new double[SignalLevels];
            double denom = 0.0;

            for (int tSig = 0; tSig < SignalLevels; tSig++)
            {
                double sum = 0.0;
                for (int h = 0; h < HiddenCount; h++)
                {
                    double p1 = model.GetSignalDistributionGivenHidden(givenParty1, h)[signal1];
                    double p2 = model.GetSignalDistributionGivenHidden(givenParty2, h)[signal2];
                    double pT = model.GetSignalDistributionGivenHidden(targetParty, h)[tSig];
                    sum += p1 * p2 * pT * prior;
                }
                manual[tSig] = sum;
                denom += sum;
            }
            for (int tSig = 0; tSig < SignalLevels; tSig++) manual[tSig] /= denom;

            double[] table = model.GetSignalDistributionGivenTwoSignals(
                targetPartyIndex: targetParty,
                givenPartyIndex1: givenParty1, givenSignalValue1: signal1,
                givenPartyIndex2: givenParty2, givenSignalValue2: signal2);

            for (int tSig = 0; tSig < SignalLevels; tSig++)
                table[tSig].Should().BeApproximately(manual[tSig], 1e-6);
        }

        // ---------------------------------------------------------------------
        // Guard-clause: duplicate “given” parties must throw
        // ---------------------------------------------------------------------

        [TestMethod]
        public void DuplicateGivenPartiesThrows()
        {
            Action act = () => model.GetSignalDistributionGivenTwoSignals(
                targetPartyIndex: 2,
                givenPartyIndex1: 0, givenSignalValue1: 1,
                givenPartyIndex2: 0, givenSignalValue2: 2);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void DeterministicSignalsYieldPointPosterior()
        {
            var sigModel = new PrecautionSignalModel(2, 2, 2, 2, 1e-5, 1e-5, 1e-5); // nearly noiseless
            double[] posterior = sigModel.GetHiddenPosteriorFromPlaintiffAndDefendantSignals(1, 1);

            posterior[0].Should().BeApproximately(0.0, 1e-6);
            posterior[1].Should().BeApproximately(1.0, 1e-6);
            posterior.Sum().Should().BeApproximately(1.0, 1e-6);
        }

        [TestMethod]
        public void NoisySignalsPosteriorSumsToOne()
        {
            var sigModel = new PrecautionSignalModel(2, 3, 3, 3, 0.25, 0.25, 0.25);
            double[] posterior = sigModel.GetHiddenPosteriorFromPlaintiffAndDefendantSignals(0, 2);

            posterior.Sum().Should().BeApproximately(1.0, 1e-8);
            posterior.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        // ---------------------------------------------------------------
        // 9) Unconditional signal distribution behaves correctly
        // ---------------------------------------------------------------
        [DataTestMethod]
        [DataRow(0)]   // plaintiff
        [DataRow(1)]   // defendant
        [DataRow(2)]   // court
        public void UnconditionalDistributionSumsToOne(int party)
        {
            double[] unconditional = model.GetUnconditionalSignalDistribution(party);
            unconditional.Sum().Should().BeApproximately(1.0, 1e-8);
            unconditional.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void UnconditionalMatchesHiddenAverage(int party)
        {
            double[] expected = new double[SignalLevels];
            double prior = 1.0 / HiddenCount;

            for (int h = 0; h < HiddenCount; h++)
            {
                double[] cond = model.GetSignalDistributionGivenHidden(party, h);
                for (int s = 0; s < SignalLevels; s++)
                    expected[s] += cond[s] * prior;
            }

            double[] table = model.GetUnconditionalSignalDistribution(party);
            for (int s = 0; s < SignalLevels; s++)
                table[s].Should().BeApproximately(expected[s], 1e-8);
        }

        [TestMethod]
        public void UnconditionalMonteCarloSampling()
        {
            var rng = new Random(314);
            const int Samples = 200_000;
            int[] counts = new int[SignalLevels];

            for (int i = 0; i < Samples; i++)
            {
                // Draw a hidden state uniformly, then its court signal
                int h = rng.Next(HiddenCount);
                int court = model.GenerateSignalsFromHidden(h, rng).Item3;
                counts[court]++;
            }

            double[] empirical = counts.Select(c => (double)c / Samples).ToArray();
            double[] theoretical = model.GetUnconditionalSignalDistribution(2); // court

            for (int s = 0; s < SignalLevels; s++)
                empirical[s].Should().BeApproximately(theoretical[s], 0.01);  // ≤1 % error
        }

        [TestMethod]
        public void IntegerDivisionMappingWorksForTenHiddenFiveSignals()
        {
            const int hiddenCount = 10;
            const int signalLevels = 5;

            var deterministic = new ThreePartyDiscreteSignals(
                hiddenCount: hiddenCount,
                signalCounts: new[] { signalLevels, signalLevels, signalLevels },
                sigmas: new[] { 0.0, 0.0, 0.0 },
                sourceIncludesExtremes: true);

            var rng = new Random(99);

            for (int h = 0; h < hiddenCount; h++)
            {
                int expected = (int)((long)h * signalLevels / hiddenCount);
                var (p, d, c) = deterministic.GenerateSignalsFromHidden(h, rng);

                p.Should().Be(expected);
                d.Should().Be(expected);
                c.Should().Be(expected);
            }
        }

        [TestMethod]
        public void UnreachableSignalReturnsUniformPrior()
        {
            const int hiddenCount = 3;
            const int signalLevels = 5;   // two signals will be unreachable

            var deterministic = new ThreePartyDiscreteSignals(
                hiddenCount: hiddenCount,
                signalCounts: new[] { signalLevels, signalLevels, signalLevels },
                sigmas: new[] { 0.0, 0.0, 0.0 },
                sourceIncludesExtremes: true);

            int unreachableSignal = 2;    // no hidden maps to this bucket
            double[] posterior = deterministic.GetHiddenDistributionGivenSignal(2, unreachableSignal);

            posterior.Should().AllBeEquivalentTo(1.0 / hiddenCount, because: "uniform prior is expected when the signal is impossible");
            posterior.Sum().Should().BeApproximately(1.0, 1e-8);
        }


    }
}
