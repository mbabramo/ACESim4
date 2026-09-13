using System;
using System.Linq;
using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Mathematics;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.GameTests;

[TestClass]
public class ExitFeeShiftingTests
{
    [TestMethod]
    public void ExtensionPlan_CrossesFiveCostsWithTwoPreferencesAndPreservesEntryAndExitChoices()
    {
        var launcher = new LitigGameCorrelatedSignalsArticleLauncher(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting);
        var options = launcher.GetOptionsSets().Cast<LitigGameOptions>().ToList();
        options.Should().HaveCount(10);
        options.Select(option => option.Name).Should().OnlyHaveUniqueItems();
        options.Should().OnlyContain(option => option.LoserPaysMultiple == 1 &&
            option.LoserPaysAfterAbandonment && option.LoserPaysAfterNonAnswer &&
            !option.SkipFileAndAnswerDecisions && option.AllowAbandonAndDefaults && option.NumOffers == 10);
        foreach (double cost in new[] { 0.25, 0.5, 1.0, 2.0, 4.0 })
        {
            var pair = options.Where(option => option.CostsMultiplier == cost).ToList();
            pair.Should().HaveCount(2);
            pair.Count(option => option.PUtilityCalculator is CARARiskAverseUtilityCalculator).Should().Be(1);
        }
        launcher.GetUninitializedTaskList().NumIndividualTasks.Should().Be(10);
        var loadedOptions = ACESimBase.Games.LitigGame.ManualReports.ArticleWorkedPathExtraction.CreateOptions(options[0].Name);
        loadedOptions.LoserPaysAfterNonAnswer.Should().BeTrue("diagnostics must reconstruct the extension's actual payoffs");
        var first = options[0];
        first.LoserPaysAfterNonAnswer = false;
        Action validate = () => launcher.ValidateProductionMatrix(options.Cast<GameOptions>().ToList());
        validate.Should().Throw<InvalidOperationException>();
    }

    [DataTestMethod]
    [DataRow("No suit", 0.0, 0.0, 0.0, 0.0)]
    [DataRow("Nonanswer", 0.85, -1.0, 1.0, -1.15)]
    [DataRow("Abandon", -0.15, -0.15, -0.30, 0.0)]
    [DataRow("Default", 0.85, -1.15, 1.0, -1.30)]
    [DataRow("Settlement", 0.35, -0.65, 0.35, -0.65)]
    [DataRow("Trial P wins", 1.0, -1.60, 1.0, -1.60)]
    [DataRow("Trial P loses", -0.60, 0.0, -0.60, 0.0)]
    public void FeeTrigger_ChangesOnlyIncurredCostAllocationOnUnilateralExit(
        string terminal, double oldP, double oldD, double newP, double newD)
    {
        foreach (bool exitFees in new[] { false, true })
        {
            var options = (LitigGameOptions)new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting)
                .GetDefaultSingleGameOptions();
            options.LoserPaysAfterAbandonment = options.LoserPaysAfterNonAnswer = exitFees;
            var definition = new LitigGameDefinition();
            definition.Setup(options);
            var outcome = LitigGame.CalculateGameOutcome(definition, default, default, default,
                options.PInitialWealth, options.DInitialWealth,
                terminal != "No suit", terminal == "Abandon",
                terminal != "No suit" && terminal != "Nonanswer", terminal == "Default",
                terminal == "Settlement" ? 0.5 : null, terminal == "Trial P wins", true, 1.0,
                terminal == "No suit" || terminal == "Nonanswer" ? (byte)0 : (byte)1,
                null, null, null, null, null, null, new LitigGameProgress(false));
            outcome.PChangeWealth.Should().BeApproximately(exitFees ? newP : oldP, 1E-12);
            outcome.DChangeWealth.Should().BeApproximately(exitFees ? newD : oldD, 1E-12);
            (outcome.PChangeWealth + outcome.DChangeWealth).Should().BeApproximately(oldP + oldD, 1E-12,
                "fee reimbursement is a transfer and cannot create unincurred trial expenses");
        }
    }

    [TestMethod]
    public void Nonanswer_ReimbursesOnlyUnsavedFilingCostAndHonorsFeeMultiplier()
    {
        var options = (LitigGameOptions)new LitigGameCorrelatedSignalsArticleLauncher(
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting)
            .GetDefaultSingleGameOptions();
        options.PFilingCost_PortionSavedIfDDoesntAnswer = 1.0 / 3;
        options.LoserPaysMultiple = 0.5;
        var definition = new LitigGameDefinition();
        definition.Setup(options);
        var outcome = LitigGame.CalculateGameOutcome(definition, default, default, default, 10, 10,
            true, false, false, false, null, false, true, 1, 0,
            null, null, null, null, null, null, new LitigGameProgress(false));
        outcome.PChangeWealth.Should().BeApproximately(0.95, 1E-12);
        outcome.DChangeWealth.Should().BeApproximately(-1.05, 1E-12);
    }
}
