using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace ACESimTest.GameTests
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

        [TestMethod]
        public void DiscreteRule_TopLevelIsSafeHarbor_ForCourtAndImpact_RelativeToNextDiscreteLevel()
        {
            // Uses the class fixtures (impact, courtDeterministic, courtNoisy) built in Init()
            // Top level is index 1 because PrecautionLevels == 2 in this file.
            int maxK = 1;

            // Impact layer: ΔP at top must be exactly 0 for all hidden states.
            for (int h = 0; h < HiddenStates; h++)
                impact.GetRiskReduction(h, maxK).Should().Be(0.0);

            // Court layer (deterministic model here): benefit-cost ratio and liability must be 0/false at top.
            for (int c = 0; c < 2; c++)
            {
                courtDeterministic.GetBenefitCostRatio(c, maxK).Should().Be(0.0);
                courtDeterministic.IsLiable(c, maxK).Should().BeFalse();
            }

            // Court layer (noisy model too): never liable at top.
            for (int c = 0; c < NumCourtSignalsNoisy; c++)
                courtNoisy.IsLiable(c, maxK).Should().BeFalse();

            // Integrated liable probability at top must be zero for each hidden state.
            for (int h = 0; h < HiddenStates; h++)
                courtNoisy.GetLiabilityOutcomeProbabilities(h, maxK)[1].Should().Be(0.0);
        }

        [TestMethod]
        public void HypotheticalRule_TopLevelNotSafeHarbor_ForCourtAndImpact()
        {
            const int HiddenStates = 2;
            const int PrecautionLevels = 2;

            var impactHypo = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.01,   // ↓ lower cost so BCR(top) > 1.0
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteHypotheticalLevel);

            // Deterministic-ish signals, 3 court buckets to avoid symmetry ties
            var detSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 3,
                1e-4, 1e-4, 1e-4, includeExtremes: true);

            var courtHypo = new PrecautionCourtDecisionModel(impactHypo, detSignals);

            int topK = PrecautionLevels - 1;

            // Impact layer: ΔP at top > 0 for both hidden states
            for (int h = 0; h < HiddenStates; h++)
            {
                double delta = impactHypo.GetRiskReduction(h, topK);
                delta.Should().BeGreaterThan(0.0);

                double ratio = impactHypo.GetBenefitCostRatio(h, topK);
                ratio.Should().BeGreaterThan(1.0);
            }

            // Court layer: at least one court signal should be liable at the top level
            bool anyLiable = false;
            for (int c = 0; c < 3; c++)
                anyLiable |= courtHypo.IsLiable(c, topK);

            anyLiable.Should().BeTrue("with ΔP>0 and BCR(top)>threshold, some court bins should imply liability at the top level");
        }

        [TestMethod]
        public void DiscreteRule_ConditioningOnImpossibleLiabilityYieldsUniformCourtSignal_RelativeToNextDiscreteLevel()
        {
            // At the top level, liability is impossible under the discrete next-level rule.
            int maxK = 1;

            // Marginal check: liable probability is zero at top (pick any p,d).
            courtNoisy.GetLiabilityOutcomeProbabilities(0, maxK)[1].Should().Be(0.0);

            // Conditional distribution on an impossible (liable) verdict must fall back to uniform.
            // Use the deterministic model to keep the court-signal count small (2).
            double[] cond = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndLiability(1, 1, maxK);

            cond.Length.Should().Be(2);
            cond.Sum().Should().BeApproximately(1.0, 1e-12);
            double expected = 1.0 / cond.Length;
            foreach (double v in cond)
                v.Should().BeApproximately(expected, 1e-12);
        }

        [TestMethod]
        public void HypotheticalRule_ConditioningOnLiabilityAtTopIsNotUniform()
        {
            const int HiddenStates = 2;
            const int PrecautionLevels = 2;

            var impactHypo = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.01,   // ensure top-level BCR is strong enough
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteHypotheticalLevel);

            var detSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 3,
                1e-4, 1e-4, 1e-4, includeExtremes: true);

            var courtHypo = new PrecautionCourtDecisionModel(impactHypo, detSignals);

            int topK = PrecautionLevels - 1;

            // Pick a concrete (p,d) slice; with near-deterministic signals this selects a
            // specific hidden state and induces a non-uniform court distribution before filtering.
            var dist = courtHypo.GetCourtSignalDistributionGivenSignalsAndLiability(1, 1, topK);

            dist.Sum().Should().BeApproximately(1.0, 1e-12);

            double uniform = 1.0 / dist.Length;
            // Non-uniformity: at least one bin differs from exact 1/C by more than rounding noise.
            dist.Should().Contain(v => Math.Abs(v - uniform) > 1e-9,
                "with real mass on liable court bins, the filtered-and-renormalized distribution should not be perfectly uniform");
        }

        [TestMethod]
        public void DiscreteRule_ThresholdStrictInequality_HitsExactlyAtThresholdIsNotLiable()
        {
            // Build a deterministic court model (tiny sigmas; include extremes for identical bin edges)
            PrecautionCourtDecisionModel BuildWithThreshold(double threshold)
            {
                var localImpact = new PrecautionImpactModel(
                    precautionPowerLevels: HiddenStates,
                    precautionLevels: PrecautionLevels,
                    pAccidentNoPrecaution: PAccidentNoPrecaution,
                    pMinLow: PMinLow,
                    pMinHigh: PMinHigh,
                    alphaLow: AlphaLow,
                    alphaHigh: AlphaHigh,
                    marginalPrecautionCost: UnitPrecautionCost,
                    harmCost: HarmCost,
                    liabilityThreshold: threshold,
                    benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

                var detSignals = new PrecautionSignalModel(
                    numPrecautionPowerLevels: HiddenStates,
                    numPlaintiffSignals: 2,
                    numDefendantSignals: 2,
                    numCourtSignals: 2,
                    sigmaPlaintiff: 1e-4,
                    sigmaDefendant: 1e-4,
                    sigmaCourt: 1e-4,
                    includeExtremes: true);

                return new PrecautionCourtDecisionModel(localImpact, detSignals);
            }

            // Scan ratios once on a fixed grid (threshold irrelevant for ratio values themselves)
            var scan = BuildWithThreshold(0.0);
            int maxK = PrecautionLevels - 1;

            double minPositive = double.PositiveInfinity;
            double maxPositive = 0.0;
            (int cStar, int kStar) = (-1, -1);

            for (int c = 0; c < 2; c++)
                for (int k = 0; k < maxK; k++)
                {
                    double r = scan.GetBenefitCostRatio(c, k);
                    if (r > 0.0)
                    {
                        if (r < minPositive)
                        {
                            minPositive = r;
                            (cStar, kStar) = (c, k);
                        }
                        if (r > maxPositive)
                            maxPositive = r;
                    }
                }

            minPositive.Should().NotBe(double.PositiveInfinity, "there must be at least one positive ratio below the top level");
            maxPositive.Should().BeGreaterThan(minPositive);

            // Below threshold: at least one (c,k) is liable; top never is.
            var below = BuildWithThreshold(minPositive - 1e-12);
            bool anyLiableBelow = false;
            for (int c = 0; c < 2; c++)
            {
                for (int k = 0; k < maxK; k++)
                    anyLiableBelow |= below.IsLiable(c, k);
                below.IsLiable(c, maxK).Should().BeFalse();
            }
            anyLiableBelow.Should().BeTrue();

            // Exactly at the smallest positive ratio: strict ">" means (c*,k*) is NOT liable; top never is.
            var at = BuildWithThreshold(minPositive);
            at.IsLiable(cStar, kStar).Should().BeFalse("strict inequality should not treat equality as liable");
            for (int c = 0; c < 2; c++)
                at.IsLiable(c, maxK).Should().BeFalse();

            // Above the largest positive ratio: nothing is liable anywhere (including top).
            var above = BuildWithThreshold(maxPositive + 1e-12);
            for (int c = 0; c < 2; c++)
            {
                for (int k = 0; k < maxK; k++)
                    above.IsLiable(c, k).Should().BeFalse();
                above.IsLiable(c, maxK).Should().BeFalse();
            }
        }


        [TestMethod]
        public void DiscreteRule_WithWrongfulAttribution_SafeHarborStillHoldsAndInteriorHasPositiveRatios()
        {
            // Build local models with nonzero wrongful attribution to ensure discrete rule ignores epsilon paths.
            var localImpact = new PrecautionImpactModel(
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
                pAccidentWrongfulAttribution: 0.20,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

            var localSignals = new PrecautionSignalModel(HiddenStates, 2, 2, 2, 0.20, 0.20, 0.20);
            var localCourt = new PrecautionCourtDecisionModel(localImpact, localSignals);

            int maxK = PrecautionLevels - 1;

            // Safe harbor at top is preserved
            for (int h = 0; h < HiddenStates; h++)
                localImpact.GetRiskReduction(h, maxK).Should().Be(0.0);
            for (int c = 0; c < 2; c++)
                localCourt.IsLiable(c, maxK).Should().BeFalse();

            // At least one interior ratio is positive (so the rule is actually meaningful below top)
            bool anyPositiveInterior = false;
            for (int c = 0; c < 2; c++)
                anyPositiveInterior |= localCourt.GetBenefitCostRatio(c, 0) > 0.0;

            anyPositiveInterior.Should().BeTrue();
        }
        [TestMethod]
        public void DiscreteRule_EdgeCase_PrecautionLevelsEqualsOne_AllZeroAndNeverLiable()
        {
            // Single available level ⇒ it is the top; ensure ΔP = 0 and liability is impossible.
            var oneLevelImpact = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: 1,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                pMinLow: PMinLow,
                pMinHigh: PMinHigh,
                alphaLow: AlphaLow,
                alphaHigh: AlphaHigh,
                marginalPrecautionCost: UnitPrecautionCost,
                harmCost: HarmCost,
                liabilityThreshold: LiabilityThreshold,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

            var sig = new PrecautionSignalModel(HiddenStates, 2, 2, 3, 0.2, 0.2, 0.2);
            var court = new PrecautionCourtDecisionModel(oneLevelImpact, sig);

            // Only level index is 0.
            for (int h = 0; h < HiddenStates; h++)
                oneLevelImpact.GetRiskReduction(h, 0).Should().Be(0.0);

            for (int c = 0; c < 3; c++)
                court.IsLiable(c, 0).Should().BeFalse();

            // Conditional-on-liable (impossible) court distribution falls back to uniform.
            double[] cond = court.GetCourtSignalDistributionGivenSignalsAndLiability(0, 0, 0);
            cond.Length.Should().Be(3);
            cond.Sum().Should().BeApproximately(1.0, 1e-12);
            double expected = 1.0 / 3.0;
            foreach (double v in cond)
                v.Should().BeApproximately(expected, 1e-12);
        }

        [TestMethod]
        public void ProbitRule_Throws_When_Using_NonHypotheticalDiscreteRule()
        {
            // Impact uses the non-hypothetical discrete forward step.
            var impactNonHypo = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.05,
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteLevel);

            var sig = new PrecautionSignalModel(2, 2, 2, 3, 0.20, 0.20, 0.20);

            Action ctor = () => new PrecautionCourtDecisionModel(
                impactNonHypo,
                sig,
                decisionRule: CourtDecisionRule.ProbitThreshold,
                probitScale: 0.1);

            ctor.Should().Throw<InvalidOperationException>();
        }

        [TestMethod]
        public void ProbitRule_TopLevelLiabilityProb_BetweenZeroAndOne_And_IncreasingWithBCR()
        {
            // Same everything except UnitPrecautionCost (to shift BCR up).
            var impactLowBCR = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.06,   // higher cost → lower BCR
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteHypotheticalLevel);

            var impactHighBCR = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.02,   // lower cost → higher BCR
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteHypotheticalLevel);

            // Mild noise + 3 court signals to avoid symmetry.
            var sig = new PrecautionSignalModel(2, 2, 2, 3, 0.20, 0.20, 0.20);

            var courtLow  = new PrecautionCourtDecisionModel(impactLowBCR,  sig, CourtDecisionRule.ProbitThreshold, probitScale: 0.15);
            var courtHigh = new PrecautionCourtDecisionModel(impactHighBCR, sig, CourtDecisionRule.ProbitThreshold, probitScale: 0.15);

            int topK = 1; // with 2 levels, the top index is 1

            for (int h = 0; h < 2; h++)
            {
                double pLiabLow  = courtLow .GetLiabilityOutcomeProbabilities(h, topK)[1];
                double pLiabHigh = courtHigh.GetLiabilityOutcomeProbabilities(h, topK)[1];

                // Proper probabilities
                pLiabLow .Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);
                pLiabHigh.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);

                // Higher BCR ⇒ higher liability probability under probit.
                pLiabHigh.Should().BeGreaterThan(pLiabLow - 1e-12);
            }
        }

        [TestMethod]
        public void ProbitRule_CourtConditionalSlices_Normalize_And_Differ_Between_Liable_And_NoLiable()
        {
            var impact = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoPrecaution: 0.25,
                pMinLow: 0.18,
                pMinHigh: 0.08,
                alphaLow: 1.0,
                alphaHigh: 1.0,
                marginalPrecautionCost: 0.03,
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                benefitRule: MarginalBenefitRule.RelativeToNextDiscreteHypotheticalLevel);

            // Deterministic-ish parties + 3 court buckets to reduce exact ties
            var sig = new PrecautionSignalModel(2, 2, 2, 3, 1e-4, 1e-4, 1e-4, includeExtremes: true);

            var court = new PrecautionCourtDecisionModel(
                impact,
                sig,
                CourtDecisionRule.ProbitThreshold,
                probitScale: 0.10);

            // Choose a specific (p,d,k) slice and compare conditional dists.
            int pSig = 1, dSig = 1, k = 1;

            var liableSlice   = court.GetCourtSignalDistributionGivenSignalsAndLiability(pSig, dSig, k);
            var noLiableSlice = court.GetCourtSignalDistributionGivenSignalsAndNoLiability(pSig, dSig, k);

            // Normalization
            liableSlice.Sum().Should().BeApproximately(1.0, 1e-12);
            noLiableSlice.Sum().Should().BeApproximately(1.0, 1e-12);

            // The two conditional slices should not be identical under non-degenerate probit weighting.
            bool allEqual = true;
            for (int i = 0; i < liableSlice.Length; i++)
                allEqual &= Math.Abs(liableSlice[i] - noLiableSlice[i]) < 1e-12;

            allEqual.Should().BeFalse("liable vs no-liable renormalizations should weight court bins differently under probit");
        }


    }
}
