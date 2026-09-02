using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class LitigGameUniformQualityDisputeGeneratorTests
    {
        [TestMethod]
        public void ContinuousSignalLocation_MatchesExistingDiscreteLocation()
        {
            var parameters = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = 10,
                NumSignals = 10,
                StdevOfNormalDistribution = 0.2,
                SourcePointsIncludeExtremes = true,
                SignalBoundaryMode = DiscreteSignalBoundaryMode.EqualWidth,
            };

            for (int source = 1; source <= parameters.NumPointsInSourceUniformDistribution; source++)
            {
                double location = parameters.MapSourceTo0To1(source);
                double[] existing = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(source, parameters);
                double[] continuous = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(location, parameters);
                continuous.Should().HaveCount(parameters.NumSignals);
                continuous.Sum().Should().BeApproximately(1.0, 1E-14);
                continuous.Should().BeEquivalentTo(existing, options => options.WithStrictOrdering());
            }
        }

        [TestMethod]
        public void UniformQuality_IntegratesContinuousQualityAndTruthWithoutAddingDecisionBranches()
        {
            LitigGameOptions options = GetUniformOptions(64);
            var definition = new LitigGameDefinition();
            definition.Setup(options);
            var generator = (LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator;

            double[] pSignals = generator.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
            pSignals.Should().HaveCount(10);
            pSignals.Sum().Should().BeApproximately(1.0, 1E-13);

            double integratedTruthProbability = 0.0;
            for (byte p = 1; p <= 10; p++)
            {
                double[] dSignals = generator.BayesianCalculations_GetDLiabilitySignalProbabilities(p);
                dSignals.Sum().Should().BeApproximately(1.0, 1E-13);
                for (byte d = 1; d <= 10; d++)
                {
                    double pathProbability = pSignals[p - 1] * dSignals[d - 1];
                    integratedTruthProbability += pathProbability *
                        generator.BayesianCalculations_GetLiabilityStrengthProbabilities(p, d, null)[1];
                    generator.BayesianCalculations_GetCLiabilitySignalProbabilities(p, d)
                        .Sum().Should().BeApproximately(1.0, 1E-13);
                }
            }

            integratedTruthProbability.Should().BeApproximately(0.5, 1E-12);
            generator.GetPosteriorMeanQuality(1, 1, null).Should().BeLessThan(0.5);
            generator.GetPosteriorMeanQuality(10, 10, null).Should().BeGreaterThan(0.5);
            (generator.GetPosteriorMeanQuality(1, 1, null) +
                generator.GetPosteriorMeanQuality(10, 10, null)).Should().BeApproximately(1.0, 1E-12);

            definition.DecisionsExecutionOrder.Any(decision =>
                decision.Name.Contains("quality", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();

            var unifiedLauncher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UnifiedThreeStructure);
            LitigGameOptions binaryOptions = unifiedLauncher.GetOptionsSets()
                .Cast<LitigGameOptions>()
                .First(candidate =>
                    candidate.VariableSettings["Signal Structure"].ToString() ==
                        LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel &&
                    candidate.VariableSettings["Information Level"].ToString() == "1x" &&
                    candidate.VariableSettings["Risk Aversion"].ToString() == "Risk Neutral");
            var binaryDefinition = new LitigGameDefinition();
            binaryDefinition.Setup(binaryOptions);
            definition.DecisionsExecutionOrder
                .Select(decision => (decision.DecisionByteCode, decision.NumPossibleActions))
                .Should().Equal(binaryDefinition.DecisionsExecutionOrder
                    .Select(decision => (decision.DecisionByteCode, decision.NumPossibleActions)));
        }

        [TestMethod]
        public void FixedQuadratureOrder_IsNumericallyStable()
        {
            var definition32 = new LitigGameDefinition();
            LitigGameOptions options32 = GetUniformOptions(32);
            definition32.Setup(options32);
            var generator32 = (LitigGameUniformQualityDisputeGenerator)options32.LitigGameDisputeGenerator;

            var definition64 = new LitigGameDefinition();
            LitigGameOptions options64 = GetUniformOptions(64);
            definition64.Setup(options64);
            var generator64 = (LitigGameUniformQualityDisputeGenerator)options64.LitigGameDisputeGenerator;

            for (byte p = 1; p <= 10; p++)
            for (byte d = 1; d <= 10; d++)
            for (byte c = 1; c <= 2; c++)
                generator64.GetPosteriorMeanQuality(p, d, c).Should().BeApproximately(
                    generator32.GetPosteriorMeanQuality(p, d, c),
                    1E-8);
        }

        private static LitigGameOptions GetUniformOptions(int quadratureOrder)
        {
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.UniformBaselineSupplement);
            LitigGameOptions options = launcher.GetOptionsSets()
                .Cast<LitigGameOptions>()
                .First();
            ((LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator).QuadratureOrder =
                quadratureOrder;
            return options;
        }
    }
}
