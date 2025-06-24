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
        // ------------------------------------------------------------------  calibration
        const int    HiddenStates           = 2;
        const int    PrecautionLevels       = 2;
        const double PAccidentNoPrecaution  = 0.25;
        const double PMinLow                = 0.18;
        const double PMinHigh               = 0.08;
        const double AlphaLow               = 1.0;
        const double AlphaHigh              = 1.0;
        const double HarmCost               = 1.0;
        const double MarginalPrecautionCost = 0.07;
        const double LiabilityThreshold     = 1.0;
        const int    NumCourtSignalsNoisy   = 100;

        static readonly bool[] Modes = { false, true };   // false = “next-level” rule, true = “chosen-level”

        // ------------------------------------------------------------------  helpers
        static (PrecautionImpactModel impact,
                PrecautionCourtDecisionModel deterministic,
                PrecautionCourtDecisionModel noisy)
        BuildModels(bool benefitAtChosenLevel)
        {
            var impact = new PrecautionImpactModel(
                HiddenStates,               // precaution-power levels
                PrecautionLevels,
                PAccidentNoPrecaution,
                PMinLow, PMinHigh,
                AlphaLow, AlphaHigh,
                MarginalPrecautionCost,
                HarmCost,
                liabilityThreshold: LiabilityThreshold,
                benefitAtChosenLevel: benefitAtChosenLevel);

            var detSig = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,      // tiny signal spaces → deterministic mapping
                1e-4, 1e-4, 1e-4);

            var noisySig = new PrecautionSignalModel(
                HiddenStates, 2, 2, NumCourtSignalsNoisy,
                0.20, 0.20, 0.20);

            return (impact,
                    new PrecautionCourtDecisionModel(impact, detSig),
                    new PrecautionCourtDecisionModel(impact, noisySig));
        }

        // ------------------------------------------------------------------  ratio ≥ 0  and  liability ↔ threshold
        [TestMethod]
        public void Deterministic_RatioNonNegativeAndLiabilityAlign()
        {
            foreach (bool mode in Modes)
            {
                var (_, courtDet, _) = BuildModels(mode);

                for (int courtSignal = 0; courtSignal < 2; courtSignal++)
                    for (int k = 0; k < PrecautionLevels; k++)
                    {
                        double ratio = courtDet.GetBenefitCostRatio(courtSignal, k);
                        ratio.Should().BeGreaterOrEqualTo(0);

                        bool liable = courtDet.IsLiable(courtSignal, k);
                        liable.Should().Be(ratio >= LiabilityThreshold);
                    }
            }
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void Noisy_RatioNonNegativeAndLiabilityAlign(int courtSignal)
        {
            foreach (bool mode in Modes)
            {
                var (_, _, courtNoisy) = BuildModels(mode);

                for (int k = 0; k < PrecautionLevels; k++)
                {
                    double ratio = courtNoisy.GetBenefitCostRatio(courtSignal, k);
                    ratio.Should().BeGreaterOrEqualTo(0);

                    bool liable = courtNoisy.IsLiable(courtSignal, k);
                    liable.Should().Be(ratio >= LiabilityThreshold);
                }
            }
        }

        // ------------------------------------------------------------------  internal consistency: benefit = ratio × cost
        [TestMethod]
        public void ExpectedBenefitEqualsRatioTimesCost()
        {
            foreach (bool mode in Modes)
            {
                var (_, _, court) = BuildModels(mode);

                for (int c = 0; c < 2; c++)
                    for (int k = 0; k < PrecautionLevels; k++)
                    {
                        double benefit = court.GetExpectedBenefit(c, k);
                        double ratio   = court.GetBenefitCostRatio(c, k);

                        benefit.Should()
                               .BeApproximately(ratio * MarginalPrecautionCost, 1e-9);
                    }
            }
        }

        // ------------------------------------------------------------------  higher threshold lowers liability odds
        [TestMethod]
        public void IncreasingThresholdReducesLiability()
        {
            foreach (bool mode in Modes)
            {
                var impact = new PrecautionImpactModel(
                    HiddenStates, PrecautionLevels,
                    PAccidentNoPrecaution,
                    PMinLow, PMinHigh,
                    AlphaLow, AlphaHigh,
                    MarginalPrecautionCost,
                    HarmCost,
                    liabilityThreshold: 3.0,          // stricter
                    benefitAtChosenLevel: mode);

                var signal = new PrecautionSignalModel(
                    HiddenStates, 2, 2, 2,
                    1e-4, 1e-4, 1e-4);

                var stricter = new PrecautionCourtDecisionModel(impact, signal);

                stricter.IsLiable(1, 0).Should().BeFalse();
            }
        }

        // ------------------------------------------------------------------  probability integrity checks (unchanged)
        [TestMethod]
        public void LiabilityOutcomeProbabilitiesNormalise()
        {
            foreach (bool mode in Modes)
            {
                var (_, _, court) = BuildModels(mode);

                for (int pSig = 0; pSig < 2; pSig++)
                    for (int dSig = 0; dSig < 2; dSig++)
                        for (int k = 0; k < PrecautionLevels; k++)
                        {
                            double[] acc = court.GetLiabilityOutcomeProbabilities(pSig, dSig, true,  k);
                            double[] no  = court.GetLiabilityOutcomeProbabilities(pSig, dSig, false, k);

                            (acc[0] + acc[1]).Should().BeApproximately(1.0, 1e-8);
                            (no [0] + no [1]).Should().BeApproximately(1.0, 1e-8);
                        }
            }
        }

                [TestMethod]
        public void Deterministic_RatioMonotoneAndLiability()
        {
            var (_, courtDet, _) = BuildModels(false);   // original “next-level” rule

            for (int courtSignal = 0; courtSignal < 2; courtSignal++)
            {
                double r0 = courtDet.GetBenefitCostRatio(courtSignal, 0);
                double r1 = courtDet.GetBenefitCostRatio(courtSignal, 1);

                r0.Should().BeGreaterOrEqualTo(r1 - 1e-12);
                courtDet.IsLiable(courtSignal, 0).Should().Be(r0 >= LiabilityThreshold);
                courtDet.IsLiable(courtSignal, 1).Should().Be(r1 >= LiabilityThreshold);
            }
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void Noisy_RatioMonotoneAndThreshold(int courtSignal)
        {
            var (_, _, courtNoisy) = BuildModels(false); // original “next-level” rule

            double r0 = courtNoisy.GetBenefitCostRatio(courtSignal, 0);
            double r1 = courtNoisy.GetBenefitCostRatio(courtSignal, 1);

            r0.Should().BeGreaterOrEqualTo(r1 - 1e-12);
            courtNoisy.IsLiable(courtSignal, 0).Should().Be(r0 >= LiabilityThreshold);
            courtNoisy.IsLiable(courtSignal, 1).Should().Be(r1 >= LiabilityThreshold);
        }

                // -------------------------------------------------- helper
        static double[] Normalize(double[] v)
        {
            double sum = v.Sum();
            return sum == 0.0 ? v.Select(_ => 0.0).ToArray()
                              : v.Select(x => x / sum).ToArray();
        }

        // -------------------------------------------------- court-signal distributions
        [TestMethod]
        public void LiabilityConditionalCourtDistributionNormalises()
        {
            foreach (bool mode in Modes)
            {
                var (_, courtDet, _) = BuildModels(mode);
                var dist = courtDet.GetCourtSignalDistributionGivenSignalsAndLiability(1, 1, 0);
                dist.Sum().Should().BeApproximately(1.0, 1e-9);
                dist.Should().OnlyContain(p => p >= 0 && p <= 1);
            }
        }

        [TestMethod]
        public void NoLiabilityConditionalCourtDistributionNormalises()
        {
            foreach (bool mode in Modes)
            {
                var (_, courtDet, _) = BuildModels(mode);
                var dist = courtDet.GetCourtSignalDistributionGivenSignalsAndNoLiability(0, 0, 1);
                dist.Sum().Should().BeApproximately(1.0, 1e-9);
                dist.Should().OnlyContain(p => p >= 0 && p <= 1);
            }
        }

        // -------------------------------------------------- posterior checks
        [TestMethod]
        public void HiddenPosteriorFromDeterministicCourtSignalIsPointMass()
        {
            foreach (bool mode in Modes)
            {
                var (_, courtDet, _) = BuildModels(mode);

                double[] courtDist = { 1.0, 0.0 };
                var posterior = courtDet.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 0, courtDist);

                posterior.Sum().Should().BeApproximately(1.0, 1e-9);
                posterior.Count(p => p > 1e-6).Should().Be(1);
            }
        }

        [DataTestMethod]
        [DataRow(0, 0, 0, true)]
        [DataRow(0, 0, 1, false)]
        [DataRow(1, 1, 0, true)]
        [DataRow(1, 1, 1, false)]
        public void CourtSignalConditioningNormalises(int plaintiffSig, int defendantSig, int precautionLevel, bool liable)
        {
            foreach (bool mode in Modes)
            {
                var (_, _, court) = BuildModels(mode);

                double[] dist = liable
                    ? court.GetCourtSignalDistributionGivenSignalsAndLiability(plaintiffSig, defendantSig, precautionLevel)
                    : court.GetCourtSignalDistributionGivenSignalsAndNoLiability(plaintiffSig, defendantSig, precautionLevel);

                dist.Sum().Should().BeApproximately(1.0, 1e-8);
                dist.Should().OnlyContain(p => p >= 0 && p <= 1);
            }
        }

        [TestMethod]
        public void PosteriorMatchesPriorWhenCourtEvidenceUniform()
        {
            foreach (bool mode in Modes)
            {
                var (_, _, courtNoisy) = BuildModels(mode);

                double[] uniformCourt = Enumerable.Repeat(1.0 / NumCourtSignalsNoisy, NumCourtSignalsNoisy).ToArray();
                double[] posterior = courtNoisy.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 1, uniformCourt);

                var freshSignals = new PrecautionSignalModel(HiddenStates, 2, 2, 2, 0.2, 0.2, 0.2);
                double[] pPost = freshSignals.GetHiddenPosteriorFromPlaintiffSignal(0);
                double[] dPost = freshSignals.GetHiddenPosteriorFromDefendantSignal(1);
                double[] expected = Normalize(pPost.Zip(dPost, (p, d) => p * d).ToArray());

                for (int h = 0; h < expected.Length; h++)
                    posterior[h].Should().BeApproximately(expected[h], 1e-8);
            }
        }

        // -------------------------------------------------- liability-probability trends
        [TestMethod]
        public void AccidentEvidenceRaisesLiabilityOdds()
        {
            foreach (bool mode in Modes)
            {
                var (_, _, court) = BuildModels(mode);

                double[] noAcc = court.GetLiabilityOutcomeProbabilities(0, 1, false, 0);
                double[] acc   = court.GetLiabilityOutcomeProbabilities(0, 1, true, 0);

                acc[1].Should().BeGreaterThan(noAcc[1] - 1e-12);
            }
        }

        [DataTestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 1)]
        public void LiabilityProbabilityDecreasesWithPrecaution(int pSig, int dSig)
        {
            foreach (bool mode in Modes)
            {
                var (_, _, court) = BuildModels(mode);

                double[] lowPrec = court.GetLiabilityOutcomeProbabilities(pSig, dSig, true, 0);
                double[] hiPrec  = court.GetLiabilityOutcomeProbabilities(pSig, dSig, true, 1);

                lowPrec[1].Should().BeGreaterOrEqualTo(hiPrec[1] - 1e-12);
            }
        }

        // -------------------------------------------------- verdict evidence shifts hidden-state odds
        [TestMethod]
        public void CourtVerdictEvidenceTiltsOdds()
        {
            foreach (bool mode in Modes)
            {
                var (_, _, courtNoisy) = BuildModels(mode);

                var sigField   = typeof(PrecautionCourtDecisionModel)
                                 .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
                var tableField = typeof(PrecautionCourtDecisionModel)
                                 .GetField("liableProbGivenHidden", BindingFlags.NonPublic | BindingFlags.Instance);

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
}
