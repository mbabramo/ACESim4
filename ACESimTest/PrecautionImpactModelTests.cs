using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ACESimTest
{
    /// <summary>
    /// Sanity checks for <see cref="PrecautionImpactModel"/> under the new
    /// P(t,p) calibration (pMinLow → pMinHigh, αLow → αHigh).
    /// </summary>
    [TestClass]
    public sealed class PrecautionImpactModelTests
    {
        PrecautionImpactModel impact;

        // ---------------------------------------------------------------------
        // Fixture
        // ---------------------------------------------------------------------
        [TestInitialize]
        public void Init()
        {
            impact = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,   // pMax
                pMinLow: 0.20,
                pMinHigh: 0.10,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                pAccidentWrongfulAttribution: 0.0);
        }

        // ---------------------------------------------------------------------
        // Accident-probability accessor
        // ---------------------------------------------------------------------
        [TestMethod]
        public void AccidentProbabilitiesExact()
        {
            // Pre-computed closed-form expectations
            double[,] expected =
            {
                { 0.25, 0.20833333333333334, 0.16666666666666669 },
                { 0.25, 0.19166666666666668, 0.13333333333333336 }
            };

            for (int h = 0; h < 2; h++)
                for (int k = 0; k <= 2; k++)
                    impact.GetAccidentProbability(h, k)
                          .Should().BeApproximately(expected[h, k], 1e-9);
        }

        [TestMethod]
        public void MarginalAccidentProbabilities()
        {
            impact.GetAccidentProbabilityMarginal(0)
                  .Should().BeApproximately(0.25, 1e-9);

            impact.GetAccidentProbabilityMarginal(1)
                  .Should().BeApproximately(0.20, 1e-9);
        }

        // ---------------------------------------------------------------------
        // Risk-reduction accessor
        // ---------------------------------------------------------------------
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
                    delta.Should().BeLessOrEqualTo(prev + 1e-12);
                    prev = delta;
                }
            }
        }

        // ---------------------------------------------------------------------
        // True-liability accessor
        // ---------------------------------------------------------------------
        [TestMethod]
        public void GroundTruthNegligence()
        {
            // With the chosen calibration every hidden state benefits enough
            // from either precaution step to clear the liability threshold.
            impact.IsTrulyLiable(0, 0).Should().BeTrue();
            impact.IsTrulyLiable(0, 1).Should().BeTrue();
            impact.IsTrulyLiable(1, 0).Should().BeTrue();
            impact.IsTrulyLiable(1, 1).Should().BeTrue();

            impact.GetTrueLiabilityProbability(0).Should().BeApproximately(1.0, 1e-9);
            impact.GetTrueLiabilityProbability(1).Should().BeApproximately(1.0, 1e-9);
        }

        // ---------------------------------------------------------------------
        // Wrongful-attribution accessors
        // ---------------------------------------------------------------------
        [TestMethod]
        public void WrongfulAttributionProbabilityMarginalIsZero()
        {
            impact.GetWrongfulAttributionProbabilityMarginal(0).Should().Be(0.0);
            impact.GetWrongfulAttributionProbabilityMarginal(1).Should().Be(0.0);
        }

        [TestMethod]
        public void WrongfulProbabilityGivenHiddenIsZero()
        {
            impact.GetWrongfulAttributionProbabilityGivenHiddenState(0, 1).Should().Be(0.0);
        }

        // ---------------------------------------------------------------------
        // Error handling
        // ---------------------------------------------------------------------
        [TestMethod]
        public void InvalidConfigurationThrows()
        {
            Action act = () => new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 0,          // invalid
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.20,
                pMinHigh: 0.10,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void OutOfBoundsAccessThrows()
        {
            Action bad = () => impact.GetAccidentProbability(99, 0);
            bad.Should().Throw<ArgumentOutOfRangeException>();

            Action bad2 = () => impact.IsTrulyLiable(0, 99);
            bad2.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
