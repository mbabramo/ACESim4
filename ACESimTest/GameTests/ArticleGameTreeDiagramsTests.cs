using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.GameTree;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class ArticleGameTreeDiagramsTests
    {
        [TestMethod]
        public void IllustrationUsesCurrentBaselineWithOnlyGridAndEndingsChanged()
        {
            foreach (bool simplified in new[] { false, true })
            {
                var options = ArticleGameTreeDiagrams.CreateOptions(simplified);
                options.LitigGameDisputeGenerator.Should().BeOfType<LitigGameUniformQualityDisputeGenerator>();
                options.NumLiabilitySignals.Should().Be(2);
                options.NumCourtLiabilitySignals.Should().Be(2);
                options.NumOffers.Should().Be(2);
                options.CollapseChanceDecisions.Should().BeTrue();
                options.CollapseAlternativeEndings.Should().Be(simplified);
                options.SkipFileAndAnswerDecisions.Should().BeFalse();
                options.PredeterminedAbandonAndDefaults.Should().BeTrue();
                options.PFilingCost.Should().BeApproximately(0.15, 1E-12);
                options.DAnswerCost.Should().BeApproximately(0.15, 1E-12);
                options.PTrialCosts.Should().BeApproximately(0.15, 1E-12);
                options.DTrialCosts.Should().BeApproximately(0.15, 1E-12);
                options.PerPartyCostsLeadingUpToBargainingRound.Should().Be(0);
                options.PLiabilityNoiseStdev.Should().Be(0.2);
                options.CourtLiabilityNoiseStdev.Should().Be(0.2);
            }
        }

        [TestMethod]
        public async Task AllFiveViewsHaveUniqueConnectedNodesAndCorrectChanceStructure()
        {
            var diagrams = await ArticleGameTreeDiagrams.GenerateAsync();
            diagrams.Should().HaveCount(5);
            foreach (var diagram in diagrams)
            {
                var nodes = Regex.Matches(diagram.Latex, @"\(N(\d+)\) at")
                    .Select(x => x.Groups[1].Value).ToArray();
                nodes.Should().OnlyHaveUniqueItems();
                var references = Regex.Matches(diagram.Latex, @"\(N(\d+)\.(east|west)\)");
                references.Count.Should().Be(2 * (nodes.Length - 1));
                foreach (Match reference in references)
                    nodes.Should().Contain(reference.Groups[1].Value);
                diagram.Latex.Should().NotContain("Case Strength");
                diagram.Latex.Should().NotContain("Continuous-merits baseline")
                    .And.NotContain("no equilibrium").And.NotContain("text width=");
                diagram.Description.Should().Contain("Continuous-merits baseline")
                    .And.Contain("no equilibrium");
            }
            diagrams.Count(x => x.FileStem.Contains("beginning")).Should().Be(1);
            diagrams.Should().NotContain(x => x.FileStem == "game tree 2x2x2 beginning simplified");
            var fullEnd = diagrams.Single(x => x.FileStem == "game tree 2x2x2 end").Latex;
            var simpleEnd = diagrams.Single(x => x.FileStem == "game tree 2x2x2 end simplified").Latex;
            fullEnd.Should().Contain("Court finding:").And.Contain("Both Quit");
            simpleEnd.Should().NotContain("Court finding:").And.NotContain("Both Quit");
            fullEnd.Should().NotContain("D signal:").And.NotContain("D Answers:");
            simpleEnd.Should().NotContain("Pr.:"); // Only strategic branches remain.
        }

        [TestMethod]
        public async Task FilteringIsRepeatableAndNumbersAreCultureIndependent()
        {
            var developer = await ArticleGameTreeDiagrams.InitializeAsync(false);
            var tree = ArticleGameTreeDiagrams.Collect(developer);
            string before = tree.GenerateTikzDiagram(null, null);
            tree.GenerateTikzDiagram(ArticleGameTreeDiagrams.AfterDefendantSignal, null);
            tree.GenerateTikzDiagram(null, ArticleGameTreeDiagrams.StartOfBargaining);
            tree.GenerateTikzDiagram(null, null).Should().Be(before);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                // Default action labels use the game's formatter; diagram geometry and
                // probabilities must remain parseable irrespective of that formatter.
                var latex = tree.GenerateTikzDiagram(null, null, edgeLabel: edge => "action");
                latex.Should().Contain("Pr.: 0.500").And.NotContain("Pr.: 0,500");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [TestMethod]
        public async Task CollapsedEndingsPreserveExpectedUtilities()
        {
            var full = await ArticleGameTreeDiagrams.InitializeAsync(false);
            var simplified = await ArticleGameTreeDiagrams.InitializeAsync(true);
            var fullUtilities = full.TreeWalk_Tree(new CalculateUtilitiesAtEachInformationSet());
            var simplifiedUtilities = simplified.TreeWalk_Tree(new CalculateUtilitiesAtEachInformationSet());
            fullUtilities.Length.Should().Be(simplifiedUtilities.Length);
            for (int player = 0; player < fullUtilities.Length; player++)
                fullUtilities[player].Should().BeApproximately(simplifiedUtilities[player], 1E-10);
        }

        [TestMethod]
        public async Task EndogenousDisputesModeStillUsesItsOriginalChanceAndActionPrefix()
        {
            string latex = await EndogenousGameTreeDiagrams.GenerateAsync();
            latex.Should().Contain("Defendant Signal").And.Contain("Engage in Activity")
                .And.Contain("Precaution").And.Contain("Accident").And.Contain("Plaintiff Signal");
            latex.Should().NotContain("Continuous-merits");
            latex.Should().NotContain("Endogenous-disputes model").And.NotContain("text width=");
            EndogenousGameTreeDiagrams.Description.Should().Contain("Endogenous-disputes model");
            latex.Should().NotContain("Pr.: 0.000").And.NotContain("Pr.: 1.000");
        }
    }
}
