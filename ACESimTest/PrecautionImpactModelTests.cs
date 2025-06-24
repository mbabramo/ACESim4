using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ACESimTest
{
    [TestClass]
    public sealed class PrecautionImpactModelTests
    {
        const double HarmCost = 1.0;
        const double MarginalPrecautionCost = 0.04;
        const double LiabilityThreshold = 1.0;

        static PrecautionImpactModel BuildImpact(bool benefitAtChosen) =>
            new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.20,
                pMinHigh: 0.10,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: MarginalPrecautionCost,
                harmCost: HarmCost,
                liabilityThreshold: LiabilityThreshold,
                pAccidentWrongfulAttribution: 0.0,
                accidentProbabilityOverride: null,
                benefitAtChosenLevel: benefitAtChosen);

        [TestMethod]
        public void AccidentProbabilitiesExact()
        {
            double[,] expected =
            {
                { 0.25, 0.20833333333333334, 0.16666666666666669 },
                { 0.25, 0.19166666666666668, 0.13333333333333336 }
            };

            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);
                for (int h = 0; h < 2; h++)
                    for (int k = 0; k <= 2; k++)
                        impact.GetAccidentProbability(h, k)
                              .Should().BeApproximately(expected[h, k], 1e-9);
            }
        }

        [TestMethod]
        public void MarginalAccidentProbabilities()
        {
            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);
                impact.GetAccidentProbabilityMarginal(0).Should().BeApproximately(0.25, 1e-9);
                impact.GetAccidentProbabilityMarginal(1).Should().BeApproximately(0.20, 1e-9);
            }
        }

        [TestMethod]
        public void RiskReductionMonotone()
        {
            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);

                for (int h = 0; h < 2; h++)
                {
                    double prev = double.MaxValue;
                    for (int k = 0; k < 2; k++)
                    {
                        double delta = impact.GetRiskReduction(h, k);
                        delta.Should().BeGreaterOrEqualTo(0);

                        if (!mode)                       // “next-level” rule only
                            delta.Should().BeLessOrEqualTo(prev + 1e-12);

                        prev = delta;
                    }
                }
            }
        }

        [TestMethod]
        public void GroundTruthNegligence()
        {
            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);

                for (int h = 0; h < 2; h++)
                    for (int k = 0; k < 2; k++)
                    {
                        double benefit  = impact.GetRiskReduction(h, k) * HarmCost;
                        bool expected   = benefit / MarginalPrecautionCost >= LiabilityThreshold - 1e-12;
                        impact.IsTrulyLiable(h, k).Should().Be(expected);
                    }
            }
        }

        [TestMethod]
        public void WrongfulAttributionProbabilityMarginalIsZero()
        {
            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);
                impact.GetWrongfulAttributionProbabilityMarginal(0).Should().Be(0.0);
                impact.GetWrongfulAttributionProbabilityMarginal(1).Should().Be(0.0);
            }
        }

        [TestMethod]
        public void WrongfulProbabilityGivenHiddenIsZero()
        {
            foreach (bool mode in new[] { false, true })
            {
                var impact = BuildImpact(mode);
                impact.GetWrongfulAttributionProbabilityGivenHiddenState(0, 1).Should().Be(0.0);
            }
        }

        [TestMethod]
        public void InvalidConfigurationThrows()
        {
            Action act = () => new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 0,            // invalid
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.20,
                pMinHigh: 0.10,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: MarginalPrecautionCost,
                harmCost: HarmCost,
                benefitAtChosenLevel: true);

            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void OutOfBoundsAccessThrows()
        {
            var impact = BuildImpact(false);
            Action bad1 = () => impact.GetAccidentProbability(99, 0);
            Action bad2 = () => impact.IsTrulyLiable(0, 99);
            bad1.Should().Throw<ArgumentOutOfRangeException>();
            bad2.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
