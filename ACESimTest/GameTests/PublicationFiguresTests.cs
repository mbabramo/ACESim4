using CsvHelper;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass]
[DoNotParallelize]
public class PublicationFiguresTests
{
    [TestMethod]
    public void PublicationFilenamesDoNotDependOnManuscriptNumbering()
    {
        Assert.AreEqual("participation-and-offers", PublicationFigures.Stem("selection-offers"));
        Assert.AreEqual("disposition", PublicationFigures.Stem("dispositions"));
    }

    private static readonly PublicationFigures.StrategyCase Selection = new("American", "Spec", "unused.csv");
    private static Dictionary<string, string> Row(int signal, int info, int action, string label,
        double probability, string decision = "P Offer", double reach = .1) => new()
    {
        ["OptionSetName"] = "Spec", ["Equilibrium Number"] = "1", ["Decision"] = decision,
        ["Information Set Labels"] = $"P Liability Signal: {signal};P Files: 1;D Answers: 1;P Abandons: 2",
        ["Information Set Number"] = info.ToString(CultureInfo.InvariantCulture),
        ["Equilibrium Reach Probability"] = reach.ToString("R", CultureInfo.InvariantCulture),
        ["Off Path"] = (reach <= ACESim.InformationSetActionReport.OffPathTolerance).ToString(), ["Action"] = action.ToString(CultureInfo.InvariantCulture),
        ["Action Label"] = label, ["Equilibrium Action Probability"] = probability.ToString("R", CultureInfo.InvariantCulture)
    };

    [TestMethod]
    public void ParticipationRetainsActualMixedActionProbabilitiesAndExactSelection()
    {
        var ignored = Row(1, 99, 1, "Yes", 1, "P Files");
        ignored["Equilibrium Number"] = "2";
        var points = PublicationFigures.BuildStrategySeries([
            Row(1, 10, 1, "Yes", .87829387434104012, "P Files"),
            Row(1, 10, 2, "No", 1 - .87829387434104012, "P Files"),
            Row(2, 20, 1, "Yes", 1, "P Files"), Row(2, 20, 2, "No", 0, "P Files"), ignored
        ], Selection, "P Files");
        Assert.AreEqual(.25, points[0].SignalValue);
        Assert.AreEqual(.75, points[1].SignalValue);
        Assert.AreEqual(.87829387434104012, points[0].Actions.Single(a => a.Value == 1).Probability);
        Assert.AreEqual(2, points[0].Actions.Length);
        CollectionAssert.AreEqual(new[] { 10 }, points[0].InformationSets);
    }

    [TestMethod]
    public void OffersPreserveSupportAndExcludeUnreachableActions()
    {
        var points = PublicationFigures.BuildStrategySeries([
            Row(1, 10, 1, "0.2", .35), Row(1, 10, 2, "0.9", .65),
            Row(2, 20, 1, "0.2", 0, reach: 0), Row(2, 20, 2, "0.9", 1, reach: 0)
        ], Selection, "P Offer");
        Assert.AreEqual(2, points[0].Actions.Length);
        Assert.AreEqual(.2, points[0].Actions[0].Value);
        Assert.AreEqual(.35, points[0].Actions[0].Probability);
        Assert.IsTrue(points[1].OffPath);
        Assert.AreEqual(0, points[1].Actions.Length);
        var data = new PublicationFigures.StrategyData(["American", "British"], ["Spec", "Spec2"], [1, 1],
            new("request", "hash"), [], [new("P Offer", "Plaintiff demand", [points, points])]);
        string tex = PublicationFigures.RenderStrategies(data);
        StringAssert.Contains(tex, "$p=0.35$");
        StringAssert.Contains(tex, "$p=0.65$");
        Assert.IsFalse(tex.Contains("Off path"));
        Assert.IsFalse(tex.Contains("(0.75,-.19)"), "No off-path marker rail.");
        Assert.IsFalse(tex.Contains("(0.75,0.9)"), "No arbitrary off-path offer marker.");
        Assert.IsFalse(tex.Contains("0.655"), "Do not replace mixed support with a mean offer.");
    }

    [TestMethod]
    public void ExactModelGridRetainsFifteenOfferPrecisionAndReportOffPathTolerance()
    {
        var grid = PublicationFigures.GridForOptionSet("Specification-Baseline__Cost-1__Fee-American__Offers-15");
        Assert.AreEqual(10, grid.SignalCount);
        Assert.AreEqual(15, grid.Offers.Length);
        Assert.AreEqual(1.0 / 30, grid.Offers[0], 1e-15);
        var rows = Enumerable.Range(1, 15).Select(a => Row(1, 10, a,
            grid.Offers[a - 1].ToString("0.00", CultureInfo.InvariantCulture), a == 1 ? 1 : 0)).ToArray();
        var points = PublicationFigures.BuildStrategySeries(rows, Selection, "P Offer", 1, grid.Offers);
        Assert.AreEqual(1.0 / 30, points[0].Actions[0].Value, 1e-15);
        Assert.AreNotEqual(.03, points[0].Actions[0].Value);
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(rows, Selection, "P Offer", 2, grid.Offers));
        var offPath = PublicationFigures.BuildStrategySeries([Row(1, 10, 1, "0.2", 1, reach: 1e-16)], Selection, "P Offer");
        Assert.IsTrue(offPath[0].OffPath);
        Assert.AreEqual(1e-16, offPath[0].Reach);
    }

    [TestMethod]
    public void MultipleReachedHistoriesAreRejectedInsteadOfPooled()
    {
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries([
            Row(1, 10, 1, "0.2", 1), Row(1, 11, 1, "0.2", 1)
        ], Selection, "P Offer"));
    }

    [TestMethod]
    public void MissingDuplicateAndInvalidActionDataFailClosed()
    {
        Dictionary<string, string>[] Valid() => [
            Row(1, 10, 1, "0.2", 1), Row(1, 10, 2, "0.9", 0),
            Row(2, 20, 1, "0.2", 1), Row(2, 20, 2, "0.9", 0)
        ];
        foreach (string bad in new[] { "", "NaN", "Infinity", "-0.1", "1.1" })
        {
            var rows = Valid();
            rows[0]["Equilibrium Action Probability"] = bad;
            Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(rows, Selection, "P Offer"));
        }
        var duplicate = Valid().Append(Row(1, 10, 2, "0.9", 0)).ToArray();
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(duplicate, Selection, "P Offer"));
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(Valid()[..3], Selection, "P Offer"));
        var reach = Valid();
        reach[0]["Off Path"] = "true";
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(reach, Selection, "P Offer"));
        var missingBin = Valid();
        foreach (var row in missingBin.Skip(2)) row["Information Set Labels"] = "P Liability Signal: 3";
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.BuildStrategySeries(missingBin, Selection, "P Offer"));
    }

    [TestMethod]
    public void DispositionRoundingIsToleratedButNoRenormalizationOrDoubleAllocationOccurs()
    {
        double[] values = [0, 0, .380480, .16580035, .16580635, .143957, .143955];
        var original = values.ToArray();
        PublicationFigures.ValidateDisposition(values, .287913);
        CollectionAssert.AreEqual(original, values);
        values[3] += .0169047 / 2;
        values[4] += .0169047 / 2;
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.ValidateDisposition(values, .287913));
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.ValidateDisposition(original, .3));
        Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.ValidateDisposition([double.NaN, 0, 0, 0, 0, 0, 1], 1));
    }

    private static string WriteDispositionRequest(string directory, bool duplicate = false)
    {
        string[] headers = ["OptionSetName", "Filter", "Equilibrium Type", "Fee Regime", .. PublicationFigures.CategoryColumns,
            "Trial", "Mutual Give-Up Probability Before 50/50 Allocation"];
        using (var writer = new StreamWriter(Path.Combine(directory, "source.csv")))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (string header in headers) csv.WriteField(header);
            csv.NextRecord();
            foreach (string name in duplicate ? new[] { "American", "British", "American" } : new[] { "American", "British" })
            {
                foreach (string field in new[] { name, "All", "Only Eq", name, "0.2", "0.3", "0.1", "0.1", "0.1", "0.1", "0.1", "0.2", "0.04" })
                    csv.WriteField(field);
                csv.NextRecord();
            }
        }
        var request = new PublicationFigures.Request([
            new("American", "American", "not-required.csv"), new("British", "British", "not-required.csv")
        ], "source.csv", [new("Example", ["American", "British"])]);
        string path = Path.Combine(directory, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request));
        return path;
    }

    [TestMethod]
    public void DispositionPatternsMatchOutcomeFamiliesInBarsAndLegend()
    {
        var source = new PublicationFigures.Source("example", new string('0', 64));
        var data = new PublicationFigures.DispositionData(source, source, PublicationFigures.CategoryLabels,
            [new("Example", "American", "Example", [.2, .3, .1, .1, .1, .1, .1], 1, 0, .2)]);
        string latex = PublicationFigures.RenderDispositions(data);
        string[] styles =
        [
            "preaction={fill=white,draw=none},pattern={Dots[distance=4pt,radius=.35pt]},pattern color=black",
            "preaction={fill=black,draw=none},pattern={Dots[distance=2.8pt,radius=.35pt]},pattern color=white",
            "fill=black!50",
            "preaction={fill=white,draw=none},pattern={north east lines},pattern color=black",
            "preaction={fill=black,draw=none},pattern={north east lines},pattern color=white",
            "fill=white",
            "fill=black"
        ];
        var paths = latex.Split('\n').Where(line => line.StartsWith(@"\path[")).ToArray();
        Assert.AreEqual(14, paths.Length);
        for (int c = 0; c < styles.Length; c++)
        {
            StringAssert.StartsWith(paths[c], $@"\path[{styles[c]},draw=black,line width=.25pt]");
            StringAssert.StartsWith(paths[c + styles.Length], $@"\path[{styles[c]},draw=black,line width=.25pt]");
        }
        StringAssert.Contains(latex, @"\usetikzlibrary{patterns,patterns.meta}");
        StringAssert.Contains(latex, @"\mbox{Example}");
        StringAssert.Contains(latex, @"\node[text=white,fill=black,font=\footnotesize");
        StringAssert.Contains(latex, @"\node[text=black,fill=white,font=\footnotesize");
        Assert.IsFalse(latex.Contains("opacity="));
        CollectionAssert.AreEqual(new[] { .2, .3, .1, .1, .1, .1, .1 }, data.Bars[0].Values);
    }

    [TestMethod]
    public void ExactDispositionRowJoinPreservesSourceAuditAndRejectsAmbiguousRows()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-publication-tests-");
        try
        {
            var figure = PublicationFigures.Generate(WriteDispositionRequest(temp.FullName), "dispositions");
            var data = (PublicationFigures.DispositionData)figure.Data;
            Assert.AreEqual(2, data.Bars.Length);
            Assert.AreEqual(.1, data.Bars[0].Values[3]);
            Assert.AreEqual(.04, data.Bars[0].MutualGiveUpAudit);
            Assert.AreEqual(1, data.Bars[0].Sum, 1e-12);
            Assert.AreEqual(64, data.Source.Sha256.Length);
            Assert.AreEqual(64, data.RequestSource.Sha256.Length);
            Assert.ThrowsException<InvalidDataException>(() => PublicationFigures.Generate(
                WriteDispositionRequest(temp.FullName, duplicate: true), "dispositions"));
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task SeparateDispositionRequestsJoinStandardWorkflowWithoutSharingSelectionsOrCaptions()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-separate-disposition-tests-");
        try
        {
            string requestPath = WriteDispositionRequest(temp.FullName);
            var request = JsonSerializer.Deserialize<PublicationFigures.Request>(File.ReadAllText(requestPath));
            var extras = Enumerable.Range(1, 2).Select(i =>
            {
                string extraRequest = Path.Combine(temp.FullName, $"extension-{i}.request.json");
                File.WriteAllText(extraRequest, JsonSerializer.Serialize(request with
                {
                    DispositionGroups = [new($"Extension {i}", ["American", "British"])],
                    DispositionIntroduction = $"Standalone extension {i}."
                }));
                return new ArticleDiagramCommand.AdditionalDispositionFigure(extraRequest, $"supplement/extension-{i}.tex");
            }).ToArray();
            string configPath = Path.Combine(temp.FullName, "config.json");
            var config = new ArticleDiagramCommand.Configuration
            {
                PublicationFiguresDirectory = "figures", PublicationFiguresRequest = requestPath,
                AdditionalDispositionFigures = extras
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--list"]));
            Assert.IsFalse(Directory.Exists(Path.Combine(temp.FullName, "figures")));
            Assert.IsFalse(Directory.Exists(Path.Combine(temp.FullName, "supplement")));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--sources-only"]));
            string mainJson = Path.Combine(temp.FullName, "figures", "disposition.json");
            var mainData = JsonSerializer.Deserialize<PublicationFigures.DispositionData>(File.ReadAllText(mainJson));
            Assert.AreEqual("Example", mainData.Bars[0].Group);
            for (int i = 1; i <= 2; i++)
            {
                string stem = Path.Combine(temp.FullName, "supplement", $"extension-{i}");
                Assert.IsTrue(File.Exists(stem + ".tex"));
                StringAssert.StartsWith(File.ReadAllText(stem + ".txt"), $"Standalone extension {i}.");
                Assert.IsFalse(File.ReadAllText(stem + ".txt").Contains("Figure 4"));
                var data = JsonSerializer.Deserialize<PublicationFigures.DispositionData>(File.ReadAllText(stem + ".json"));
                Assert.AreEqual($"Extension {i}", data.Bars[0].Group);
                Assert.AreEqual(2, data.Bars.Length);
                Assert.AreNotEqual(mainData.RequestSource.Sha256, data.RequestSource.Sha256);
            }
            string review = Path.Combine(temp.FullName, "review");
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--sources-only", "--output-root", review]));
            Assert.AreEqual(3, Directory.GetFiles(review, "*.tex", SearchOption.AllDirectories).Length);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config with
            {
                PublicationFiguresRequest = "absent.json",
                AdditionalDispositionFigures = extras.Select(e => e with { Request = "absent.json" }).ToArray()
            }));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--compile-only", "--list"]));
            File.WriteAllText(configPath, JsonSerializer.Serialize(config with
            {
                AdditionalDispositionFigures = [extras[0] with { Request = "absent.json" }]
            }));
            string invalidOutput = Path.Combine(temp.FullName, "invalid-review");
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--sources-only", "--output-root", invalidOutput]));
            Assert.IsFalse(Directory.Exists(invalidOutput));
            File.WriteAllText(configPath, JsonSerializer.Serialize(config with
            {
                AdditionalDispositionFigures = [extras[0] with { Output = "extension.pdf" }]
            }));
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--list"]));
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task PublicationTargetsSupportReadOnlyPreflightAndSourceIndependentCompilation()
    {
        CollectionAssert.AreEqual(new[] { "selection-offers", "dispositions" }, ArticleDiagramCommand.ExpandTarget("publication"));
        CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget("all"), "dispositions");
        var temp = Directory.CreateTempSubdirectory("acesim-publication-tests-");
        try
        {
            string request = WriteDispositionRequest(temp.FullName);
            string configPath = Path.Combine(temp.FullName, "config.json");
            var config = new ArticleDiagramCommand.Configuration
            {
                PublicationFiguresDirectory = "figures", PublicationFiguresRequest = request
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--list"]));
            Assert.IsFalse(Directory.Exists(Path.Combine(temp.FullName, "figures")));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--sources-only"]));
            string stem = Path.Combine(temp.FullName, "figures", PublicationFigures.Stem("dispositions"));
            Assert.IsTrue(File.Exists(stem + ".tex"));
            Assert.IsTrue(File.Exists(stem + ".json"));
            Assert.IsTrue(File.Exists(stem + ".txt"));
            File.WriteAllText(configPath, JsonSerializer.Serialize(config with { PublicationFiguresRequest = "absent.json" }));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--compile-only", "--list"]));
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "dispositions", "--config", configPath, "--list"]));
        }
        finally { temp.Delete(recursive: true); }
    }
}
