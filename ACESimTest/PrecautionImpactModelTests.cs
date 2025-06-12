using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace ACESimTest
{
    /// <summary>
    /// Unit checks for <see cref="PrecautionImpactModel"/>.  Each section exercises one
    /// public accessor of the model; hidden implementation details (integration,
    /// table builders, etc.) remain untested here by design.
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
                pAccidentNoPrecaution: 0.25,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6, // hidden 0
                liabilityThreshold: 1.0);
        }

        // ---------------------------------------------------------------------
        // Accident‑probability accessor
        // ---------------------------------------------------------------------
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

        // ---------------------------------------------------------------------
        // Risk‑reduction accessor
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
                    delta.Should().BeLessOrEqualTo(prev + 1e-9);
                    prev = delta;
                }
            }
        }

        // ---------------------------------------------------------------------
        // True‑liability accessor
        // ---------------------------------------------------------------------
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

        // ---------------------------------------------------------------------
        // Wrongful‑attribution accessors
        // ---------------------------------------------------------------------
        [TestMethod]
        public void WrongfulAttributionProbabilityMarginalMatchesHiddenMixture()
        {
            const int precautionLevel = 0;
            double expected = 0.0;
            double weight = 1.0 / impact.HiddenCount;

            for (int h = 0; h < impact.HiddenCount; h++)
                expected += weight *
                            impact.GetWrongfulAttributionProbabilityGivenHiddenState(h, precautionLevel);

            impact.GetWrongfulAttributionProbabilityMarginal(precautionLevel)
                  .Should().BeApproximately(expected, 1e-12);
        }

        [TestMethod]
        public void WrongfulProbabilityGivenHiddenMatchesClosedForm()
        {
            // Reflect precaution‑power factors
            var ppField = typeof(PrecautionImpactModel)
                .GetField("precautionPowerFactors", BindingFlags.NonPublic | BindingFlags.Instance);
            var powerFactors = (double[])ppField.GetValue(impact);

            const int h = 0;
            const int k = 1;

            double pCaused = impact.PAccidentNoPrecaution *
                             Math.Pow(powerFactors[h], k);

            double pWrongful = (1.0 - pCaused) * impact.PAccidentWrongfulAttribution;
            double expected = pWrongful / (pCaused + pWrongful);

            double actual = impact.GetWrongfulAttributionProbabilityGivenHiddenState(h, k);
            actual.Should().BeApproximately(expected, 1e-12);
        }


        // ---------------------------------------------------------------------
        // Error handling
        // ---------------------------------------------------------------------
        [TestMethod]
        public void InvalidConfigurationThrows()
        {
            Action act = () => new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 0,
                pAccidentNoPrecaution: 0.25,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactors: new[] { 0.8, 0.6 });

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
