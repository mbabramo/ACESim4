using System;
using System.Linq;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class LitigGameDisputeGeneratorNoiseCalibrationTests
    {
        public TestContext TestContext { get; set; }

        /// <summary>
        /// Calibrates the identity-shaped binary-truth signal model separately at each retained
        /// symmetric information level. The target is the unconditional 10-by-10 joint
        /// distribution of the parties' signals in the intermediate-case-quality model.
        /// The objective is D_KL(target case-quality distribution || binary-truth distribution).
        /// </summary>
        [TestMethod]
        public void Find_DirectSignal_NoiseStdevs_That_Minimize_JointPartySignal_KLDivergence()
        {
            const int numLiabilityStrengthPoints = 10;
            const int numPartyLiabilitySignals = 10;
            const int numCourtSignals = 2;

            const double probabilityTrulyLiable = 0.5;
            const double stdevNoiseToProduceLiabilityStrength = 0.35;
            const double baselinePartyLiabilityNoiseStdev = 0.2;

            var targets = new[]
            {
                new CalibrationTarget(0.5, 0.2964025888),
                new CalibrationTarget(1.0, 0.3498283040),
                new CalibrationTarget(2.0, 0.5507929452),
            };

            foreach (CalibrationTarget target in targets)
            {
                double caseQualityPartyNoiseStdev = baselinePartyLiabilityNoiseStdev * target.NoiseMultiplier;

                SignalChannelModel intermediateCaseQualityModel =
                    BuildIntermediateCaseQualityLiabilitySignalChannelModel(
                        probabilityTrulyLiable,
                        numLiabilityStrengthPoints,
                        numPartyLiabilitySignals,
                        caseQualityPartyNoiseStdev,
                        numCourtSignals,
                        caseQualityPartyNoiseStdev,
                        stdevNoiseToProduceLiabilityStrength);

                double[][] targetJointDistribution =
                    CalculateUnconditionalJointPartySignalDistribution(intermediateCaseQualityModel);

                CalibrationSummary summary = CalibrateBinaryTruthSignalModel(
                    targetJointDistribution,
                    probabilityTrulyLiable,
                    numPartyLiabilitySignals,
                    numCourtSignals,
                    coarseMinStdev: 0.01,
                    coarseMaxStdev: 1.50,
                    coarseStepSize: 0.001);

                PrintCalibrationSummary(
                    target.NoiseMultiplier,
                    caseQualityPartyNoiseStdev,
                    summary);

                summary.BestCandidatePartyNoiseStdev.Should()
                    .BeApproximately(target.ExpectedBinaryTruthNoiseStdev, 0.00001);
                summary.KullbackLeiblerDivergence.Should().BeGreaterThanOrEqualTo(0.0);
                summary.TargetDistributionSum.Should().BeApproximately(1.0, 1E-12);
                summary.CandidateDistributionSum.Should().BeApproximately(1.0, 1E-12);
            }
        }

        /// <summary>
        /// With the calibrated party sigma held fixed, calibrates the binary-truth court sigma
        /// against the full unconditional plaintiff-by-defendant-by-court joint distribution.
        /// A court-marginal calibration would be unidentified here because both symmetric models
        /// give the two court signals equal unconditional probability.
        /// </summary>
        [TestMethod]
        public void Find_DirectSignal_CourtNoiseStdevs_That_Minimize_JointThreeSignal_KLDivergence()
        {
            const int numLiabilityStrengthPoints = 10;
            const int numPartyLiabilitySignals = 10;
            const int numCourtSignals = 2;
            const double probabilityTrulyLiable = 0.5;
            const double stdevNoiseToProduceLiabilityStrength = 0.35;

            var targets = new[]
            {
                (caseQualitySigma: 0.1000000000, binaryTruthPartySigma: 0.2964025888, binaryTruthCourtSigma: 0.1947624474),
                (caseQualitySigma: 0.2000000000, binaryTruthPartySigma: 0.3498283040, binaryTruthCourtSigma: 0.3060453855),
                (caseQualitySigma: 0.4000000000, binaryTruthPartySigma: 0.5507929452, binaryTruthCourtSigma: 0.5210455266),
            };

            foreach (var target in targets)
            {
                SignalChannelModel caseQualityModel =
                    BuildIntermediateCaseQualityLiabilitySignalChannelModel(
                        probabilityTrulyLiable,
                        numLiabilityStrengthPoints,
                        numPartyLiabilitySignals,
                        target.caseQualitySigma,
                        numCourtSignals,
                        target.caseQualitySigma,
                        stdevNoiseToProduceLiabilityStrength);

                double[][][] targetJointDistribution =
                    CalculateUnconditionalJointThreeSignalDistribution(caseQualityModel);

                double calibratedCourtSigma = CalibrateBinaryTruthCourtSignalModel(
                    targetJointDistribution,
                    probabilityTrulyLiable,
                    numPartyLiabilitySignals,
                    numCourtSignals,
                    target.binaryTruthPartySigma,
                    coarseMinStdev: 0.01,
                    coarseMaxStdev: 1.50,
                    coarseStepSize: 0.001);

                SignalChannelModel candidateModel = BuildBinaryTruthSignalChannelModel(
                    probabilityTrulyLiable,
                    numPartyLiabilitySignals,
                    numCourtSignals,
                    target.binaryTruthPartySigma,
                    calibratedCourtSigma);
                double[][][] candidateJointDistribution =
                    CalculateUnconditionalJointThreeSignalDistribution(candidateModel);

                TestContext?.WriteLine(
                    $"Case-quality sigma {target.caseQualitySigma:0.0000000000}; " +
                    $"binary-truth party sigma {target.binaryTruthPartySigma:0.0000000000}; " +
                    $"calibrated binary-truth court sigma {calibratedCourtSigma:0.0000000000}; " +
                    $"joint D_KL {CalculateKullbackLeiblerDivergence(targetJointDistribution, candidateJointDistribution):0.0000000000}");

                calibratedCourtSigma.Should().BeApproximately(target.binaryTruthCourtSigma, 1E-8);
                Sum(targetJointDistribution).Should().BeApproximately(1.0, 1E-12);
                Sum(candidateJointDistribution).Should().BeApproximately(1.0, 1E-12);
            }
        }

        private static SignalChannelModel BuildIntermediateCaseQualityLiabilitySignalChannelModel(
            double probabilityTrulyLiable,
            int numLiabilityStrengthPoints,
            int numPartyLiabilitySignals,
            double partyLiabilityNoiseStdev,
            int numCourtSignals,
            double courtLiabilityNoiseStdev,
            double stdevNoiseToProduceLiabilityStrength)
        {
            double[] priorTrueLiability = { 1.0 - probabilityTrulyLiable, probabilityTrulyLiable };

            var liabilityStrengthGenerationParameters = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = 2,
                NumSignals = numLiabilityStrengthPoints,
                StdevOfNormalDistribution = stdevNoiseToProduceLiabilityStrength,
                SourcePointsIncludeExtremes = true,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth,
            };

            double[] liabilityStrengthGivenNotLiable =
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, liabilityStrengthGenerationParameters);
            double[] liabilityStrengthGivenLiable =
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, liabilityStrengthGenerationParameters);

            double[] liabilityStrengthPrior = new double[numLiabilityStrengthPoints];
            for (int i = 0; i < numLiabilityStrengthPoints; i++)
            {
                liabilityStrengthPrior[i] =
                    priorTrueLiability[0] * liabilityStrengthGivenNotLiable[i]
                    + priorTrueLiability[1] * liabilityStrengthGivenLiable[i];
            }

            var plaintiffSignalParameters = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = numLiabilityStrengthPoints,
                NumSignals = numPartyLiabilitySignals,
                StdevOfNormalDistribution = partyLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth,
            };

            var defendantSignalParameters = plaintiffSignalParameters;

            var courtSignalParameters = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = numLiabilityStrengthPoints,
                NumSignals = numCourtSignals,
                StdevOfNormalDistribution = courtLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth,
            };

            return SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                liabilityStrengthPrior,
                plaintiffSignalParameters,
                defendantSignalParameters,
                courtSignalParameters,
                IdentitySignalShapeParameters());
        }

        private static SignalChannelModel BuildBinaryTruthSignalChannelModel(
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtSignals,
            double partyNoiseStdev)
            => BuildBinaryTruthSignalChannelModel(
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtSignals,
                partyNoiseStdev,
                partyNoiseStdev);

        private static SignalChannelModel BuildBinaryTruthSignalChannelModel(
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtSignals,
            double partyNoiseStdev,
            double courtNoiseStdev)
        {
            double[] trueLiabilityPrior = { 1.0 - probabilityTrulyLiable, probabilityTrulyLiable };

            return SignalChannelBuilder.BuildFromNoise(
                trueLiabilityPrior,
                numPartyLiabilitySignals,
                partyNoiseStdev,
                numPartyLiabilitySignals,
                partyNoiseStdev,
                numCourtSignals,
                courtNoiseStdev,
                sourcePointsIncludeExtremes: true,
                signalShapeParameters: IdentitySignalShapeParameters());
        }

        private static double CalibrateBinaryTruthCourtSignalModel(
            double[][][] targetJointDistribution,
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtSignals,
            double calibratedPartyNoiseStdev,
            double coarseMinStdev,
            double coarseMaxStdev,
            double coarseStepSize)
        {
            double Objective(double courtNoiseStdev)
            {
                SignalChannelModel candidateModel = BuildBinaryTruthSignalChannelModel(
                    probabilityTrulyLiable,
                    numPartyLiabilitySignals,
                    numCourtSignals,
                    calibratedPartyNoiseStdev,
                    courtNoiseStdev);
                return CalculateKullbackLeiblerDivergence(
                    targetJointDistribution,
                    CalculateUnconditionalJointThreeSignalDistribution(candidateModel));
            }

            return MinimizeOneDimensional(
                Objective,
                coarseMinStdev,
                coarseMaxStdev,
                coarseStepSize);
        }

        private static double MinimizeOneDimensional(
            Func<double, double> objective,
            double coarseMin,
            double coarseMax,
            double coarseStep)
        {
            if (coarseStep <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(coarseStep));
            if (coarseMax <= coarseMin)
                throw new ArgumentOutOfRangeException(nameof(coarseMax));

            int stepCount = (int)Math.Floor((coarseMax - coarseMin) / coarseStep) + 1;
            int bestStep = Enumerable.Range(0, stepCount)
                .Select(step => (step, value: objective(coarseMin + step * coarseStep)))
                .OrderBy(x => x.value)
                .ThenBy(x => x.step)
                .First().step;

            double left = Math.Max(coarseMin, coarseMin + (bestStep - 1) * coarseStep);
            double right = Math.Min(coarseMax, coarseMin + (bestStep + 1) * coarseStep);
            const double goldenSectionRatio = 0.6180339887498948482;
            double c = right - goldenSectionRatio * (right - left);
            double d = left + goldenSectionRatio * (right - left);
            double valueAtC = objective(c);
            double valueAtD = objective(d);

            for (int iteration = 0; iteration < 100; iteration++)
            {
                if (valueAtC < valueAtD)
                {
                    right = d;
                    d = c;
                    valueAtD = valueAtC;
                    c = right - goldenSectionRatio * (right - left);
                    valueAtC = objective(c);
                }
                else
                {
                    left = c;
                    c = d;
                    valueAtC = valueAtD;
                    d = left + goldenSectionRatio * (right - left);
                    valueAtD = objective(d);
                }
            }

            return (left + right) / 2.0;
        }

        private static SignalShapeParameters IdentitySignalShapeParameters() =>
            new SignalShapeParameters { Mode = SignalShapeMode.Identity };

        private static CalibrationSummary CalibrateBinaryTruthSignalModel(
            double[][] targetJointDistribution,
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtSignals,
            double coarseMinStdev,
            double coarseMaxStdev,
            double coarseStepSize)
        {
            if (coarseStepSize <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(coarseStepSize));
            if (coarseMaxStdev <= coarseMinStdev)
                throw new ArgumentOutOfRangeException(nameof(coarseMaxStdev));

            int stepCount = (int)Math.Floor((coarseMaxStdev - coarseMinStdev) / coarseStepSize) + 1;
            int bestStep = 0;
            double bestDivergence = double.PositiveInfinity;

            for (int step = 0; step < stepCount; step++)
            {
                double candidateStdev = coarseMinStdev + step * coarseStepSize;
                double divergence = EvaluateKullbackLeiblerDivergence(
                    targetJointDistribution,
                    probabilityTrulyLiable,
                    numPartyLiabilitySignals,
                    numCourtSignals,
                    candidateStdev);

                if (divergence < bestDivergence)
                {
                    bestDivergence = divergence;
                    bestStep = step;
                }
            }

            double left = Math.Max(coarseMinStdev, coarseMinStdev + (bestStep - 1) * coarseStepSize);
            double right = Math.Min(coarseMaxStdev, coarseMinStdev + (bestStep + 1) * coarseStepSize);

            const double goldenSectionRatio = 0.6180339887498948482;
            double c = right - goldenSectionRatio * (right - left);
            double d = left + goldenSectionRatio * (right - left);
            double divergenceAtC = EvaluateKullbackLeiblerDivergence(
                targetJointDistribution,
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtSignals,
                c);
            double divergenceAtD = EvaluateKullbackLeiblerDivergence(
                targetJointDistribution,
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtSignals,
                d);

            for (int iteration = 0; iteration < 100; iteration++)
            {
                if (divergenceAtC < divergenceAtD)
                {
                    right = d;
                    d = c;
                    divergenceAtD = divergenceAtC;
                    c = right - goldenSectionRatio * (right - left);
                    divergenceAtC = EvaluateKullbackLeiblerDivergence(
                        targetJointDistribution,
                        probabilityTrulyLiable,
                        numPartyLiabilitySignals,
                        numCourtSignals,
                        c);
                }
                else
                {
                    left = c;
                    c = d;
                    divergenceAtC = divergenceAtD;
                    d = left + goldenSectionRatio * (right - left);
                    divergenceAtD = EvaluateKullbackLeiblerDivergence(
                        targetJointDistribution,
                        probabilityTrulyLiable,
                        numPartyLiabilitySignals,
                        numCourtSignals,
                        d);
                }
            }

            double bestCandidateStdev = (left + right) / 2.0;
            SignalChannelModel bestCandidateModel = BuildBinaryTruthSignalChannelModel(
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtSignals,
                bestCandidateStdev);
            double[][] candidateJointDistribution =
                CalculateUnconditionalJointPartySignalDistribution(bestCandidateModel);

            return new CalibrationSummary(
                bestCandidateStdev,
                CalculateKullbackLeiblerDivergence(targetJointDistribution, candidateJointDistribution),
                CalculateTotalVariationDistance(targetJointDistribution, candidateJointDistribution),
                CalculateSignalCorrelation(targetJointDistribution),
                CalculateSignalCorrelation(candidateJointDistribution),
                Sum(targetJointDistribution),
                Sum(candidateJointDistribution));
        }

        private static double EvaluateKullbackLeiblerDivergence(
            double[][] targetJointDistribution,
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtSignals,
            double candidateStdev)
        {
            SignalChannelModel candidateModel = BuildBinaryTruthSignalChannelModel(
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtSignals,
                candidateStdev);
            double[][] candidateJointDistribution =
                CalculateUnconditionalJointPartySignalDistribution(candidateModel);

            return CalculateKullbackLeiblerDivergence(
                targetJointDistribution,
                candidateJointDistribution);
        }

        private static double[][] CalculateUnconditionalJointPartySignalDistribution(
            SignalChannelModel signalChannelModel)
        {
            double[] hiddenPrior = signalChannelModel.PriorHiddenValues;
            double[][] plaintiffSignalsGivenHidden = signalChannelModel.PlaintiffSignalProbabilitiesGivenHidden;
            double[][] defendantSignalsGivenHidden = signalChannelModel.DefendantSignalProbabilitiesGivenHidden;

            int plaintiffSignalCount = plaintiffSignalsGivenHidden[0].Length;
            int defendantSignalCount = defendantSignalsGivenHidden[0].Length;
            double[][] jointDistribution = Enumerable.Range(0, plaintiffSignalCount)
                .Select(_ => new double[defendantSignalCount])
                .ToArray();

            for (int hidden = 0; hidden < hiddenPrior.Length; hidden++)
            {
                for (int plaintiffSignal = 0; plaintiffSignal < plaintiffSignalCount; plaintiffSignal++)
                {
                    for (int defendantSignal = 0; defendantSignal < defendantSignalCount; defendantSignal++)
                    {
                        jointDistribution[plaintiffSignal][defendantSignal] +=
                            hiddenPrior[hidden]
                            * plaintiffSignalsGivenHidden[hidden][plaintiffSignal]
                            * defendantSignalsGivenHidden[hidden][defendantSignal];
                    }
                }
            }

            return jointDistribution;
        }

        private static double[][][] CalculateUnconditionalJointThreeSignalDistribution(
            SignalChannelModel signalChannelModel)
        {
            double[] hiddenPrior = signalChannelModel.PriorHiddenValues;
            double[][] plaintiffSignalsGivenHidden = signalChannelModel.PlaintiffSignalProbabilitiesGivenHidden;
            double[][] defendantSignalsGivenHidden = signalChannelModel.DefendantSignalProbabilitiesGivenHidden;
            double[][] courtSignalsGivenHidden = signalChannelModel.CourtSignalProbabilitiesGivenHidden;

            int plaintiffSignalCount = plaintiffSignalsGivenHidden[0].Length;
            int defendantSignalCount = defendantSignalsGivenHidden[0].Length;
            int courtSignalCount = courtSignalsGivenHidden[0].Length;
            double[][][] jointDistribution = Enumerable.Range(0, plaintiffSignalCount)
                .Select(_ => Enumerable.Range(0, defendantSignalCount)
                    .Select(_ => new double[courtSignalCount])
                    .ToArray())
                .ToArray();

            for (int hidden = 0; hidden < hiddenPrior.Length; hidden++)
            {
                for (int plaintiffSignal = 0; plaintiffSignal < plaintiffSignalCount; plaintiffSignal++)
                {
                    for (int defendantSignal = 0; defendantSignal < defendantSignalCount; defendantSignal++)
                    {
                        for (int courtSignal = 0; courtSignal < courtSignalCount; courtSignal++)
                        {
                            jointDistribution[plaintiffSignal][defendantSignal][courtSignal] +=
                                hiddenPrior[hidden]
                                * plaintiffSignalsGivenHidden[hidden][plaintiffSignal]
                                * defendantSignalsGivenHidden[hidden][defendantSignal]
                                * courtSignalsGivenHidden[hidden][courtSignal];
                        }
                    }
                }
            }

            return jointDistribution;
        }

        private static double CalculateKullbackLeiblerDivergence(
            double[][] targetDistribution,
            double[][] candidateDistribution)
        {
            double divergence = 0.0;

            for (int p = 0; p < targetDistribution.Length; p++)
            {
                for (int d = 0; d < targetDistribution[p].Length; d++)
                {
                    double targetProbability = targetDistribution[p][d];
                    double candidateProbability = candidateDistribution[p][d];

                    if (targetProbability <= 0.0)
                        continue;
                    if (candidateProbability <= 0.0)
                        return double.PositiveInfinity;

                    divergence += targetProbability * Math.Log(targetProbability / candidateProbability);
                }
            }

            return divergence;
        }

        private static double CalculateKullbackLeiblerDivergence(
            double[][][] targetDistribution,
            double[][][] candidateDistribution)
        {
            double divergence = 0.0;
            for (int p = 0; p < targetDistribution.Length; p++)
            {
                for (int d = 0; d < targetDistribution[p].Length; d++)
                {
                    for (int c = 0; c < targetDistribution[p][d].Length; c++)
                    {
                        double targetProbability = targetDistribution[p][d][c];
                        double candidateProbability = candidateDistribution[p][d][c];
                        if (targetProbability <= 0.0)
                            continue;
                        if (candidateProbability <= 0.0)
                            return double.PositiveInfinity;
                        divergence += targetProbability * Math.Log(targetProbability / candidateProbability);
                    }
                }
            }

            return divergence;
        }

        private static double CalculateTotalVariationDistance(
            double[][] targetDistribution,
            double[][] candidateDistribution)
        {
            double absoluteDifference = 0.0;
            for (int p = 0; p < targetDistribution.Length; p++)
            {
                for (int d = 0; d < targetDistribution[p].Length; d++)
                    absoluteDifference += Math.Abs(targetDistribution[p][d] - candidateDistribution[p][d]);
            }

            return 0.5 * absoluteDifference;
        }

        private static double CalculateSignalCorrelation(double[][] jointDistribution)
        {
            double expectedP = 0.0;
            double expectedD = 0.0;
            double expectedPD = 0.0;
            double expectedPSquared = 0.0;
            double expectedDSquared = 0.0;

            for (int p = 0; p < jointDistribution.Length; p++)
            {
                for (int d = 0; d < jointDistribution[p].Length; d++)
                {
                    double probability = jointDistribution[p][d];
                    double pSignal = p + 1.0;
                    double dSignal = d + 1.0;

                    expectedP += probability * pSignal;
                    expectedD += probability * dSignal;
                    expectedPD += probability * pSignal * dSignal;
                    expectedPSquared += probability * pSignal * pSignal;
                    expectedDSquared += probability * dSignal * dSignal;
                }
            }

            double covariance = expectedPD - expectedP * expectedD;
            double pVariance = expectedPSquared - expectedP * expectedP;
            double dVariance = expectedDSquared - expectedD * expectedD;
            return covariance / Math.Sqrt(pVariance * dVariance);
        }

        private static double Sum(double[][] distribution) => distribution.Sum(row => row.Sum());

        private static double Sum(double[][][] distribution) =>
            distribution.Sum(plane => plane.Sum(row => row.Sum()));

        private void PrintCalibrationSummary(
            double noiseMultiplier,
            double caseQualityPartyNoiseStdev,
            CalibrationSummary summary)
        {
            TestContext?.WriteLine($"Symmetric noise multiplier:       {noiseMultiplier:0.0###}x");
            TestContext?.WriteLine($"  Case-quality party stdev:       {caseQualityPartyNoiseStdev:0.000000}");
            TestContext?.WriteLine($"  Calibrated binary-truth stdev:  {summary.BestCandidatePartyNoiseStdev:0.000000}");
            TestContext?.WriteLine($"  D_KL(quality || truth):         {summary.KullbackLeiblerDivergence:0.000000}");
            TestContext?.WriteLine($"  Total variation distance:       {summary.TotalVariationDistance:0.000000}");
            TestContext?.WriteLine($"  Signal correlation (quality):   {summary.TargetSignalCorrelation:0.000000}");
            TestContext?.WriteLine($"  Signal correlation (truth):     {summary.CandidateSignalCorrelation:0.000000}");
        }

        private sealed class CalibrationTarget
        {
            public CalibrationTarget(double noiseMultiplier, double expectedBinaryTruthNoiseStdev)
            {
                NoiseMultiplier = noiseMultiplier;
                ExpectedBinaryTruthNoiseStdev = expectedBinaryTruthNoiseStdev;
            }

            public double NoiseMultiplier { get; }
            public double ExpectedBinaryTruthNoiseStdev { get; }
        }

        private sealed class CalibrationSummary
        {
            public CalibrationSummary(
                double bestCandidatePartyNoiseStdev,
                double kullbackLeiblerDivergence,
                double totalVariationDistance,
                double targetSignalCorrelation,
                double candidateSignalCorrelation,
                double targetDistributionSum,
                double candidateDistributionSum)
            {
                BestCandidatePartyNoiseStdev = bestCandidatePartyNoiseStdev;
                KullbackLeiblerDivergence = kullbackLeiblerDivergence;
                TotalVariationDistance = totalVariationDistance;
                TargetSignalCorrelation = targetSignalCorrelation;
                CandidateSignalCorrelation = candidateSignalCorrelation;
                TargetDistributionSum = targetDistributionSum;
                CandidateDistributionSum = candidateDistributionSum;
            }

            public double BestCandidatePartyNoiseStdev { get; }
            public double KullbackLeiblerDivergence { get; }
            public double TotalVariationDistance { get; }
            public double TargetSignalCorrelation { get; }
            public double CandidateSignalCorrelation { get; }
            public double TargetDistributionSum { get; }
            public double CandidateDistributionSum { get; }
        }
    }
}
