using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public sealed class SignalChannelModelTests
    {
        [TestMethod]
        public void Constructor_DeepClonesInputs()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] p = new[]
            {
                new[] { 0.7, 0.3 },
                new[] { 0.2, 0.8 }
            };

            double[][] d = new[]
            {
                new[] { 0.6, 0.4 },
                new[] { 0.1, 0.9 }
            };

            double[][] c = new[]
            {
                new[] { 0.55, 0.45 },
                new[] { 0.25, 0.75 }
            };

            var model = new SignalChannelModel(prior, p, d, c);

            prior[0] = 0.123;
            p[0][0] = 0.999;
            d[1][1] = 0.111;
            c[0][1] = 0.222;

            model.PriorHiddenValues[0].Should().Be(0.5);
            model.PriorHiddenValues[1].Should().Be(0.5);

            model.PlaintiffSignalProbabilitiesGivenHidden[0][0].Should().Be(0.7);
            model.DefendantSignalProbabilitiesGivenHidden[1][1].Should().Be(0.9);
            model.CourtSignalProbabilitiesGivenHidden[0][1].Should().Be(0.45);
        }

        [TestMethod]
        public void Constructor_RejectsNonNormalizedPrior()
        {
            var prior = new[] { 0.2, 0.2 }; // sums to 0.4

            double[][] p = new[]
            {
                new[] { 0.7, 0.3 },
                new[] { 0.2, 0.8 }
            };

            double[][] d = new[]
            {
                new[] { 0.6, 0.4 },
                new[] { 0.1, 0.9 }
            };

            double[][] c = new[]
            {
                new[] { 0.55, 0.45 },
                new[] { 0.25, 0.75 }
            };

            Action act = () => new SignalChannelModel(prior, p, d, c);
            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void Constructor_RejectsNegativeProbabilityInConditionalTable()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] p = new[]
            {
                new[] { -0.1, 1.1 }, // sums to 1 but contains negative entry
                new[] { 0.2, 0.8 }
            };

            double[][] d = new[]
            {
                new[] { 0.6, 0.4 },
                new[] { 0.1, 0.9 }
            };

            double[][] c = new[]
            {
                new[] { 0.55, 0.45 },
                new[] { 0.25, 0.75 }
            };

            Action act = () => new SignalChannelModel(prior, p, d, c);
            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void Constructor_RejectsConditionalRowThatDoesNotSumToOne()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] p = new[]
            {
                new[] { 0.7, 0.3 },
                new[] { 0.2, 0.8 }
            };

            double[][] d = new[]
            {
                new[] { 0.6, 0.4 },
                new[] { 0.1, 0.9 }
            };

            double[][] c = new[]
            {
                new[] { 0.55, 0.45 },
                new[] { 0.25, 0.80 } // sums to 1.05
            };

            Action act = () => new SignalChannelModel(prior, p, d, c);
            act.Should().Throw<ArgumentException>();
        }
    }

    [TestClass]
    public sealed class IdentitySignalShapeTransformerTests
    {
        [TestMethod]
        public void Transform_ReturnsDeepClone()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] input = new[]
            {
                new[] { 0.7, 0.3 },
                new[] { 0.2, 0.8 }
            };

            double[][] transformed = IdentitySignalShapeTransformer.Instance.Transform(prior, input);

            ReferenceEquals(transformed, input).Should().BeFalse();
            ReferenceEquals(transformed[0], input[0]).Should().BeFalse();
            ReferenceEquals(transformed[1], input[1]).Should().BeFalse();

            transformed[0][0] = 0.999;
            input[0][0].Should().Be(0.7);
        }

        [TestMethod]
        public void Transform_ThrowsIfAnyRowIsNull()
        {
            var prior = new[] { 0.5, 0.5 };

            double[][] input = new double[][]
            {
                null,
                new[] { 0.2, 0.8 }
            };

            Action act = () => IdentitySignalShapeTransformer.Instance.Transform(prior, input);
            act.Should().Throw<ArgumentException>();
        }
    }

    [TestClass]
    public sealed class SignalChannelBuilderTests
    {
        private static double[] UniformPrior(int count)
            => Enumerable.Range(0, count).Select(_ => 1.0 / count).ToArray();

        private static DiscreteValueSignalParameters MakeParameters(
            int hiddenCount,
            int signalCount,
            double stdev,
            bool includeExtremes,
            DiscreteSignalBoundaryMode signalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth)
            => new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = hiddenCount,
                NumSignals = signalCount,
                StdevOfNormalDistribution = stdev,
                SourcePointsIncludeExtremes = includeExtremes,
                SignalBoundaryMode = signalBoundaryMode
            };

        private static void AssertJaggedTablesApproximatelyEqual(double[][] expected, double[][] actual, double tolerance)
        {
            actual.Length.Should().Be(expected.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                actual[i].Length.Should().Be(expected[i].Length);
                for (int j = 0; j < expected[i].Length; j++)
                    actual[i][j].Should().BeApproximately(expected[i][j], tolerance);
            }
        }

        private static double[][] MultiplyEachEntry(double[] hiddenPrior, double[][] probabilitiesSignalGivenHidden)
        {
            int hiddenCount = probabilitiesSignalGivenHidden.Length;
            var result = new double[hiddenCount][];

            for (int h = 0; h < hiddenCount; h++)
            {
                double[] row = probabilitiesSignalGivenHidden[h];
                var newRow = new double[row.Length];
                for (int s = 0; s < row.Length; s++)
                    newRow[s] = 2.0 * row[s];
                result[h] = newRow;
            }

            return result;
        }

        private static double[][] AllZerosSameShape(double[] hiddenPrior, double[][] probabilitiesSignalGivenHidden)
        {
            int hiddenCount = probabilitiesSignalGivenHidden.Length;
            var result = new double[hiddenCount][];

            for (int h = 0; h < hiddenCount; h++)
                result[h] = new double[probabilitiesSignalGivenHidden[h].Length];

            return result;
        }

        private static double[][] NegativeEntryButPositiveSum(double[] hiddenPrior, double[][] probabilitiesSignalGivenHidden)
        {
            int hiddenCount = probabilitiesSignalGivenHidden.Length;
            var result = new double[hiddenCount][];

            for (int h = 0; h < hiddenCount; h++)
                result[h] = (double[])probabilitiesSignalGivenHidden[h].Clone();

            if (result.Length > 0 && result[0].Length >= 2)
            {
                result[0][0] = -0.1;
                result[0][1] += 0.1; // preserve sum=1 while introducing negativity
            }

            return result;
        }

        private static double[][] WrongHiddenShape(double[] hiddenPrior, double[][] probabilitiesSignalGivenHidden)
        {
            int hiddenCount = Math.Max(0, probabilitiesSignalGivenHidden.Length - 1);
            var result = new double[hiddenCount][];

            for (int h = 0; h < hiddenCount; h++)
                result[h] = (double[])probabilitiesSignalGivenHidden[h].Clone();

            return result;
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_IdentityMatchesDiscreteValueSignal()
        {
            const int hiddenCount = 5;
            const int signalCount = 7;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.33, includeExtremes: true, signalBoundaryMode: DiscreteSignalBoundaryMode.EqualWidth);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.33, includeExtremes: true, signalBoundaryMode: DiscreteSignalBoundaryMode.EqualWidth);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.33, includeExtremes: true, signalBoundaryMode: DiscreteSignalBoundaryMode.EqualWidth);

            SignalChannelModel model = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams);

            for (byte h = 1; h <= hiddenCount; h++)
            {
                double[] expectedP = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, pParams);
                double[] expectedD = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, dParams);
                double[] expectedC = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(h, cParams);

                double[] actualP = model.PlaintiffSignalProbabilitiesGivenHidden[h - 1];
                double[] actualD = model.DefendantSignalProbabilitiesGivenHidden[h - 1];
                double[] actualC = model.CourtSignalProbabilitiesGivenHidden[h - 1];

                actualP.Sum().Should().BeApproximately(1.0, 1e-10);
                actualD.Sum().Should().BeApproximately(1.0, 1e-10);
                actualC.Sum().Should().BeApproximately(1.0, 1e-10);

                for (int s = 0; s < signalCount; s++)
                {
                    actualP[s].Should().BeApproximately(expectedP[s], 1e-12);
                    actualD[s].Should().BeApproximately(expectedD[s], 1e-12);
                    actualC[s].Should().BeApproximately(expectedC[s], 1e-12);
                }
            }
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_TransformerIsInvokedOncePerParty()
        {
            const int hiddenCount = 4;
            const int signalCount = 3;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);

            int callCount = 0;

            double[][] CountingTransformer(double[] hiddenPrior, double[][] table)
            {
                callCount++;
                return table;
            }

            SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                CountingTransformer);

            callCount.Should().Be(3);
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_ScalingTransformerIsRenormalizedBackToOriginal()
        {
            const int hiddenCount = 6;
            const int signalCount = 5;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.4, includeExtremes: true);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.4, includeExtremes: true);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.4, includeExtremes: true);

            SignalChannelModel identity = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams);

            SignalChannelModel scaled = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                MultiplyEachEntry);

            for (int h = 0; h < hiddenCount; h++)
            {
                scaled.PlaintiffSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);
                scaled.DefendantSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);
                scaled.CourtSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);
            }

            AssertJaggedTablesApproximatelyEqual(identity.PlaintiffSignalProbabilitiesGivenHidden, scaled.PlaintiffSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(identity.DefendantSignalProbabilitiesGivenHidden, scaled.DefendantSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(identity.CourtSignalProbabilitiesGivenHidden, scaled.CourtSignalProbabilitiesGivenHidden, 1e-12);
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_AllZerosTransformerProducesUniformRows()
        {
            const int hiddenCount = 3;
            const int signalCount = 4;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: false);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: false);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: false);

            SignalChannelModel model = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                AllZerosSameShape);

            double expected = 1.0 / signalCount;

            for (int h = 0; h < hiddenCount; h++)
            {
                model.PlaintiffSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);
                model.DefendantSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);
                model.CourtSignalProbabilitiesGivenHidden[h].Sum().Should().BeApproximately(1.0, 1e-10);

                model.PlaintiffSignalProbabilitiesGivenHidden[h].Should().OnlyContain(v => Math.Abs(v - expected) < 1e-12);
                model.DefendantSignalProbabilitiesGivenHidden[h].Should().OnlyContain(v => Math.Abs(v - expected) < 1e-12);
                model.CourtSignalProbabilitiesGivenHidden[h].Should().OnlyContain(v => Math.Abs(v - expected) < 1e-12);
            }
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_TransformerReturningWrongShapeThrows()
        {
            const int hiddenCount = 4;
            const int signalCount = 3;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.2, includeExtremes: true);

            Action act = () => SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                WrongHiddenShape);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_TransformerProducingNegativeProbabilityThrows()
        {
            const int hiddenCount = 4;
            const int signalCount = 3;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.25, includeExtremes: true);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.25, includeExtremes: true);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.25, includeExtremes: true);

            Action act = () => SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                NegativeEntryButPositiveSum);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_PriorLengthMismatchThrows()
        {
            const int hiddenCountInParams = 5;
            const int priorLength = 4;
            const int signalCount = 3;

            var prior = UniformPrior(priorLength);

            var pParams = MakeParameters(hiddenCountInParams, signalCount, 0.2, includeExtremes: true);
            var dParams = MakeParameters(hiddenCountInParams, signalCount, 0.2, includeExtremes: true);
            var cParams = MakeParameters(hiddenCountInParams, signalCount, 0.2, includeExtremes: true);

            Action act = () => SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void BuildUsingDiscreteValueSignalParameters_NullTransformerAndIdentitySignalShapeTransformerProduceSameResults()
        {
            const int hiddenCount = 5;
            const int signalCount = 6;
            var prior = UniformPrior(hiddenCount);

            var pParams = MakeParameters(hiddenCount, signalCount, 0.35, includeExtremes: true);
            var dParams = MakeParameters(hiddenCount, signalCount, 0.35, includeExtremes: true);
            var cParams = MakeParameters(hiddenCount, signalCount, 0.35, includeExtremes: true);

            SignalChannelModel nullTransformer = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                signalShapeTransformer: null);

            SignalChannelModel identityTransformer = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams,
                IdentitySignalShapeTransformer.Instance.Transform);

            AssertJaggedTablesApproximatelyEqual(nullTransformer.PlaintiffSignalProbabilitiesGivenHidden, identityTransformer.PlaintiffSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(nullTransformer.DefendantSignalProbabilitiesGivenHidden, identityTransformer.DefendantSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(nullTransformer.CourtSignalProbabilitiesGivenHidden, identityTransformer.CourtSignalProbabilitiesGivenHidden, 1e-12);
        }

        [TestMethod]
        public void BuildFromNoise_NoiseZeroProducesDeterministicRowsWithExpectedBucket()
        {
            const int hiddenCount = 10;
            const int signalCount = 5;
            var prior = UniformPrior(hiddenCount);

            SignalChannelModel model = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior: prior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: 0.0,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: 0.0,
                courtSignalCount: signalCount,
                courtNoiseStdev: 0.0,
                sourcePointsIncludeExtremes: true);

            var parameters = MakeParameters(
                hiddenCount: hiddenCount,
                signalCount: signalCount,
                stdev: 0.0,
                includeExtremes: true,
                signalBoundaryMode: DiscreteSignalBoundaryMode.EqualWidth);

            for (int h = 1; h <= hiddenCount; h++)
            {
                double location = parameters.MapSourceTo0To1(h);
                int expectedIndex = DiscreteSignalBoundaries.MapLocationIn0To1ToZeroBasedSignalIndex(location, signalCount, DiscreteSignalBoundaryMode.EqualWidth);

                foreach (double[][] table in new[]
                {
                    model.PlaintiffSignalProbabilitiesGivenHidden,
                    model.DefendantSignalProbabilitiesGivenHidden,
                    model.CourtSignalProbabilitiesGivenHidden
                })
                {
                    double[] row = table[h - 1];
                    row.Sum().Should().BeApproximately(1.0, 1e-12);

                    int ones = row.Count(v => Math.Abs(v - 1.0) < 1e-12);
                    int zeros = row.Count(v => Math.Abs(v) < 1e-12);

                    ones.Should().Be(1);
                    zeros.Should().Be(signalCount - 1);

                    row[expectedIndex].Should().BeApproximately(1.0, 1e-12);
                }
            }
        }

        [TestMethod]
        public void BuildFromNoise_NoisePositiveMatchesEquivalentDiscreteValueSignalParameters()
        {
            const int hiddenCount = 6;
            const int signalCount = 5;
            const double stdev = 0.27;
            const bool includeExtremes = false;

            var prior = UniformPrior(hiddenCount);

            SignalChannelModel fromNoise = SignalChannelBuilder.BuildFromNoise(
                hiddenPrior: prior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: stdev,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: stdev,
                courtSignalCount: signalCount,
                courtNoiseStdev: stdev,
                sourcePointsIncludeExtremes: includeExtremes);

            var pParams = MakeParameters(hiddenCount, signalCount, stdev, includeExtremes, DiscreteSignalBoundaryMode.EqualWidth);
            var dParams = MakeParameters(hiddenCount, signalCount, stdev, includeExtremes, DiscreteSignalBoundaryMode.EqualWidth);
            var cParams = MakeParameters(hiddenCount, signalCount, stdev, includeExtremes, DiscreteSignalBoundaryMode.EqualWidth);

            SignalChannelModel fromParams = SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                prior,
                pParams,
                dParams,
                cParams);

            AssertJaggedTablesApproximatelyEqual(fromParams.PlaintiffSignalProbabilitiesGivenHidden, fromNoise.PlaintiffSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(fromParams.DefendantSignalProbabilitiesGivenHidden, fromNoise.DefendantSignalProbabilitiesGivenHidden, 1e-12);
            AssertJaggedTablesApproximatelyEqual(fromParams.CourtSignalProbabilitiesGivenHidden, fromNoise.CourtSignalProbabilitiesGivenHidden, 1e-12);
        }

        [TestMethod]
        public void BuildFromNoise_TransformerIsInvokedOncePerParty()
        {
            const int hiddenCount = 4;
            const int signalCount = 3;
            var prior = UniformPrior(hiddenCount);

            int callCount = 0;

            double[][] CountingTransformer(double[] hiddenPrior, double[][] table)
            {
                callCount++;
                return table;
            }

            SignalChannelBuilder.BuildFromNoise(
                hiddenPrior: prior,
                plaintiffSignalCount: signalCount,
                plaintiffNoiseStdev: 0.2,
                defendantSignalCount: signalCount,
                defendantNoiseStdev: 0.2,
                courtSignalCount: signalCount,
                courtNoiseStdev: 0.2,
                sourcePointsIncludeExtremes: true,
                signalShapeTransformer: CountingTransformer);

            callCount.Should().Be(3);
        }
    }
}
