using System;
using System.Linq;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.GameTests
{
    [TestClass]
    public sealed class SignalShapeEndToEndTests
    {
        private static DiscreteValueSignalParameters MakeParameters(
            int hiddenValueCount,
            int signalCount,
            double noiseStdev,
            bool sourcePointsIncludeExtremes)
        {
            return new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = hiddenValueCount,
                NumSignals = signalCount,
                StdevOfNormalDistribution = noiseStdev,
                SourcePointsIncludeExtremes = sourcePointsIncludeExtremes,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };
        }

        private static void AssertProbabilityVectorIsValid(double[] probabilities, double tolerance)
        {
            probabilities.Should().NotBeNull();
            probabilities.Length.Should().BeGreaterThan(0);

            probabilities.Should().OnlyContain(p => !double.IsNaN(p) && !double.IsInfinity(p));
            probabilities.Should().OnlyContain(p => p >= -tolerance);

            probabilities.Sum().Should().BeApproximately(1.0, tolerance);
        }

        private static void AssertConditionalProbabilityTableIsValid(double[][] table, double tolerance)
        {
            table.Should().NotBeNull();
            table.Length.Should().BeGreaterThan(0);

            foreach (double[] row in table)
                AssertProbabilityVectorIsValid(row, tolerance);
        }

        private static void AssertVectorsApproximatelyEqual(double[] a, double[] b, double tolerance)
        {
            a.Should().NotBeNull();
            b.Should().NotBeNull();
            a.Length.Should().Be(b.Length);

            for (int i = 0; i < a.Length; i++)
                a[i].Should().BeApproximately(b[i], tolerance);
        }

        [TestMethod]
        public void IdentityMode_BuildUsingDiscreteValueSignalParameters_ReproducesRawConditionalTables()
        {
            int hiddenCount = 4;
            int signalCount = 7;

            double[] hiddenPrior = new[] { 0.25, 0.25, 0.25, 0.25 };

            var plaintiffParameters = MakeParameters(hiddenCount, signalCount, noiseStdev: 0.35, sourcePointsIncludeExtremes: true);
            var defendantParameters = MakeParameters(hiddenCount, signalCount, noiseStdev: 0.55, sourcePointsIncludeExtremes: true);
            var courtParameters = MakeParameters(hiddenCount, signalCount, noiseStdev: 0.45, sourcePointsIncludeExtremes: true);

            var identity = new SignalShapeParameters()
            {
                Mode = SignalShapeMode.Identity,
                TailDecay = 0.0
            };

            SignalChannelModel model = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                hiddenPrior,
                plaintiffParameters,
                defendantParameters,
                courtParameters,
                identity);

            AssertProbabilityVectorIsValid(model.PriorHiddenValues, tolerance: 1e-12);
            AssertConditionalProbabilityTableIsValid(model.PlaintiffSignalProbabilitiesGivenHidden, tolerance: 1e-12);
            AssertConditionalProbabilityTableIsValid(model.DefendantSignalProbabilitiesGivenHidden, tolerance: 1e-12);
            AssertConditionalProbabilityTableIsValid(model.CourtSignalProbabilitiesGivenHidden, tolerance: 1e-12);

            for (int h = 1; h <= hiddenCount; h++)
            {
                double[] rawP = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, plaintiffParameters);
                double[] rawD = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, defendantParameters);
                double[] rawC = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, courtParameters);

                for (int s = 0; s < signalCount; s++)
                {
                    model.PlaintiffSignalProbabilitiesGivenHidden[h - 1][s].Should().BeApproximately(rawP[s], 1e-12);
                    model.DefendantSignalProbabilitiesGivenHidden[h - 1][s].Should().BeApproximately(rawD[s], 1e-12);
                    model.CourtSignalProbabilitiesGivenHidden[h - 1][s].Should().BeApproximately(rawC[s], 1e-12);
                }
            }
        }

        [TestMethod]
        public void IdentityMode_BuildFromNoise_ReproducesLegacyThreePartyCorrelatedSignalsBayes()
        {
            double[] hiddenPrior = new[] { 0.5, 0.5 };
            int signalCount = 5;
            double noiseStdev = 0.22;
            bool sourcePointsIncludeExtremes = true;

            var identity = new SignalShapeParameters()
            {
                Mode = SignalShapeMode.Identity,
                TailDecay = 0.0
            };

            SignalChannelModel channel = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: noiseStdev,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: noiseStdev,
                courtSignalCount: signalCount,
                courtNoiseStdev: noiseStdev,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                signalShapeParameters: identity);

            ThreePartyCorrelatedSignalsBayes bayesFromChannel = new ThreePartyCorrelatedSignalsBayes(
                channel.PriorHiddenValues,
                channel.PlaintiffSignalProbabilitiesGivenHidden,
                channel.DefendantSignalProbabilitiesGivenHidden,
                channel.CourtSignalProbabilitiesGivenHidden);

            ThreePartyCorrelatedSignalsBayes bayesLegacy = ThreePartyCorrelatedSignalsBayes.CreateUsingDiscreteValueSignalParameters(
                hiddenPrior,
                party0SignalCount: signalCount,
                party0NoiseStdev: noiseStdev,
                party1SignalCount: signalCount,
                party1NoiseStdev: noiseStdev,
                party2SignalCount: signalCount,
                party2NoiseStdev: noiseStdev,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes);

            AssertVectorsApproximatelyEqual(
                bayesFromChannel.GetParty0SignalProbabilitiesUnconditional(),
                bayesLegacy.GetParty0SignalProbabilitiesUnconditional(),
                tolerance: 1e-12);

            AssertVectorsApproximatelyEqual(
                bayesFromChannel.GetParty1SignalProbabilitiesUnconditional(),
                bayesLegacy.GetParty1SignalProbabilitiesUnconditional(),
                tolerance: 1e-12);

            AssertVectorsApproximatelyEqual(
                bayesFromChannel.GetParty2SignalProbabilitiesUnconditional(),
                bayesLegacy.GetParty2SignalProbabilitiesUnconditional(),
                tolerance: 1e-12);

            for (byte s0 = 1; s0 <= signalCount; s0++)
            {
                for (byte s1 = 1; s1 <= signalCount; s1++)
                {
                    double[] posteriorFromChannel = bayesFromChannel.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, party2Signal: null);
                    double[] posteriorLegacy = bayesLegacy.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, party2Signal: null);

                    AssertVectorsApproximatelyEqual(posteriorFromChannel, posteriorLegacy, tolerance: 1e-12);
                }
            }

            for (byte s0 = 1; s0 <= signalCount; s0++)
            {
                for (byte s1 = 1; s1 <= signalCount; s1++)
                {
                    for (byte s2 = 1; s2 <= signalCount; s2++)
                    {
                        double[] posteriorFromChannel = bayesFromChannel.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, s2);
                        double[] posteriorLegacy = bayesLegacy.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, s2);

                        AssertVectorsApproximatelyEqual(posteriorFromChannel, posteriorLegacy, tolerance: 1e-12);
                    }
                }
            }
        }

        [TestMethod]
        public void EqualMarginalMode_MakesUnconditionalSignalFrequenciesUniform_WithoutDestroyingInformativeness()
        {
            double[] hiddenPrior = new[] { 0.75, 0.25 };
            int signalCount = 5;
            double noiseStdev = 0.25;
            bool sourcePointsIncludeExtremes = true;

            var equalMarginal = new SignalShapeParameters()
            {
                Mode = SignalShapeMode.EqualMarginal,
                TailDecay = 0.0
            };

            SignalChannelModel channel = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: noiseStdev,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: noiseStdev,
                courtSignalCount: signalCount,
                courtNoiseStdev: noiseStdev,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                signalShapeParameters: equalMarginal);

            double uniform = 1.0 / signalCount;

            double[] pUnconditional = SignalChannelDiagnostics.GetUnconditionalSignalProbabilities(
                channel.PriorHiddenValues,
                channel.PlaintiffSignalProbabilitiesGivenHidden);

            double[] dUnconditional = SignalChannelDiagnostics.GetUnconditionalSignalProbabilities(
                channel.PriorHiddenValues,
                channel.DefendantSignalProbabilitiesGivenHidden);

            double[] cUnconditional = SignalChannelDiagnostics.GetUnconditionalSignalProbabilities(
                channel.PriorHiddenValues,
                channel.CourtSignalProbabilitiesGivenHidden);

            for (int i = 0; i < signalCount; i++)
            {
                pUnconditional[i].Should().BeApproximately(uniform, 1e-6);
                dUnconditional[i].Should().BeApproximately(uniform, 1e-6);
                cUnconditional[i].Should().BeApproximately(uniform, 1e-6);
            }

            double[] rowHiddenLow = channel.PlaintiffSignalProbabilitiesGivenHidden[0];
            (rowHiddenLow.Max() - rowHiddenLow.Min()).Should().BeGreaterThan(0.01);

            ThreePartyCorrelatedSignalsBayes bayes = new ThreePartyCorrelatedSignalsBayes(
                channel.PriorHiddenValues,
                channel.PlaintiffSignalProbabilitiesGivenHidden,
                channel.DefendantSignalProbabilitiesGivenHidden,
                channel.CourtSignalProbabilitiesGivenHidden);

            double priorHighHidden = channel.PriorHiddenValues[1];
            byte lowSignal = 1;
            byte highSignal = (byte)signalCount;

            double posteriorHighGivenLowSignals = bayes.GetPosteriorHiddenProbabilitiesGivenSignals(lowSignal, lowSignal, party2Signal: null)[1];
            double posteriorHighGivenHighSignals = bayes.GetPosteriorHiddenProbabilitiesGivenSignals(highSignal, highSignal, party2Signal: null)[1];

            posteriorHighGivenLowSignals.Should().BeLessThan(priorHighHidden);
            posteriorHighGivenHighSignals.Should().BeGreaterThan(priorHighHidden);
        }

        [TestMethod]
        public void TailDecayMode_MatchesExpectedPriorWeightedUnconditionalTargetDistribution()
        {
            double[] hiddenPrior = new[] { 0.5, 0.5 };
            int signalCount = 7;
            double noiseStdev = 0.35;
            bool sourcePointsIncludeExtremes = true;

            double tailDecay = 3.0;

            var tailDecayMode = new SignalShapeParameters()
            {
                Mode = SignalShapeMode.TailDecay,
                TailDecay = tailDecay
            };

            SignalChannelModel channel = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: noiseStdev,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: noiseStdev,
                courtSignalCount: signalCount,
                courtNoiseStdev: noiseStdev,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                signalShapeParameters: tailDecayMode);

            double[] unconditional = SignalChannelDiagnostics.GetUnconditionalSignalProbabilities(
                channel.PriorHiddenValues,
                channel.PlaintiffSignalProbabilitiesGivenHidden);

            double[] target = SignalChannelDiagnostics.BuildSymmetricTailDecayTarget(signalCount, tailDecay);

            for (int i = 0; i < signalCount; i++)
                unconditional[i].Should().BeApproximately(target[i], 1e-6);

            int centerIndex = (signalCount - 1) / 2;

            unconditional[0].Should().BeLessThan(unconditional[centerIndex]);
            unconditional[signalCount - 1].Should().BeLessThan(unconditional[centerIndex]);
            unconditional[0].Should().BeApproximately(unconditional[signalCount - 1], 1e-6);
        }

        [TestMethod]
        public void ShapedChannels_AlwaysProduceWellFormedBayesianPosteriors()
        {
            double[] hiddenPrior = new[] { 0.5, 0.5 };
            int signalCount = 5;
            double noiseStdev = 0.30;
            bool sourcePointsIncludeExtremes = true;

            SignalShapeParameters[] modesToTest = new[]
            {
                new SignalShapeParameters() { Mode = SignalShapeMode.Identity, TailDecay = 0.0 },
                new SignalShapeParameters() { Mode = SignalShapeMode.EqualMarginal, TailDecay = 0.0 },
                new SignalShapeParameters() { Mode = SignalShapeMode.TailDecay, TailDecay = 2.0 }
            };

            foreach (SignalShapeParameters mode in modesToTest)
            {
                SignalChannelModel channel = SignalChannelBuilder.BuildFromNoise(
                    hiddenPrior,
                    plaintiffSignalCount: signalCount,
                    plaintiffNoiseStdev: noiseStdev,
                    defendantSignalCount: signalCount,
                    defendantNoiseStdev: noiseStdev,
                    courtSignalCount: signalCount,
                    courtNoiseStdev: noiseStdev,
                    sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                    signalShapeParameters: mode);

                ThreePartyCorrelatedSignalsBayes bayes = new ThreePartyCorrelatedSignalsBayes(
                    channel.PriorHiddenValues,
                    channel.PlaintiffSignalProbabilitiesGivenHidden,
                    channel.DefendantSignalProbabilitiesGivenHidden,
                    channel.CourtSignalProbabilitiesGivenHidden);

                for (byte s0 = 1; s0 <= signalCount; s0++)
                {
                    for (byte s1 = 1; s1 <= signalCount; s1++)
                    {
                        double[] posteriorWithoutCourt = bayes.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, party2Signal: null);
                        AssertProbabilityVectorIsValid(posteriorWithoutCourt, tolerance: 1e-12);

                        double[] courtDistribution = bayes.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(s0, s1);
                        AssertProbabilityVectorIsValid(courtDistribution, tolerance: 1e-12);

                        for (byte s2 = 1; s2 <= signalCount; s2++)
                        {
                            double[] posteriorWithCourt = bayes.GetPosteriorHiddenProbabilitiesGivenSignals(s0, s1, s2);
                            AssertProbabilityVectorIsValid(posteriorWithCourt, tolerance: 1e-12);
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void NoiseParameter_ControlsInformativeness_IndependentlyOfSignalShape()
        {
            double[] hiddenPrior = new[] { 0.5, 0.5 };
            int signalCount = 5;
            bool sourcePointsIncludeExtremes = true;

            double lowNoise = 0.12;
            double highNoise = 1.20;

            var equalMarginal = new SignalShapeParameters()
            {
                Mode = SignalShapeMode.EqualMarginal,
                TailDecay = 0.0
            };

            SignalChannelModel lowNoiseChannel = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: lowNoise,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: lowNoise,
                courtSignalCount: signalCount,
                courtNoiseStdev: lowNoise,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                signalShapeParameters: equalMarginal);

            SignalChannelModel highNoiseChannel = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: highNoise,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: highNoise,
                courtSignalCount: signalCount,
                courtNoiseStdev: highNoise,
                sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                signalShapeParameters: equalMarginal);

            ThreePartyCorrelatedSignalsBayes bayesLowNoise = new ThreePartyCorrelatedSignalsBayes(
                lowNoiseChannel.PriorHiddenValues,
                lowNoiseChannel.PlaintiffSignalProbabilitiesGivenHidden,
                lowNoiseChannel.DefendantSignalProbabilitiesGivenHidden,
                lowNoiseChannel.CourtSignalProbabilitiesGivenHidden);

            ThreePartyCorrelatedSignalsBayes bayesHighNoise = new ThreePartyCorrelatedSignalsBayes(
                highNoiseChannel.PriorHiddenValues,
                highNoiseChannel.PlaintiffSignalProbabilitiesGivenHidden,
                highNoiseChannel.DefendantSignalProbabilitiesGivenHidden,
                highNoiseChannel.CourtSignalProbabilitiesGivenHidden);

            double priorHighHidden = 0.5;
            byte lowSignal = 1;
            byte highSignal = (byte)signalCount;

            double posteriorHighGivenHighSignals_LowNoise = bayesLowNoise.GetPosteriorHiddenProbabilitiesGivenSignals(highSignal, highSignal, party2Signal: null)[1];
            double posteriorHighGivenHighSignals_HighNoise = bayesHighNoise.GetPosteriorHiddenProbabilitiesGivenSignals(highSignal, highSignal, party2Signal: null)[1];

            Math.Abs(posteriorHighGivenHighSignals_LowNoise - priorHighHidden)
                .Should()
                .BeGreaterThan(Math.Abs(posteriorHighGivenHighSignals_HighNoise - priorHighHidden));

            double posteriorHighGivenLowSignals_LowNoise = bayesLowNoise.GetPosteriorHiddenProbabilitiesGivenSignals(lowSignal, lowSignal, party2Signal: null)[1];
            double posteriorHighGivenLowSignals_HighNoise = bayesHighNoise.GetPosteriorHiddenProbabilitiesGivenSignals(lowSignal, lowSignal, party2Signal: null)[1];

            Math.Abs(posteriorHighGivenLowSignals_LowNoise - priorHighHidden)
                .Should()
                .BeGreaterThan(Math.Abs(posteriorHighGivenLowSignals_HighNoise - priorHighHidden));
        }
    }
}
