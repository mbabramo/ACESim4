using System;
using System.Collections.Generic;
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

        [TestMethod]
        public void Find_DirectSignal_LiabilityNoiseStdev_That_Matches_IntermediateStrengthBaseline_ByPartyAgreementRate()
        {
            const int baselineNumLiabilityStrengthPoints = 10;
            const int baselineNumLiabilitySignals = 10;
            const int baselineNumCourtSignals = 2;

            const double baselineProbabilityTrulyLiable = 0.5;
            const double baselineStdevNoiseToProduceLiabilityStrength = 0.35;

            const double baselinePartyLiabilityNoiseStdev = 0.2;
            const double baselineCourtLiabilityNoiseStdev = 0.2;

            Func<int, bool> courtDecisionRule = courtSignalActionOneBased => courtSignalActionOneBased == 2;

            SignalChannelModel baselineIntermediateStrengthModel = BuildIntermediateStrengthExogenousLiabilitySignalChannelModel(
                probabilityTrulyLiable: baselineProbabilityTrulyLiable,
                numLiabilityStrengthPoints: baselineNumLiabilityStrengthPoints,
                numPartyLiabilitySignals: baselineNumLiabilitySignals,
                partyLiabilityNoiseStdev: baselinePartyLiabilityNoiseStdev,
                numCourtLiabilitySignals: baselineNumCourtSignals,
                courtLiabilityNoiseStdev: baselineCourtLiabilityNoiseStdev,
                stdevNoiseToProduceLiabilityStrength: baselineStdevNoiseToProduceLiabilityStrength);

            double baselineAgreementRate = CalculateExpectedPartyAgreementRate(
                baselineIntermediateStrengthModel,
                courtDecisionRule);

            TestContext?.WriteLine($"Baseline (intermediate-strength exogenous) agreement rate = {baselineAgreementRate:0.000000}");

            CalibrationSummary extremesMappingSummary = CalibrateDirectSignalModelToMatchAgreementRate(
                targetAgreementRate: baselineAgreementRate,
                probabilityTrulyLiable: baselineProbabilityTrulyLiable,
                numPartyLiabilitySignals: baselineNumLiabilitySignals,
                numCourtLiabilitySignals: baselineNumCourtSignals,
                sourcePointsIncludeExtremes: true,
                courtDecisionRule: courtDecisionRule,
                coarseMinStdev: 0.01,
                coarseMaxStdev: 1.50,
                coarseStepSize: 0.001,
                refineHalfWindow: 0.005,
                refineStepSize: 0.0001);

            CalibrationSummary midpointsMappingSummary = CalibrateDirectSignalModelToMatchAgreementRate(
                targetAgreementRate: baselineAgreementRate,
                probabilityTrulyLiable: baselineProbabilityTrulyLiable,
                numPartyLiabilitySignals: baselineNumLiabilitySignals,
                numCourtLiabilitySignals: baselineNumCourtSignals,
                sourcePointsIncludeExtremes: false,
                courtDecisionRule: courtDecisionRule,
                coarseMinStdev: 0.01,
                coarseMaxStdev: 1.50,
                coarseStepSize: 0.001,
                refineHalfWindow: 0.005,
                refineStepSize: 0.0001);

            PrintCalibrationSummary("Direct-signal calibration (sourcePointsIncludeExtremes: true)", extremesMappingSummary);
            PrintCalibrationSummary("Direct-signal calibration (sourcePointsIncludeExtremes: false)", midpointsMappingSummary);

            baselineAgreementRate.Should().BeInRange(0.0, 1.0);
            extremesMappingSummary.BestCandidateAgreementRate.Should().BeInRange(0.0, 1.0);
            midpointsMappingSummary.BestCandidateAgreementRate.Should().BeInRange(0.0, 1.0);
        }

        private static SignalChannelModel BuildIntermediateStrengthExogenousLiabilitySignalChannelModel(
            double probabilityTrulyLiable,
            int numLiabilityStrengthPoints,
            int numPartyLiabilitySignals,
            double partyLiabilityNoiseStdev,
            int numCourtLiabilitySignals,
            double courtLiabilityNoiseStdev,
            double stdevNoiseToProduceLiabilityStrength)
        {
            double[] priorTrueLiability = new[] { 1.0 - probabilityTrulyLiable, probabilityTrulyLiable };

            DiscreteValueSignalParameters liabilityStrengthGenerationParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = 2,
                NumSignals = numLiabilityStrengthPoints,
                StdevOfNormalDistribution = stdevNoiseToProduceLiabilityStrength,
                SourcePointsIncludeExtremes = true,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };

            double[] probabilitiesLiabilityStrengthGivenTrulyNotLiable =
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, liabilityStrengthGenerationParameters);

            double[] probabilitiesLiabilityStrengthGivenTrulyLiable =
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, liabilityStrengthGenerationParameters);

            double[] liabilityStrengthPrior = new double[numLiabilityStrengthPoints];
            for (int i = 0; i < numLiabilityStrengthPoints; i++)
            {
                liabilityStrengthPrior[i] =
                    priorTrueLiability[0] * probabilitiesLiabilityStrengthGivenTrulyNotLiable[i]
                    + priorTrueLiability[1] * probabilitiesLiabilityStrengthGivenTrulyLiable[i];
            }

            DiscreteValueSignalParameters plaintiffSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = numLiabilityStrengthPoints,
                NumSignals = numPartyLiabilitySignals,
                StdevOfNormalDistribution = partyLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };

            DiscreteValueSignalParameters defendantSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = numLiabilityStrengthPoints,
                NumSignals = numPartyLiabilitySignals,
                StdevOfNormalDistribution = partyLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };

            DiscreteValueSignalParameters courtSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = numLiabilityStrengthPoints,
                NumSignals = numCourtLiabilitySignals,
                StdevOfNormalDistribution = courtLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth
            };

            return SignalChannelBuilder.BuildUsingDiscreteValueSignalParameters(
                liabilityStrengthPrior,
                plaintiffSignalParameters,
                defendantSignalParameters,
                courtSignalParameters,
                signalShapeParameters: default(SignalShapeParameters));
        }

        private static CalibrationSummary CalibrateDirectSignalModelToMatchAgreementRate(
            double targetAgreementRate,
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtLiabilitySignals,
            bool sourcePointsIncludeExtremes,
            Func<int, bool> courtDecisionRule,
            double coarseMinStdev,
            double coarseMaxStdev,
            double coarseStepSize,
            double refineHalfWindow,
            double refineStepSize)
        {
            IReadOnlyList<CalibrationCandidate> coarseCandidates = EvaluateDirectSignalCandidates(
                targetAgreementRate,
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtLiabilitySignals,
                sourcePointsIncludeExtremes,
                courtDecisionRule,
                coarseMinStdev,
                coarseMaxStdev,
                coarseStepSize);

            CalibrationCandidate coarseBest = coarseCandidates
                .OrderBy(c => c.AbsoluteDifferenceFromTarget)
                .ThenBy(c => c.PartyNoiseStdev)
                .First();

            double refineMin = Math.Max(coarseMinStdev, coarseBest.PartyNoiseStdev - refineHalfWindow);
            double refineMax = Math.Min(coarseMaxStdev, coarseBest.PartyNoiseStdev + refineHalfWindow);

            IReadOnlyList<CalibrationCandidate> refinedCandidates = EvaluateDirectSignalCandidates(
                targetAgreementRate,
                probabilityTrulyLiable,
                numPartyLiabilitySignals,
                numCourtLiabilitySignals,
                sourcePointsIncludeExtremes,
                courtDecisionRule,
                refineMin,
                refineMax,
                refineStepSize);

            CalibrationCandidate refinedBest = refinedCandidates
                .OrderBy(c => c.AbsoluteDifferenceFromTarget)
                .ThenBy(c => c.PartyNoiseStdev)
                .First();

            List<CalibrationCandidate> topCandidates = refinedCandidates
                .OrderBy(c => c.AbsoluteDifferenceFromTarget)
                .ThenBy(c => c.PartyNoiseStdev)
                .Take(10)
                .ToList();

            return new CalibrationSummary(
                targetAgreementRate,
                sourcePointsIncludeExtremes,
                refinedBest.PartyNoiseStdev,
                refinedBest.CourtNoiseStdev,
                refinedBest.AgreementRate,
                refinedBest.AbsoluteDifferenceFromTarget,
                topCandidates);
        }

        private static IReadOnlyList<CalibrationCandidate> EvaluateDirectSignalCandidates(
            double targetAgreementRate,
            double probabilityTrulyLiable,
            int numPartyLiabilitySignals,
            int numCourtLiabilitySignals,
            bool sourcePointsIncludeExtremes,
            Func<int, bool> courtDecisionRule,
            double minStdev,
            double maxStdev,
            double stepSize)
        {
            if (stepSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepSize));

            if (maxStdev < minStdev)
                throw new ArgumentOutOfRangeException(nameof(maxStdev));

            double[] trueLiabilityPrior = new[] { 1.0 - probabilityTrulyLiable, probabilityTrulyLiable };

            int stepCount = (int)Math.Floor((maxStdev - minStdev) / stepSize) + 1;

            List<CalibrationCandidate> candidates = new List<CalibrationCandidate>(stepCount);

            for (int i = 0; i < stepCount; i++)
            {
                double partyNoiseStdev = minStdev + i * stepSize;
                double courtNoiseStdev = partyNoiseStdev;

                SignalChannelModel directSignalModel = SignalChannelBuilder.BuildFromNoise(
                    hiddenPrior: trueLiabilityPrior,
                    plaintiffSignalCount: numPartyLiabilitySignals,
                    plaintiffNoiseStdev: partyNoiseStdev,
                    defendantSignalCount: numPartyLiabilitySignals,
                    defendantNoiseStdev: partyNoiseStdev,
                    courtSignalCount: numCourtLiabilitySignals,
                    courtNoiseStdev: courtNoiseStdev,
                    sourcePointsIncludeExtremes: sourcePointsIncludeExtremes,
                    signalShapeParameters: default(SignalShapeParameters));

                double agreementRate = CalculateExpectedPartyAgreementRate(directSignalModel, courtDecisionRule);
                double absoluteDifference = Math.Abs(agreementRate - targetAgreementRate);

                candidates.Add(new CalibrationCandidate(
                    partyNoiseStdev,
                    courtNoiseStdev,
                    agreementRate,
                    absoluteDifference));
            }

            return candidates;
        }

        private static double CalculateExpectedPartyAgreementRate(
            SignalChannelModel signalChannelModel,
            Func<int, bool> plaintiffWinsGivenCourtSignalActionOneBased)
        {
            if (signalChannelModel == null)
                throw new ArgumentNullException(nameof(signalChannelModel));

            if (plaintiffWinsGivenCourtSignalActionOneBased == null)
                throw new ArgumentNullException(nameof(plaintiffWinsGivenCourtSignalActionOneBased));

            double[] priorHiddenValues = signalChannelModel.PriorHiddenValues
                ?? throw new InvalidOperationException("SignalChannelModel.PriorHiddenValues is null.");

            double[][] plaintiffSignalProbabilitiesGivenHidden = signalChannelModel.PlaintiffSignalProbabilitiesGivenHidden
                ?? throw new InvalidOperationException("SignalChannelModel.PlaintiffSignalProbabilitiesGivenHidden is null.");

            double[][] defendantSignalProbabilitiesGivenHidden = signalChannelModel.DefendantSignalProbabilitiesGivenHidden
                ?? throw new InvalidOperationException("SignalChannelModel.DefendantSignalProbabilitiesGivenHidden is null.");

            double[][] courtSignalProbabilitiesGivenHidden = signalChannelModel.CourtSignalProbabilitiesGivenHidden
                ?? throw new InvalidOperationException("SignalChannelModel.CourtSignalProbabilitiesGivenHidden is null.");

            int hiddenCount = priorHiddenValues.Length;
            if (hiddenCount == 0)
                throw new InvalidOperationException("Hidden state count is 0.");

            int plaintiffSignalCount = plaintiffSignalProbabilitiesGivenHidden[0].Length;
            int defendantSignalCount = defendantSignalProbabilitiesGivenHidden[0].Length;
            int courtSignalCount = courtSignalProbabilitiesGivenHidden[0].Length;

            double[] probabilityPlaintiffWinsGivenHidden = new double[hiddenCount];
            for (int h = 0; h < hiddenCount; h++)
            {
                double sum = 0.0;
                for (int c = 0; c < courtSignalCount; c++)
                {
                    int courtSignalActionOneBased = c + 1;
                    if (plaintiffWinsGivenCourtSignalActionOneBased(courtSignalActionOneBased))
                        sum += courtSignalProbabilitiesGivenHidden[h][c];
                }
                probabilityPlaintiffWinsGivenHidden[h] = sum;
            }

            double[] probabilityGuessPlaintiff_Plaintiff = CalculateProbabilityGuessPlaintiffGivenOwnSignal(
                priorHiddenValues,
                plaintiffSignalProbabilitiesGivenHidden,
                probabilityPlaintiffWinsGivenHidden);

            double[] probabilityGuessPlaintiff_Defendant = CalculateProbabilityGuessPlaintiffGivenOwnSignal(
                priorHiddenValues,
                defendantSignalProbabilitiesGivenHidden,
                probabilityPlaintiffWinsGivenHidden);

            double agreement = 0.0;

            for (int h = 0; h < hiddenCount; h++)
            {
                double priorH = priorHiddenValues[h];

                for (int pSignal = 0; pSignal < plaintiffSignalCount; pSignal++)
                {
                    double pSignalGivenH = plaintiffSignalProbabilitiesGivenHidden[h][pSignal];
                    double pGuess = probabilityGuessPlaintiff_Plaintiff[pSignal];

                    for (int dSignal = 0; dSignal < defendantSignalCount; dSignal++)
                    {
                        double dSignalGivenH = defendantSignalProbabilitiesGivenHidden[h][dSignal];
                        double dGuess = probabilityGuessPlaintiff_Defendant[dSignal];

                        double jointProbability = priorH * pSignalGivenH * dSignalGivenH;

                        double probabilityAgree =
                            (pGuess * dGuess)
                            + ((1.0 - pGuess) * (1.0 - dGuess));

                        agreement += jointProbability * probabilityAgree;
                    }
                }
            }

            return agreement;
        }

        private static double[] CalculateProbabilityGuessPlaintiffGivenOwnSignal(
            double[] priorHiddenValues,
            double[][] partySignalProbabilitiesGivenHidden,
            double[] probabilityPlaintiffWinsGivenHidden)
        {
            const double tieTolerance = 1e-12;

            int hiddenCount = priorHiddenValues.Length;
            int signalCount = partySignalProbabilitiesGivenHidden[0].Length;

            double[] probabilityGuessPlaintiff = new double[signalCount];

            for (int signal = 0; signal < signalCount; signal++)
            {
                double denominator = 0.0;
                for (int h = 0; h < hiddenCount; h++)
                    denominator += priorHiddenValues[h] * partySignalProbabilitiesGivenHidden[h][signal];

                double probabilityPlaintiffWinsGivenSignal;

                if (denominator <= 0.0)
                {
                    double priorSum = priorHiddenValues.Sum();
                    if (priorSum <= 0.0)
                        probabilityPlaintiffWinsGivenSignal = 0.5;
                    else
                    {
                        double value = 0.0;
                        for (int h = 0; h < hiddenCount; h++)
                            value += (priorHiddenValues[h] / priorSum) * probabilityPlaintiffWinsGivenHidden[h];
                        probabilityPlaintiffWinsGivenSignal = value;
                    }
                }
                else
                {
                    double value = 0.0;
                    for (int h = 0; h < hiddenCount; h++)
                    {
                        double posteriorH = (priorHiddenValues[h] * partySignalProbabilitiesGivenHidden[h][signal]) / denominator;
                        value += posteriorH * probabilityPlaintiffWinsGivenHidden[h];
                    }
                    probabilityPlaintiffWinsGivenSignal = value;
                }

                if (probabilityPlaintiffWinsGivenSignal > 0.5 + tieTolerance)
                    probabilityGuessPlaintiff[signal] = 1.0;
                else if (probabilityPlaintiffWinsGivenSignal < 0.5 - tieTolerance)
                    probabilityGuessPlaintiff[signal] = 0.0;
                else
                    probabilityGuessPlaintiff[signal] = 0.5;
            }

            return probabilityGuessPlaintiff;
        }

        private void PrintCalibrationSummary(string label, CalibrationSummary summary)
        {
            TestContext?.WriteLine(label);
            TestContext?.WriteLine($"  Target agreement rate: {summary.TargetAgreementRate:0.000000}");
            TestContext?.WriteLine($"  Best stdev (party):    {summary.BestCandidatePartyNoiseStdev:0.000000}");
            TestContext?.WriteLine($"  Best stdev (court):    {summary.BestCandidateCourtNoiseStdev:0.000000}");
            TestContext?.WriteLine($"  Agreement at best:     {summary.BestCandidateAgreementRate:0.000000}");
            TestContext?.WriteLine($"  Absolute difference:   {summary.BestCandidateAbsoluteDifference:0.000000}");
            TestContext?.WriteLine("  Top candidates:");

            foreach (CalibrationCandidate candidate in summary.TopCandidates)
            {
                TestContext?.WriteLine(
                    $"    stdev={candidate.PartyNoiseStdev:0.000000}  agree={candidate.AgreementRate:0.000000}  diff={candidate.AbsoluteDifferenceFromTarget:0.000000}");
            }
        }

        private sealed class CalibrationCandidate
        {
            public CalibrationCandidate(
                double partyNoiseStdev,
                double courtNoiseStdev,
                double agreementRate,
                double absoluteDifferenceFromTarget)
            {
                PartyNoiseStdev = partyNoiseStdev;
                CourtNoiseStdev = courtNoiseStdev;
                AgreementRate = agreementRate;
                AbsoluteDifferenceFromTarget = absoluteDifferenceFromTarget;
            }

            public double PartyNoiseStdev { get; }
            public double CourtNoiseStdev { get; }
            public double AgreementRate { get; }
            public double AbsoluteDifferenceFromTarget { get; }
        }

        private sealed class CalibrationSummary
        {
            public CalibrationSummary(
                double targetAgreementRate,
                bool sourcePointsIncludeExtremes,
                double bestCandidatePartyNoiseStdev,
                double bestCandidateCourtNoiseStdev,
                double bestCandidateAgreementRate,
                double bestCandidateAbsoluteDifference,
                IReadOnlyList<CalibrationCandidate> topCandidates)
            {
                TargetAgreementRate = targetAgreementRate;
                SourcePointsIncludeExtremes = sourcePointsIncludeExtremes;
                BestCandidatePartyNoiseStdev = bestCandidatePartyNoiseStdev;
                BestCandidateCourtNoiseStdev = bestCandidateCourtNoiseStdev;
                BestCandidateAgreementRate = bestCandidateAgreementRate;
                BestCandidateAbsoluteDifference = bestCandidateAbsoluteDifference;
                TopCandidates = topCandidates ?? Array.Empty<CalibrationCandidate>();
            }

            public double TargetAgreementRate { get; }
            public bool SourcePointsIncludeExtremes { get; }
            public double BestCandidatePartyNoiseStdev { get; }
            public double BestCandidateCourtNoiseStdev { get; }
            public double BestCandidateAgreementRate { get; }
            public double BestCandidateAbsoluteDifference { get; }
            public IReadOnlyList<CalibrationCandidate> TopCandidates { get; }
        }
    }
}
