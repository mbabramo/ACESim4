using System;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Games.LitigGame.PrecautionModel;

namespace ACESimTest
{
    /// <summary>
    /// MSTest + FluentAssertions checks for CourtDecisionModel and the new Bayesian helpers.
    /// Hidden 0 ⇒ factor 0.8 ; Hidden 1 ⇒ factor 0.6.
    /// </summary>
    [TestClass]
    public sealed class PrecautionCourtDecisionModelTests
    {
        PrecautionCourtDecisionModel courtDeterministic;
        PrecautionCourtDecisionModel courtNoisy;
        PrecautionImpactModel impact;

        [TestInitialize]
        public void Init()
        {
            impact = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoActivity: 0.01,
                pAccidentNoPrecaution: 0.25,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6,
                liabilityThreshold: 1.0);

            // deterministic signals (σ ≈ 0)
            var detSignals = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4);
            courtDeterministic = new PrecautionCourtDecisionModel(impact, detSignals, 1.0, 1.0);

            // noisy signals (σ = 0.2)
            var noisySignals = new PrecautionSignalModel(2, 2, 2, 2, 0.2, 0.2, 0.2);
            courtNoisy = new PrecautionCourtDecisionModel(impact, noisySignals, 1.0, 1.0);
        }

        // ------------------------------------------------------------------
        // Existing baseline tests
        // ------------------------------------------------------------------

        [TestMethod]
        public void Deterministic_RatiosAndLiability()
        {
            courtDeterministic.GetBenefitCostRatio(0, 0).Should().BeApproximately(1.25, 1e-6);
            courtDeterministic.GetBenefitCostRatio(0, 1).Should().BeApproximately(1.00, 1e-6);
            courtDeterministic.IsLiable(0, 0).Should().BeTrue();
            courtDeterministic.IsLiable(0, 1).Should().BeFalse();

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
            courtNoisy.IsLiable(signal, 1).Should().BeFalse();   // max precaution never liable
        }

        [TestMethod]
        public void ExpectedBenefitMatchesRatioTimesCost()
        {
            for (int s = 0; s < 2; s++)
                for (int k = 0; k < 2; k++)
                {
                    double benefit = courtDeterministic.GetExpectedBenefit(s, k);
                    double ratio = courtDeterministic.GetBenefitCostRatio(s, k);
                    benefit.Should().BeApproximately(ratio * 0.04, 1e-6);
                }
        }

        [TestMethod]
        public void InvalidSignalOrPrecautionThrows()
        {
            Action bad1 = () => courtDeterministic.GetBenefitCostRatio(99, 0);
            Action bad2 = () => courtDeterministic.GetExpectedBenefit(0, 99);
            Action bad3 = () => courtDeterministic.IsLiable(-1, 1);

            bad1.Should().Throw<ArgumentOutOfRangeException>();
            bad2.Should().Throw<ArgumentOutOfRangeException>();
            bad3.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void ThresholdMattersForLiability()
        {
            var sig = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4);
            var stricter = new PrecautionCourtDecisionModel(impact, sig, 1.0, 3.0); // threshold = 3.0

            stricter.IsLiable(0, 0).Should().BeFalse(); // ratio 1.25 < threshold 3.0
        }

        // ------------------------------------------------------------------
        // New Bayesian-helper tests
        // ------------------------------------------------------------------

        [TestMethod]
        public void LiabilityConditionedCourtDistDeterministic()
        {
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndLiability(0, 0, 0);

            dist.Sum().Should().BeApproximately(1.0, 1e-6);
            dist[0].Should().BeApproximately(1.0, 1e-6);
            dist[1].Should().BeApproximately(0.0, 1e-6);
        }

        [TestMethod]
        public void NoLiabilityConditionedCourtDistDeterministic()
        {
            // plaintiff 0, defendant 0 → hidden 0 → court signal 0 with certainty
            // precaution level 1 can never be negligent, so non-liability must be on signal 0
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndNoLiability(0, 0, 1);

            dist.Sum().Should().BeApproximately(1.0, 1e-6);
            dist[0].Should().BeApproximately(1.0, 1e-6);
            dist[1].Should().BeApproximately(0.0, 1e-6);
        }

        [TestMethod]
        public void HiddenPosteriorDeterministic()
        {
            double[] courtDist = { 1.0, 0.0 }; // court signal 0 certain
            var posterior = courtDeterministic.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 0, courtDist);

            posterior[0].Should().BeApproximately(1.0, 1e-6);
            posterior[1].Should().BeApproximately(0.0, 1e-6);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0, true)]   // liable path
        [DataRow(0, 0, 1, false)]  // no-liable path
        [DataRow(1, 1, 0, true)]
        [DataRow(1, 1, 1, false)]
        public void CourtSignalConditioningSumsToOne(
            int plaintiffSig, int defendantSig, int precautionLevel, bool liable)
        {
            double[] dist = liable
                ? courtNoisy.GetCourtSignalDistributionGivenSignalsAndLiability(plaintiffSig, defendantSig, precautionLevel)
                : courtNoisy.GetCourtSignalDistributionGivenSignalsAndNoLiability(plaintiffSig, defendantSig, precautionLevel);

            dist.Sum().Should().BeApproximately(1.0, 1e-8);
            dist.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        [TestMethod]
        public void PosteriorMatchesPriorWhenCourtEvidenceIsUniform()
        {
            // uniform court evidence → posterior should equal PD-posterior
            double[] uniformCourt = { 0.5, 0.5 };
            var posterior = courtNoisy.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 1, uniformCourt);

            // recreate PD-only posterior using a fresh signal model (same parameters as courtNoisy)
            var sigModel = new PrecautionSignalModel(2, 2, 2, 2, 0.2, 0.2, 0.2);
            double[] pPost = sigModel.GetHiddenPosteriorFromPlaintiffSignal(0);
            double[] dPost = sigModel.GetHiddenPosteriorFromDefendantSignal(1);
            double[] pdOnly = Normalize(pPost.Zip(dPost, (p, d) => p * d).ToArray());

            posterior.Length.Should().Be(pdOnly.Length);
            for (int h = 0; h < posterior.Length; h++)
                posterior[h].Should().BeApproximately(pdOnly[h], 1e-8);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0)]   // hidden 0 most likely
        [DataRow(1, 1, 1)]   // hidden 1 most likely
        public void DeterministicSignalsYieldPointPosterior(int pSig, int dSig, int hiddenExpected)
        {
            double[] courtDist = { hiddenExpected == 0 ? 1.0 : 0.0,
                                   hiddenExpected == 1 ? 1.0 : 0.0 };
            var posterior = courtDeterministic.GetHiddenPosteriorFromSignalsAndCourtDistribution(pSig, dSig, courtDist);

            posterior[hiddenExpected].Should().BeApproximately(1.0, 1e-6);
            posterior.Sum().Should().BeApproximately(1.0, 1e-6);
        }

        private static double[] Normalize(double[] vec)
        {
            double sum = vec.Sum();
            return sum == 0.0 ? vec.Select(_ => 0.0).ToArray()
                              : vec.Select(v => v / sum).ToArray();
        }
    }


    
}
