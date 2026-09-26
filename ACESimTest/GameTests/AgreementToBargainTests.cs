using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class AgreementToBargainTests
{
    private static LitigGameOptions Options(int fee = 0, bool risk = false)
    {
        var options = new LitigGameCorrelatedSignalsArticleLauncher(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
            .GetOptionsSets().Cast<LitigGameOptions>().Single(o =>
                o.CostsMultiplier == 1 && (o.LoserPaysMultiple == 0 ? 0 : o.LoserPaysAfterAbandonment ? 2 : 1) == fee &&
                (Convert.ToDouble(o.VariableSettings["CARA Alpha"]) == 2) == risk);
        return options;
    }

    private static LitigGameProgress Play(LitigGameOptions options, byte pAgree, byte dAgree,
        byte pExit, byte dExit, byte court = 1, byte lottery = 1, bool overlap = false,
        Action<Decision, GameProgress> observe = null)
        => LitigGameLauncherBase.PlayLitigGameOnce(options, (decision, progress) =>
        {
            observe?.Invoke(decision, progress);
            return (LitigGameDecisions)decision.DecisionByteCode switch
            {
                LitigGameDecisions.PLiabilitySignal or LitigGameDecisions.DLiabilitySignal => 5,
                LitigGameDecisions.PAgreeToBargain => pAgree,
                LitigGameDecisions.DAgreeToBargain => dAgree,
                LitigGameDecisions.PAbandon => pExit,
                LitigGameDecisions.DDefault => dExit,
                LitigGameDecisions.POffer => overlap ? (byte)1 : options.NumOffers,
                LitigGameDecisions.DOffer => 1,
                LitigGameDecisions.CourtDecisionLiability => court,
                LitigGameDecisions.MutualGiveUp => lottery,
                _ => 1
            };
        });

    [TestMethod]
    public void Plan_IsSeparateAndChangesOnlyAgreementAtAllCostsWithOneStart()
    {
        var expanded = new LitigGameCorrelatedSignalsArticleLauncher(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain);
        var original = new LitigGameCorrelatedSignalsArticleLauncher(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness);
        expanded.MasterReportNameForDistributedProcessing.Should().Be("CS007AB");
        var all = expanded.GetOptionsSets().Cast<LitigGameOptions>().ToArray();
        all.Should().HaveCount(30);
        all.Select(o => o.CostsMultiplier).Distinct().Should().BeEquivalentTo(new[] { .25, .5, 1, 2, 4 });
        var baselineOptions = LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans
            .SelectMany(plan => new LitigGameCorrelatedSignalsArticleLauncher(plan).GetOptionsSets()).Cast<LitigGameOptions>().ToArray();
        all.Select(o => o.Name).Should().OnlyHaveUniqueItems();
        foreach (var option in all)
        {
            var baseline = baselineOptions
                .Single(o => option.Name == "Agreement-Enabled__" + o.Name);
            option.IncludeAgreementToBargainDecisions.Should().BeTrue();
            baseline.IncludeAgreementToBargainDecisions.Should().BeFalse();
            option.NumOffers.Should().Be(10);
            option.NumLiabilitySignals.Should().Be(10);
            option.DamagesMax.Should().Be(1);
            option.PInitialWealth.Should().Be(10);
            option.DInitialWealth.Should().Be(10);
            foreach (var cost in new[] { option.PFilingCost, option.DAnswerCost, option.PTrialCosts, option.DTrialCosts })
                cost.Should().Be(0.15);
            var settings = new ACESimBase.GameSolvingSupport.Settings.EvolutionSettings();
            option.ModifyEvolutionSettings(settings);
            settings.SequenceFormNumPriorsToUseToGenerateEquilibria.Should().Be(1);
            settings.TryInexactArithmeticForAdditionalEquilibria.Should().BeFalse();
            settings.UseExistingEquilibriaIfAvailable.Should().BeTrue("resume may reuse only identically named expanded-game profiles after validation");
            option.PLiabilityNoiseStdev.Should().Be(baseline.PLiabilityNoiseStdev);
            option.CourtLiabilityNoiseStdev.Should().Be(baseline.CourtLiabilityNoiseStdev);
        }
        expanded.GetUninitializedTaskList().PlanFingerprint.Should().NotBe(original.GetUninitializedTaskList().PlanFingerprint);
    }

    [TestMethod]
    public void Refusal_ResolvesEveryCommitmentAndFeeRuleWithNoOfferOrExtraCost()
    {
        foreach (int fee in new[] { 0, 1, 2 })
        foreach (var agreement in new[] { ((byte)2, (byte)1), ((byte)1, (byte)2), ((byte)2, (byte)2), ((byte)1, (byte)1) })
        foreach (byte pExit in new byte[] { 1, 2 })
        foreach (byte dExit in new byte[] { 1, 2 })
        foreach (byte result in new byte[] { 1, 2 })
        {
            var options = Options(fee);
            options.CollapseAlternativeEndings = false;
            var visited = new List<LitigGameDecisions>();
            var p = Play(options, agreement.Item1, agreement.Item2, pExit, dExit, result, result,
                observe: (d, _) => visited.Add((LitigGameDecisions)d.DecisionByteCode));
            p.GameComplete.Should().BeTrue();
            p.CaseSettles.Should().BeFalse();
            p.SettlementValue.Should().BeNull();
            p.BargainingRoundsComplete.Should().Be(1);
            bool refuses = agreement.Item1 == 2 || agreement.Item2 == 2;
            visited.Contains(LitigGameDecisions.POffer).Should().Be(!refuses);
            visited.Contains(LitigGameDecisions.DOffer).Should().Be(!refuses);
            if (refuses)
            {
                p.PLastOffer.Should().BeNull(); p.DLastOffer.Should().BeNull();
                p.PFinalWealthWithBestOffer.Should().BeNull(); p.DFinalWealthWithBestOffer.Should().BeNull();
            }
            bool trial = pExit == 2 && dExit == 2;
            bool abandon = pExit == 1 && (dExit == 2 || result == 1);
            bool defaults = dExit == 1 && (pExit == 2 || result == 2);
            p.TrialOccurs.Should().Be(trial);
            p.PAbandons.Should().Be(abandon);
            p.DDefaults.Should().Be(defaults);
            double damages = defaults || trial && result == 2 ? 1 : 0;
            double costs = trial ? 0.3 : 0.15;
            bool shift = trial ? fee > 0 : fee == 2;
            double pCost = shift ? (damages == 1 ? 0 : 2 * costs) : costs;
            double dCost = 2 * costs - pCost;
            p.PFinalWealth.Should().BeApproximately(10 + damages - pCost, 1e-10);
            p.DFinalWealth.Should().BeApproximately(10 - damages - dCost, 1e-10);
        }
    }

    [TestMethod]
    public void CollapsedRefusals_MatchOrdinaryLotteryAndTrialIncludingRiskAversion()
    {
        foreach (int fee in new[] { 0, 1, 2 })
        foreach (bool risk in new[] { false, true })
        foreach (var agreement in new[] { (2, 1), (1, 2), (2, 2), (1, 1) })
        foreach (byte pExit in new byte[] { 1, 2 })
        foreach (byte dExit in new byte[] { 1, 2 })
        {
            var options = Options(fee, risk);
            var collapsed = Play(options, (byte)agreement.Item1, (byte)agreement.Item2, pExit, dExit);
            options.CollapseAlternativeEndings = false;
            var first = Play(options, (byte)agreement.Item1, (byte)agreement.Item2, pExit, dExit, 1, 1);
            var second = Play(options, (byte)agreement.Item1, (byte)agreement.Item2, pExit, dExit, 2, 2);
            double firstWeight = pExit == 2 && dExit == 2
                ? first.LitigGameDefinition.GetUnevenChanceActionProbabilities((byte)LitigGameDecisions.CourtDecisionLiability, first)[0] : 0.5;
            collapsed.PFinalWealth.Should().BeApproximately(firstWeight * first.PFinalWealth + (1 - firstWeight) * second.PFinalWealth, 1e-10);
            collapsed.DFinalWealth.Should().BeApproximately(firstWeight * first.DFinalWealth + (1 - firstWeight) * second.DFinalWealth, 1e-10);
            collapsed.PWelfare.Should().BeApproximately(firstWeight * first.PWelfare + (1 - firstWeight) * second.PWelfare, 1e-10);
            collapsed.DWelfare.Should().BeApproximately(firstWeight * first.DWelfare + (1 - firstWeight) * second.DWelfare, 1e-10);
        }
    }

    [TestMethod]
    public void BothAgree_OffersSettleAndDisabledGameReproducesPayoffs()
    {
        foreach (int fee in new[] { 0, 1, 2 })
        foreach (bool overlap in new[] { false, true })
        foreach (byte pExit in new byte[] { 1, 2 })
        foreach (byte dExit in new byte[] { 1, 2 })
        {
            var options = Options(fee);
            var enabled = Play(options, 1, 1, pExit, dExit, overlap: overlap);
            options.IncludeAgreementToBargainDecisions = false;
            var disabled = Play(options, 1, 1, pExit, dExit, overlap: overlap);
            enabled.CaseSettles.Should().Be(overlap);
            enabled.PFinalWealth.Should().BeApproximately(disabled.PFinalWealth, 1e-12);
            enabled.DFinalWealth.Should().BeApproximately(disabled.DFinalWealth, 1e-12);
            enabled.TrialOccurs.Should().Be(disabled.TrialOccurs);
            if (overlap) enabled.SettlementValue.Should().BeApproximately(0.05, 1e-12);
        }
    }

    [TestMethod]
    public void Agreement_IsSimultaneousAndExitCommitmentsStayPrivate()
    {
        string Info(byte pAgree, byte pExit, LitigGameDecisions at, byte player)
        {
            string info = null;
            Play(Options(), pAgree, 1, pExit, 2, observe: (d, p) =>
            {
                if (d.DecisionByteCode == (byte)at) info = p.GameHistory.GetCurrentPlayerInformationString(player);
            });
            return info;
        }
        Info(1, 1, LitigGameDecisions.DAgreeToBargain, 1).Should().Be(Info(2, 2, LitigGameDecisions.DAgreeToBargain, 1));
        Info(1, 1, LitigGameDecisions.PAgreeToBargain, 0).Should().NotBe(Info(1, 2, LitigGameDecisions.PAgreeToBargain, 0));
        Info(1, 2, LitigGameDecisions.POffer, 0).Should().EndWith(",1,1");
        Info(1, 1, LitigGameDecisions.DOffer, 1).Should().Be(Info(1, 2, LitigGameDecisions.DOffer, 1));
        foreach (bool collapsed in new[] { false, true })
        {
            var options = Options(); options.CollapseAlternativeEndings = collapsed;
            var refused = Play(options, 2, 1, 2, 2);
            refused.GameHistory.GetCurrentPlayerInformationString(0).Should().EndWith(",2,1");
            refused.GameHistory.GetCurrentPlayerInformationString(1).Should().EndWith(",2,1");
        }
    }

    [TestMethod]
    public void RefusedRound_CannotSettleFromEmptyOrStaleOffers()
    {
        var options = Options();
        var definition = new LitigGameDefinition(); definition.Setup(options);
        foreach (bool stale in new[] { false, true })
        {
            var p = new LitigGameProgress(false) { GameDefinition = definition, PFiles = true, DAnswers = true,
                PAgreesToBargain = new() { false }, DAgreesToBargain = new() { true },
                POffers = stale ? new() { 0.05 } : new(), DOffers = stale ? new() { 0.95 } : new() };
            p.ConcludeMainPortionOfBargainingRound(definition);
            p.CaseSettles.Should().BeFalse(); p.SettlementValue.Should().BeNull();
            p.PFinalWealthWithBestOffer.Should().BeNull(); p.DFinalWealthWithBestOffer.Should().BeNull();
        }
    }

    [TestMethod]
    public async Task FullBestResponse_OptimizesAgreementDecisions()
    {
        var developer = await ArticleWorkedPathExtraction.InitializeAsync(Options());
        var nodes = developer.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).ToArray();
        nodes.Count(n => n.DecisionByteCode == (byte)LitigGameDecisions.PAgreeToBargain).Should().Be(20);
        nodes.Count(n => n.DecisionByteCode == (byte)LitigGameDecisions.DAgreeToBargain).Should().Be(20);
        double[] profile = nodes.SelectMany(n => Enumerable.Range(1, n.NumPossibleActions).Select(a =>
            a == ((LitigGameDecisions)n.DecisionByteCode is LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault or LitigGameDecisions.DAgreeToBargain ? 2 : 1) ? 1.0 : 0.0)).ToArray();
        ArticleWorkedPathExtraction.LoadProfile(developer, profile).Should().BeEmpty();
        developer.EvolutionSettings.UseAcceleratedBestResponse = true;
        developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
        developer.CalculateBestResponse(false);
        developer.Status.BestResponseReflectsCurrentStrategy.Should().BeTrue();
        developer.Status.BestResponseImprovement[1].Should().BeGreaterThan(0.15,
            "D can agree and offer 0.05 instead of paying expected trial damages and costs");
    }
}
