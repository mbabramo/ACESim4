using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using ACESimBase.Games.LitigGame.PrecautionModel;

namespace ACESimTest
{
    /// <summary>
    /// MSTest + FluentAssertions checks for PrecautionImpactModel.
    /// Hidden 0 ⇒ factor 0.8 ; Hidden 1 ⇒ factor 0.6.
    /// Two real precaution levels (0,1) plus hypothetical 2.
    /// </summary>
    [TestClass]
    public sealed class PrecautionImpactModelTests
    {
        PrecautionImpactModel impact;

        [TestInitialize]
        public void Init()
        {
            impact = new PrecautionImpactModel(
                hiddenCount: 2,
                precautionLevels: 2,
                pAccidentNoActivity: 0.01,
                pAccidentNoPrecaution: 0.25,
                precautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8, // hidden 0
                precautionPowerFactorMostEffective: 0.6, // hidden 1
                liabilityThreshold: 1.0);
        }

        [TestMethod]
        public void AccidentProbabilitiesExact()
        {
            double[,] expected =
            {
                { 0.25, 0.20, 0.16 },   // hidden 0 (factor 0.8)
                { 0.25, 0.15, 0.09 }    // hidden 1 (factor 0.6)
            };

            for (int h = 0; h < 2; h++)
                for (int k = 0; k <= 2; k++)
                    impact.GetAccidentProbability(h, k)
                          .Should().BeApproximately(expected[h, k], 1e-6);
        }

        [TestMethod]
        public void MarginalAccidentProbabilities()
        {
            impact.GetAccidentProbabilityMarginal(0).Should().BeApproximately(0.25, 1e-6);
            impact.GetAccidentProbabilityMarginal(1).Should().BeApproximately(0.175, 1e-6);
        }

        [TestMethod]
        public void RiskReductionMonotone()
        {
            for (int h = 0; h < 2; h++)
            {
                double prev = double.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    double delta = impact.GetRiskReduction(h, k);
                    delta.Should().BeGreaterOrEqualTo(0);
                    delta.Should().BeLessOrEqualTo(prev + 1e-9);
                    prev = delta;
                }
            }
        }

        [TestMethod]
        public void GroundTruthNegligence()
        {
            // hidden 0 (factor 0.8)
            impact.IsTrulyLiable(0, 0).Should().BeTrue();
            impact.IsTrulyLiable(0, 1).Should().BeFalse();

            // hidden 1 (factor 0.6)
            impact.IsTrulyLiable(1, 0).Should().BeTrue();
            impact.IsTrulyLiable(1, 1).Should().BeTrue();

            impact.GetTrueLiabilityProbability(0).Should().BeApproximately(1.0, 1e-6);
            impact.GetTrueLiabilityProbability(1).Should().BeApproximately(0.5, 1e-6);
        }

        [TestMethod]
        public void AccidentRiskAboveBaseline()
        {
            double baseline = impact.PAccidentNoActivity;
            for (int h = 0; h < 2; h++)
                for (int k = 0; k <= 2; k++)
                    impact.GetAccidentProbability(h, k)
                          .Should().BeGreaterOrEqualTo(baseline - 1e-9);
        }
    }
}
