using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class FinalArticleReplayTests
{
    private static LitigGameOptions Options(bool complete, bool riskAverse = false) =>
        new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
            .GetOptionsSets().Cast<LitigGameOptions>().Single(o => o.Name ==
                "Agreement-Enabled__Specification-"+(riskAverse ? "ModerateRiskAversion" : "Baseline")+
                "__Cost-1__Fee-"+(complete ? "British__ExitFees-AllUnilateralExits" : "American"));

    private static async Task<(StrategiesDeveloperBase developer, SavedProfileWelfare.Result welfare)> UniformReplay(LitigGameOptions options)
    {
        var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
        double[] profile = developer.InformationSets.OrderBy(i => i.PlayerIndex).ThenBy(i => i.InformationSetNodeNumber)
            .SelectMany(i => Enumerable.Repeat(1.0/i.NumPossibleActions, i.NumPossibleActions)).ToArray();
        ArticleWorkedPathExtraction.LoadProfile(developer, profile);
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
        developer.SaveWeightedGameProgressesAfterEachReport = true;
        developer.ActionStrategy = ActionStrategies.CurrentProbability;
        await developer.GenerateReportsByPlaying(false);
        CollectionAssert.AreEqual(profile, developer.GetEquilibriumFromInformationSets());
        return (developer, SavedProfileWelfare.Evaluate(options, developer.SavedWeightedGameProgresses));
    }

    [TestMethod]
    public async Task FixedBehaviorFeeChangePreservesGrossErrorAndRealCostsAndTruthIdentity()
    {
        var aOptions = Options(false);
        var (a, aw) = await UniformReplay(aOptions);
        var (_, cw) = await UniformReplay(Options(true));
        Assert.AreEqual(aw.Headline.GrossOutcomeError, cw.Headline.GrossOutcomeError, 1e-10);
        Assert.AreEqual(aw.Headline.RealLitigationExpenditures, cw.Headline.RealLitigationExpenditures, 1e-10);
        Assert.IsTrue(Math.Abs(aw.Headline.NonliableDefendantBurden-cw.Headline.NonliableDefendantBurden) > 1e-6);
        var before = StrategicGameFingerprint.Capture(a);
        var mapped = TruthMappingReplay.Evaluate(aOptions, a.SavedWeightedGameProgresses, new[] { 1.0, 0.5, 2.0 });
        var identity = mapped.Mappings.Single(m => m.Exponent == 1).Headline;
        Assert.AreEqual(aw.Headline.MeritoriousPlaintiffShortfall, identity.MeritoriousPlaintiffShortfall, 1e-10);
        Assert.AreEqual(aw.Headline.NonliableDefendantBurden, identity.NonliableDefendantBurden, 1e-10);
        Assert.AreEqual(aw.Headline.LiableDefendantExcessBurden, identity.LiableDefendantExcessBurden, 1e-10);
        Assert.AreEqual(aw.Headline.GrossOutcomeError, identity.GrossOutcomeError, 1e-10);
        Assert.AreEqual(aw.Headline.RealLitigationExpenditures, identity.RealLitigationExpenditures, 1e-10);
        Assert.AreEqual(before, StrategicGameFingerprint.Capture(a));
    }

    [TestMethod]
    public async Task FixedBehaviorRiskChangeHasZeroMonetaryEffect()
    {
        var (_, rn) = await UniformReplay(Options(false));
        var (_, ra) = await UniformReplay(Options(false, true));
        Assert.AreEqual(rn.Headline.MeritoriousPlaintiffShortfall, ra.Headline.MeritoriousPlaintiffShortfall, 1e-10);
        Assert.AreEqual(rn.Headline.NonliableDefendantBurden, ra.Headline.NonliableDefendantBurden, 1e-10);
        Assert.AreEqual(rn.Headline.LiableDefendantExcessBurden, ra.Headline.LiableDefendantExcessBurden, 1e-10);
        Assert.AreEqual(rn.Headline.GrossOutcomeError, ra.Headline.GrossOutcomeError, 1e-10);
        Assert.AreEqual(rn.Headline.RealLitigationExpenditures, ra.Headline.RealLitigationExpenditures, 1e-10);
    }
}
