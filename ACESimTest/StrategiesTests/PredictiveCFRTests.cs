using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests
{
    [TestClass]
    public class PredictiveCFRTests
    {
        private static InformationSetNode Node() => new InformationSetNode(
            new Decision { NumPossibleActions = 2, PlayerIndex = 0 }, 0, new EvolutionSettings(), 0);

        private static void Update(InformationSetNode node, int iteration, PredictiveCFRSettings settings,
            double regret0, double regret1, double average0 = .5, double average1 = .5)
        {
            node.NodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, 0] = regret0;
            node.NodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, 1] = regret1;
            node.NodeInformation[InformationSetNode.lastCumulativeStrategyIncrementsDimension, 0] = average0;
            node.NodeInformation[InformationSetNode.lastCumulativeStrategyIncrementsDimension, 1] = average1;
            PredictiveCFR.UpdateInformationSet(node, iteration, settings);
        }

        [TestMethod]
        public void PredictionDoesNotOverwriteBaseRegretsAndClipsAfterAddingPrediction()
        {
            var node = Node();
            var settings = new PredictiveCFRSettings { Variant = PredictiveCFRVariant.PCFRPlus };
            Update(node, 1, settings, -1, 1);
            Assert.AreEqual(1, node.GetCumulativeRegret(2), 1E-14);
            Update(node, 2, settings, 1, -.5, 0, 1);
            Assert.AreEqual(1, node.GetCumulativeRegret(1), 1E-14);
            Assert.AreEqual(.5, node.GetCumulativeRegret(2), 1E-14);
            Assert.AreEqual(1, node.GetCurrentProbability(1, false), 1E-14);
            // Average the policies that produced the regrets: [.5,.5] + 4*[0,1].
            Assert.AreEqual(.1, node.GetAverageStrategiesAsArray()[0], 1E-14);
        }

        [TestMethod]
        public void DiscountedPredictionUsesPreviousAndNextIterationDiscounts()
        {
            var node = Node();
            var settings = new PredictiveCFRSettings { Variant = PredictiveCFRVariant.PDCFRPlus, Alpha = 1, Gamma = 2 };
            Update(node, 1, settings, -1, 1);
            Update(node, 2, settings, 1, -.25, 0, 1);
            Assert.AreEqual(1, node.GetCumulativeRegret(1), 1E-14);
            Assert.AreEqual(.25, node.GetCumulativeRegret(2), 1E-14);
            // Prediction: [2/3+1, (2/3)*.25-.25]+ -> pure action 1.
            Assert.AreEqual(1, node.GetCurrentProbability(1, false), 1E-14);
            Assert.AreEqual(.1, node.GetAverageStrategiesAsArray()[0], 1E-14);
            Assert.AreEqual(.5, settings.RegretDiscount(1), 1E-14);
        }

        [TestMethod]
        public void DiscountedRegretsAreNotDividedByOpponentReach()
        {
            var node = Node();
            node.NodeInformation[InformationSetNode.sumInversePiDimension, 0] = 100;
            node.NodeInformation[InformationSetNode.sumInversePiDimension, 1] = .001;
            var settings = new PredictiveCFRSettings { Variant = PredictiveCFRVariant.DCFRPlus, Alpha = 1 };
            Update(node, 1, settings, 2, 4);
            Assert.AreEqual(2, node.GetCumulativeRegret(1), 1E-14);
            Assert.AreEqual(4, node.GetCumulativeRegret(2), 1E-14);
            Assert.AreEqual(1.0 / 3, node.GetCurrentProbability(1, false), 1E-14);
        }

        [TestMethod]
        public void ZeroRegretAndZeroOwnReachRemainUniformWithoutInventedContributions()
        {
            var node = Node();
            Update(node, 1, new PredictiveCFRSettings(), 0, 0, 0, 0);
            CollectionAssert.AreEqual(new[] { .5, .5 }, node.GetCurrentProbabilitiesAsArray());
            CollectionAssert.AreEqual(new[] { 0.0, 0.0 }, node.GetCumulativeStrategiesAsArray());
            CollectionAssert.AreEqual(new[] { .5, .5 }, node.GetAverageStrategiesAsArray());
        }

        private const string MatrixGame = @"
EFG 2 R ""Unequal matching pennies"" { ""Row"" ""Column"" }
p """" 1 1 ""row"" { ""A"" ""B"" } 0
p """" 2 1 ""column"" { ""A"" ""B"" } 0
t """" 1 """" { 3, -3 }
t """" 2 """" { 0, 0 }
p """" 2 1 ""column"" { ""A"" ""B"" } 0
t """" 3 """" { 0, 0 }
t """" 4 """" { 1, -1 }
";

        private static async Task<PredictiveCFR> Create(string source = MatrixGame, PredictiveCFRVariant variant = PredictiveCFRVariant.PDCFRPlus,
            bool alternating = true)
        {
            var definition = new EFGFileGameDefinition();
            definition.Setup(new EFGFileGameOptions(), source);
            var settings = new EvolutionSettings
            {
                Algorithm = GameApproximationAlgorithm.PredictiveCFR,
                ParallelOptimization = false,
                UseAcceleratedBestResponse = true,
                ReportEveryNIterations = null,
                BestResponseEveryMIterations = null,
                MiniReportEveryPIterations = null,
                PredictiveCFR = new PredictiveCFRSettings { Variant = variant, AlternatingUpdates = alternating }
            };
            var launcher = new EFGFileGameLauncher();
            var solver = (PredictiveCFR)launcher.GetStrategiesDeveloper(Strategy.GetStarterStrategies(definition), settings, definition);
            await solver.Initialize();
            return solver;
        }

        [TestMethod]
        public async Task AlternatingPlayerSweepsUseUpdatedOpponentButAverageOldOwnPolicy()
        {
            var solver = await Create();
            solver.RunIteration();
            var row = solver.InformationSets.Single(x => x.PlayerIndex == 0);
            var column = solver.InformationSets.Single(x => x.PlayerIndex == 1);
            Assert.AreEqual(.5, row.GetCumulativeRegret(1), 1E-14);
            Assert.AreEqual(1.5, column.GetCumulativeRegret(2), 1E-14);
            CollectionAssert.AreEqual(new[] { .5, .5 }, column.GetAverageStrategiesAsArray());
            var simultaneous = await Create(alternating: false);
            simultaneous.RunIteration();
            Assert.AreEqual(.5, simultaneous.InformationSets.Single(x => x.PlayerIndex == 1).GetCumulativeRegret(2), 1E-14);
        }

        [TestMethod]
        public async Task RegretsAggregateAcrossChanceHistoriesBeforeClipping()
        {
            const string source = @"
EFG 2 R ""Hidden chance"" { ""Row"" }
c """" 1 ""chance"" { ""left"" 1/2 ""right"" 1/2 } 0
p """" 1 1 ""row"" { ""A"" ""B"" } 0
t """" 1 """" { -2 }
t """" 2 """" { 2 }
p """" 1 1 ""row"" { ""A"" ""B"" } 0
t """" 3 """" { 1 }
t """" 4 """" { -1 }
";
            var solver = await Create(source);
            solver.RunIteration();
            var row = solver.InformationSets.Single();
            Assert.AreEqual(0, row.GetCumulativeRegret(1), 1E-14);
            Assert.AreEqual(.5, row.GetCumulativeRegret(2), 1E-14);
            // Only one own-reach contribution despite two chance histories.
            CollectionAssert.AreEqual(new[] { .5, .5 }, row.GetCumulativeStrategiesAsArray());
        }

        [TestMethod]
        [DataRow(PredictiveCFRVariant.CFRPlus)]
        [DataRow(PredictiveCFRVariant.PCFRPlus)]
        [DataRow(PredictiveCFRVariant.DCFRPlus)]
        [DataRow(PredictiveCFRVariant.PDCFRPlus)]
        public async Task AverageStrategyConvergesToKnownMatrixEquilibrium(PredictiveCFRVariant variant)
        {
            var solver = await Create(variant: variant);
            for (int iteration = 0; iteration < 1000; iteration++) solver.RunIteration();
            double rowA = solver.InformationSets.Single(x => x.PlayerIndex == 0).GetAverageStrategiesAsArray()[0];
            double columnA = solver.InformationSets.Single(x => x.PlayerIndex == 1).GetAverageStrategiesAsArray()[0];
            double nashConv = Math.Max(3 * columnA, 1 - columnA) - Math.Min(3 * rowA, 1 - rowA);
            Assert.IsTrue(nashConv < .025, $"{variant}: NashConv {nashConv}, strategies {rowA}, {columnA}");
        }

        [TestMethod]
        public async Task CheckpointRoundTripAndDeepCopyContinueIdenticallyWithoutSharingArrays()
        {
            var original = await Create();
            for (int iteration = 0; iteration < 7; iteration++) original.RunIteration();
            var checkpoint = (PredictiveCFRState)BinarySerialization.GetObjectFromByteArray(BinarySerialization.GetByteArray(original.CaptureState()));
            // Initialization may discover information sets in a different order.
            Array.Reverse(checkpoint.InformationSetKeys);
            Array.Reverse(checkpoint.NodeInformation);
            var restored = await Create();
            restored.RestoreState(checkpoint);
            var copy = (PredictiveCFR)original.DeepCopy();
            copy.EvolutionSettings.PredictiveCFR.Alpha = 2.3;
            Assert.IsNull(original.EvolutionSettings.PredictiveCFR.Alpha);
            Assert.AreNotSame(original.InformationSets[0].NodeInformation, copy.InformationSets[0].NodeInformation);
            original.RunIteration();
            restored.RunIteration();
            copy.RunIteration();
            Assert.AreEqual(8, restored.CompletedIterations);
            Assert.AreEqual(8, restored.Status.IterationNum);
            for (int node = 0; node < original.InformationSets.Count; node++)
            {
                CollectionAssert.AreEqual(original.InformationSets[node].NodeInformation, restored.InformationSets[node].NodeInformation);
                CollectionAssert.AreEqual(original.InformationSets[node].NodeInformation, copy.InformationSets[node].NodeInformation);
            }
            restored.ResetLearningState();
            Assert.AreEqual(0, restored.CompletedIterations);
            Assert.AreEqual(0, restored.Status.IterationNum);
            Assert.AreEqual(8, original.CompletedIterations);
            Assert.AreEqual(0, restored.InformationSets[0].GetCumulativeRegret(1), 1E-14);
        }

        [TestMethod]
        public async Task RunAlgorithmReportsExactAverageWithoutRewindingLearningAndCanResume()
        {
            var reported = await Create();
            var direct = await Create();
            reported.EvolutionSettings.TotalIterations = 8;
            reported.EvolutionSettings.BestResponseEveryMIterations = 2;
            // These are legacy defaults. The new family reports exact probabilities
            // while retaining the caller's settings for other solver families.
            reported.EvolutionSettings.RoundOffThreshold = .4;
            reported.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
            int callbacks = 0;
            reported.IterationCompleted += (_, _) => callbacks++;
            await reported.RunAlgorithm("test");
            for (int iteration = 0; iteration < 8; iteration++) direct.RunIteration();
            Assert.AreEqual(8, callbacks);
            Assert.IsFalse(reported.BestBecomesResult);
            Assert.IsTrue(reported.EvolutionSettings.UseCurrentStrategyForBestResponse);
            // Best-response scratch dimensions may differ; all learning dimensions
            // must remain identical, including the policy used by the next sweep.
            void AssertLearningMatches()
            {
                for (int node = 0; node < direct.InformationSets.Count; node++)
                    foreach (int dimension in new[] { 0, 1, 2, 5, 6, 8, 9, 10 })
                        for (int action = 0; action < direct.InformationSets[node].NumPossibleActions; action++)
                            Assert.AreEqual(direct.InformationSets[node].NodeInformation[dimension, action],
                                reported.InformationSets[node].NodeInformation[dimension, action], 1E-14);
            }
            AssertLearningMatches();
            reported.RunIteration();
            direct.RunIteration();
            AssertLearningMatches();
        }
    }
}
