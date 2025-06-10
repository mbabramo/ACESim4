using ACESimBase.Games.LitigGame.PrecautionModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

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
                marginalPrecautionCost: 0.07,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6,
                liabilityThreshold: 1.0);

            // deterministic signals (σ ≈ 0)
            var detSignals = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4);
            courtDeterministic = new PrecautionCourtDecisionModel(impact, detSignals);

            // noisy signals (σ = 0.2)... Note that we use many court signals to ensure that there will always be some possibility of liability / no-liability
            const int numCourtSignals = 100; // DEBUG
            var noisySignals = new PrecautionSignalModel(2, 2, 2, numCourtSignals, 0.2, 0.2, 0.2);
            courtNoisy = new PrecautionCourtDecisionModel(impact, noisySignals);

            // DEBUG
            int countNonLiable = 0;
            for (int s = 0; s < numCourtSignals; s++)
            {
                if (!courtNoisy.IsLiable(s, 0))
                    countNonLiable++;
            }
            System.Diagnostics.Debug.WriteLine($"Non-liable court signals at level 0: {countNonLiable}");
            for (int s = 0; s < numCourtSignals; s += numCourtSignals / 100)  // limit to first 100 to avoid console spam
            {
                var post = courtNoisy.GetExpectedBenefit(s, 0);
                var ratio = courtNoisy.GetBenefitCostRatio(s, 0);
                var liable = courtNoisy.IsLiable(s, 0);
                System.Diagnostics.Debug.WriteLine($"s = {s}: ratio = {ratio:F3}, liable = {liable}");
            }
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
            courtNoisy.IsLiable(signal, 1).Should().Be(r1 >= 1.0);   // liability still depends on benefit-cost ratio
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
            var impact2 = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoActivity: 0.01,
                pAccidentNoPrecaution: 0.25,
                marginalPrecautionCost: 0.04,
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6,
                liabilityThreshold: 3.0); // <--- from 1
            var sig = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4);
            var stricter = new PrecautionCourtDecisionModel(impact2, sig); 

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
        // ------------------------------------------------------------------
        // GetLiabilityOutcomeProbabilities tests
        // ------------------------------------------------------------------

        [TestMethod]
        public void LiabilityProbabilitiesDeterministicModel()
        {
            // σ≈0 ⇒ every signal is perfectly informative; the court must
            // find liability at k=0 and must not at k=1.
            var probsNegligent = courtDeterministic
                .GetLiabilityOutcomeProbabilities(pSig: 0, dSig: 0,
                                                  accident: true, precautionLevel: 0);
            var probsSafe = courtDeterministic
                .GetLiabilityOutcomeProbabilities(pSig: 0, dSig: 0,
                                                  accident: true, precautionLevel: 1);

            probsNegligent[1].Should().BeApproximately(1.0, 1e-6);  // liable
            probsNegligent[0].Should().BeApproximately(0.0, 1e-6);

            probsSafe[1].Should().BeApproximately(0.0, 1e-6);       // not liable
            probsSafe[0].Should().BeApproximately(1.0, 1e-6);
        }

        [TestMethod]
        public void AccidentEvidenceRaisesLiabilityOdds()
        {
            // same signals, same precaution; adding the accident fact should
            // (weakly) increase the probability of liability.
            double[] noAcc = courtNoisy.GetLiabilityOutcomeProbabilities(0, 1, false, 0);
            double[] acc = courtNoisy.GetLiabilityOutcomeProbabilities(0, 1, true, 0);

            acc[1].Should().BeGreaterThan(noAcc[1] - 1e-12);
            (acc[0] + acc[1]).Should().BeApproximately(1.0, 1e-10);
            (noAcc[0] + noAcc[1]).Should().BeApproximately(1.0, 1e-10);
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
                        pr[0].Should().BeInRange(0, 1);
                        pr[1].Should().BeInRange(0, 1);
                        (pr[0] + pr[1]).Should().BeApproximately(1.0, 1e-8);
                    }
        }

        [TestMethod]
        public void CourtVerdictEvidenceTiltsHiddenOdds()
        {
            // -------- reflection helpers ---------------------------------------------
            var sigField = typeof(PrecautionCourtDecisionModel)
                             .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var impactField = typeof(PrecautionCourtDecisionModel)
                             .GetField("impact", BindingFlags.NonPublic | BindingFlags.Instance);
            var tableField = typeof(PrecautionCourtDecisionModel)
                             .GetField("liableProbGivenHidden", BindingFlags.NonPublic | BindingFlags.Instance);

            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            var impact = (PrecautionImpactModel)impactField.GetValue(courtNoisy);
            var liableTable = (double[][])tableField.GetValue(courtNoisy);

            int hiddenCount = sigModel.HiddenStatesCount;
            int pCount = sigModel.NumPSignals;
            int dCount = sigModel.NumDSignals;
            const int PrecautionLevels = 2;
            const bool accidentOccurred = true;        // keep one accident value for clarity

            //---------------------------------------------------------------------------
            // helper to build posterior P(h | p,d,acc,k, verdict)
            double[] Posterior(int pSig, int dSig, int k, bool? verdictLiable)
            {
                double uniformPrior = 1.0 / hiddenCount;
                var posterior = new double[hiddenCount];
                double total = 0.0;

                for (int h = 0; h < hiddenCount; h++)
                {
                    double w = uniformPrior *
                               sigModel.GetPlaintiffSignalProbability(h, pSig) *
                               sigModel.GetDefendantSignalProbability(h, dSig);

                    double pAcc = impact.GetAccidentProbability(h, k);
                    w *= accidentOccurred ? pAcc : 1.0 - pAcc;

                    if (verdictLiable.HasValue)
                    {
                        double liableP = liableTable[h][k];
                        w *= verdictLiable.Value ? liableP : 1.0 - liableP;
                    }
                    posterior[h] = w;
                    total += w;
                }

                if (total == 0.0) return posterior;      // impossible evidence path → all zeros

                for (int h = 0; h < hiddenCount; h++) posterior[h] /= total;
                return posterior;
            }

            //---------------------------------------------------------------------------
            // scan for first scenario; if none meets “informative” criteria we still test
            (int p, int d, int k, double liableProbAnalytic)? informative = null;
            (int p, int d, int k, double liableProbAnalytic) deterministic = (0, 0, 0, 0.0);

            for (int k = 0; k < PrecautionLevels && informative == null; k++)
            {
                bool informativeK =
                    Math.Abs(liableTable[0][k] - liableTable[1][k]) > 1e-9;

                for (int p = 0; p < pCount && informative == null; p++)
                    for (int d = 0; d < dCount && informative == null; d++)
                    {
                        double liableAnalytic =
                            courtNoisy.GetLiabilityOutcomeProbabilities(p, d, accidentOccurred, k)[1];

                        if (informativeK &&
                            liableAnalytic > 1e-6 && liableAnalytic < 1.0 - 1e-6)
                        {
                            informative = (p, d, k, liableAnalytic);
                            break;
                        }

                        // remember a deterministic example for the fall-back path
                        if (liableAnalytic < 1e-6 || liableAnalytic > 1.0 - 1e-6)
                            deterministic = (p, d, k, liableAnalytic);
                    }
            }

            //---------------------------------------------------------------------------
            if (informative.HasValue)
            {
                // ----------  verdict is informative: odds must move -------------------
                var (p, d, k, _) = informative.Value;

                int hi = liableTable[1][k] > liableTable[0][k] ? 1 : 0;
                int lo = 1 - hi;

                var postLiable = Posterior(p, d, k, verdictLiable: true);
                var postNoLiable = Posterior(p, d, k, verdictLiable: false);

                double oddsLiable = postLiable[hi] / (postLiable[lo] + 1e-12);
                double oddsNoLiable = postNoLiable[hi] / (postNoLiable[lo] + 1e-12);

                oddsLiable.Should().BeGreaterThan(oddsNoLiable,
                    "a liability verdict should raise the odds of the hidden state that more often triggers liability");

                postLiable.Sum().Should().BeApproximately(1.0, 1e-12);
                postNoLiable.Sum().Should().BeApproximately(1.0, 1e-12);
            }
            else
            {
                // ----------  verdict is *not* informative across ALL scenarios --------
                var (p, d, k, liableAnalytic) = deterministic;

                var postLiable = Posterior(p, d, k, verdictLiable: true);
                var postNoLiable = Posterior(p, d, k, verdictLiable: false);
                var postBaseline = Posterior(p, d, k, verdictLiable: null);

                if (liableAnalytic < 1e-6)          // court can ONLY rule no-liability
                {
                    // impossible verdict ⇒ zero posterior
                    postLiable.Sum().Should().BeApproximately(0.0, 1e-12);

                    // possible verdict posterior equals the baseline
                    for (int h = 0; h < hiddenCount; h++)
                        postNoLiable[h].Should().BeApproximately(postBaseline[h], 1e-12);
                }
                else                                // court can ONLY rule liability
                {
                    postNoLiable.Sum().Should().BeApproximately(0.0, 1e-12);

                    for (int h = 0; h < hiddenCount; h++)
                        postLiable[h].Should().BeApproximately(postBaseline[h], 1e-12);
                }
            }
        }




        [TestMethod]
        public void LiabilityProbabilityMonteCarloMatchesAnalytic()
        {
            const int plaintiffSignal = 0;
            const int defendantSignal = 1;
            const int precautionLevel = 0;
            const bool accidentOccurred = true;

            var rng = new Random(1234);

            // access the signal model for sampling
            var sigField = typeof(PrecautionCourtDecisionModel).GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            int hiddenCount = sigModel.HiddenStatesCount;

            int accepted = 0, liableCount = 0;
            const int TargetSamples = 100_000;

            while (accepted < TargetSamples)
            {
                int h = rng.Next(hiddenCount);

                // sample the two private signals and court signal
                var (pSig, dSig, cSig) = sigModel.GenerateSignals(h, rng);
                if (pSig != plaintiffSignal || dSig != defendantSignal) continue;

                // sample accident outcome
                double pAcc = impact.GetAccidentProbability(h, precautionLevel);
                if (rng.NextDouble() > pAcc) continue;                       // want accidentOccurred == true

                accepted++;
                if (courtNoisy.IsLiable(cSig, precautionLevel))
                    liableCount++;
            }

            double empirical = (double)liableCount / accepted;
            double analytic = courtNoisy
                .GetLiabilityOutcomeProbabilities(plaintiffSignal, defendantSignal, accidentOccurred, precautionLevel)[1];

            empirical.Should().BeApproximately(analytic, 0.01); // ≤2 % absolute error with 8 000 samples
        }

        [TestMethod]
        public void PosteriorFromDefendantSignalNormalisesCorrectly()
        {
            const int defendantSignal = 0;      // or any valid index
            double[] posterior = courtNoisy.GetHiddenPosteriorFromDefendantSignal(defendantSignal);

            double sum = posterior.Sum();
            if (sum > 0.0)
                sum.Should().BeApproximately(1.0, 1e-12);     // evidence possible
            else
                posterior.Should().AllBeEquivalentTo(0.0);    // evidence impossible
        }
        [TestMethod]
        public void HiddenPosteriorFromDefendantSignalMatchesAnalyticComputation()
        {
            // ---------------- reflect into the signal model ---------------------------
            var sigField = typeof(PrecautionCourtDecisionModel)
                .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);

            int hiddenCount = sigModel.HiddenStatesCount;
            int dCount = sigModel.NumDSignals;

            // -------------- choose a defendant signal with support on all states ------
            int chosenDSig = -1;
            for (int dSig = 0; dSig < dCount && chosenDSig < 0; dSig++)
            {
                bool hasSupportEverywhere = true;
                for (int h = 0; h < hiddenCount; h++)
                    if (sigModel.GetDefendantSignalProbability(h, dSig) < 1e-15)
                    {
                        hasSupportEverywhere = false;
                        break;
                    }
                if (hasSupportEverywhere) chosenDSig = dSig;
            }

            if (chosenDSig < 0)
                Assert.Inconclusive(
                    "No defendant signal has non-zero likelihood under every hidden state; "
                  + "posterior is undefined in that degenerate parameter regime.");

            // -------------- analytic posterior ----------------------------------------
            double uniformPrior = 1.0 / hiddenCount;
            var expected = new double[hiddenCount];
            double total = 0.0;

            for (int h = 0; h < hiddenCount; h++)
            {
                expected[h] = uniformPrior *
                              sigModel.GetDefendantSignalProbability(h, chosenDSig);
                total += expected[h];
            }
            for (int h = 0; h < hiddenCount; h++) expected[h] /= total;

            // -------------- model posterior -------------------------------------------
            double[] actual =
                courtNoisy.GetHiddenPosteriorFromDefendantSignal(chosenDSig);

            // -------------- assertions -------------------------------------------------
            actual.Length.Should().Be(hiddenCount);
            for (int h = 0; h < hiddenCount; h++)
                actual[h].Should().BeApproximately(expected[h], 1e-12,
                    $"entry {h} should equal the analytic posterior");

            actual.Sum().Should().BeApproximately(1.0, 1e-12,
                "posterior must sum to one");
        }

        [TestMethod]
        public void HiddenPosteriorFromNoAccidentScenarioMatchesAnalyticComputation()
        {
            // reflect into the helper models
            var sigField = typeof(PrecautionCourtDecisionModel)
                .GetField("signal", BindingFlags.NonPublic | BindingFlags.Instance);
            var impactField = typeof(PrecautionCourtDecisionModel)
                .GetField("impact", BindingFlags.NonPublic | BindingFlags.Instance);

            var sigModel = (PrecautionSignalModel)sigField.GetValue(courtNoisy);
            var impact = (PrecautionImpactModel)impactField.GetValue(courtNoisy);

            int hiddenCount = sigModel.HiddenStatesCount;
            int dCount = sigModel.NumDSignals;
            const int precautionLevel = 0;      // pick any valid k

            // choose a defendant signal whose likelihood is >0 for every hidden state
            int dSig = Enumerable.Range(0, dCount)
                                 .First(ds => Enumerable.Range(0, hiddenCount)
                                      .All(h => sigModel.GetDefendantSignalProbability(h, ds) > 0.0));

            // analytic posterior
            double prior = 1.0 / hiddenCount;
            var expected = new double[hiddenCount];
            double norm = 0.0;

            for (int h = 0; h < hiddenCount; h++)
            {
                expected[h] = prior *
                               sigModel.GetDefendantSignalProbability(h, dSig) *
                               (1.0 - impact.GetAccidentProbability(h, precautionLevel));
                norm += expected[h];
            }
            for (int h = 0; h < hiddenCount; h++) expected[h] /= norm;

            // model posterior
            double[] actual =
                courtNoisy.GetHiddenPosteriorFromNoAccidentScenario(dSig, precautionLevel);

            // assertions
            actual.Length.Should().Be(hiddenCount);
            for (int h = 0; h < hiddenCount; h++)
                actual[h].Should().BeApproximately(expected[h], 1e-12);

            actual.Sum().Should().BeApproximately(1.0, 1e-12);
        }

        [TestMethod]
        public void LiabilityCanOccurAtMaxPrecautionIfNextBenefitJustifiesIt()
        {
            // Configure model such that next-level benefit is above threshold at max level
            var impactModified = new PrecautionImpactModel(
                precautionPowerLevels: 2,
                precautionLevels: 2,
                pAccidentNoActivity: 0.01,
                pAccidentNoPrecaution: 0.25,
                marginalPrecautionCost: 0.01, // lower cost to raise benefit/cost ratio
                harmCost: 1.0,
                precautionPowerFactorLeastEffective: 0.8,
                precautionPowerFactorMostEffective: 0.6,
                liabilityThreshold: 1.0);

            var signals = new PrecautionSignalModel(2, 2, 2, 2, 1e-4, 1e-4, 1e-4); // deterministic signals
            var model = new PrecautionCourtDecisionModel(impactModified, signals);

            // At precaution level 1 (max), compute liability status
            for (int s = 0; s < 2; s++)
            {
                double ratio = model.GetBenefitCostRatio(s, 1);
                bool shouldBeLiable = ratio >= 1.0;

                model.IsLiable(s, 1).Should().Be(shouldBeLiable,
                    $"signal {s} at max precaution should be liable if ratio {ratio:F2} ≥ threshold");
            }
        }


    }



}
