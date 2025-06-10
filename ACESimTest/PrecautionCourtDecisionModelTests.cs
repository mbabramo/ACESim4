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
        // ------------------------------------------------------------------  model-wide constants
        const int HiddenStates = 2;
        const int PrecautionLevels = 2;
        const double PAccidentNoPrecaution = 0.25;
        const double PAccidentNoActivity = 0.01;
        const double HarmCost = 1.0;
        const double MarginalPrecautionCost = 0.07;   // single source of truth
        const double PrecautionPowerFactorLeastEffective = 0.8;  // hidden 0
        const double PrecautionPowerFactorMostEffective = 0.6;  // hidden 1
        const double LiabilityThreshold = 1.0;
        const int NumCourtSignalsNoisy = 100;

        // ------------------------------------------------------------------  helpers
        static double ExpectedRatio(double factor, int k) =>
            (PAccidentNoPrecaution * Math.Pow(factor, k) * (1.0 - factor) * HarmCost)
            / MarginalPrecautionCost;

        static double[] Normalize(double[] vec)
        {
            double sum = vec.Sum();
            return sum == 0.0
                ? vec.Select(_ => 0.0).ToArray()
                : vec.Select(v => v / sum).ToArray();
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
                pAccidentNoActivity: PAccidentNoActivity,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                marginalPrecautionCost: MarginalPrecautionCost,
                harmCost: HarmCost,
                precautionPowerFactorLeastEffective: PrecautionPowerFactorLeastEffective,
                precautionPowerFactorMostEffective: PrecautionPowerFactorMostEffective,
                liabilityThreshold: LiabilityThreshold);

            var detSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,
                1e-4, 1e-4, 1e-4);
            courtDeterministic = new PrecautionCourtDecisionModel(impact, detSignals);

            var noisySignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, NumCourtSignalsNoisy,
                0.20, 0.20, 0.20);
            courtNoisy = new PrecautionCourtDecisionModel(impact, noisySignals);
        }

        // ------------------------------------------------------------------  deterministic baseline tests
        [TestMethod]
        public void Deterministic_RatiosAndLiability()
        {
            courtDeterministic.GetBenefitCostRatio(0, 0)
                .Should().BeApproximately(ExpectedRatio(PrecautionPowerFactorLeastEffective, 0), 1e-9);
            courtDeterministic.GetBenefitCostRatio(0, 1)
                .Should().BeApproximately(ExpectedRatio(PrecautionPowerFactorLeastEffective, 1), 1e-9);
            courtDeterministic.GetBenefitCostRatio(1, 0)
                .Should().BeApproximately(ExpectedRatio(PrecautionPowerFactorMostEffective, 0), 1e-9);
            courtDeterministic.GetBenefitCostRatio(1, 1)
                .Should().BeApproximately(ExpectedRatio(PrecautionPowerFactorMostEffective, 1), 1e-9);

            courtDeterministic.IsLiable(0, 0).Should().BeFalse();
            courtDeterministic.IsLiable(0, 1).Should().BeFalse();
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

            courtNoisy.IsLiable(signal, 0).Should().Be(r0 >= LiabilityThreshold);
            courtNoisy.IsLiable(signal, 1).Should().Be(r1 >= LiabilityThreshold);
        }

        [TestMethod]
        public void ExpectedBenefitMatchesRatioTimesCost()
        {
            for (int s = 0; s < 2; s++)
                for (int k = 0; k < 2; k++)
                {
                    double benefit = courtDeterministic.GetExpectedBenefit(s, k);
                    double ratio = courtDeterministic.GetBenefitCostRatio(s, k);
                    benefit.Should().BeApproximately(ratio * MarginalPrecautionCost, 1e-9);
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
            var impact2 = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoActivity: PAccidentNoActivity,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                marginalPrecautionCost: MarginalPrecautionCost,
                harmCost: HarmCost,
                precautionPowerFactorLeastEffective: PrecautionPowerFactorLeastEffective,
                precautionPowerFactorMostEffective: PrecautionPowerFactorMostEffective,
                liabilityThreshold: 3.0);

            var sig = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,
                1e-4, 1e-4, 1e-4);

            var stricter = new PrecautionCourtDecisionModel(impact2, sig);
            stricter.IsLiable(1, 0).Should().BeFalse();
        }

        // ------------------------------------------------------------------  conditional court-signal distributions
        [TestMethod]
        public void LiabilityConditionedCourtDistDeterministic()
        {
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndLiability(1, 1, 0);
            dist.Sum().Should().BeApproximately(1.0, 1e-9);
            dist[1].Should().BeApproximately(1.0, 1e-9);
        }

        [TestMethod]
        public void NoLiabilityConditionedCourtDistDeterministic()
        {
            var dist = courtDeterministic.GetCourtSignalDistributionGivenSignalsAndNoLiability(0, 0, 1);
            dist.Sum().Should().BeApproximately(1.0, 1e-9);
            dist[0].Should().BeApproximately(1.0, 1e-9);
        }

        // ------------------------------------------------------------------  posterior checks
        [TestMethod]
        public void HiddenPosteriorDeterministic()
        {
            double[] courtDist = { 1.0, 0.0 };
            var posterior = courtDeterministic.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 0, courtDist);

            posterior[0].Should().BeApproximately(1.0, 1e-9);
            posterior[1].Should().BeApproximately(0.0, 1e-9);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0, true)]
        [DataRow(0, 0, 1, false)]
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
            double[] uniformCourt = Enumerable.Repeat(1.0 / NumCourtSignalsNoisy, NumCourtSignalsNoisy).ToArray();

            double[] posterior = courtNoisy.GetHiddenPosteriorFromSignalsAndCourtDistribution(0, 1, uniformCourt);

            var freshSignals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2, 0.2, 0.2, 0.2);

            double[] pPost = freshSignals.GetHiddenPosteriorFromPlaintiffSignal(0);
            double[] dPost = freshSignals.GetHiddenPosteriorFromDefendantSignal(1);
            double[] pdOnly = Normalize(pPost.Zip(dPost, (p, d) => p * d).ToArray());

            for (int h = 0; h < pdOnly.Length; h++)
                posterior[h].Should().BeApproximately(pdOnly[h], 1e-8);
        }

        [DataTestMethod]
        [DataRow(0, 0, 0)]
        [DataRow(1, 1, 1)]
        public void DeterministicSignalsYieldPointPosterior(int pSig, int dSig, int hiddenExpected)
        {
            double[] courtDist = { hiddenExpected == 0 ? 1.0 : 0.0,
                                   hiddenExpected == 1 ? 1.0 : 0.0 };
            var posterior = courtDeterministic.GetHiddenPosteriorFromSignalsAndCourtDistribution(pSig, dSig, courtDist);

            posterior[hiddenExpected].Should().BeApproximately(1.0, 1e-9);
            posterior.Sum().Should().BeApproximately(1.0, 1e-9);
        }

        // ------------------------------------------------------------------  liability-probability accessor
        [TestMethod]
        public void LiabilityProbabilitiesDeterministicModel()
        {
            var probsNegligent = courtDeterministic
                .GetLiabilityOutcomeProbabilities(1, 1, true, 0);
            probsNegligent[1].Should().BeApproximately(1.0, 1e-9);

            var probsSafe = courtDeterministic
                .GetLiabilityOutcomeProbabilities(0, 0, true, 1);
            probsSafe[0].Should().BeApproximately(1.0, 1e-9);
        }

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
        public void ProbabilitiesAlwaysNormalise()
        {
            for (int p = 0; p < 2; p++)
                for (int d = 0; d < 2; d++)
                    for (int k = 0; k < 2; k++)
                    {
                        double[] pr = courtNoisy.GetLiabilityOutcomeProbabilities(p, d, true, k);
                        (pr[0] + pr[1]).Should().BeApproximately(1.0, 1e-8);
                    }
        }

        // ------------------------------------------------------------------  verdict evidence and hidden-state odds
        [TestMethod]
        public void CourtVerdictEvidenceTiltsHiddenOdds()
        {
            var sigField = typeof(PrecautionCourtDecisionModel)
                .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var impactField = typeof(PrecautionCourtDecisionModel)
                .GetField("impact", BindingFlags.NonPublic | BindingFlags.Instance);
            var tableField = typeof(PrecautionCourtDecisionModel)
                .GetField("liableProbGivenHidden", BindingFlags.NonPublic | BindingFlags.Instance);

            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            var liableTbl = (double[][])tableField.GetValue(courtNoisy);

            int hi = liableTbl[1][0] > liableTbl[0][0] ? 1 : 0;
            int lo = 1 - hi;

            double[] postLiable = courtNoisy.GetHiddenPosteriorFromPath(1, 1, true, 0, true);
            double[] postNoLiab = courtNoisy.GetHiddenPosteriorFromPath(1, 1, true, 0, false);

            double oddsLiab = postLiable[hi] / (postLiable[lo] + 1e-12);
            double oddsNoLi = postNoLiab[hi] / (postNoLiab[lo] + 1e-12);

            oddsLiab.Should().BeGreaterThan(oddsNoLi);
        }

        // ------------------------------------------------------------------  Monte-Carlo validation
        [TestMethod]
        public void LiabilityProbabilityMonteCarloMatchesAnalytic()
        {
            const int plaintiffSignal = 0;
            const int defendantSignal = 1;
            const int precautionLevel = 0;

            var rng = new Random(1234);

            var sigField =
                typeof(PrecautionCourtDecisionModel).GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            int hiddenCount = sigModel.HiddenStatesCount;

            int accepted = 0, liableCount = 0;
            while (accepted < 80_000)   // ~80k samples for ≤3 % error
            {
                int h = rng.Next(hiddenCount);
                var (pSig, dSig, cSig) = sigModel.GenerateSignals(h, rng);
                if (pSig != plaintiffSignal || dSig != defendantSignal) continue;

                double pAcc = impact.GetAccidentProbability(h, precautionLevel);
                if (rng.NextDouble() > pAcc) continue;

                accepted++;
                if (courtNoisy.IsLiable(cSig, precautionLevel)) liableCount++;
            }

            double empirical = (double)liableCount / accepted;
            double analytic = courtNoisy
                .GetLiabilityOutcomeProbabilities(plaintiffSignal, defendantSignal, true, precautionLevel)[1];

            empirical.Should().BeApproximately(analytic, 0.03);
        }

        // ------------------------------------------------------------------  posterior helpers
        [TestMethod]
        public void PosteriorFromDefendantSignalNormalisesCorrectly()
        {
            double[] posterior = courtNoisy.GetHiddenPosteriorFromDefendantSignal(0);
            if (posterior.Sum() > 0.0)
                posterior.Sum().Should().BeApproximately(1.0, 1e-12);
        }

        [TestMethod]
        public void HiddenPosteriorFromDefendantSignalMatchesAnalyticComputation()
        {
            var sigField = typeof(PrecautionCourtDecisionModel)
                .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);

            int hiddenCount = sigModel.HiddenStatesCount;
            int dSig = Enumerable.Range(0, sigModel.NumDSignals)
                                 .First(ds => Enumerable.Range(0, hiddenCount)
                                       .All(h => sigModel.GetDefendantSignalProbability(h, ds) > 0.0));

            double prior = 1.0 / hiddenCount;
            var expected = new double[hiddenCount];
            double norm = 0.0;

            for (int h = 0; h < hiddenCount; h++)
            {
                expected[h] = prior * sigModel.GetDefendantSignalProbability(h, dSig);
                norm += expected[h];
            }
            for (int h = 0; h < hiddenCount; h++) expected[h] /= norm;

            double[] actual = courtNoisy.GetHiddenPosteriorFromDefendantSignal(dSig);
            for (int h = 0; h < hiddenCount; h++)
                actual[h].Should().BeApproximately(expected[h], 1e-12);
        }

        [TestMethod]
        public void HiddenPosteriorFromNoAccidentScenarioMatchesAnalyticComputation()
        {
            var sigField = typeof(PrecautionCourtDecisionModel)
                .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var impactField = typeof(PrecautionCourtDecisionModel)
                .GetField("impact", BindingFlags.NonPublic | BindingFlags.Instance);

            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            var impact = (PrecautionImpactModel)impactField.GetValue(courtNoisy);

            int hiddenCount = sigModel.HiddenStatesCount;
            int dSig = Enumerable.Range(0, sigModel.NumDSignals)
                                 .First(ds => Enumerable.Range(0, hiddenCount)
                                      .All(h => sigModel.GetDefendantSignalProbability(h, ds) > 0.0));

            const int k = 0;
            double prior = 1.0 / hiddenCount;
            var expected = new double[hiddenCount];
            double norm = 0.0;

            for (int h = 0; h < hiddenCount; h++)
            {
                expected[h] = prior *
                               sigModel.GetDefendantSignalProbability(h, dSig) *
                               (1.0 - impact.GetAccidentProbability(h, k));
                norm += expected[h];
            }
            for (int h = 0; h < hiddenCount; h++) expected[h] /= norm;

            double[] actual = courtNoisy.GetHiddenPosteriorFromNoAccidentScenario(dSig, k);
            for (int h = 0; h < hiddenCount; h++)
                actual[h].Should().BeApproximately(expected[h], 1e-12);
        }

        // ------------------------------------------------------------------  liability at max precaution
        [TestMethod]
        public void LiabilityCanOccurAtMaxPrecautionIfNextBenefitJustifiesIt()
        {
            var impactModified = new PrecautionImpactModel(
                precautionPowerLevels: HiddenStates,
                precautionLevels: PrecautionLevels,
                pAccidentNoActivity: PAccidentNoActivity,
                pAccidentNoPrecaution: PAccidentNoPrecaution,
                marginalPrecautionCost: 0.01,
                harmCost: HarmCost,
                precautionPowerFactorLeastEffective: PrecautionPowerFactorLeastEffective,
                precautionPowerFactorMostEffective: PrecautionPowerFactorMostEffective,
                liabilityThreshold: LiabilityThreshold);

            var signals = new PrecautionSignalModel(
                HiddenStates, 2, 2, 2,
                1e-4, 1e-4, 1e-4);

            var model = new PrecautionCourtDecisionModel(impactModified, signals);

            for (int s = 0; s < 2; s++)
            {
                double ratio = model.GetBenefitCostRatio(s, 1);
                model.IsLiable(s, 1).Should().Be(ratio >= LiabilityThreshold);
            }
        }
    }
}
