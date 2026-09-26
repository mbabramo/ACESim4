using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleEquilibriumPaths;

namespace ACESimTest.GameTests;

[TestClass]
public class EquilibriumPathAnimationTests
{
    [TestMethod]
    public void RendererDistinguishesEveryCoreFeeAndRiskCase()
    {
        var runs = (from risk in new[] { "Baseline", "ModerateRiskAversion" }
                    from fee in new[] { "American", "British", "British__ExitFees-AllUnilateralExits" }
                    let toy = Toy()
                    select toy with { Metadata = toy.Metadata with {
                        Id = risk + fee, OptionSet = "Specification-" + risk + "__Cost-1__Fee-" + fee } }).ToArray();
        string html = EquilibriumPathAnimation.BuildHtml(runs);
        string encoded = Regex.Match(html, "<script id=\"trace-data\" type=\"application/gzip\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var packed = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        var titles = data.RootElement.EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToArray();
        titles.Distinct().Should().HaveCount(6);
        foreach (string fee in new[] { "American", "Trial Fee-Shifting", "Complete Fee-Shifting" })
            foreach (string risk in new[] { "Risk Neutral", "Risk Averse" })
                titles.Should().Contain(fee + " · " + risk);
    }
    private static EquilibriumPathAnimation.Run Toy(bool agreement = false)
    {
        int stages = agreement ? 6 : 4, setCount = stages * 4, actionCount = setCount * 2;
        var sets = Enumerable.Range(0, setCount).Select(i =>
        {
            byte p = (byte)(i / (stages * 2)); int stage = i % stages;
            string decision = stage switch { 0 => p == 0 ? "P Files" : "D Answers",
                1 => p == 0 ? "P Abandons" : "D Defaults",
                4 or 5 => p == 0 ? "P Agrees To Bargain" : "D Agrees To Bargain",
                _ => p == 0 ? "P Offer" : "D Offer" };
            int signal = i % (stages * 2) / stages;
            int? commitment = stage switch { 2 or 4 => 1, 3 or 5 => 2, _ => null };
            return new SetMetadata(i, i + 3, "set-" + i, i + 1, p, decision, signal + 1,
                signal == 0 ? .25 : .75, commitment, i * 2,
                stage is 2 or 3 ? new[] { "0.25", "0.75" } : new[] { "Yes", "No" });
        }).ToArray();
        var m = JsonSerializer.Deserialize<PathResult>("""
            {"Schema":"2","Id":"test","OptionSet":"Specification-Baseline__Cost-1__Fee-American",
             "Seed":0,"Exact":true,"Steps":3,"Pivots":2,"OriginalPivots":2,"FinalEpsilon":0,
             "MaximumSavedPolicyDifference":0,"Inputs":[]}
            """, CompactJson) with { InformationSets = sets,
                OptionSet = (agreement ? "Agreement-Enabled__" : "") + "Specification-Baseline__Cost-1__Fee-American" };
        var strategy = new ECTAIncentives(Enumerable.Repeat(.5, actionCount).ToArray(), new double[2], new double[2],
            new double[2], 0, 0, Enumerable.Repeat(.5, setCount).ToArray(), Enumerable.Repeat(.5, setCount).ToArray(),
            new double?[actionCount], new double?[actionCount], new double?[setCount], new double?[setCount], new double?[setCount]);
        var frames = Enumerable.Range(0, 3).Select(i => new PathFrame(i,
            i == 0 ? "initial-prior" : i == 2 ? "final-pivot" : "pivot",
            i == 0 ? null : new ECTAPivotSnapshot(i, 0, 1, i == 2, i == 2 ? 0 : 1,
                new double[4], new double[4], 0, 0, 0), 0, Array.Empty<int>(), strategy)).ToArray();
        return new(m, frames);
    }

    [TestMethod]
    public void AgreementRendererIncludesBothOwnCommitmentsWithoutLosingOtherDecisions()
    {
        var run = Toy(agreement: true);
        EquilibriumPathAnimation.ValidateFrames(run.Metadata, run.Frames);
        string html = EquilibriumPathAnimation.BuildHtml(new[] { run });
        string encoded = Regex.Match(html, "<script id=\"trace-data\" type=\"application/gzip\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var packed = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        var groups = data.RootElement[0].GetProperty("groups").EnumerateArray().ToArray();
        groups.Should().HaveCount(12);
        groups.SelectMany(g => g.GetProperty("sets").EnumerateArray().Select(s => s.GetInt32()))
            .Order().Should().Equal(Enumerable.Range(0, 24));
        foreach (byte player in new byte[] { 0, 1 })
        {
            var own = groups.Where(g => g.GetProperty("player").GetByte() == player).ToArray();
            own.Select(g => g.GetProperty("label").GetString()).Should().Equal(
                "Enter", "Commit to exit", "Agree · continue", "Agree · exit", "Offer · continue", "Offer · exit");
            foreach (var group in own.Where(g => g.GetProperty("label").GetString().StartsWith("Agree")))
                foreach (var index in group.GetProperty("sets").EnumerateArray())
                    run.Metadata.InformationSets[index.GetInt32()].Decision.Should().Contain("Agrees To Bargain");
        }
        html.Should().Contain("Offers occur only after both parties agree");
    }

    [TestMethod]
    public void RendererIncludesBothPlayersEveryHistoryAndEveryStep()
    {
        var run = Toy(); EquilibriumPathAnimation.ValidateFrames(run.Metadata, run.Frames);
        string html = EquilibriumPathAnimation.BuildHtml(new[] { run });
        var encoded = Regex.Match(html,
            "<script id=\"trace-data\" type=\"application/gzip\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var packed = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        var data = json.RootElement[0];
        data.GetProperty("sets").GetArrayLength().Should().Be(16);
        data.GetProperty("frames").GetArrayLength().Should().Be(3);
        data.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("sets").EnumerateArray()
            .Select(s => s.GetInt32())).Order().Should().Equal(Enumerable.Range(0, 16));
        foreach (var group in data.GetProperty("groups").EnumerateArray())
            group.GetProperty("sets").EnumerateArray().Select(s => run.Metadata.InformationSets[s.GetInt32()].Signal)
                .Should().BeInDescendingOrder("low signals belong at the bottom of each panel");
        html.Should().Contain("Skip unchanged probabilities").And.NotContain("Show off-path advantages")
            .And.Contain("Smooth color transitions").And.Contain("prefers-reduced-motion")
            .And.Contain("Save frame as PNG").And.NotContain("__TRACE_DATA__")
            .And.Contain("<h1>Solution paths</h1>").And.NotContain("class=\"sub\"")
            .And.Contain("class=\"primary\">Play</button>").And.NotContain("Play all")
            .And.Contain("Pivot in selected case").And.Contain("ctx.rotate(-Math.PI/2)")
            .And.NotContain("Reading the animation").And.NotContain("Color transition")
            .And.NotContain("<details>");
        html.Should().Contain("unreached?'#ffffff':color([225,236,244]")
            .And.Contain("if(!unreached && advantage")
            .And.NotContain("swatch hatch").And.NotContain("ctx.arc(")
            .And.Contain("Stored off-path probability:");
    }

    [TestMethod]
    public void ValidatorRejectsMissingStepsAndIncorrectProbabilities()
    {
        var run = Toy();
        Action missing = () => EquilibriumPathAnimation.ValidateFrames(run.Metadata, run.Frames.Skip(1).ToArray());
        missing.Should().Throw<InvalidDataException>();
        var p = run.Frames[0].Strategy.Probabilities.ToArray(); p[0] = .9; p[1] = .1;
        var changed = run.Frames.ToArray(); changed[0] = changed[0] with { Strategy = changed[0].Strategy with { Probabilities = p } };
        Action nonuniform = () => EquilibriumPathAnimation.ValidateFrames(run.Metadata, changed);
        nonuniform.Should().Throw<InvalidDataException>();
        p[0] = .8;
        Action invalidSum = () => EquilibriumPathAnimation.ValidateFrames(run.Metadata, changed);
        invalidSum.Should().Throw<InvalidDataException>();
    }

    [TestMethod]
    public void RendererRefusesToSilentlyOmitUnexpectedDecisions()
    {
        var run = Toy(); var sets = run.Metadata.InformationSets.ToArray();
        sets[0] = sets[0] with { Decision = "Unexpected decision" };
        Action render = () => EquilibriumPathAnimation.BuildHtml(new[] { run with { Metadata = run.Metadata with { InformationSets = sets } } });
        render.Should().Throw<InvalidDataException>();
    }

    [TestMethod]
    public void ReaderRejectsCorruptedTraceFingerprint()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ecta-animation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "test.json"); var run = Toy();
            var metadata = run.Metadata with { Frames = new(Path.ChangeExtension(file, ".jsonl"), "not-the-hash") };
            File.WriteAllText(file, JsonSerializer.Serialize(metadata, CompactJson));
            File.WriteAllText(Path.ChangeExtension(file, ".jsonl"), "corrupted");
            Action read = () => EquilibriumPathAnimation.ReadVerified(file);
            read.Should().Throw<InvalidDataException>();
        }
        finally { Directory.Delete(directory, true); }
    }
}
