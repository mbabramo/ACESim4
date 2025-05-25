using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimTest
{
    /// <summary>
    /// MSTest + FluentAssertions checks for CourtDecisionModel.
    /// Hidden 0 ⇒ factor 0.8 ; Hidden 1 ⇒ factor 0.6.
    /// </summary>
    [TestClass]
    public sealed class CourtDecisionModelTests
    {
        CourtDecisionModel courtDeterministic;
        CourtDecisionModel courtNoisy;

        [TestInitialize]
        public void Init()
        {
            var impact = new PrecautionImpactModel(
                hiddenCount: 2,
                precautionLevels: 2,
                pAccidentNoActivity: 0.01,
                pAccidentNoPrecaution: 0.25,
                precautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6,
                liabilityThreshold: 1.0);

            // deterministic signals (σ ~ 0)
            var detSignals = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4);
            courtDeterministic = new CourtDecisionModel(impact, detSignals, 0.04, 1.0, 1.0);

            // noisy signals (σ = 0.2)
            var noisySignals = new PrecautionSignalModel(2, 2, 2, 2, 0.2, 0.2, 0.2);
            courtNoisy = new CourtDecisionModel(impact, noisySignals, 0.04, 1.0, 1.0);
        }

        [TestMethod]
        public void Deterministic_RatiosAndLiability()
        {
            // Court signal 0 ➔ hidden 0  (ratio 1.25 / 1.00)
            courtDeterministic.GetBenefitCostRatio(0, 0).Should().BeApproximately(1.25, 1e-6);
            courtDeterministic.GetBenefitCostRatio(0, 1).Should().BeApproximately(1.00, 1e-6);
            courtDeterministic.IsLiable(0, 0).Should().BeTrue();
            courtDeterministic.IsLiable(0, 1).Should().BeFalse();

            // Court signal 1 ➔ hidden 1  (ratio 2.50 / 1.50)
            courtDeterministic.GetBenefitCostRatio(1, 0).Should().BeApproximately(2.50, 1e-6);
            courtDeterministic.GetBenefitCostRatio(1, 1).Should().BeApproximately(1.50, 1e-6);
            courtDeterministic.IsLiable(1, 0).Should().BeTrue();
            courtDeterministic.IsLiable(1, 1).Should().BeFalse();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void Noisy_RatioMonotoneAndThreshold(int signal)
        {
            double r0 = courtNoisy.GetBenefitCostRatio(signal, 0);
            double r1 = courtNoisy.GetBenefitCostRatio(signal, 1);
            r0.Should().BeGreaterOrEqualTo(r1 - 1e-9);

            courtNoisy.IsLiable(signal, 0).Should().Be(r0 >= 1.0);
            courtNoisy.IsLiable(signal, 1).Should().BeFalse(); // max precaution never liable
        }
    }
}
