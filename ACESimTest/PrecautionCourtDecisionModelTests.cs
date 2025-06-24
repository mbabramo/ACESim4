using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace ACESimTest
{
    [TestClass]
    public sealed class PrecautionCourtDecisionModelTests
    {
        // ------------------------------------------------------------------  calibration constants
        const int HiddenStates = 2;
        const int PrecautionLevels = 2;
        const double PAccidentNoPrecaution = 0.25;   // pMax
        const double PMinLow = 0.18;
        const double PMinHigh = 0.08;
        const double AlphaLow = 1.0;
        const double AlphaHigh = 1.0;
        const double HarmCost = 1.0;
        const double UnitPrecautionCost = 0.07;
        const double LiabilityThreshold = 1.0;
        const int NumCourtSignalsNoisy = 100;

        // ------------------------------------------------------------------  helpers
        static double[] Normalize(double[] v)
        {
            double sum = v.Sum();
            return sum == 0.0 ? v.Select(_ => 0.0).ToArray()
                              : v.Select(x => x / sum).ToArray();
        }

        // ------------------------------------------------------------------  fixtures
        PrecautionImpactModel impact;
        PrecautionCourtDecisionModel courtDeterministic;
        PrecautionCourtDecisionModel courtNoisy;

        [TestInitialize]
        public void Init()
        {
            impact = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                pMinLow: PMinLow,
                pMinHigh: PMinHigh,
                alphaLow: AlphaLow,
                alphaHigh: AlphaHigh,
                marginalPrecautionCost: UnitPrecautionCost,
                harmCost: HarmCost,
                liabilityThreshold: LiabilityThreshold,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);   // ensure monotone ratios

            // almost noise-free ⇒ deterministic
            var detSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,
                1e-4, 1e-4, 1e-4);
            courtDeterministic = new PrecautionCourtDecisionModel(impact, detSignals);

            // realistic noise model
            var noisySignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, NumCourtSignalsNoisy,
                0.20, 0.20, 0.20);
            courtNoisy = new PrecautionCourtDecisionModel(impact, noisySignals);
        }


        // ------------------------------------------------------------------  deterministic baseline
        [TestMethod]
        public void Deterministic_RatioMonotoneAndLiability()
        {
            for (int c = 0; c < 2; c++)
            {
                double r0 = courtDeterministic.GetBenefitCostRatio(c, 0);
                double r1 = courtDeterministic.GetBenefitCostRatio(c, 1);

                r0.Should().BeGreaterOrEqualTo(r1 - 1e-12);
                courtDeterministic.IsLiable(c, 0).Should().Be(r0 >= LiabilityThreshold);
                courtDeterministic.IsLiable(c, 1).Should().Be(r1 >= LiabilityThreshold);
            }
        }

        // ------------------------------------------------------------------  noisy model – basic sanity
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void Noisy_RatioMonotoneAndThreshold(int courtSignal)
        {
            double r0 = courtNoisy.GetBenefitCostRatio(courtSignal, 0);
            double r1 = courtNoisy.GetBenefitCostRatio(courtSignal, 1);

            r0.Should().BeGreaterOrEqualTo(r1 - 1e-12);
            courtNoisy.IsLiable(courtSignal, 0).Should().Be(r0 >= LiabilityThreshold);
            courtNoisy.IsLiable(courtSignal, 1).Should().Be(r1 >= LiabilityThreshold);
        }

        // ------------------------------------------------------------------  internal consistency
        [TestMethod]
        public void ExpectedBenefitEqualsRatioTimesCost()
        {
            for (int c = 0; c < 2; c++)
                for (int k = 0; k < 2; k++)
                {
                    double benefit = courtNoisy.GetExpectedBenefit(c, k);
                    double ratio = courtNoisy.GetBenefitCostRatio(c, k);
                    benefit.Should().BeApproximately(ratio * UnitPrecautionCost, 1e-9);
                }
        }


        // ------------------------------------------------------------------  threshold sensitivity
        [TestMethod]
        public void HigherThresholdReducesLiability()
        {
            var stricterImpact = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                pMinLow: PMinLow,
                pMinHigh: PMinHigh,
                alphaLow: AlphaLow,
                alphaHigh: AlphaHigh,
                marginalPrecautionCost: UnitPrecautionCost,
                harmCost: HarmCost,
                liabilityThreshold: 3.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

            var sig = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,
                1e-4, 1e-4, 1e-4);

            var stricter = new PrecautionCourtDecisionModel(stricterImpact, sig);
            stricter.IsLiable(1, 0).Should().BeFalse();
        }


        // ------------------------------------------------------------------  court-signal distributions (deterministic model)
        [TestMethod]
        public void LiabilityConditionalCourtDistributionNormalises()
        {
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndLiability(1, 1, 0);
            dist.Sum().Should().BeApproximately(1.0, 1e-9);
            dist.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        [TestMethod]
        public void NoLiabilityConditionalCourtDistributionNormalises()
        {
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndNoLiability(0, 0, 1);
            dist.Sum().Should().BeApproximately(1.0, 1e-9);
            dist.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        // ------------------------------------------------------------------  posterior checks
        [TestMethod]
        public void HiddenPosteriorFromDeterministicCourtSignalIsPointMass()
        {
            double[] courtDist = { 1.0, 0.0 };
            var posterior = courtDeterministic.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 0, courtDist);

            posterior.Sum().Should().BeApproximately(1.0, 1e-9);
            posterior.Count(p => p > 1e-6).Should().Be(1);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0, true)]
        [DataRow(0, 0, 1, false)]
        [DataRow(1, 1, 0, true)]
        [DataRow(1, 1, 1, false)]
        public void CourtSignalConditioningNormalises(
            int plaintiffSig, int defendantSig, int precautionLevel, bool liable)
        {
            double[] dist = liable
                ? courtNoisy.GetCourtSignalDistributionGivenSignalsAndLiability(plaintiffSig, defendantSig, precautionLevel)
                : courtNoisy.GetCourtSignalDistributionGivenSignalsAndNoLiability(plaintiffSig, defendantSig, precautionLevel);

            dist.Sum().Should().BeApproximately(1.0, 1e-8);
            dist.Should().OnlyContain(p => p >= 0 && p <= 1);
        }

        [TestMethod]
        public void PosteriorMatchesPriorWhenCourtEvidenceUniform()
        {
            double[] uniformCourt = Enumerable.Repeat(1.0 / NumCourtSignalsNoisy, NumCourtSignalsNoisy).ToArray();
            double[] posterior = courtNoisy.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 1, uniformCourt);

            var freshSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2, 0.2, 0.2, 0.2);
            double[] pPost = freshSignals.GetHiddenPosteriorFromPlaintiffSignal(0);
            double[] dPost = freshSignals.GetHiddenPosteriorFromDefendantSignal(1);
            double[] expected = Normalize(pPost.Zip(dPost, (p, d) => p * d).ToArray());

            for (int h = 0; h < expected.Length; h++)
                posterior[h].Should().BeApproximately(expected[h], 1e-8);
        }

        // ------------------------------------------------------------------  liability probability trends
        [TestMethod]
        public void AccidentEvidenceRaisesLiabilityOdds()
        {
            double[] noAcc = courtNoisy.GetLiabilityOutcomeProbabilities(0, 1, false, 0);
            double[] acc = courtNoisy.GetLiabilityOutcomeProbabilities(0, 1, true, 0);

            acc[1].Should().BeGreaterThan(noAcc[1] - 1e-12);
        }

        [DataTestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 1)]
        public void LiabilityProbabilityDecreasesWithPrecaution(int pSig, int dSig)
        {
            double[] lowPrec = courtNoisy.GetLiabilityOutcomeProbabilities(pSig, dSig, true, 0);
            double[] hiPrec = courtNoisy.GetLiabilityOutcomeProbabilities(pSig, dSig, true, 1);

            lowPrec[1].Should().BeGreaterOrEqualTo(hiPrec[1] - 1e-12);
        }

        [TestMethod]
        public void AllOutcomeProbabilitiesNormalise()
        {
            for (int p = 0; p < 2; p++)
                for (int d = 0; d < 2; d++)
                    for (int k = 0; k < 2; k++)
                    {
                        double[] prAcc = courtNoisy.GetLiabilityOutcomeProbabilities(p, d, true, k);
                        double[] prNo = courtNoisy.GetLiabilityOutcomeProbabilities(p, d, false, k);
                        (prAcc[0] + prAcc[1]).Should().BeApproximately(1.0, 1e-8);
                        (prNo[0] + prNo[1]).Should().BeApproximately(1.0, 1e-8);
                    }
        }

        // ------------------------------------------------------------------  verdict evidence tilts hidden-state odds
        [TestMethod]
        public void CourtVerdictEvidenceTiltsOdds()
        {
            var sigField = typeof(PrecautionCourtDecisionModel)
                             .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var tableField = typeof(PrecautionCourtDecisionModel)
                             .GetField("liableProbGivenHidden", BindingFlags.NonPublic | BindingFlags.Instance);

            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            var liableTbl = (double[][])tableField.GetValue(courtNoisy);

            int hi = liableTbl[1][0] > liableTbl[0][0] ? 1 : 0;
            int lo = 1 - hi;

            double[] postLiab = courtNoisy.GetHiddenPosteriorFromPath(1, 1, true, 0, true);
            double[] postNoLi = courtNoisy.GetHiddenPosteriorFromPath(1, 1, true, 0, false);

            double oddsLiab = postLiab[hi] / (postLiab[lo] + 1e-12);
            double oddsNoLi = postNoLi[hi] / (postNoLi[lo] + 1e-12);

            oddsLiab.Should().BeGreaterThan(oddsNoLi);
        }
    }
}
