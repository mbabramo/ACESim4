using LitigGameDecisions = ACESim.LitigGameDecisions;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass]
public class EquilibriumChangeDecompositionTests
{
    private static EquilibriumChangeFocus.Row Focus(InformationSet old, InformationSet end, Scenario[] scenarios = null)
    {
        scenarios ??= Scenarios(end, Enumerable.Repeat(end.Actions[0].Probability, 8).ToArray());
        var changes = BuildRows(ReferenceFor(old), ReferenceFor(end), scenarios, new[] { .25, .75 });
        var result = new ContrastResult("2", new("test", "test", "old", "new"), "old", "new", new(),
            ReferenceFor(old), ReferenceFor(end), scenarios, Array.Empty<string>(), changes.Rows, changes.Excluded, new[] { .25, .75 });
        return EquilibriumChangeFocus.Build(result).Single();
    }

    [TestMethod]
    public void FocusDoesNotMistakeDisjointTiedActionsForStrictIncentives()
    {
        var old = Info("P Offer", 1);
        var row = Focus(old, Policy(old, 0));
        row.SupportsDisjoint.Should().BeTrue();
        row.Focus.Should().BeFalse();
        row.OriginalPolicyLossAtTarget.Should().Be(0);
        row.ShiftedProbability.Should().Be(1);
    }

    [TestMethod]
    public void FocusRetainsStrictlyInferiorOldActionsEvenWhenSupportsOverlap()
    {
        var old = Info("P Offer", .75);
        var end = Policy(old, .25) with { Actions = new[]
        {
            old.Actions[0] with { Probability = .25, CounterfactualConditionalUtility = 1 },
            old.Actions[1] with { Probability = .75, CounterfactualConditionalUtility = 2 }
        } };
        var row = Focus(old, end);
        row.SupportsDisjoint.Should().BeFalse();
        row.Focus.Should().BeTrue();
        row.InferiorOriginalMass.Should().Be(.75);
        row.OriginalPolicyLossAtTarget.Should().Be(.75);
        row.Allocation.Change.Should().Be(50);
        row.Allocation.Direct.Should().Be(50);
    }

    [TestMethod]
    public void FocusUsesGainingProbabilityIncludingNoRatherThanSignedYesChange()
    {
        var old = Info();
        var row = Focus(old, Policy(old, 0));
        row.GainingActions.Should().Equal(2);
        row.Allocation.Original.Should().Be(0);
        row.Allocation.Target.Should().Be(100);
        row.Allocation.Explained.Should().Be(100);
    }

    [TestMethod]
    public void FocusNeverAllocatesUndefinedCounterfactualPolicies()
    {
        var old = Info();
        var end = Policy(old, 0);
        var scenarios = Scenarios(end with { CounterfactuallyUnreachable = true }, Enumerable.Repeat(0.0, 8).ToArray());
        var row = Focus(old, end, scenarios);
        row.CounterfactualUndefined.Should().BeTrue();
        row.Allocation.Should().BeNull();
    }

    [TestMethod]
    public void FocusIgnoresNumericalNearTiesWithoutDroppingAuditRow()
    {
        var old = Info();
        var end = Policy(old, 0);
        end = end with { Actions = end.Actions.Select((a, i) => a with { CounterfactualConditionalUtility = i * 1e-7 }).ToArray() };
        var row = Focus(old, end);
        row.Focus.Should().BeFalse();
        row.OriginalPolicyLossAtTarget.Should().Be(1e-7);
    }

    [TestMethod]
    public void FocusReoptimizesContinuationRatherThanTreatingAnOffPathCompletionAsStrictLoss()
    {
        var old = Info();
        var end = Policy(old, 0);
        end = end with { Actions = end.Actions.Select((a, i) => a with { CounterfactualConditionalUtility = i }).ToArray() };
        var optimized = Scenarios(old, Enumerable.Repeat(1.0, 8).ToArray()); // Both Qs tied, retaining old action.
        var row = Focus(old, end, optimized);
        row.FixedTargetContinuationLoss.Should().Be(1);
        row.OriginalPolicyLossAtTarget.Should().Be(0);
        row.Focus.Should().BeFalse();
        row.Allocation.SelectionResidual.Should().Be(100);
    }

    [TestMethod]
    public void FocusAggregatesSeveralGainingActionsWithoutAveragingOfferAmounts()
    {
        var old = Info("P Offer") with { Actions = new[]
        {
            new ActionValue(1, "0.25", .7, 0, 0, true, true),
            new ActionValue(2, "0.50", .3, 0, 0, true, true),
            new ActionValue(3, "0.75", 0, 0, 0, true, true)
        } };
        var end = old with { Actions = old.Actions.Select((a, i) => a with { Probability = new[] { .2, .4, .4 }[i] }).ToArray() };
        var scenarios = Scenarios(old, Enumerable.Repeat(.4, 8).ToArray()).Select(s => s with
        {
            Result = s.Result with { InformationSets = new[] { old with
            { Actions = old.Actions.Select((a, i) => a with { Probability = new[] { .4, .3, .3 }[i] }).ToArray() } } }
        }).ToArray();
        var row = Focus(old, end, scenarios);
        row.GainingActions.Should().Equal(2, 3);
        row.Allocation.Original.Should().BeApproximately(30, 1e-10);
        row.Allocation.Target.Should().BeApproximately(80, 1e-10);
        row.Allocation.Direct.Should().BeApproximately(30, 1e-10);
        row.Allocation.SelectionResidual.Should().BeApproximately(20, 1e-10);
        row.ShiftedProbability.Should().BeApproximately(.5, 1e-10);
    }

    [TestMethod]
    public void AdditiveEffectsAndInteractionsReconcileToObservedChange()
    {
        var values = Enumerable.Range(0, 8).Select(m => 20.0 + ((m & 1) != 0 ? 10 : 0) +
            ((m & 2) != 0 ? 30 : 0) + ((m & 4) != 0 ? -5 : 0)).ToArray();
        var a = Allocate(0, 55, values);
        a.Direct.Should().Be(20);
        a.Entry.Should().BeApproximately(10, 1e-12);
        a.Offers.Should().BeApproximately(30, 1e-12);
        a.Exit.Should().BeApproximately(-5, 1e-12);
        a.SelectionResidual.Should().Be(0);
        var interaction = Allocate(0, 60, Enumerable.Range(0, 8).Select(m => m == 7 ? 60.0 : 0).ToArray());
        interaction.Entry.Should().BeApproximately(20, 1e-12);
        interaction.Offers.Should().BeApproximately(20, 1e-12);
        interaction.Exit.Should().BeApproximately(20, 1e-12);
        Orders.Select(o => string.Join(",", o)).Distinct().Count().Should().Be(6);
    }

    [TestMethod]
    public void FullDirectEffectLeavesZeroNetOpponentEffectButAllowsOffsets()
    {
        var a = Allocate(0, 100, new[] { 100.0, 80, 120, 100, 100, 80, 120, 100 });
        a.Direct.Should().Be(100);
        a.Entry.Should().BeApproximately(-20, 1e-12);
        a.Offers.Should().BeApproximately(20, 1e-12);
        (a.Entry + a.Offers + a.Exit).Should().BeApproximately(0, 1e-12);
        a.Explained.Should().BeApproximately(a.Change, 1e-12);
    }

    [TestMethod]
    public void EndpointResidualIsExplicitRatherThanAssignedToOpponentMechanisms()
    {
        var a = Allocate(0, 91.4, Enumerable.Repeat(100.0, 8).ToArray());
        a.SelectionResidual.Should().BeApproximately(-8.6, 1e-12);
        a.Explained.Should().Be(100);
        Action invalid = () => Allocate(0, 1, new[] { double.NaN });
        invalid.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void TieHandlingPreservesOriginalMixAndRejectsInferiorActions()
    {
        PreserveOptimalReference(new[] { 2.0, 2.0 }, new[] { .31, .69 }, 1e-10).Should().Equal(.31, .69);
        PreserveOptimalReference(new[] { 1.0, 2.0 }, new[] { 1.0, 0.0 }, 1e-10).Should().Equal(0, 1);
        PreserveOptimalReference(new[] { 2.0, 2.0, 1.0 }, new[] { .2, .3, .5 }, 1e-10).Should().Equal(.4, .6, 0);
    }

    private static InformationSet Info(string decision = "P Files", double p = 1, int signal = 1) =>
        new("P/" + decision + "/" + signal, signal, 0, decision, "", signal, (signal - .5) / 10,
            decision == "P Offer" ? 2 : null, .1, .1, false, false, new[] { .5, .5 }, 0,
            new[] { new ActionValue(1, decision == "P Offer" ? "0.25" : "Yes", p, 0, 0, true, true),
                new ActionValue(2, decision == "P Offer" ? "0.75" : "No", 1 - p, 0, 0, true, true) }, 0, 0);
    private static InformationSet Policy(InformationSet info, double p) => info with
    { Actions = info.Actions.Select((a, i) => a with { Probability = i == 0 ? p : 1 - p }).ToArray() };
    private static Reference ReferenceFor(InformationSet info) => new("test", "test", new[] { 0.0, 0.0 }, 1, new[] { info });
    private static Scenario[] Scenarios(InformationSet info, double[] p) => p.Select((prob, mask) =>
        new Scenario("coalition", mask.ToString(), new Result("test", "test", 0, "test", 0, 0, 0, 0, 0, 1,
            new Profile("test", "test", new Dictionary<string, Strategy>()),
            new[] { Policy(info, prob) }, Array.Empty<CompletionWarning>()))).ToArray();

    [TestMethod]
    public void OnlyChangedCommonlyReachedHistoriesAreIncluded()
    {
        var old = Info();
        var scenario = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        var result = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenario, new[] { .25, .75 });
        result.Rows.Should().ContainSingle();
        result.Rows[0].Allocation.Direct.Should().Be(-100);
        result.Rows[0].EndpointSelection.Should().BeFalse();
        BuildRows(ReferenceFor(old), ReferenceFor(old), scenario, new[] { .25, .75 }).Rows.Should().BeEmpty();
        BuildRows(ReferenceFor(old), ReferenceFor(old), scenario, new[] { .25, .75 }, includeUnchanged: true)
            .Rows.Should().ContainSingle("optional diagnostic accounting includes unchanged policies without changing the default");
        result = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0) with { ActualOffPath = true, ActualReach = 0 }), scenario, new[] { .25, .75 });
        result.Rows.Should().BeEmpty();
        result.Excluded.Should().ContainSingle().Which.Reason.Should().StartWith("no longer");
    }

    [TestMethod]
    public void MixedOffersRemainActionSharesAndUnmatchedTargetIsFlagged()
    {
        var old = Info("P Offer", .31);
        var result = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, .69)),
            Scenarios(old, Enumerable.Repeat(.5, 8).ToArray()), new[] { .25, .75 });
        result.Rows.Length.Should().Be(2);
        result.Rows.Should().OnlyContain(r => r.Metric == "offer action probability" && r.EndpointSelection);
        result.Rows[0].Allocation.Original.Should().BeApproximately(31, 1e-10);
        result.Rows[0].Allocation.Target.Should().BeApproximately(69, 1e-10);
    }

    [TestMethod]
    public void ConditionalButUnreachedHybridsAreDistinguishedFromUndefinedCounterfactuals()
    {
        var old = Info();
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        scenarios[3] = scenarios[3] with { Result = scenarios[3].Result with { InformationSets = new[]
            { Policy(old, 0) with { ActualOffPath = true, ActualReach = 0 } } } };
        var row = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, new[] { .25, .75 }).Rows.Single();
        row.UnreachedCoalitions.Should().Equal(3);
        row.CounterfactualUndefined.Should().BeFalse();
        scenarios[3] = scenarios[3] with { Result = scenarios[3].Result with { InformationSets = new[]
            { Policy(old, 0) with { CounterfactuallyUnreachable = true, CounterfactualReach = 0 } } } };
        BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, new[] { .25, .75 })
            .Rows.Single().CounterfactualUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void GroupingRequiresMatchingMechanismsNotJustMatchingEndpoints()
    {
        var old = Info();
        var row = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)),
            Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray()), new[] { .25, .75 }).Rows.Single();
        var next = row with { Signal = 2, SignalValue = .15 };
        LitigCharts.EquilibriumChangeTables.GroupRows(new[] { row, next }).Should().ContainSingle();
        next = next with { Allocation = next.Allocation with { Direct = -50, Offers = -50 } };
        LitigCharts.EquilibriumChangeTables.GroupRows(new[] { row, next }).Length.Should().Be(2);
        LitigCharts.EquilibriumChangeTables.Number(-.0000001, false, true).Should().Be("0");
    }

    [TestMethod]
    public void PublicationRowsRetainPartialAttributionsAndExplicitResiduals()
    {
        var old = Info();
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        var row = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, new[] { .25, .75 }).Rows.Single();
        var report = new ContrastResult("2", new("test", "test", "old", "new"),
            "Specification-Baseline__Cost-1__Fee-American", "Specification-Baseline__Cost-1__Fee-British",
            new(), ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, Array.Empty<string>(),
            new[] { row }, Array.Empty<ExcludedHistory>(), new[] { .25, .75 });
        string RowText(ChangeRow candidate) => LitigCharts.EquilibriumChangeTables.Latex(new[]
            { report with { Changes = new[] { candidate } } }).Split('\n').First(x => x.StartsWith("File"));
        RowText(row).Should().Contain(" & -100 & -100 & 0 & 0 & 0");
        string selected = RowText(row with { EndpointSelection = true });
        selected.Should().Contain(@"File (\%)");
        selected.Should().Contain("$^{*}$");
        selected.Should().Contain(" & -100 & -100 & 0 & 0 & 0 & 0");
        selected.Should().NotContain("Selection-dependent");
        string undefined = RowText(row with { CounterfactualUndefined = true, EndpointSelection = true });
        undefined.Should().Contain(@"\multicolumn{5}{c}{Undefined counterfactual}");
        undefined.Should().NotContain("Selection-dependent");
        LitigCharts.EquilibriumChangeTables.Latex(new[] { report }).Should()
            .Contain("Original & Target & Change & Direct & Entry & Offers & Exit & Remaining");
    }

    private static InformationSet Q(InformationSet info, double? first, double? second) => info with
    {
        Actions = info.Actions.Select((a, i) => a with { CounterfactualConditionalUtility = i == 0 ? first : second }).ToArray()
    };

    [TestMethod]
    public void PayoffGapsExplainVanishingDisadvantageWithoutAttributingMixingProbability()
    {
        var old = Q(Info("P Offer", 0), 996, 1000);
        var end = Q(Policy(old, .43081), 1000, 1000);
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        for (int m = 0; m < 8; m++)
        {
            double gap = -3 + ((m & 1) != 0 ? 1 : 0) + ((m & 2) != 0 ? 4 : 0) - ((m & 4) != 0 ? 2 : 0);
            scenarios[m] = scenarios[m] with { Result = scenarios[m].Result with { InformationSets = new[] { Q(old, 1000 + gap, 1000) } } };
        }
        var changes = BuildRows(ReferenceFor(old), ReferenceFor(end), scenarios, new[] { .25, .75 }).Rows;
        changes.Should().HaveCount(2);
        changes[0].Allocation.SelectionResidual.Should().BeApproximately(43.081, 1e-9);
        var rows = BuildPayoffGaps(ReferenceFor(old), ReferenceFor(end), scenarios, changes);
        rows.Should().ContainSingle("both probability rows describe the same probability transfer");
        var row = rows[0];
        row.GainingWeights.Should().Equal(1, 0); row.LosingWeights.Should().Equal(0, 1);
        row.ShiftedProbability.Should().BeApproximately(.43081, 1e-9);
        row.Allocation.Original.Should().Be(-4); row.Allocation.Target.Should().Be(0);
        row.Allocation.Direct.Should().Be(1); row.Allocation.Entry.Should().BeApproximately(1, 1e-9);
        row.Allocation.Offers.Should().BeApproximately(4, 1e-9); row.Allocation.Exit.Should().BeApproximately(-2, 1e-9);
        row.Allocation.SelectionResidual.Should().Be(0);
        var mismatch = Q(end, 1002, 1000);
        BuildPayoffGaps(ReferenceFor(old), ReferenceFor(mismatch), scenarios, changes)
            .Single().Allocation.SelectionResidual.Should().Be(2, "endpoint continuation differences must remain explicit");
    }

    [TestMethod]
    public void PayoffGapsDistinguishUnreachedFromUndefinedAndSkipUnaffectedRows()
    {
        var old = Q(Info("P Offer", 0), 999, 1000); var end = Q(Policy(old, .5), 1000, 1000);
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        var changes = BuildRows(ReferenceFor(old), ReferenceFor(end), scenarios, new[] { .25, .75 }).Rows;
        scenarios[0] = scenarios[0] with { Result = scenarios[0].Result with { InformationSets = new[] { old with { ActualOffPath = true } } } };
        var gap = BuildPayoffGaps(ReferenceFor(old), ReferenceFor(end), scenarios, changes).Single();
        gap.CounterfactualUndefined.Should().BeFalse(); gap.UnreachedCoalitions.Should().Equal(0);
        scenarios[0] = scenarios[0] with { Result = scenarios[0].Result with { InformationSets = new[] { Q(old, null, 1000) } } };
        gap = BuildPayoffGaps(ReferenceFor(old), ReferenceFor(end), scenarios, changes).Single();
        gap.CounterfactualUndefined.Should().BeTrue(); gap.CoalitionGaps[0].Should().BeNull(); gap.Allocation.Should().BeNull();
        BuildPayoffGaps(ReferenceFor(old), ReferenceFor(end), scenarios,
            changes.Select(r => r with { EndpointSelection = false, CounterfactualUndefined = false }).ToArray()).Should().BeEmpty();
    }

    [TestMethod]
    public void PayoffGapsWeightActualProbabilityTransfersAndIgnoreUnusedUndefinedActions()
    {
        var old = Info("P Offer", .25) with { Actions = new[] {
            new ActionValue(1, "0.25", .25, 1, 0, true, true),
            new ActionValue(2, "0.50", .75, 2, 0, true, true),
            new ActionValue(3, "0.75", 0, 3, 0, true, true),
            new ActionValue(4, "0.95", 0, null, null, null, null) } };
        var end = old with { Actions = old.Actions.Select((a, i) => a with { Probability = new[] { 0.0, .25, .75, 0 }[i] }).ToArray() };
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray()).Select(s =>
            s with { Result = s.Result with { InformationSets = new[] { old } } }).ToArray();
        var changes = BuildRows(ReferenceFor(old), ReferenceFor(end), scenarios, new[] { .25, .5, .75, .95 }).Rows;
        var gap = BuildPayoffGaps(ReferenceFor(old), ReferenceFor(end), scenarios, changes).Single();
        gap.GainingWeights.Should().Equal(0, 0, 1, 0);
        gap.LosingWeights[0].Should().BeApproximately(1.0 / 3, 1e-9);
        gap.LosingWeights[1].Should().BeApproximately(2.0 / 3, 1e-9);
        gap.OriginalGap.Should().BeApproximately(4.0 / 3, 1e-9);
        gap.CounterfactualUndefined.Should().BeFalse();
        LitigCharts.EquilibriumChangeTables.GapComparison(gap).Should().Be("0.75 vs 33.3% 0.25 + 66.7% 0.50");
        LitigCharts.EquilibriumChangeTables.PayoffNumber(.0038863, true).Should().Be("+3.8863");
    }

    [TestMethod]
    public void AllCoalitionsReplaceOnlyRequestedOpponentComponents()
    {
        var strategies = new Dictionary<string, Strategy>();
        foreach (byte player in new byte[] { 0, 1 })
            foreach (var decision in new[] { LitigGameDecisions.PFile,
                LitigGameDecisions.POffer, LitigGameDecisions.PAbandon })
            {
                string key = player + "/" + decision;
                strategies[key] = new(key, player, (byte)decision, "", new[] { "a", "b" }, new[] { .2, .8 }, 1, false, "old");
            }
        var old = new Profile("old", "test", strategies);
        var end = old with { Strategies = strategies.ToDictionary(x => x.Key, x => x.Value with { Probabilities = new[] { .7, .3 } }) };
        for (int mask = 0; mask < 8; mask++)
            foreach (var strategy in Coalition(old, end, 0, mask).Strategies.Values)
            {
                int component = Array.IndexOf(Components, Component(strategy.Decision));
                bool changed = strategy.Player == 1 && (mask & (1 << component)) != 0;
                strategy.Probabilities.Should().Equal(changed ? new[] { .7, .3 } : new[] { .2, .8 });
            }
    }
}
