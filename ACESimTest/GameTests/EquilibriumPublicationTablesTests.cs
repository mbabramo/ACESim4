using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Collections.Generic;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass]
public class EquilibriumPublicationTablesTests
{
    [TestMethod]
    public void ManuscriptUsesTheSevenOrderedComparisonsAndAllSuppliedCoordinates()
    {
        EquilibriumManuscriptTable.Comparisons().Select(c => c.Id).Should().Equal(
            "american-to-trial-risk-neutral", "trial-to-complete-risk-neutral",
            "american-to-trial-risk-averse", "trial-to-complete-risk-averse",
            "risk-neutral-to-risk-averse-american", "risk-neutral-to-risk-averse-trial",
            "risk-neutral-to-risk-averse-complete");
        var offset = EquilibriumPublicationTables.Select(OffsetFixture()).Single();
        var rows = new[] { Row(), Row(2, true), Row(8), offset };
        var latex = EquilibriumManuscriptTable.Latex(new[] {
            new EquilibriumManuscriptTable.Panel(new("example", "Example"), rows) });
        // Includes formerly omitted low signals, a distant signal, and an unchanged offset.
        latex.Should().Contain(EquilibriumPublicationTables.LatexBody(rows).Replace("\r\n", "\n"))
            .And.Contain("0.05--0.15").And.Contain("0.75")
            .And.Contain("Unchanged actions with offsetting effects").And.Contain("At some signals");
    }

    private static ChangeRow Row(int signal = 1, bool sensitive = false) => new(
        "p-file-" + signal, 0, "P Files", signal, (signal - .5) / 10, null,
        "decision probability", null, new[] { "Yes", "No" }, new[] { 1.0, 0 }, new[] { 0.0, 1 },
        new(100, 0, 0, 0, -100, 0, 0), false, false, sensitive, false, 0, Array.Empty<int>());

    [TestMethod]
    public void GroupsEqualNumericalRowsAndExplicitlyMarksSensitivityAtSomeSignals()
    {
        var groups = EquilibriumPublicationTables.GroupRows(new[] { Row(), Row(2), Row(3, true) });
        groups.Should().ContainSingle();
        groups[0].Rows.Length.Should().Be(3);
        groups[0].Sensitivity.Should().Be("At some signals");
        EquilibriumPublicationTables.SensitivityLabel(new[] { Row(), Row(2) }).Should().Be("No");
        EquilibriumPublicationTables.SensitivityLabel(new[] { Row(1, true), Row(2, true) }).Should().Be("Yes");
    }

    [TestMethod]
    public void DoesNotGroupAcrossMissingSignalsDifferentEffectsOrDifferentPolicies()
    {
        EquilibriumPublicationTables.GroupRows(new[] { Row(), Row(3) }).Length.Should().Be(2);
        EquilibriumPublicationTables.GroupRows(new[] { Row(), Row(2) with
        { Allocation = new(100, 0, -100, 0, 0, 0, 0) } }).Length.Should().Be(2);
        EquilibriumPublicationTables.GroupRows(new[] { Row(), Row(2) with
        { OriginalPolicy = new[] { .9, .1 } } }).Length.Should().Be(2);
    }

    [TestMethod]
    public void GeneralizedLayoutAcceptsExplicitRemaindersUndefinedValuesAndMixedOfferMetrics()
    {
        foreach (var row in new[]
        {
            Row() with { Allocation = new(100, 0, 0, 0, 0, 0, -100) },
            Row() with { CounterfactualUndefined = true },
            Row() with { Metric = "offer action probability" }
        })
        {
            Action validate = () => EquilibriumPublicationTables.ValidateRows(new[] { row });
            validate.Should().NotThrow();
        }
    }

    [TestMethod]
    public void LatexIncludesOnlyHeadingsTableAndExplicitUnitsWithoutNotes()
    {
        var result = new ContrastResult("2", new("fee-shifting-risk-averse-cost-1", "", "old", "new"),
            "Specification-ModerateRiskAversion__Cost-1__Fee-American", "Specification-ModerateRiskAversion__Cost-1__Fee-British",
            new(), null, null, Array.Empty<Scenario>(), Array.Empty<string>(), new[] { Row() }, Array.Empty<ExcludedHistory>(), null);
        var latex = EquilibriumPublicationTables.Latex(result, new[] { Row(), Row(2, true) });
        latex.Should().Contain("At some signals").And.Contain("Remaining").And.Contain(@"100\%");
        latex.Should().NotContain("footnote").And.NotContain("ordinary costs").And.NotContain("Methodology");
        EquilibriumPublicationTables.FileName(result).Should().Be("fee-shifting-risk-averse-cost-1");
    }

    private static ContrastResult OffsetFixture(double directLoss = .02)
    {
        var info = new InformationSet("p-file-4", 4, 0, "P Files", "", 4, .35, null,
            .1, .1, false, false, new[] { 1.0 }, 0, new[] {
                new ActionValue(1, "Yes", 1, 0, 0, true, true),
                new ActionValue(2, "No", 0, 0, 0, true, true) }, 0, 0);
        var reference = new Reference("test", "test", new[] { 0.0, 0.0 }, 1, new[] { info });
        var scenarios = Enumerable.Range(0, 8).Select(mask =>
        {
            bool offers = (mask & 2) != 0;
            var value = info with { Actions = info.Actions.Select((a, i) => a with {
                Probability = (i == 0) == offers ? 1 : 0,
                CounterfactualConditionalUtility = (i == 0) == offers ? directLoss : 0
            }).ToArray() };
            return new Scenario("coalition", mask.ToString(), new Result("test", "test", 0, "test",
                0, 0, 0, 0, 0, 1, new Profile("test", "test", new Dictionary<string, Strategy>()),
                new[] { value }, Array.Empty<CompletionWarning>()));
        }).ToArray();
        return new("2", new("risk-aversion-american-cost-1", "", "old", "new"),
            "Specification-Baseline__Cost-1__Fee-American", "Specification-ModerateRiskAversion__Cost-1__Fee-American",
            new(), reference, reference, scenarios, Array.Empty<string>(), Array.Empty<ChangeRow>(),
            Array.Empty<ExcludedHistory>(), new[] { .25, .75 });
    }

    [TestMethod]
    public void IncludesStrictDirectEffectOffsetByOpponentDespiteIdenticalEndpoints()
    {
        var result = OffsetFixture();
        var rows = EquilibriumPublicationTables.Select(result);
        rows.Should().ContainSingle();
        EquilibriumPublicationTables.Unchanged(rows[0]).Should().BeTrue();
        rows[0].Allocation.Direct.Should().Be(-100);
        rows[0].Allocation.Offers.Should().BeApproximately(100, 1e-10);
        rows[0].Allocation.Entry.Should().Be(0);
        rows[0].Allocation.Exit.Should().Be(0);
        rows[0].Allocation.Change.Should().Be(0);
        rows[0].Allocation.SelectionResidual.Should().Be(0);
        result.Changes.Should().BeEmpty("cached changed-only results must not be mutated");
    }

    [TestMethod]
    public void OffsetsRejectDirectTiesAndUnchangedBestResponses()
    {
        var tied = OffsetFixture(1e-7);
        EquilibriumPublicationTables.OffsettingEffects(tied).Should().BeEmpty();
        var strict = OffsetFixture();
        EquilibriumPublicationTables.Select(tied).Should().BeEmpty(
            "a direct switch among tied actions does not establish offsetting incentives");
        EquilibriumPublicationTables.Select(strict).Should().ContainSingle();
        var unchangedResponse = strict with { Scenarios = strict.Scenarios.Select(s => s with {
            Result = s.Result with { InformationSets = strict.SourceEquilibrium.InformationSets }
        }).ToArray() };
        EquilibriumPublicationTables.OffsettingEffects(unchangedResponse).Should().BeEmpty();
    }

    [TestMethod]
    public void OffsetsRejectEndpointReachChangesUndefinedHybridsAndNonzeroRemainders()
    {
        var result = OffsetFixture();
        var target = result.TargetEquilibrium with { InformationSets = result.TargetEquilibrium.InformationSets
            .Select(s => s with { ActualOffPath = true, ActualReach = 0 }).ToArray() };
        EquilibriumPublicationTables.OffsettingEffects(result with { TargetEquilibrium = target }).Should().BeEmpty();
        var undefined = result.Scenarios.Select(s => s.Component != "1" ? s : s with {
            Result = s.Result with { InformationSets = s.Result.InformationSets.Select(i => i with {
                CounterfactuallyUnreachable = true, CounterfactualReach = 0 }).ToArray() }
        }).ToArray();
        EquilibriumPublicationTables.OffsettingEffects(result with { Scenarios = undefined }).Should().BeEmpty();
        var unmatched = result.Scenarios.Select(s => s.Component != "7" ? s : s with {
            Result = result.Scenarios[0].Result
        }).ToArray();
        EquilibriumPublicationTables.OffsettingEffects(result with { Scenarios = unmatched }).Should().BeEmpty();
    }

    [TestMethod]
    public void SeparatesOffsetSectionAndMarksConditionallyDefinedUnreachedHybridsWithoutNotes()
    {
        var result = OffsetFixture();
        var scenarios = result.Scenarios.Select(s => s.Component != "0" ? s : s with {
            Result = s.Result with { InformationSets = s.Result.InformationSets.Select(i => i with {
                ActualOffPath = true, ActualReach = 0 }).ToArray() }
        }).ToArray();
        result = result with { Scenarios = scenarios };
        var row = EquilibriumPublicationTables.OffsettingEffects(result).Single();
        row.UnreachedCoalitions.Should().Equal(0);
        row.CounterfactualUndefined.Should().BeFalse();
        var latex = EquilibriumPublicationTables.Latex(result, new[] { Row(), row });
        latex.Should().Contain("Changed actions").And.Contain("Unchanged actions with offsetting effects")
            .And.Contain("P files$^{*}$").And.Contain(@"100\%\to 100\%");
        latex.Should().NotContain("footnote").And.NotContain("Methodology");
    }
}
