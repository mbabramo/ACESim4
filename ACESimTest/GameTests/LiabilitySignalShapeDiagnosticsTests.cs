using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public sealed class LiabilitySignalShapeDiagnosticsTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void LiabilitySignalShapeMode_PrintsUnconditionalSignalProbabilities_VaryingTailDecay()
        {
            if (TestContext == null)
                throw new InvalidOperationException("TestContext is not available.");

            const int hiddenValueCount = 10;

            int plaintiffSignalCount = 10;
            int defendantSignalCount = 10;
            int courtSignalCount = 2;

            const double plaintiffNoiseStdev = 0.35;
            const double defendantNoiseStdev = 0.35;
            const double courtNoiseStdev = 0.35;

            const bool sourcePointsIncludeExtremes = false;
            const int decimalPlaces = 8;

            double[] hiddenPrior = MakeUniformPrior(hiddenValueCount);

            double[] tailDecayValuesToTry = new[]
            {
                0.0,   // equivalent target to EqualMarginal (uniform)
                0.25,
                0.5,
                1.0,
                2.0,
                4.0
            };

            TestContext.WriteLine("================================================================================");
            TestContext.WriteLine("Liability signal shape diagnostics (unconditional P(signal))");
            TestContext.WriteLine($"HiddenValueCount={hiddenValueCount}");
            TestContext.WriteLine($"SignalCounts P/D/C = {plaintiffSignalCount}/{defendantSignalCount}/{courtSignalCount}");
            TestContext.WriteLine($"NoiseStdevs  P/D/C = {plaintiffNoiseStdev.ToString(CultureInfo.InvariantCulture)}/" +
                                 $"{defendantNoiseStdev.ToString(CultureInfo.InvariantCulture)}/" +
                                 $"{courtNoiseStdev.ToString(CultureInfo.InvariantCulture)}");
            TestContext.WriteLine($"SourcePointsIncludeExtremes={sourcePointsIncludeExtremes}");
            TestContext.WriteLine($"Hidden prior = {FormatVector(hiddenPrior, decimalPlaces)}");
            TestContext.WriteLine("================================================================================");
            TestContext.WriteLine(string.Empty);

            foreach (SignalShapeMode mode in Enum.GetValues(typeof(SignalShapeMode)).Cast<SignalShapeMode>())
            {
                double[] tailDecaysForMode = mode == SignalShapeMode.TailDecay
                    ? tailDecayValuesToTry
                    : new[] { 0.0 };

                foreach (double tailDecay in tailDecaysForMode)
                {
                    var shapeParameters = new SignalShapeParameters()
                    {
                        Mode = mode,
                        TailDecay = tailDecay
                    };

                    SignalChannelModel channelModel = SignalChannelBuilder.BuildFromNoise(
                        hiddenPrior,
                        plaintiffSignalCount,
                        plaintiffNoiseStdev,
                        defendantSignalCount,
                        defendantNoiseStdev,
                        courtSignalCount,
                        courtNoiseStdev,
                        sourcePointsIncludeExtremes,
                        signalShapeParameters: shapeParameters);

                    double[] pUnconditional = SignalChannelDiagnostics.GetUnconditionalPlaintiffSignalProbabilities(channelModel);
                    double[] dUnconditional = SignalChannelDiagnostics.GetUnconditionalDefendantSignalProbabilities(channelModel);
                    double[] cUnconditional = SignalChannelDiagnostics.GetUnconditionalCourtSignalProbabilities(channelModel);

                    AssertProbabilityVectorWellFormed("Plaintiff unconditional", pUnconditional);
                    AssertProbabilityVectorWellFormed("Defendant unconditional", dUnconditional);
                    AssertProbabilityVectorWellFormed("Court unconditional", cUnconditional);

                    double[] uniformTargetP = MakeUniformPrior(pUnconditional.Length);
                    double[] uniformTargetD = MakeUniformPrior(dUnconditional.Length);
                    double[] uniformTargetC = MakeUniformPrior(cUnconditional.Length);

                    double[] modeTargetP = GetModeTarget(mode, pUnconditional.Length, tailDecay);
                    double[] modeTargetD = GetModeTarget(mode, dUnconditional.Length, tailDecay);
                    double[] modeTargetC = GetModeTarget(mode, cUnconditional.Length, tailDecay);

                    TestContext.WriteLine("--------------------------------------------------------------------------------");
                    TestContext.WriteLine($"Mode={mode}, TailDecay={tailDecay.ToString(CultureInfo.InvariantCulture)}");
                    TestContext.WriteLine("--------------------------------------------------------------------------------");

                    TestContext.WriteLine("Uniform target references (for max|P(signal)-uniform|):");
                    TestContext.WriteLine($"Uniform P signals: {FormatVector(uniformTargetP, decimalPlaces)}");
                    if (defendantSignalCount != plaintiffSignalCount)
                        TestContext.WriteLine($"Uniform D signals: {FormatVector(uniformTargetD, decimalPlaces)}");
                    if (courtSignalCount != plaintiffSignalCount && courtSignalCount != defendantSignalCount)
                        TestContext.WriteLine($"Uniform C signals: {FormatVector(uniformTargetC, decimalPlaces)}");

                    if (modeTargetP != null)
                    {
                        TestContext.WriteLine(string.Empty);
                        TestContext.WriteLine($"Mode target reference for {mode}:");
                        TestContext.WriteLine($"Target P signals: {FormatVector(modeTargetP, decimalPlaces)}");
                        if (defendantSignalCount != plaintiffSignalCount)
                            TestContext.WriteLine($"Target D signals: {FormatVector(modeTargetD, decimalPlaces)}");
                        if (courtSignalCount != plaintiffSignalCount && courtSignalCount != defendantSignalCount)
                            TestContext.WriteLine($"Target C signals: {FormatVector(modeTargetC, decimalPlaces)}");
                    }

                    TestContext.WriteLine(string.Empty);
                    WritePartyUnconditionalDistribution("Plaintiff", pUnconditional, uniformTargetP, modeTargetP, decimalPlaces);
                    TestContext.WriteLine(string.Empty);
                    WritePartyUnconditionalDistribution("Defendant", dUnconditional, uniformTargetD, modeTargetD, decimalPlaces);
                    TestContext.WriteLine(string.Empty);
                    WritePartyUnconditionalDistribution("Court", cUnconditional, uniformTargetC, modeTargetC, decimalPlaces);
                    TestContext.WriteLine(string.Empty);
                }
            }
        }

        private void WritePartyUnconditionalDistribution(
            string partyName,
            double[] unconditional,
            double[] uniformTarget,
            double[] modeTarget,
            int decimalPlaces)
        {
            double sum = unconditional.Sum();
            double min = unconditional.Min();
            double max = unconditional.Max();

            TestContext.WriteLine($"{partyName} P(signal): {FormatVector(unconditional, decimalPlaces)}");
            TestContext.WriteLine($"{partyName} sum={sum.ToString("0.###############", CultureInfo.InvariantCulture)}, " +
                                 $"min={min.ToString("0.###############", CultureInfo.InvariantCulture)}, " +
                                 $"max={max.ToString("0.###############", CultureInfo.InvariantCulture)}");

            double maxAbsFromUniform = GetMaximumAbsoluteDifference(unconditional, uniformTarget);
            TestContext.WriteLine($"{partyName} max|P(signal)-uniform| = {maxAbsFromUniform.ToString("0.###############", CultureInfo.InvariantCulture)}");

            if (modeTarget != null)
            {
                double maxAbsFromModeTarget = GetMaximumAbsoluteDifference(unconditional, modeTarget);
                TestContext.WriteLine($"{partyName} max|P(signal)-target|  = {maxAbsFromModeTarget.ToString("0.###############", CultureInfo.InvariantCulture)}");
            }

            TestContext.WriteLine($"{partyName} per-signal probabilities:");
            for (int i = 0; i < unconditional.Length; i++)
            {
                string sIndex = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ');
                string actual = unconditional[i].ToString(NumberFormat(decimalPlaces), CultureInfo.InvariantCulture);

                if (modeTarget == null)
                {
                    TestContext.WriteLine($"  s={sIndex}: {actual}");
                }
                else
                {
                    string target = modeTarget[i].ToString(NumberFormat(decimalPlaces), CultureInfo.InvariantCulture);
                    string delta = (unconditional[i] - modeTarget[i]).ToString(NumberFormat(decimalPlaces), CultureInfo.InvariantCulture);
                    TestContext.WriteLine($"  s={sIndex}: actual={actual}, target={target}, delta={delta}");
                }
            }
        }

        private static void AssertProbabilityVectorWellFormed(string label, double[] probabilities)
        {
            probabilities.Should().NotBeNull(label);
            probabilities.Length.Should().BeGreaterThan(0, label);

            double sum = probabilities.Sum();
            sum.Should().BeApproximately(1.0, 1e-10, $"{label} must sum to 1");

            double min = probabilities.Min();
            min.Should().BeGreaterOrEqualTo(-1e-14, $"{label} must not contain negative probabilities");
        }

        private static double[] GetModeTarget(SignalShapeMode mode, int signalCount, double tailDecay)
        {
            switch (mode)
            {
                case SignalShapeMode.EqualMarginal:
                    return MakeUniformPrior(signalCount);

                case SignalShapeMode.TailDecay:
                    return SignalChannelDiagnostics.BuildSymmetricTailDecayTarget(signalCount, tailDecay);

                case SignalShapeMode.Identity:
                default:
                    return null;
            }
        }

        private static double[] MakeUniformPrior(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

            double[] prior = Enumerable.Repeat(1.0 / count, count).ToArray();
            prior[count - 1] = 1.0 - prior.Take(count - 1).Sum();
            return prior;
        }

        private static double GetMaximumAbsoluteDifference(double[] a, double[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Length mismatch.");

            double maxAbs = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                double abs = Math.Abs(a[i] - b[i]);
                if (abs > maxAbs) maxAbs = abs;
            }
            return maxAbs;
        }

        private static string FormatVector(double[] values, int decimalPlaces)
        {
            if (values == null) return "null";
            string format = NumberFormat(decimalPlaces);

            return "[" + string.Join(", ", values.Select(v => v.ToString(format, CultureInfo.InvariantCulture))) + "]";
        }

        private static string NumberFormat(int decimalPlaces)
        {
            if (decimalPlaces < 0) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
            return "0." + new string('#', Math.Max(1, decimalPlaces));
        }
    }
}
