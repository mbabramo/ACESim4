using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass]
public class WelfareOutcomeExhibitsTests
{
    private static void WriteCsv(string path, Dictionary<string, string>[] rows)
    {
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        string[] columns = rows.SelectMany(r => r.Keys).Distinct().ToArray();
        foreach (string c in columns) csv.WriteField(c);
        csv.NextRecord();
        foreach (var row in rows) { foreach (string c in columns) csv.WriteField(row.GetValueOrDefault(c, "")); csv.NextRecord(); }
    }

    // Synthetic no-entry records exercise reporting/accounting, not equilibrium determination.
    private static string Fixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "details"));
        var rows = new List<Dictionary<string, string>>();
        foreach (var (spec, alpha, cost, fee) in new[] {
            ("Baseline", "0", "1", "American"), ("ModerateRiskAversion", "2", "1", "British"),
            ("Baseline", "0", "2", "American"), ("LowNoise", "0", "1", "American"),
            ("LowNoiseModerateRiskAversion", "2", "1", "British") })
        {
            string name = $"Specification-{spec}__Cost-{cost}__Fee-{fee}";
            var row = new Dictionary<string, string>
            {
                ["Equilibrium Type"] = "Only Eq", ["Filter"] = "All", ["OptionSetName"] = name,
                ["Fee Regime"] = fee, ["Fee Shifting Multiplier"] = fee == "American" ? "0" : "1",
                ["CARA Alpha"] = alpha, ["Costs Multiplier"] = cost, ["Number of Offers"] = "10",
                ["Probability Truly Liable"] = ".5", ["P Files"] = "0", ["D Answers"] = "0", ["Trial"] = "0",
                ["Real Litigation Costs"] = "0", ["Liability Transfer to Plaintiff"] = "0",
                [CorrelatedSignalsFocusedReport.MeritoriousPlaintiffRecoveryShortfallColumn] = "1",
                [CorrelatedSignalsFocusedReport.NonliableDefendantNetBurdenColumn] = "0",
                [CorrelatedSignalsFocusedReport.LiableDefendantExcessNetBurdenColumn] = "0",
                [CorrelatedSignalsFocusedReport.NetOutcomeFidelityLossColumn] = ".5",
                [CorrelatedSignalsFocusedReport.MutualGiveUpBeforeAllocationColumn] = "0"
            };
            foreach (string c in PublicationFigures.CategoryColumns) row[c] = c == "No Suit" ? "1" : "0";
            rows.Add(row);
            var details = new[] { "All", "Truly Liable", "Truly Not Liable" }.Select(filter =>
            {
                var d = Row();
                d["OptionSet"] = name; d["Filter"] = filter;
                d["TrulyLiable"] = filter == "Truly Liable" ? "1" : filter == "Truly Not Liable" ? "0" : ".5";
                d["P Files"] = "0"; d["D Answers"] = "0";
                d["False-"] = filter == "Truly Liable" ? "1" : "0"; d["False+"] = "0";
                return d;
            }).ToArray();
            WriteCsv(Path.Combine(root, "details", "Test " + name + ".csv"), details);
        }
        WriteCsv(Path.Combine(root, "summary.csv"), rows.ToArray());
        string request = Path.Combine(root, "request.json");
        File.WriteAllText(request, JsonSerializer.Serialize(new WelfareOutcomeExhibits.Request(
            [new("summary.csv", "details", "Test")], "Results", RequireCompleteRoutineMatrix: false)));
        return request;
    }

    [TestMethod]
    public void RoutineDiagramsRejectIncompleteSourcesBeforeWritingAnything()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-incomplete-routine-");
        try
        {
            string path = Fixture(temp.FullName);
            var request = JsonSerializer.Deserialize<WelfareOutcomeExhibits.Request>(File.ReadAllText(path));
            File.WriteAllText(path, JsonSerializer.Serialize(new WelfareOutcomeExhibits.Request(request.Inputs, request.OutputDirectory)));
            Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.Prepare(path));
            Assert.IsFalse(Directory.Exists(Path.Combine(temp.FullName, "Results")));
        }
        finally { temp.Delete(true); }
    }

    [TestMethod]
    public void EveryAvailableExtensionAndCostGetsSeparateCleanArtifacts()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-welfare-tests-");
        try
        {
            var generation = WelfareOutcomeExhibits.Prepare(Fixture(temp.FullName));
            Assert.IsFalse(Directory.Exists(generation.OutputDirectory));
            Assert.AreEqual(5, generation.Cases);
            Assert.AreEqual(14, generation.Exhibits.Length);
            Assert.AreEqual(6, generation.Exhibits.Count(e => e.Family == "low-noise"));
            foreach (var e in generation.Exhibits)
            {
                string tex = generation.Files[e.TexFile];
                StringAssert.Contains(Path.GetFileName(e.TexFile), "cost-" + e.Cost);
                StringAssert.Contains(tex, @"\documentclass");
                Assert.IsFalse(tex.Contains("cost multiplier", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(tex.Contains(@"\caption"));
                Assert.IsFalse(tex.Contains("Core outcomes"));
                if (e.Kind == "welfare-outcomes")
                {
                    StringAssert.Contains(tex, "Xccccc");
                    Assert.IsFalse(tex.Contains(@"\footnotesize\noindent"));
                    Assert.IsFalse(tex.Contains(@"\large\bfseries"));
                }
            }
            string extensionTable = generation.Files[generation.Exhibits.Single(e => e.Family == "low-noise" && e.Kind == "welfare-outcomes" && e.TexFile.Contains("Risk Comparison")).TexFile];
            StringAssert.Contains(extensionTable, "Trial Fee-Shifting");
            Assert.IsFalse(extensionTable.Contains("Complete Fee-Shifting"));
            Assert.IsFalse(generation.Files.Keys.Any(p => p.EndsWith(".txt") || p.Contains("packet")));
        }
        finally { temp.Delete(true); }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task DiagramWorkflowValidatesWithoutWritingAndCompileOnlyNeedsNoResultInputs()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-welfare-cli-");
        try
        {
            Fixture(temp.FullName);
            string config = Path.Combine(temp.FullName, "config.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new ArticleDiagramCommand.Configuration { WelfareExhibitsRequest = "request.json" }));
            // dispositions must generate both new families without requiring the legacy figure request.
            string[] args = ["diagrams", "dispositions", "--config", config];
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Append("--list").ToArray()));
            string output = Path.Combine(temp.FullName, "Results");
            Assert.IsFalse(Directory.Exists(output));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Append("--sources-only").ToArray()));
            Assert.AreEqual(14, Directory.GetFiles(output, "*.tex", SearchOption.AllDirectories).Length);
            File.Delete(Path.Combine(temp.FullName, "summary.csv"));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Concat(new[] { "--compile-only", "--list" }).ToArray()));
            string failed = Path.Combine(temp.FullName, "failed");
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(args.Concat(new[] { "--sources-only", "--output-root", failed }).ToArray()));
            Assert.IsFalse(Directory.Exists(failed));
        }
        finally { temp.Delete(true); }
    }

    [TestMethod]
    public void DuplicateRowsAndMismatchedPrimitivesAreRejected()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-welfare-validation-");
        try
        {
            Fixture(temp.FullName);
            var rows = PublicationFigures.ReadCsv(Path.Combine(temp.FullName, "summary.csv"));
            Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.ValidateGroup([rows[0], rows[0]]));
            rows[1]["Party Signal Sigma"] = ".4";
            Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.ValidateGroup([rows[0], rows[1]]));
            rows[1]["Fees After Nonanswer"] = "true";
            Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.FeeLabel(rows[1]));
            rows[1]["Fee Shifting Trigger"] = ACESim.LitigGameCorrelatedSignalsArticleLauncher.ExitFeeTriggerLabel;
            Assert.AreEqual("Complete Fee-Shifting", WelfareOutcomeExhibits.FeeLabel(rows[1]));
        }
        finally { temp.Delete(true); }
    }

    [TestMethod]
    public void RoutineTargetsIncludeWelfareExhibits()
    {
        foreach (string target in new[] { "all", "results", "publication" })
            CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget(target), "welfare-outcomes");
    }

    private static Dictionary<string, string> Row(string settles = "0", string mean = "", string noAnswer = "0",
        string defaults = "0", string abandons = "0", string mutual = "0", string trial = "0", string answers = "0", string win = "0") => new()
    {
        ["Settles"] = settles, ["ValIfSettled"] = mean, ["DDoesntAnswer"] = noAnswer,
        ["DDefaults"] = defaults, ["PAbandons"] = abandons, ["BothReadyToGiveUp"] = mutual,
        ["Trial"] = trial, ["DAnswers"] = answers, ["P Wins"] = win
    };

    [TestMethod]
    public void NoEntryHasMeritoriousLossDespiteZeroExpenditureAndUndefinedSettlementMean()
    {
        var payment = WelfareOutcomeExhibits.GrossPayment(Row());
        Assert.IsNull(payment.SettlementMean);
        var error = WelfareOutcomeExhibits.OutcomeError(.5, payment, payment);
        Assert.AreEqual(.5, error.Error, 1e-14);
        Assert.AreEqual(.5, error.MeritoriousUnderpayment, 1e-14);
        Assert.AreEqual(0, error.NonliablePayment, 1e-14);
    }

    [TestMethod]
    public void ErrorUsesWithinTruthPaymentsRatherThanAbsoluteErrorOfOverallMean()
    {
        var liable = WelfareOutcomeExhibits.GrossPayment(Row("1", ".8", answers: "1"));
        var nonliable = WelfareOutcomeExhibits.GrossPayment(Row("1", ".2", answers: "1"));
        // Mean payment and mean truth both equal .5, but expected absolute error is .2.
        Assert.AreEqual(.2, WelfareOutcomeExhibits.OutcomeError(.5, liable, nonliable).Error, 1e-14);
        Assert.AreEqual(.2, WelfareOutcomeExhibits.OutcomeError(.3, liable, nonliable).Error, 1e-14);
    }

    [TestMethod]
    public void MutualGiveUpAllocationIsCountedOnceAndFeesNeverEnterBasePayment()
    {
        var unallocated = Row(".2", ".5", noAnswer: ".1", defaults: ".1", abandons: ".1", mutual: ".2", trial: ".3", answers: ".9", win: ".2");
        var allocated = Row(".2", ".5", noAnswer: ".1", defaults: ".2", abandons: ".2", mutual: ".2", trial: ".3", answers: ".9", win: ".2");
        allocated["Fee-Shifting Transfer to Plaintiff"] = "100";
        allocated["PWealth"] = "999";
        var a = WelfareOutcomeExhibits.GrossPayment(unallocated);
        var b = WelfareOutcomeExhibits.GrossPayment(allocated);
        Assert.AreEqual(.6, a.MeanPayment, 1e-14);
        Assert.AreEqual(a.MeanPayment, b.MeanPayment, 1e-14);
        Assert.IsTrue(a.AddedMutualAllocation);
        Assert.IsFalse(b.AddedMutualAllocation);
    }

    [TestMethod]
    public void PositiveSettlementRequiresItsMeanAndInvalidSupportOrMassFails()
    {
        Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.GrossPayment(Row("1", "", answers: "1")));
        Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.GrossPayment(Row("1", "1.1", answers: "1")));
        Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.GrossPayment(Row(".5", ".5", answers: "1")));
        Assert.ThrowsException<InvalidDataException>(() => WelfareOutcomeExhibits.OutcomeError(1.1, WelfareOutcomeExhibits.GrossPayment(Row()), WelfareOutcomeExhibits.GrossPayment(Row())));
    }
}
