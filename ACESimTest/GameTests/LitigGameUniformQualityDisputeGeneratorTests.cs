using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
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

        [TestMethod]
        public void BaselineProbabilities_StayWithinOneBillionth_WhenQuadratureIncreasesFrom64To128()
        {
            // Compare the production probability calculations before solving an equilibrium.
            // The bound applies to the baseline information structure, not equilibrium outcomes.
            const double maximumAbsoluteChange = 1E-9;
            Dictionary<string, double> probabilities64 = GetBaselineProbabilitySnapshot(64);
            Dictionary<string, double> probabilities128 = GetBaselineProbabilitySnapshot(128);

            probabilities128.Keys.Should().BeEquivalentTo(probabilities64.Keys);
            probabilities64.Values.Concat(probabilities128.Values).Should().OnlyContain(
                probability => double.IsFinite(probability) && probability >= 0.0 && probability <= 1.0);

            var largestChange = probabilities64.Select(entry => new
                {
                    Probability = entry.Key,
                    Change = Math.Abs(entry.Value - probabilities128[entry.Key]),
                })
                .OrderByDescending(entry => entry.Change)
                .First();

            Console.WriteLine(FormattableString.Invariant(
                $"Compared {probabilities64.Count} baseline probabilities at 64 and 128 quadrature points. Maximum absolute change: {largestChange.Change:G17}; probability: {largestChange.Probability}."));
            largestChange.Change.Should().BeLessThanOrEqualTo(maximumAbsoluteChange,
                "every baseline probability must satisfy the integration bound; the largest change is for {0}",
                largestChange.Probability);
        }

        [TestMethod]
        public void BetaQualityDistributions_AreStableSymmetricAndIntegratedOutsideTheGameTree()
        {
            double[] extremeMass = new double[3];
            double[] centerMass = new double[3];
            foreach (ContinuousQualityDistribution distribution in
                Enum.GetValues<ContinuousQualityDistribution>())
            {
                LitigGameOptions options = GetUniformOptions(64);
                var generator = (LitigGameUniformQualityDisputeGenerator)
                    options.LitigGameDisputeGenerator;
                generator.QualityDistribution = distribution;
                var definition = new LitigGameDefinition();
                definition.Setup(options);

                double[] pSignals = generator.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
                pSignals.Should().OnlyContain(probability =>
                    !double.IsNaN(probability) && !double.IsInfinity(probability) && probability >= 0.0);
                pSignals.Sum().Should().BeApproximately(1.0, 1E-12);
                generator.GetPosteriorMeanQuality(1, 1, null).Should().BeLessThan(0.5);
                generator.GetPosteriorMeanQuality(10, 10, null).Should().BeGreaterThan(0.5);
                (generator.GetPosteriorMeanQuality(1, 1, null) +
                    generator.GetPosteriorMeanQuality(10, 10, null))
                    .Should().BeApproximately(1.0, 1E-11);
                definition.DecisionsExecutionOrder.Any(decision =>
                    decision.Name.Contains("quality", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();

                int index = (int)distribution;
                extremeMass[index] = pSignals[0] + pSignals[9];
                centerMass[index] = pSignals[4] + pSignals[5];
            }

            extremeMass[(int)ContinuousQualityDistribution.BetaHalfHalf]
                .Should().BeGreaterThan(extremeMass[(int)ContinuousQualityDistribution.BetaTwoTwo]);
            centerMass[(int)ContinuousQualityDistribution.BetaTwoTwo]
                .Should().BeGreaterThan(centerMass[(int)ContinuousQualityDistribution.BetaHalfHalf]);
        }

        private static Dictionary<string, double> GetBaselineProbabilitySnapshot(int quadratureOrder)
        {
            LitigGameOptions options = GetUniformOptions(quadratureOrder);
            options.NumLiabilitySignals.Should().Be(10);
            options.NumCourtLiabilitySignals.Should().Be(2);
            options.PLiabilityNoiseStdev.Should().Be(0.2);
            options.DLiabilityNoiseStdev.Should().Be(0.2);
            options.CourtLiabilityNoiseStdev.Should().Be(0.2);
            options.PLiabilitySignalParameters.SignalBoundaryMode.Should().Be(DiscreteSignalBoundaryMode.EqualWidth);
            options.DLiabilitySignalParameters.SignalBoundaryMode.Should().Be(DiscreteSignalBoundaryMode.EqualWidth);

            var generator = (LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator;
            generator.QualityDistribution.Should().Be(ContinuousQualityDistribution.Uniform);
            var definition = new LitigGameDefinition();
            definition.Setup(options);

            var probabilities = new Dictionary<string, double>();
            void AddDistribution(string name, double[] values)
            {
                values.Sum().Should().BeApproximately(1.0, 1E-12, "{0} is a probability distribution", name);
                for (int i = 0; i < values.Length; i++)
                    probabilities.Add($"{name}[{i + 1}]", values[i]);
            }

            double[] pSignals = generator.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
            AddDistribution("P signal", pSignals);
            AddDistribution("D signal", generator.BayesianCalculations_GetDLiabilitySignalProbabilities(null));
            AddDistribution("Truth", generator.GetPostPrimaryChanceProbabilities(definition, default));

            for (byte p = 1; p <= options.NumLiabilitySignals; p++)
            {
                double[] dSignals = generator.BayesianCalculations_GetDLiabilitySignalProbabilities(p);
                AddDistribution($"D signal | P={p}", dSignals);
                for (byte d = 1; d <= options.NumLiabilitySignals; d++)
                {
                    double jointPD = pSignals[p - 1] * dSignals[d - 1];
                    probabilities.Add($"P={p}, D={d}", jointPD);
                    AddDistribution($"Truth | P={p}, D={d}",
                        generator.BayesianCalculations_GetLiabilityStrengthProbabilities(p, d, null));

                    double[] courtSignals = generator.BayesianCalculations_GetCLiabilitySignalProbabilities(p, d);
                    AddDistribution($"Court signal | P={p}, D={d}", courtSignals);
                    for (byte c = 1; c <= options.NumCourtLiabilitySignals; c++)
                    {
                        double jointPDC = jointPD * courtSignals[c - 1];
                        probabilities.Add($"P={p}, D={d}, C={c}", jointPDC);
                        double[] truth = generator.BayesianCalculations_GetLiabilityStrengthProbabilities(p, d, c);
                        AddDistribution($"Truth | P={p}, D={d}, C={c}", truth);
                        for (int t = 0; t < truth.Length; t++)
                            probabilities.Add($"P={p}, D={d}, C={c}, T={t}", jointPDC * truth[t]);
                    }
                }
            }

            return probabilities;
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
