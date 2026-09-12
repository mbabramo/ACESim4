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
    public void PublicationRowsSuppressUnreconciledAndUndefinedAttributions()
    {
        var old = Info();
        var scenarios = Scenarios(old, Enumerable.Repeat(0.0, 8).ToArray());
        var row = BuildRows(ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, new[] { .25, .75 }).Rows.Single();
        var report = new ContrastResult("2", new("test", "test", "old", "new"),
            "Specification-Baseline__Cost-1__Fee-American", "Specification-Baseline__Cost-1__Fee-British",
            new(), ReferenceFor(old), ReferenceFor(Policy(old, 0)), scenarios, Array.Empty<string>(),
            new[] { row }, Array.Empty<ExcludedHistory>(), new[] { .25, .75 });
        string RowText(ChangeRow candidate) => LitigCharts.EquilibriumChangeTables.Latex(new[]
            { report with { Changes = new[] { candidate } } }).Split('\n').Single(x => x.StartsWith("File"));
        RowText(row).Should().Contain(" & -100 & -100 & 0 & 0 & 0");
        string selected = RowText(row with { EndpointSelection = true });
        selected.Should().Contain(@"\multicolumn{4}{c}{Selection-dependent}");
        selected.Should().NotContain(" & -100 & -100");
        string undefined = RowText(row with { CounterfactualUndefined = true, EndpointSelection = true });
        undefined.Should().Contain(@"\multicolumn{4}{c}{Undefined counterfactual}");
        undefined.Should().NotContain("Selection-dependent");
        LitigCharts.EquilibriumChangeTables.Latex(new[] { report }).Should()
            .Contain("Original & Target & Change & Direct & Entry & Offers & Exit");
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
