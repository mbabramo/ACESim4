using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.DiscreteProbabilities;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ACESimTest.GameTests;

[TestClass]
public class ArticleSignalDiagramsTests
{
    [TestMethod]
    public void AllArticleStructuresUseProductionPartyMarginalsAndConserveMass()
    {
        foreach (string specification in ArticleSignalDiagrams.DefaultSpecifications.Concat(
            new[] { "LowNoise", "HighNoise", "CenterWeightedContinuousMerits", "PolarizedContinuousMerits" }))
        {
            var diagram = ArticleSignalDiagrams.Generate(specification, true);
            var options = ArticleWorkedPathExtraction.CreateOptions("Specification-" + specification + "__Cost-1__Fee-American");
            var definition = new LitigGameDefinition();
            definition.Setup(options);
            var production = options.LitigGameDisputeGenerator.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
            var panels = diagram.SelectMany(d => d.Panels).ToArray();
            var panel = panels.Single(p => p.DestinationTitle.StartsWith("Party signal"));
            for (int j = 0; j < production.Length; j++)
                // Same signal kernel; whole-interval vs split-interval quadrature crosses
                // the numerical normal-CDF approximation at different abscissas.
                Assert.AreEqual(production[j], panel.JointMass.Sum(r => r[j]), 1E-7, specification);
            foreach (var p in panels)
            {
                ArticleSignalDiagrams.Validate(p);
                for (int i = 0; i < p.JointMass.Length; i++)
                    for (int j = 0; j < p.JointMass[i].Length; j++)
                        Assert.AreEqual(p.JointMass[i][j], p.JointMass[^(i + 1)][^(j + 1)], 1E-7);
            }
        }
    }

    [TestMethod]
    public void ContinuousDisplayGroupsIntegrateTruthExactlyAndConverge()
    {
        var masses = ArticleSignalDiagrams.ContinuousMass(ContinuousQualityDistribution.Uniform, q => [1 - q, q]);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(.2, masses[i].Sum(), 1E-12);
            Assert.AreEqual((i + .5) / 5, masses[i][1] / masses[i].Sum(), 1E-12);
        }
        foreach (var distribution in Enum.GetValues<ContinuousQualityDistribution>())
        {
            var low = ArticleSignalDiagrams.ContinuousMass(distribution, q => [1 - q, q], 32);
            var high = ArticleSignalDiagrams.ContinuousMass(distribution, q => [1 - q, q], 64);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 2; j++) Assert.AreEqual(high[i][j], low[i][j], 1E-12);
        }
    }

    [TestMethod]
    public void ColorAndMonochromeShareDataWithoutFragilePdfEffects()
    {
        foreach (string specification in ArticleSignalDiagrams.DefaultSpecifications.Append("Damages"))
        {
            var color = ArticleSignalDiagrams.Generate(specification, false);
            var bw = ArticleSignalDiagrams.Generate(specification, true);
            Assert.AreEqual(JsonSerializer.Serialize(color.SelectMany(d => d.Panels)), JsonSerializer.Serialize(bw.SelectMany(d => d.Panels)));
            Assert.IsTrue(color.Concat(bw).All(d => d.Panels.Length == 1));
            foreach (string latex in color.Concat(bw).Select(d => d.Latex))
            {
                Assert.IsFalse(latex.Contains("opacity"));
                Assert.IsFalse(latex.Contains("left color"));
                Assert.IsFalse(latex.Contains("shading"));
                Assert.IsFalse(latex.Contains("Outlined fan"));
                Assert.IsFalse(latex.Contains("Conditional p"));
                Assert.IsFalse(latex.Contains("The same merits"));
            }
            Assert.IsTrue(bw.All(d => !d.Latex.Contains("blue")));
            Assert.IsTrue(bw.All(d => !d.Latex.Contains("orange")));
        }
        CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget("all"), "signals");
        CollectionAssert.DoesNotContain(ArticleDiagramCommand.ExpandTarget("all"), "damages-signals");
    }

    [TestMethod]
    public void MinimalLabelsUseMathNotationAndConditionalValuesLiveInCompanions()
    {
        foreach (var diagram in ArticleSignalDiagrams.Generate("Baseline", true))
        {
            StringAssert.Contains(diagram.Latex, "$Q$");
            Assert.IsFalse(diagram.Latex.Contains(diagram.Panels[0].Title));
            Assert.IsFalse(diagram.Latex.Contains("text width="));
            StringAssert.Contains(diagram.Latex, @"\usetikzlibrary{fit}");
            StringAssert.Contains(diagram.Latex, "source-labels-0.west |- axis-midline-0");
            StringAssert.Contains(diagram.Latex, "destination-labels-0.east |- axis-midline-0");
            StringAssert.Contains(diagram.Latex, "rotate=90,anchor=south");
            StringAssert.Contains(diagram.Latex, "rotate=90,anchor=north");
            Assert.IsFalse(diagram.Latex.Contains("heading-baseline"));
            StringAssert.Contains(diagram.Description, "Conditional destination probabilities, bottom to top");
            var panel = diagram.Panels[0];
            Assert.AreEqual(.2, panel.HighlightedSourceMass, 1e-10);
            Assert.AreEqual(1, panel.ConditionalDestinationProbabilities.Sum(), 1e-12);
            for (int j = 0; j < panel.Destinations.Length; j++)
                Assert.AreEqual(panel.JointMass[panel.Highlight][j],
                    panel.ConditionalDestinationProbabilities[j] * panel.HighlightedSourceMass, 1e-12);
            if (panel.DestinationTitle.Contains("sigma")) StringAssert.Contains(diagram.Latex, @"$\sigma=0.20$");
            else Assert.AreEqual(.7, panel.ConditionalDestinationProbabilities[1], 1e-12);
            StringAssert.Contains(JsonSerializer.Serialize(panel), "ConditionalDestinationProbabilities");
        }
    }

    [TestMethod]
    public void EverySignalFamilyHighlightsBothPalettesWithoutEncodingSignalStrengthInGray()
    {
        foreach (string specification in ArticleSignalDiagrams.DefaultSpecifications.Concat(
            new[] { "Damages", "LowNoise", "HighNoise", "CenterWeightedContinuousMerits", "PolarizedContinuousMerits" }))
        {
            Func<string, bool, ArticleSignalDiagrams.Diagram[]>[] generators = specification == "Damages"
                ? [ArticleSignalDiagrams.Generate]
                : [ArticleSignalDiagrams.Generate, ArticleSignalDiagrams.GenerateInverse, ArticleSignalDiagrams.GeneratePartyToParty];
            foreach (var generate in generators)
            {
                var colors = generate(specification, false);
                var grays = generate(specification, true);
                for (int k = 0; k < colors.Length; k++)
                {
                    var panel = colors[k].Panels.Single();
                    Assert.AreEqual(JsonSerializer.Serialize(panel), JsonSerializer.Serialize(grays[k].Panels.Single()));
                    // Match ribbons only, not the white bucket rectangles.
                    const string ribbonPattern = @"\\path\[([^\]]+)\] \([^,]+,([^)]+)\) \.\. controls";
                    var colorPaths = Regex.Matches(colors[k].Latex, ribbonPattern).Cast<Match>().ToArray();
                    var grayPaths = Regex.Matches(grays[k].Latex, ribbonPattern).Cast<Match>().ToArray();
                    Assert.AreEqual(panel.Sources.Length * panel.Destinations.Length, colorPaths.Length);
                    Assert.AreEqual(colorPaths.Length, grayPaths.Length);
                    int[] drawOrder = Enumerable.Range(0, panel.Sources.Length).OrderBy(i => i == panel.Highlight).ToArray();
                    for (int row = 0; row < drawOrder.Length; row++)
                    {
                        int source = drawOrder[row];
                        bool lower = (source + .5) / panel.Sources.Length < .5;
                        bool reference = source == panel.Highlight;
                        string hue = lower ? "orange" : "blue";
                        string fill = hue + (reference ? "!50!white" : lower ? "!16!white" : "!12!white");
                        string expected = $"fill={fill},draw=none";
                        foreach (var path in colorPaths.Skip(row * panel.Destinations.Length).Take(panel.Destinations.Length))
                            Assert.AreEqual(expected, path.Groups[1].Value, colors[k].FileStem);
                        string grayStyle = reference ? "fill=black!55,draw=none" : "fill=black!12,draw=none";
                        foreach (var path in grayPaths.Skip(row * panel.Destinations.Length).Take(panel.Destinations.Length))
                            Assert.AreEqual(grayStyle, path.Groups[1].Value, grays[k].FileStem);
                    }
                    CollectionAssert.AreEqual(colorPaths.Select(p => p.Value[(p.Value.IndexOf(']') + 1)..]).ToArray(),
                        grayPaths.Select(p => p.Value[(p.Value.IndexOf(']') + 1)..]).ToArray(),
                        "Both palettes must draw the reference fan last, with identical ribbon coordinates.");
                    StringAssert.Contains(grays[k].Description, "Gray shade indicates highlighting, not signal strength");
                    StringAssert.Contains(colors[k].Description, "retains its source hue");
                    foreach (var diagram in new[] { colors[k], grays[k] })
                    {
                        StringAssert.Contains(diagram.Description, "No ribbons have outlines");
                        StringAssert.Contains(diagram.Latex, "fill=white,draw=black,line width=0.3pt");
                        Assert.IsFalse(diagram.Latex.Contains("line width=0.15pt"));
                        Assert.IsFalse(diagram.Latex.Contains("line width=0.08pt"));
                    }
                }
            }
        }
    }

    [TestMethod]
    public void InversePartyFlowsPreserveJointMassAndApplyBayesWithThePrior()
    {
        foreach (string specification in ArticleSignalDiagrams.DefaultSpecifications)
        {
            var forward = ArticleSignalDiagrams.Generate(specification, true).Single(d => d.Panels[0].DestinationTitle.StartsWith("Party signal")).Panels[0];
            var diagram = ArticleSignalDiagrams.GenerateInverse(specification, true).Single();
            var inverse = diagram.Panels.Single();
            Assert.AreEqual("0.45", inverse.Sources[inverse.Highlight]);
            Assert.AreEqual(1, inverse.ConditionalDestinationProbabilities.Sum(), 1e-12);
            CollectionAssert.AreEqual(forward.Destinations, inverse.Sources);
            for (int j = 0; j < forward.Destinations.Length; j++)
            {
                Assert.AreEqual(forward.JointMass.Sum(row => row[j]), inverse.JointMass[j].Sum(), 1e-12);
                for (int i = 0; i < inverse.Destinations.Length; i++)
                {
                    double expected = specification == "TruthConditionedLatentMerits"
                        ? forward.JointMass[2 * i][j] + forward.JointMass[2 * i + 1][j]
                        : forward.JointMass[i][j];
                    Assert.AreEqual(expected, inverse.JointMass[j][i], 1e-12);
                    if (j == inverse.Highlight)
                        Assert.AreEqual(expected / inverse.HighlightedSourceMass, inverse.ConditionalDestinationProbabilities[i], 1e-12);
                }
            }
            Assert.AreEqual(JsonSerializer.Serialize(inverse), JsonSerializer.Serialize(
                ArticleSignalDiagrams.GenerateInverse(specification, false).Single().Panels.Single()));
            Assert.IsTrue(diagram.Latex.Contains("rotate=90"));
            Assert.IsFalse(diagram.Latex.Contains("Conditional p"));
            StringAssert.Contains(diagram.Description, "NOT conditioned on filing");
        }
    }

    [TestMethod]
    public void CommonMeritsBinsRevealTheActualModerateMiddleSignalDifference()
    {
        var continuous = ArticleSignalDiagrams.GenerateInverse("Baseline", true).Single().Panels.Single();
        var latent = ArticleSignalDiagrams.GenerateInverse("TruthConditionedLatentMerits", true).Single().Panels.Single();
        CollectionAssert.AreEqual(continuous.Destinations, latent.Destinations);
        Assert.AreEqual("0.40--0.60", continuous.Destinations[2]);
        Assert.AreEqual(.348309, continuous.ConditionalDestinationProbabilities[2], 1e-6);
        Assert.AreEqual(.313767, latent.ConditionalDestinationProbabilities[2], 1e-6);
        var direct = ArticleSignalDiagrams.GenerateInverse("DirectBinaryStateSignals", true).Single().Panels.Single();
        CollectionAssert.AreEqual(new[] { "Not liable", "Liable" }, direct.Destinations);
        Assert.AreEqual(.399921, direct.ConditionalDestinationProbabilities[1], 1e-6);
        CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget("all"), "inverse-signals");
        CollectionAssert.AreEqual(new[] { "inverse-signals" }, ArticleDiagramCommand.ExpandTarget("inverse-signals"));
    }

    [TestMethod]
    public void PartyPredictionsMatchIndependentKernelProductsNotGroupedMeritsApproximations()
    {
        foreach (string specification in ArticleSignalDiagrams.DefaultSpecifications.Concat(
            new[] { "LowNoise", "HighNoise", "CenterWeightedContinuousMerits", "PolarizedContinuousMerits" }))
        {
            var options = ArticleWorkedPathExtraction.CreateOptions("Specification-" + specification + "__Cost-1__Fee-American");
            var definition = new LitigGameDefinition();
            definition.Setup(options);
            var diagram = ArticleSignalDiagrams.GeneratePartyToParty(specification, true).Single();
            var panel = diagram.Panels.Single();
            ArticleSignalDiagrams.Validate(panel);
            int n = panel.Sources.Length, m = panel.Destinations.Length;
            double[][] expected;
            if (options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator continuous)
            {
                var groups = ArticleSignalDiagrams.ContinuousMass(continuous.QualityDistribution, q =>
                {
                    var p = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, options.PLiabilitySignalParameters);
                    var d = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, options.DLiabilitySignalParameters);
                    return p.SelectMany(pi => d.Select(dj => pi * dj)).ToArray();
                });
                expected = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, m)
                    .Select(j => groups.Sum(group => group[i * m + j])).ToArray()).ToArray();
            }
            else
            {
                var forward = ArticleSignalDiagrams.Generate(specification, true)
                    .Single(d => d.Panels[0].DestinationTitle.StartsWith("Party signal")).Panels.Single();
                double[] q = specification == "DirectBinaryStateSignals" ? [0, 1] :
                    Enumerable.Range(0, forward.Sources.Length).Select(i => (i + .5) / forward.Sources.Length).ToArray();
                var kernels = q.Select(value => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(
                    value, options.DLiabilitySignalParameters)).ToArray();
                expected = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, m)
                    .Select(j => Enumerable.Range(0, q.Length).Sum(k => forward.JointMass[k][i] * kernels[k][j])).ToArray()).ToArray();
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                {
                    Assert.AreEqual(expected[i][j], panel.JointMass[i][j], 1e-7, specification);
                    Assert.AreEqual(panel.JointMass[i][j], panel.JointMass[j][i], 1e-10, specification);
                    Assert.AreEqual(panel.JointMass[i][j], panel.JointMass[^(i + 1)][^(j + 1)], 1e-7, specification);
                }
            Assert.AreEqual("0.45", panel.Sources[panel.Highlight]);
            Assert.AreEqual(1, panel.ConditionalDestinationProbabilities.Sum(), 1e-12);
            Assert.AreEqual(JsonSerializer.Serialize(panel), JsonSerializer.Serialize(
                ArticleSignalDiagrams.GeneratePartyToParty(specification, false).Single().Panels.Single()));
            StringAssert.Contains(diagram.Description, "NOT conditioned on filing");
            StringAssert.Contains(diagram.Description, "Unconditional defendant signal probabilities");
            Assert.IsFalse(diagram.Latex.Contains("independent"));
            Assert.IsFalse(diagram.Latex.Contains("Conditional p"));
        }
    }

    [TestMethod]
    public void DirectBinaryOwnSignalCannotPredictOpponentDistanceFromMiddleButCanPredictItsSide()
    {
        var direct = ArticleSignalDiagrams.GeneratePartyToParty("DirectBinaryStateSignals", true).Single().Panels.Single();
        double unconditionalMiddle = direct.JointMass.Sum(row => row[4] + row[5]);
        for (int i = 0; i < direct.Sources.Length; i++)
        {
            // Production normal-CDF approximation introduces sub-nanoprobability asymmetry.
            Assert.AreEqual(unconditionalMiddle, (direct.JointMass[i][4] + direct.JointMass[i][5]) / direct.JointMass[i].Sum(), 1e-8);
            for (int j = 0; j < 5; j++)
                Assert.AreEqual(direct.JointMass.Sum(row => row[j] + row[^(j + 1)]),
                    (direct.JointMass[i][j] + direct.JointMass[i][^(j + 1)]) / direct.JointMass[i].Sum(), 1e-8);
        }
        Assert.AreEqual(.167255147, unconditionalMiddle, 1e-8);
        Assert.IsTrue(direct.JointMass[0].Skip(5).Sum() / direct.JointMass[0].Sum() <
            direct.JointMass[9].Skip(5).Sum() / direct.JointMass[9].Sum());
        foreach (string specification in new[] { "Baseline", "TruthConditionedLatentMerits" })
        {
            var shared = ArticleSignalDiagrams.GeneratePartyToParty(specification, true).Single().Panels.Single();
            double priorMiddle = shared.JointMass.Sum(row => row[4] + row[5]);
            double givenMiddle = shared.ConditionalDestinationProbabilities[4] + shared.ConditionalDestinationProbabilities[5];
            Assert.IsTrue(givenMiddle > priorMiddle, specification);
            Assert.IsTrue(givenMiddle > unconditionalMiddle, specification);
            Assert.AreEqual(specification == "Baseline" ? .278400065 : .267061988, givenMiddle, 1e-8);
        }
        CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget("all"), "party-to-party");
        CollectionAssert.AreEqual(new[] { "party-to-party" }, ArticleDiagramCommand.ExpandTarget("party-to-party"));
    }
}
