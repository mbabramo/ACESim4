using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass]
public class EquilibriumPublicationTablesTests
{
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
    public void CompactLayoutRejectsHiddenRemaindersUndefinedValuesAndMixedOfferMetrics()
    {
        foreach (var row in new[]
        {
            Row() with { Allocation = new(100, 0, 0, 0, 0, 0, -100) },
            Row() with { CounterfactualUndefined = true },
            Row() with { Metric = "offer action probability" }
        })
        {
            Action validate = () => EquilibriumPublicationTables.ValidateRows(new[] { row });
            validate.Should().Throw<System.IO.InvalidDataException>();
        }
    }

    [TestMethod]
    public void LatexIncludesOnlyHeadingsTableAndExplicitUnitsWithoutNotes()
    {
        var result = new ContrastResult("2", new("fee-shifting-risk-averse-cost-1", "", "old", "new"),
            "Specification-ModerateRiskAversion__Cost-1__Fee-American", "Specification-ModerateRiskAversion__Cost-1__Fee-British",
            new(), null, null, Array.Empty<Scenario>(), Array.Empty<string>(), new[] { Row() }, Array.Empty<ExcludedHistory>(), null);
        var latex = EquilibriumPublicationTables.Latex(result, new[] { Row(), Row(2, true) });
        latex.Should().Contain("At some signals").And.Contain(@"\mathrm{pp}").And.Contain(@"100\%");
        latex.Should().NotContain("footnote").And.NotContain("Remaining").And.NotContain("Methodology");
        EquilibriumPublicationTables.FileName(result).Should().Be("American to British - moderate risk aversion");
    }
}
