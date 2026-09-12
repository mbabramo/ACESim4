using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ACESimBase.Games.LitigGame.ManualReports;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleWorkedPathExtraction;

namespace ACESimTest.GameTests
{
    [TestClass]
    [DoNotParallelize]
    public class ArticleWorkedPathExtractionTests
    {
        private static StrategiesDeveloperBase developer;
        private static LitigGameOptions options;
        private static CalculateUtilitiesAtEachInformationSet calculator;
        private static RecordGamePathsProcessor recorder;
        private static HashSet<int> fallbacks;
        private static double[] profile;

        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            options = CreateOptions("Specification-Baseline__Cost-1__Fee-American");
            developer = await InitializeAsync(options);
            // Deliberately synthetic profile: tests never solve or require external files,
            // and do not assert that these arbitrary choices constitute an equilibrium.
            var values = new List<double>();
            foreach (var node in developer.InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber))
            {
                var decision = (LitigGameDecisions)node.DecisionByteCode;
                bool plaintiffFour = node.PlayerIndex == 0 && node.InformationSetContentsString.StartsWith("4");
                int selected = decision is LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault ? 2 :
                    decision == LitigGameDecisions.POffer && plaintiffFour ? 8 : 1;
                bool mixed = decision == LitigGameDecisions.PFile && node.InformationSetContentsString == "3";
                bool unspecified = decision == LitigGameDecisions.POffer && node.InformationSetContentsString == "4,1,1,1";
                for (int a = 1; a <= node.NumPossibleActions; a++)
                    values.Add(unspecified ? 0 : mixed ? (a == 1 ? 0.878 : 0.122) : a == selected ? 1 : 0);
            }
            profile = values.ToArray();
            fallbacks = LoadProfile(developer, profile);
            calculator = new CalculateUtilitiesAtEachInformationSet();
            developer.TreeWalk_Tree(calculator);
            recorder = new RecordGamePathsProcessor();
            developer.TreeWalk_Tree(recorder);
        }

        private static Choice[] Choices(byte p = 4, byte d = 3, byte demand = 8, byte exit = 2) => new[]
        {
            new Choice(LitigGameDecisions.PLiabilitySignal, p),
            new Choice(LitigGameDecisions.DLiabilitySignal, d),
            new Choice(LitigGameDecisions.PFile, 1),
            new Choice(LitigGameDecisions.DAnswer, 1),
            new Choice(LitigGameDecisions.PAbandon, exit),
            new Choice(LitigGameDecisions.DDefault, 2),
            new Choice(LitigGameDecisions.POffer, demand),
            new Choice(LitigGameDecisions.DOffer, 1),
        };

        private static PathData Extract(Choice[] choices) => ExtractPath(developer, options, calculator,
            fallbacks, SelectPath(recorder.Paths, choices), "test", "test");

        [TestMethod]
        public void UtilitiesAndMixingBelongToInformationSetsNotSelectedOpponentSignals()
        {
            var p3d3 = Extract(Choices(p: 3, demand: 1));
            var p3d4 = Extract(Choices(p: 3, d: 4, demand: 1));
            var first = p3d3.Steps.Single(x => x.Decision == LitigGameDecisions.PFile);
            var second = p3d4.Steps.Single(x => x.Decision == LitigGameDecisions.PFile);
            first.InformationSetNumber.Should().Be(second.InformationSetNumber);
            first.InformationSetContents.Should().Be("3");
            first.Actions[0].Probability.Should().Be(0.878);
            first.Actions.Should().BeEquivalentTo(second.Actions);
            first.HistoryReachProbability.Should().NotBe(second.HistoryReachProbability);
            first.InformationSetReachProbability.Should().Be(second.InformationSetReachProbability);
            first.InformationSetReachProbability.Should().BeGreaterThan(first.HistoryReachProbability);
        }

        [TestMethod]
        public void DeviationAtReachedInformationSetKeepsUtilityAndDoesNotRevealOfferToDefendant()
        {
            var trial = Extract(Choices());
            var deviation = Extract(Choices(demand: 1));
            deviation.EquilibriumProbability.Should().Be(0);
            var pOffer = deviation.Steps.Single(x => x.Decision == LitigGameDecisions.POffer);
            pOffer.OffPathInformationSet.Should().BeFalse();
            pOffer.Actions[0].ConditionalActingPlayerUtility.Should().NotBeNull();
            var dOffer = deviation.Steps.Single(x => x.Decision == LitigGameDecisions.DOffer);
            dOffer.HistoryReachProbability.Should().Be(0);
            dOffer.OffPathInformationSet.Should().BeFalse();
            dOffer.InformationSetNumber.Should().Be(trial.Steps.Single(x => x.Decision == LitigGameDecisions.DOffer).InformationSetNumber);
        }

        [TestMethod]
        public void OffPathUtilitiesAreNullAndUnspecifiedStrategyIsFlagged()
        {
            var path = Extract(Choices(exit: 1));
            var offer = path.Steps.Single(x => x.Decision == LitigGameDecisions.POffer);
            offer.OffPathInformationSet.Should().BeTrue();
            offer.UniformFallbackForUnspecifiedStrategy.Should().BeTrue();
            offer.ConditionalActingPlayerUtility.Should().BeNull();
            offer.Actions.Should().OnlyContain(x => x.ConditionalActingPlayerUtility == null && x.UtilityLossFromBest == null);
            offer.Actions.Should().OnlyContain(x => Math.Abs(x.Probability - 0.1) < 1E-12);
        }

        [TestMethod]
        public void ReplayExpandsCourtLotteryAndPreservesMonetaryAccounting()
        {
            var path = Extract(Choices());
            path.Outcomes.Should().HaveCount(2);
            path.Outcomes.Sum(x => x.ConditionalProbability).Should().BeApproximately(1, 1E-12);
            var lose = path.Outcomes.Single(x => x.CourtSignal == 1);
            var win = path.Outcomes.Single(x => x.CourtSignal == 2);
            lose.TransferToPlaintiff.Should().Be(0);
            lose.PlaintiffNetMonetaryPayoff.Should().BeApproximately(-0.3, 1E-12);
            lose.DefendantNetMonetaryPayoff.Should().BeApproximately(-0.3, 1E-12);
            win.TransferToPlaintiff.Should().Be(1);
            win.PlaintiffNetMonetaryPayoff.Should().BeApproximately(0.7, 1E-12);
            win.DefendantNetMonetaryPayoff.Should().BeApproximately(-1.3, 1E-12);
            foreach (var outcome in path.Outcomes)
            {
                outcome.PlaintiffNetLitigationExpense.Should().BeApproximately(0.3, 1E-12);
                outcome.DefendantNetLitigationExpense.Should().BeApproximately(0.3, 1E-12);
                outcome.PlaintiffFinalWealth.Should().BeApproximately(10 + outcome.PlaintiffNetMonetaryPayoff, 1E-12);
                outcome.EquilibriumHistoryProbability.Should().BeApproximately(path.EquilibriumProbability * outcome.ConditionalProbability, 1E-12);
            }
            recorder.Paths.Sum(x => x.Probability).Should().BeApproximately(1, 1E-10);
        }

        [TestMethod]
        public void InvalidOrIncompletePathsAndMalformedProfilesAreRejectedWithoutChangingProfile()
        {
            Action incomplete = () => SelectPath(recorder.Paths, Choices().Take(4).ToArray());
            incomplete.Should().Throw<InvalidDataException>();
            Action impossible = () => SelectPath(recorder.Paths, Choices(p: 11));
            impossible.Should().Throw<InvalidDataException>();
            var before = developer.GetEquilibriumFromInformationSets();
            Action length = () => LoadProfile(developer, profile.Take(10).ToArray());
            length.Should().Throw<InvalidDataException>();
            var invalid = profile.ToArray(); invalid[0] = double.NaN;
            Action nonfinite = () => LoadProfile(developer, invalid);
            nonfinite.Should().Throw<InvalidDataException>();
            developer.GetEquilibriumFromInformationSets().Should().Equal(before);
        }

        [TestMethod]
        public void SourceReportMustMatchSemanticInformationSetsAndNumericalResults()
        {
            string file = Path.Combine(Path.GetTempPath(), "acesim-path-report-test-" + Guid.NewGuid() + ".csv");
            try
            {
                string csv = InformationSetActionReport.BuildCsv(developer, 1);
                File.WriteAllText(file, csv);
                ValidateActionReport(developer, 1, file).Should().Be(480);
                File.WriteAllText(file, csv.Replace("P Liability Signal: 4", "P Liability Signal: 5"));
                Action mismatched = () => ValidateActionReport(developer, 1, file);
                mismatched.Should().Throw<InvalidDataException>();
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void BespokeBindingsUseExtractedNumbersAndRejectAFalseInformationSetConnection()
        {
            PathData Named(string name, Choice[] choices) => Extract(choices) with { Name = name };
            var paths = new[]
            {
                Named("trial", Choices()),
                Named("settlement", Choices(p: 3, demand: 1)),
                Named("adjacent-defendant-signal", Choices(d: 4)),
                Named("lower-demand-deviation", Choices(demand: 1)),
                Named("higher-offer-deviation", Choices().Take(7).Append(new Choice(LitigGameDecisions.DOffer, 8)).ToArray()),
                Named("no-filing", Choices(p: 3).Take(2).Append(new Choice(LitigGameDecisions.PFile, 2)).ToArray()),
                Named("no-answer-deviation", Choices().Take(3).Append(new Choice(LitigGameDecisions.DAnswer, 2)).ToArray()),
            };
            var data = new Extraction("1", options.Name, 1, null, null, 480, 100000, 6,
                new(), new[] { 10.0, 10.0 }, Array.Empty<string>(), paths);
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string latex = ArticleWorkedPathLatexData.Build(data);
                latex.Should().Contain(@"\newcommand{\FileMixedProbability}{0.878}")
                    .And.Contain(@"\newcommand{\TrialLosePayoff}{-0.30,\,-0.30}");
                string diagram = WorkedPathDiagram.Generate(data);
                diagram.Should().Contain(@"\documentclass").And.Contain(latex)
                    .And.NotContain(@"\input{worked equilibrium path values.tex}");
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
            var wrongPath = paths.Single(x => x.Name == "lower-demand-deviation");
            var wrongSteps = wrongPath.Steps.Select(x => x.Decision == LitigGameDecisions.DOffer
                ? x with { InformationSetNumber = 99999 } : x).ToArray();
            var wrong = data with { Paths = paths.Select(x => x == wrongPath ? x with { Steps = wrongSteps } : x).ToArray() };
            Action invalid = () => ArticleWorkedPathLatexData.Build(wrong);
            invalid.Should().Throw<InvalidDataException>();
            var changedChoice = wrongPath.Steps.Select(x => x.Decision == LitigGameDecisions.POffer
                ? x with { SelectedAction = 2 } : x).ToArray();
            wrong = data with { Paths = paths.Select(x => x == wrongPath ? x with { Steps = changedChoice } : x).ToArray() };
            invalid = () => ArticleWorkedPathLatexData.Build(wrong);
            invalid.Should().Throw<InvalidDataException>();
        }
    }
}
