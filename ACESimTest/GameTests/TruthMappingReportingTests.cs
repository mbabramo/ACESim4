using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class TruthMappingReportingTests
    {
        [TestMethod]
        public void NonlinearMapIntegratesPosteriorInsteadOfTransformingItsMean()
        {
            var nodes = new[] { 0.1, 0.8 };
            var weights = new[] { 0.75, 0.25 };
            double expected = 0.75 * (0.01 / 0.82) + 0.25 * (0.64 / 0.68);
            double actual = TruthMappingMath.Integrate(nodes, weights, 2);
            Assert.AreEqual(expected, actual, 1e-15);
            Assert.IsTrue(Math.Abs(actual - TruthMappingMath.Map(0.275, 2)) > 0.05);
            Assert.AreEqual(0.275, TruthMappingMath.Integrate(nodes, weights, 1), 1e-15);
            Assert.ThrowsException<ArgumentException>(() => TruthMappingMath.Integrate(nodes, new[] { 0.5, 0.4 }, 2));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => TruthMappingMath.Map(0.5, 0));
        }

        [TestMethod]
        public void ReportingSnapshotsPreserveStrategicProbabilitiesAndIdentityTruth()
        {
            var option = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                .GetOptionsSets().Cast<LitigGameOptions>().First();
            var definition = new LitigGameDefinition(); definition.Setup(option);
            var generator = (LitigGameUniformQualityDisputeGenerator)option.LitigGameDisputeGenerator;
            var originalCourt = generator.BayesianCalculations_GetCLiabilitySignalProbabilities(2, 7).ToArray();
            var prior = generator.GetPriorQualityForReporting();
            foreach (double exponent in new[] { 0.5, 1.0, 2.0 })
                Assert.AreEqual(0.5, TruthMappingMath.Integrate(prior.Quality, prior.Weights, exponent), 1e-12);
            foreach (byte? court in new byte?[] { null, 1, 2 })
            {
                var snapshot = generator.GetPosteriorQualityForReporting(2, 7, court);
                double original = generator.GetPosteriorMeanQuality(2, 7, court);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(original),
                    BitConverter.DoubleToInt64Bits(TruthMappingMath.Integrate(snapshot.Quality, snapshot.Weights, 1)));
                snapshot.Quality[0] = 0; snapshot.Weights[0] = 1;
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(original), BitConverter.DoubleToInt64Bits(generator.GetPosteriorMeanQuality(2, 7, court)));
            }
            prior.Quality[0] = 1; prior.Weights[0] = 1;
            CollectionAssert.AreEqual(originalCourt, generator.BayesianCalculations_GetCLiabilitySignalProbabilities(2, 7));
        }

        [TestMethod]
        public void SymmetricRuleBehaviorSplitIncludesInteractionAndIsReversible()
        {
            var split = WelfareRuleBehaviorDecomposition.Calculate(1, 4, 6, 15);
            Assert.AreEqual(6, split.MechanicalRuleEffect);
            Assert.AreEqual(8, split.BehavioralEffect);
            Assert.AreEqual(0, split.Residual);
            var reverse = WelfareRuleBehaviorDecomposition.Calculate(15, 6, 4, 1);
            Assert.AreEqual(-split.MechanicalRuleEffect, reverse.MechanicalRuleEffect);
            Assert.AreEqual(-split.BehavioralEffect, reverse.BehavioralEffect);
            var invariantCost = WelfareRuleBehaviorDecomposition.Calculate(0.2, 0.2, 0.4, 0.4);
            Assert.AreEqual(0, invariantCost.MechanicalRuleEffect);
        }
    }
}
