using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Settings;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.GameTests
{
    [TestClass, DoNotParallelize]
    public class EnumeratedPureStrategiesTests
    {
        private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "ACESim-pure-tests", Guid.NewGuid().ToString("N"));

        [TestMethod]
        public void FiveByFiveCountsUniquenessAndDirections()
        {
            var catalog = new MonotonePureStrategyCatalog(5, 5);
            MonotonePureStrategyCatalog.ExpectedCount(5, 5).Should().Be(1302);
            catalog.Count(0).Should().Be(1302); catalog.Count(1).Should().Be(1302);
            (catalog.Count(0) * catalog.Count(1)).Should().Be(1695204);
            foreach (var strategies in new[] { catalog.Plaintiff, catalog.Defendant })
            {
                strategies.Select(s => (s.ParticipatingTypes, s.ContinuingTypes)).Distinct().Should().HaveCount(21);
                strategies.Select(s => s.Id).Distinct().Should().HaveCount(1302);
                strategies.Select(s => Key(s)).Distinct().Should().HaveCount(1302);
                foreach (var s in strategies)
                {
                    var part = Enumerable.Range(0, 5).Select(i => s.Participates(i) ? 1 : 0).ToArray();
                    var cont = Enumerable.Range(0, 5).Where(s.Participates).Select(i => s.Continues(i) ? 1 : 0).ToArray();
                    var offer = Enumerable.Range(0, 5).Where(s.Participates).Select(i => s.Offers[i]).ToArray();
                    Ordered(part, s.Plaintiff).Should().BeTrue(); Ordered(cont, s.Plaintiff).Should().BeTrue(); Ordered(offer, true).Should().BeTrue();
                    Enumerable.Range(0, 5).Where(i => !s.Participates(i)).All(i => s.Offers[i] == 0 && !s.Continues(i)).Should().BeTrue();
                }
            }
        }

        private static bool Ordered(int[] values, bool increasing) => values.Zip(values.Skip(1), (a, b) => increasing ? a <= b : a >= b).All(x => x);
        private static string Key(MonotoneLitigationStrategy s) => string.Join(";", Enumerable.Range(0, s.SignalCount).Select(i => !s.Participates(i) ? "0" : $"{(s.Continues(i) ? 2 : 1)}:{s.Offers[i]}"));
        private static IEnumerable<int[]> RawStrategies(int n, int m)
        {
            int radix = 1 + 2 * m, total = (int)Math.Pow(radix, n);
            for (int v = 0; v < total; v++)
            {
                int remainder = v; var result = new int[n];
                for (int i = 0; i < n; i++) { result[i] = remainder % radix; remainder /= radix; }
                yield return result;
            }
        }
        private static string RawKey(int[] actions, int m) => string.Join(";", actions.Select(v => v == 0 ? "0" : $"{(v > m ? 2 : 1)}:{(v - 1) % m + 1}"));

        [TestMethod]
        public void SmallCatalogsMatchIndependentBruteForce()
        {
            for (int n = 1; n <= 3; n++) for (int m = 1; m <= 3; m++) foreach (bool plaintiff in new[] { true, false })
            {
                var brute = RawStrategies(n, m).Where(s =>
                    Ordered(s.Select(a => a > 0 ? 1 : 0).ToArray(), plaintiff) &&
                    Ordered(s.Where(a => a > 0).Select(a => a > m ? 1 : 0).ToArray(), plaintiff) &&
                    Ordered(s.Where(a => a > 0).Select(a => (a - 1) % m + 1).ToArray(), true)).Select(s => RawKey(s, m)).ToHashSet();
                var actual = MonotonePureStrategyCatalog.Enumerate(n, m, plaintiff).Select(Key).ToHashSet();
                actual.Should().BeEquivalentTo(brute);
                actual.Count.Should().Be((int)MonotonePureStrategyCatalog.ExpectedCount(n, m));
            }
        }

        [TestMethod]
        public void OffersCrossExitBoundaryAndCompletionsArePure()
        {
            var catalog = new MonotonePureStrategyCatalog(3, 3);
            foreach (var s in catalog.Plaintiff.Concat(catalog.Defendant))
            {
                for (int i = 0; i < 3; i++)
                {
                    s.Action(s.Plaintiff ? LitigGameDecisions.POffer : LitigGameDecisions.DOffer, i).Should().BeInRange((byte)1, (byte)3);
                    if (s.Participates(i) && !s.Continues(i))
                    {
                        s.Offers[i].Should().BeGreaterThan(0);
                        s.Action(s.Plaintiff ? LitigGameDecisions.PAbandon : LitigGameDecisions.DDefault, i).Should().Be(1);
                    }
                }
            }
            catalog.Plaintiff.Should().Contain(s => s.ParticipatingTypes == 3 && s.ContinuingTypes == 1 && s.Offers.SequenceEqual(new[] { 1, 2, 3 }));
            catalog.Plaintiff.Should().NotContain(s => s.Offers.SequenceEqual(new[] { 2, 2, 1 }));
            catalog.Defendant.Should().Contain(s => s.ParticipatingTypes == 3 && s.ContinuingTypes == 1 && s.Offers.SequenceEqual(new[] { 1, 2, 3 }));
        }

        [TestMethod]
        public void HandCheckableMatrixKeepsBothGainsAndTiedBestResponses()
        {
            var matrix = new PurePayoffMatrix(3, 2, new double[] { 3, 0, 3, 2, 1, 2 }, new double[] { 1, 4, 2, 2, 7, 0 });
            var scores = matrix.Score(0);
            scores.PlaintiffBestResponses[0].Should().Equal(0, 1);
            scores.PlaintiffBestResponses[1].Should().Equal(1, 2);
            scores.DefendantBestResponses[1].Should().Equal(0, 1);
            scores.At(0, 0).PlaintiffGain.Should().Be(0);
            scores.At(0, 0).DefendantGain.Should().Be(3);
            scores.At(0, 0).Epsilon.Should().Be(3);
            scores.At(2, 1).Epsilon.Should().Be(7);
            scores.At(1, 0).Epsilon.Should().Be(0);
            scores.At(1, 1).Epsilon.Should().Be(0);
            new Action(() => matrix.Score(double.NaN)).Should().Throw<ArgumentException>();
            new Action(() => new PurePayoffMatrix(1, 1, new[] { double.NaN }, new[] { 0.0 }).Score()).Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void ExperimentalOptionsPreserveArticleSpecificationAndProductionValidation()
        {
            foreach (bool british in new[] { false, true })
            {
                var o = LitigGameEnumeratedPureLauncher.CreateOptions(5, 5, british);
                o.NumOffers.Should().Be(5); o.NumLiabilitySignals.Should().Be(5);
                o.NumLiabilityStrengthPoints.Should().Be(2); o.NumCourtLiabilitySignals.Should().Be(2);
                o.NumPotentialBargainingRounds.Should().Be(1); o.PredeterminedAbandonAndDefaults.Should().BeTrue();
                o.PLiabilityNoiseStdev.Should().Be(0.2); o.DLiabilityNoiseStdev.Should().Be(0.2); o.CourtLiabilityNoiseStdev.Should().Be(0.2);
                o.PFilingCost.Should().Be(0.15); o.DAnswerCost.Should().Be(0.15); o.PTrialCosts.Should().Be(0.15); o.DTrialCosts.Should().Be(0.15);
                o.LoserPaysMultiple.Should().Be(british ? 1 : 0);
                ((LitigGameUniformQualityDisputeGenerator)o.LitigGameDisputeGenerator).QuadratureOrder.Should().Be(64);
                LitigGameEnumeratedPureLauncher.Configuration(o).Length.Should().BeLessThan(100_000);
            }
            var production = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            production.ValidateProductionMatrix().OptionSetCount.Should().Be(134);
            production.GetOptionsSets().Cast<LitigGameOptions>().Should().OnlyContain(o => o.NumLiabilitySignals == 10 && (o.NumOffers == 10 || o.NumOffers == 15));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CachedPayoffsEqualOrdinaryTreeAndUnrestrictedWholeStrategyBruteForce(bool british)
        {
            var options = LitigGameEnumeratedPureLauncher.CreateOptions(2, 2, british);
            var solver = await LitigGameEnumeratedPureLauncher.InitializeAsync(options, NewDirectory(), "test-source", 2);
            try
            {
                await solver.RunAlgorithm(options.Name);
                foreach (var score in solver.Scores.Profiles())
                {
                    var profile = solver.Profile(score.Row, score.Column);
                    profile.Should().OnlyContain(x => x == 0 || x == 1);
                    solver.SetInformationSetsToEquilibrium(profile);
                    solver.InformationSets.Should().OnlyContain(n => n.GetCurrentProbabilitiesAsArray().Sum() == 1);
                    double[] ordinary = solver.GetAverageUtilities(false);
                    int index = score.Row * solver.Matrix.Columns + score.Column;
                    ordinary[0].Should().BeApproximately(solver.Matrix.Plaintiff[index], 1E-11);
                    ordinary[1].Should().BeApproximately(solver.Matrix.Defendant[index], 1E-11);
                }
                var ordered = solver.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).ToArray();
                foreach (byte player in new byte[] { 0, 1 }) for (int opponent = 0; opponent < solver.Space.Count(1 - player); opponent++)
                {
                    var br = solver.VerifyOpponent(player, opponent);
                    double best = double.NegativeInfinity;
                    foreach (var raw in RawStrategies(2, 2))
                    {
                        var vector = solver.Profile(player == 0 ? 0 : opponent, player == 0 ? opponent : 0);
                        int offset = 0;
                        foreach (var node in ordered)
                        {
                            if (node.PlayerIndex == player)
                            {
                                var signalCode = player == 0 ? LitigGameDecisions.PLiabilitySignal : LitigGameDecisions.DLiabilitySignal;
                                int signal = node.LabeledInformationSet.Single(x => solver.GameDefinition.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode == (byte)signalCode).information - 1;
                                int choice = raw[signal];
                                int selected = (LitigGameDecisions)node.DecisionByteCode switch
                                {
                                    LitigGameDecisions.PFile or LitigGameDecisions.DAnswer => choice == 0 ? 2 : 1,
                                    LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault => choice > 0 && choice <= 2 ? 1 : 2,
                                    _ => choice == 0 ? 1 : (choice - 1) % 2 + 1
                                };
                                for (int a = 1; a <= node.NumPossibleActions; a++) vector[offset + a - 1] = a == selected ? 1 : 0;
                            }
                            offset += node.NumPossibleActions;
                        }
                        solver.SetInformationSetsToEquilibrium(vector);
                        best = Math.Max(best, solver.GetAverageUtilities(false)[player]);
                    }
                    br.Utility.Should().BeApproximately(best, 1E-10, "BR must jointly optimize participation, exit, and offers");
                    double restricted = player == 0 ? solver.Scores.PlaintiffBestUtilities[opponent] : solver.Scores.DefendantBestUtilities[opponent];
                    br.Utility.Should().BeGreaterThanOrEqualTo(restricted - 1E-10);
                    solver.VerifyOpponent(player, opponent).Should().BeEquivalentTo(br);
                }
                var metric = EnumeratedPureReport.BuildMetricCache(solver, 2, 2, british);
                var profiles = EnumeratedPureReport.Aggregate(solver.Scores.Profiles().ToArray(), solver.Terminals, metric, 6);
                foreach (var p in profiles)
                {
                    double[] v = p.Outcomes;
                    (1 - v[1] + v[2] + v[3] + v[4] + v[5]).Should().BeApproximately(1, 1E-10);
                    v[1].Should().BeLessThanOrEqualTo(v[0] + 1E-12);
                    v[6].Should().BeApproximately(-v[7] - v[8], 1E-10);
                    v[6].Should().BeApproximately(v[12] + v[13], 1E-10);
                    v[9].Should().BeGreaterThanOrEqualTo(-1E-12); v[10].Should().BeGreaterThanOrEqualTo(-1E-12);
                }
                // No filing: defendant policies and all continuations are irrelevant to reached play.
                var noFiling = profiles.Where(p => p.Score.Row == 0).ToArray();
                EnumeratedPureReport.Cluster(noFiling, p => p.StrategyFeatures, 0).Clusters.Should().HaveCount(1);
            }
            finally { solver.Store?.Dispose(); }
        }

        [TestMethod]
        public async Task WorkerReproducibilityResumeAndCorruptionRejection()
        {
            var options = LitigGameEnumeratedPureLauncher.CreateOptions(2, 2, false);
            string directory = NewDirectory();
            var first = await LitigGameEnumeratedPureLauncher.InitializeAsync(options, directory, "test-source", 1);
            await first.RunAlgorithm(options.Name);
            var original = first.Matrix.Plaintiff.ToArray();
            byte[] block = File.ReadAllBytes(Path.Combine(directory, "payoffs-000000.bin"));
            first.Store.Dispose();
            var settings = first.EvolutionSettings.EnumeratedPure;
            new Action(() => PureRunStore.Open(settings, first.Space, first.Terminals)).Should().Throw<IOException>();
            settings.Resume = true;
            using (var store = PureRunStore.Open(settings, first.Space, first.Terminals))
            {
                File.WriteAllText(Path.Combine(directory, "ignored.partial"), "interrupted write");
                store.BuildOrLoadMatrix(first.Terminals, 2).Plaintiff.Should().Equal(original);
                File.ReadAllBytes(Path.Combine(directory, "payoffs-000000.bin")).Should().Equal(block);
            }
            settings.OutputDirectory = NewDirectory(); settings.Resume = false;
            using (var second = PureRunStore.Open(settings, first.Space, first.Terminals))
            {
                second.BuildOrLoadMatrix(first.Terminals, 2).Plaintiff.Should().Equal(original);
                File.ReadAllBytes(Path.Combine(settings.OutputDirectory, "payoffs-000000.bin")).Should().Equal(block);
            }
            settings.OutputDirectory = directory; settings.Resume = true; settings.SourceStateJson = "different-source";
            new Action(() => PureRunStore.Open(settings, first.Space, first.Terminals)).Should().Throw<InvalidDataException>();
            block[block.Length / 2] ^= 1; File.WriteAllBytes(Path.Combine(directory, "payoffs-000000.bin"), block);
            using var reader = PureRunStore.Read(directory);
            new Action(() => reader.LoadMatrix()).Should().Throw<InvalidDataException>();
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task FiveSignalMatrixMatchesSeparatelyInitializedSequenceFormReference(bool british)
        {
            var options = LitigGameEnumeratedPureLauncher.CreateOptions(5, 5, british);
            var solver = await LitigGameEnumeratedPureLauncher.InitializeAsync(options, NewDirectory(), "test-source", 2);
            try
            {
                await solver.RunAlgorithm(options.Name);
                var referenceOptions = LitigGameEnumeratedPureLauncher.CreateOptions(5, 5, british);
                var settings = new EvolutionSettings { Algorithm = GameApproximationAlgorithm.SequenceForm, ParallelOptimization = false,
                    UseAcceleratedBestResponse = false, CreateEFGFile = false, CreateEquilibriaFile = false, GenerateManualReports = false, GenerateReportsByPlaying = false };
                var launcher = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
                var reference = (StrategiesDeveloperBase)await launcher.GetInitializedDevelper(referenceOptions, referenceOptions.Name, settings);
                foreach (var chance in reference.ChanceNodes) chance.GetProbabilitiesAsRationals(false, EvolutionSettings.MaxIntegralUtility);
                reference.ChanceNodes.SelectMany(c => c.GetActionProbabilities()).Should().Equal(solver.ChanceNodes.SelectMany(c => c.GetActionProbabilities()));
                reference.FinalUtilitiesNodes.SelectMany(f => f.Utilities).Should().Equal(solver.FinalUtilitiesNodes.SelectMany(f => f.Utilities));
                var candidates = solver.Scores.OrderedProfiles().Take(4).Concat(Enumerable.Range(0, 20).Select(i => solver.Scores.At(i * 61 % 1302, i * 113 % 1302))).ToArray();
                foreach (var p in candidates)
                {
                    reference.SetInformationSetsToEquilibrium(solver.Profile(p.Row, p.Column));
                    double[] utilities = reference.GetAverageUtilities(false); int index = p.Row * solver.Matrix.Columns + p.Column;
                    utilities[0].Should().BeApproximately(solver.Matrix.Plaintiff[index], 1E-10);
                    utilities[1].Should().BeApproximately(solver.Matrix.Defendant[index], 1E-10);
                    solver.VerifyOpponent(0, p.Column).Utility.Should().BeGreaterThanOrEqualTo(solver.Scores.PlaintiffBestUtilities[p.Column] - 1E-9);
                    solver.VerifyOpponent(1, p.Row).Utility.Should().BeGreaterThanOrEqualTo(solver.Scores.DefendantBestUtilities[p.Row] - 1E-9);
                }
            }
            finally { solver.Store?.Dispose(); }
        }

        [TestMethod]
        public async Task ReportsRegenerateWithoutChangingPayoffs()
        {
            var options = LitigGameEnumeratedPureLauncher.CreateOptions(2, 2, false);
            var solver = await LitigGameEnumeratedPureLauncher.InitializeAsync(options, NewDirectory(), "test-source");
            try
            {
                await solver.RunAlgorithm(options.Name);
                string first = NewDirectory(), second = NewDirectory();
                var parameters = new Dictionary<string, string> { ["verify"] = "0", ["tolerances"] = "0,0.025" };
                await EnumeratedPureReport.GenerateAsync(solver.Store, first, parameters, solver);
                string payoff = Path.Combine(solver.Store.DirectoryPath, "payoffs-000000.bin");
                byte[] before = File.ReadAllBytes(payoff); DateTime time = File.GetLastWriteTimeUtc(payoff);
                await EnumeratedPureReport.GenerateAsync(solver.Store, second, parameters);
                File.ReadAllBytes(payoff).Should().Equal(before); File.GetLastWriteTimeUtc(payoff).Should().Be(time);
                foreach (string name in new[] { "tolerance-0.csv", "tolerance-0.025.csv", "tolerance-0.025.svg", "tolerance-0.025-clusters.json" })
                    File.ReadAllBytes(Path.Combine(first, name)).Should().Equal(File.ReadAllBytes(Path.Combine(second, name)));
                parameters["tolerances"] = "0.05";
                await EnumeratedPureReport.GenerateAsync(solver.Store, NewDirectory(), parameters);
                File.ReadAllBytes(payoff).Should().Equal(before);
            }
            finally { solver.Store?.Dispose(); }
        }

        [TestMethod]
        public void ClusteringIsDeterministicAndReceivesOnlyFilteredProfiles()
        {
            var profiles = new[]
            {
                new PureRetainedProfile(new(0, 0, 0, 0), new[] { 0.0 }, new[] { 0.0 }),
                new PureRetainedProfile(new(1, 0, 0.01, 0.02), new[] { 0.01 }, new[] { 1.0 }),
                new PureRetainedProfile(new(2, 0, 0.2, 0.3), new[] { 0.0 }, new[] { 0.0 })
            };
            var filtered = profiles.Where(p => p.Score.Epsilon <= 0.02).ToArray();
            var outcome = EnumeratedPureReport.Cluster(filtered, p => p.Outcomes, 0.025);
            outcome.Clusters.Should().ContainSingle().Which.Count.Should().Be(2);
            EnumeratedPureReport.Cluster(filtered, p => p.StrategyFeatures, 0.025).Clusters.Should().HaveCount(2);
            EnumeratedPureReport.Cluster(filtered, p => p.Outcomes, 0.025).Should().BeEquivalentTo(outcome);
            outcome.Clusters[0].RepresentativeRow.Should().Be(0);
            EnumeratedPureReport.Cluster(Array.Empty<PureRetainedProfile>(), p => p.Outcomes, 0.025).Clusters.Should().BeEmpty();
            EnumeratedPureReport.Plot(Array.Empty<PureRetainedProfile>(), new(Array.Empty<PureCluster>(), Array.Empty<int>()), 0).Should().Contain("<svg");
        }

        [TestMethod]
        public void ExitCommitmentsOnSettledPathsDoNotManufactureStrategyClusters()
        {
            double[] outcomes = new double[EnumeratedPureReport.OutcomeNames.Length]; outcomes[2] = 1;
            var terms = new List<PureTerminal>
            {
                new(new[] { 0 }, new[] { 0 }, 1, 1, 1, Array.Empty<PurePathStep>()),
                new(new[] { 1 }, new[] { 0 }, 1, 1, 1, Array.Empty<PurePathStep>())
            };
            var metrics = new PureMetricCache("test", EnumeratedPureReport.OutcomeNames, 12, new[]
            {
                new PureTerminalMetrics(outcomes, new[] { 0, 2, 4 }),
                new PureTerminalMetrics(outcomes, new[] { 0, 3, 4 })
            });
            var scores = new[] { new PureProfileScore(0, 0, 0, 0), new PureProfileScore(1, 0, 0, 0) };
            var profiles = EnumeratedPureReport.Aggregate(scores, terms, metrics, 6);
            profiles[0].StrategyFeatures.Should().Equal(profiles[1].StrategyFeatures);
            EnumeratedPureReport.Cluster(profiles, p => p.StrategyFeatures, 0).Clusters.Should().ContainSingle();
            outcomes[2] = 0;
            profiles = EnumeratedPureReport.Aggregate(scores, terms, metrics, 6);
            EnumeratedPureReport.Cluster(profiles, p => p.StrategyFeatures, 0).Clusters.Should().HaveCount(2);
        }
    }
}
