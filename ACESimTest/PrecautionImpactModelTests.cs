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
            var model = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.20,
                pMinHigh: 0.10,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                pAccidentWrongfulAttribution: 0.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);   // discrete-step rule

            for (int h = 0; h < 2; h++)
            {
                double prev = double.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    double delta = model.GetRiskReduction(h, k);
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
        public void GroundTruthNegligence_BothRules()
        {
            // shared calibration
            const int HLevels = 2, KLevels = 2;
            const double pMax = 0.25, pMinLow = 0.20, pMinHigh = 0.10;
            const double alpha = 1.0, cost = 0.04, harm = 1.0, thresh = 1.0;

            // ε-based rule (default) — nobody liable
            var epsImpact = new PrecautionImpactModel(
                HLevels, KLevels, pMax, pMinLow, pMinHigh, alpha, alpha,
                cost, harm, thresh,
                benefitRule: MarginalBenefitRule.AtChosenPrecautionLevel,
                epsilonForBenefit: 1e-6);

            epsImpact.IsTrulyLiable(0, 0).Should().BeFalse();
            epsImpact.IsTrulyLiable(0, 1).Should().BeFalse();
            epsImpact.IsTrulyLiable(1, 0).Should().BeFalse();
            epsImpact.IsTrulyLiable(1, 1).Should().BeFalse();

            // discrete-step rule — only non-top levels can be liable
            var discImpact = new PrecautionImpactModel(
                HLevels, KLevels, pMax, pMinLow, pMinHigh, alpha, alpha,
                cost, harm, thresh,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

            discImpact.IsTrulyLiable(0, 0).Should().BeTrue();
            discImpact.IsTrulyLiable(0, 1).Should().BeFalse();   // top level ⇒ ΔP = 0
            discImpact.IsTrulyLiable(1, 0).Should().BeTrue();
            discImpact.IsTrulyLiable(1, 1).Should().BeFalse();
        }

        [TestMethod]
        public void EpsilonRuleDerivativeSanity()
        {
            // model with clearly analytic curve
            const int HiddenStates   = 2;
            const int PrecLevels     = 4;
            const double pMax        = 0.30;
            const double pMinLow     = 0.15;
            const double pMinHigh    = 0.05;
            const double alpha       = 1.2;
            const double cost        = 0.01;
            const double harm        = 1.0;
            const double thresh      = 1.0;
            const double eps         = 1e-6;

            var model = new PrecautionImpactModel(
                HiddenStates, PrecLevels, pMax, pMinLow, pMinHigh, alpha, alpha,
                cost, harm, thresh,
                benefitRule: MarginalBenefitRule.AtChosenPrecautionLevel,
                epsilonForBenefit: eps);

            int h = 0;          // sample hidden index
            int k = 2;          // interior precaution level

            // finite-difference from the model
            double delta = model.GetRiskReduction(h, k);
            double fdDerivative = delta / eps;

            // analytic derivative (see model’s closed form)
            double hiddenFrac = (h + 1.0) / (HiddenStates + 1.0);
            double pMin       = pMinLow + (pMinHigh - pMinLow) * hiddenFrac;
            double A          = pMax - pMin;
            double t          = k / (double)PrecLevels;
            double analytic   = -A * alpha * Math.Pow(1.0 - t, alpha - 1.0) / PrecLevels;

            Math.Abs(fdDerivative - (-analytic)).Should().BeLessThan(1e-3);
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
