using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using ACESimBase.Util.DiscreteProbabilities;

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
    }
}
