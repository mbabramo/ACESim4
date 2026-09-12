using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace ACESimTest.GameTests;

[TestClass]
public class PublicationTablesTests
{
    private static Dictionary<string, string> Summary(string name = "Baseline", string filter = "All", string eq = "Only Eq", string group = "") => new()
    {
        ["OptionSetName"] = name, ["Filter"] = filter, ["Equilibrium Type"] = eq, ["GroupName"] = group, ["Trial"] = "0.123456"
    };

    [TestMethod]
    public void SelectsOneExactUngroupedSummaryInsteadOfPooling()
    {
        var wanted = Summary();
        var result = PublicationTables.SelectSummary([wanted, Summary("Other"), Summary(filter: "Truly Liable"),
            Summary(eq: "Average"), Summary(group: "comparison")], "Baseline");
        Assert.AreSame(wanted, result);
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.SelectSummary([wanted, Summary()], "Baseline"));
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.SelectSummary([wanted], "Missing"));
    }

    [TestMethod]
    public void ConditionalAnsweringUsesFilingDenominatorAndMarksUndefined()
    {
        Assert.AreEqual(.75, PublicationTables.ConditionalRate(.3, .4).Value, 1e-15);
        Assert.IsNull(PublicationTables.ConditionalRate(0, 0));
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.ConditionalRate(.3, .2));
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.ConditionalRate(0, double.NaN));
    }

    [TestMethod]
    public void NumericCellsFailClosedForMissingNonfiniteAndInvalidValues()
    {
        foreach (string value in new[] { "", "NaN", "Infinity", "not a number" })
            Assert.ThrowsException<InvalidDataException>(() => PublicationTables.Number(new Dictionary<string, string> { ["x"] = value }, "x"));
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.Number(new Dictionary<string, string>(), "missing"));
        Assert.AreEqual(.123456, PublicationTables.Number(Summary(), "Trial"));
    }

    [TestMethod]
    public void FormatsPercentagesDeltasAndScientificDiagnosticsWithoutNegativeZero()
    {
        Assert.AreEqual("12.35", PublicationTables.Format(.123456, "percent"));
        Assert.AreEqual("+0.056", PublicationTables.Format(.05618, "delta"));
        Assert.AreEqual("-0.056", PublicationTables.Format(-.05618, "delta"));
        Assert.AreEqual("0.000", PublicationTables.Format(-.000001, "delta"));
        Assert.AreEqual(@"$8.88\times10^{-16}$", PublicationTables.Format(8.882e-16, "scientific"));
    }

    private static PublicationTables.Table Table(params PublicationTables.Cell[] cells) => new("descriptive-name", "Test table",
        [new("A. Test", "@{}Xr@{}", ["Outcome", "Value"], [new(cells)])], "Test notes.", "Caption.", [], []);

    [TestMethod]
    public void DeliveredLatexIsAnUnnumberedManuscriptFragment()
    {
        string fragment = PublicationTables.RenderFragment(Table(new("Trial"), new("12.35", .123456)));
        StringAssert.Contains(fragment, @"\begin{tabularx}{\linewidth}");
        StringAssert.Contains(fragment, "Trial & 12.35");
        Assert.IsFalse(fragment.Contains(@"\documentclass"));
        Assert.IsFalse(fragment.Contains(@"\caption"));
        Assert.IsFalse(fragment.Contains(@"\begin{table}"));
        string standalone = PublicationTables.Standalone("Test table", fragment);
        StringAssert.Contains(standalone, fragment);
        StringAssert.Contains(standalone, @"\documentclass");
    }

    [TestMethod]
    public void RendererRejectsMismatchedRowsInsteadOfDroppingCells()
    {
        Assert.ThrowsException<InvalidDataException>(() => PublicationTables.RenderFragment(Table(new PublicationTables.Cell("Only one cell"))));
    }
}
