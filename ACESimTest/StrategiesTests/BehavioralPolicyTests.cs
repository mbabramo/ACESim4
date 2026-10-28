using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class BehavioralPolicyTests
{
    private const string Matrix = """
        EFG 2 R "Asymmetric coordination" { "Row" "Column" }
        p "" 1 1 "row" { "A" "B" } 0
        p "" 2 1 "column" { "A" "B" } 0
        t "" 1 "" { 3, 2 }
        t "" 2 "" { 0, 0 }
        p "" 2 1 "column" { "A" "B" } 0
        t "" 3 "" { 0, 0 }
        t "" 4 "" { 1, 4 }
        """;

    internal static async Task<PredictiveCFR> Create(string source = Matrix)
    {
        var game = new EFGFileGameDefinition();
        game.Setup(new EFGFileGameOptions(), source);
        var settings = new EvolutionSettings
        {
            Algorithm = GameApproximationAlgorithm.PredictiveCFR,
            ParallelOptimization = false,
            ReportEveryNIterations = null, BestResponseEveryMIterations = null, MiniReportEveryPIterations = null,
            PredictiveCFR = new() { Variant = PredictiveCFRVariant.PDCFRPlus }
        };
        var solver = (PredictiveCFR)new EFGFileGameLauncher().GetStrategiesDeveloper(Strategy.GetStarterStrategies(game), settings, game);
        await solver.Initialize();
        return solver;
    }

    private static BehavioralProfile Profile(StrategiesDeveloperBase solver, double rowA, double columnA) =>
        new("test", solver.GameDefinition.OptionSetName, solver.InformationSets.Select(n =>
            new InformationSetPolicy(PolicyAccess.Key(n, solver.GameDefinition), n.PlayerIndex, n.DecisionByteCode,
                PolicyAccess.ActionLabels(n, solver.GameDefinition),
                new[] { n.PlayerIndex == 0 ? rowA : columnA, 1 - (n.PlayerIndex == 0 ? rowA : columnA) })));

    [TestMethod]
    public async Task CrossPlayerEvaluationUsesSuppliedPoliciesAndLeavesAllTrainingStateUntouched()
    {
        var solver = await Create();
        for (int i = 0; i < 7; i++) solver.RunIteration();
        var before = solver.CaptureState();
        var first = Profile(solver, .2, .1);
        var second = Profile(solver, .6, .7);
        var crossed = BehavioralProfile.Combine("crossed", null, first.ForPlayer(0), second.ForPlayer(1));
        // AA has probability .14, BB has probability .24. The player's original pairing is irrelevant.
        var result = PolicyEvaluator.Evaluate(solver, crossed);
        Assert.AreEqual(.66, result.Utilities[0], 1e-14);
        Assert.AreEqual(1.24, result.Utilities[1], 1e-14);
        Assert.AreEqual(1, result.TerminalProbability, 1e-14);
        Assert.AreEqual(before.CompletedIterations, solver.CompletedIterations);
        for (int i = 0; i < solver.InformationSets.Count; i++)
            CollectionAssert.AreEqual(before.NodeInformation[i], solver.InformationSets[i].NodeInformation);
        Assert.ThrowsException<ArgumentException>(() => BehavioralProfile.Combine("bad", null, first.ForPlayer(0), second.ForPlayer(0)));
    }

    [TestMethod]
    public async Task EvaluationWeightsChanceHistoriesAtSharedInformationSet()
    {
        const string game = """
            EFG 2 R "Hidden chance" { "Row" }
            c "" 1 "chance" { "left" 1/4 "right" 3/4 } 0
            p "" 1 1 "row" { "A" "B" } 0
            t "" 1 "" { -2 }
            t "" 2 "" { 2 }
            p "" 1 1 "row" { "A" "B" } 0
            t "" 3 "" { 1 }
            t "" 4 "" { -1 }
            """;
        var solver = await Create(game);
        var result = PolicyEvaluator.Evaluate(solver, Profile(solver, .8, .5));
        Assert.AreEqual(.15, result.Utilities[0], 1e-14);
        Assert.AreEqual(1, result.TerminalProbability, 1e-14);
    }

    [TestMethod]
    public async Task MappingIsSemanticAndRejectsWholeInvalidLayoutBeforeWriting()
    {
        var solver = await Create();
        solver.RunIteration();
        var before = solver.CaptureState();
        var wanted = Profile(solver, .2, .7);
        var policies = wanted.InformationSets.Values.Reverse().ToArray();
        var last = policies[^1];
        policies[^1] = new(last.Key, last.Player, last.Decision, new[] { "wrong", "labels" }, last.Probabilities);
        Assert.ThrowsException<InvalidDataException>(() => PolicyAccess.ApplyCurrent(solver, new("bad", null, policies)));
        for (int i = 0; i < solver.InformationSets.Count; i++)
            CollectionAssert.AreEqual(before.NodeInformation[i], solver.InformationSets[i].NodeInformation);
        PolicyAccess.ApplyCurrent(solver, new("reordered", null, wanted.InformationSets.Values.Reverse()));
        for (int i = 0; i < solver.InformationSets.Count; i++)
        {
            var node = solver.InformationSets[i];
            var expected = wanted.InformationSets[PolicyAccess.Key(node, solver.GameDefinition)].Probabilities;
            for (int d = 0; d < InformationSetNode.totalDimensions; d++)
                for (int a = 0; a < node.NumPossibleActions; a++)
                    Assert.AreEqual(d == InformationSetNode.currentProbabilityDimension || d == InformationSetNode.currentProbabilityForOpponentDimension
                        ? expected[a] : before.NodeInformation[i][d, a], node.NodeInformation[d, a]);
        }
    }

    [TestMethod]
    public async Task SnapshotsOwnTheirDataAndAverageCaptureDoesNotChangeLearning()
    {
        var labels = new[] { "A", "B" };
        var values = new[] { .2, .8 };
        var policy = new InformationSetPolicy("0/1/", 0, 1, labels, values);
        labels[0] = "changed";
        values[0] = .9;
        Assert.AreEqual("A", policy.ActionLabels[0]);
        Assert.AreEqual(.2, policy.Probabilities[0]);
        Assert.ThrowsException<InvalidDataException>(() => new InformationSetPolicy("bad", 0, 1, labels, values));
        var solver = await Create();
        for (int i = 0; i < 5; i++) solver.RunIteration();
        var before = solver.CaptureState();
        var current = PolicyAccess.CaptureCurrent(solver, "current");
        var average = PolicyAccess.CaptureAverage(solver, "average");
        for (int i = 0; i < solver.InformationSets.Count; i++)
            CollectionAssert.AreEqual(before.NodeInformation[i], solver.InformationSets[i].NodeInformation);
        solver.RunIteration();
        foreach (var node in solver.InformationSets)
        {
            int index = Array.IndexOf(before.InformationSetKeys, $"{node.PlayerIndex}:{node.DecisionIndex}:{node.NumPossibleActions}:{string.Join(",", node.InformationSetContents ?? Array.Empty<byte>())}");
            for (int a = 0; a < node.NumPossibleActions; a++)
                Assert.AreEqual(before.NodeInformation[index][InformationSetNode.currentProbabilityDimension, a],
                    current.InformationSets[PolicyAccess.Key(node, solver.GameDefinition)].Probabilities[a]);
        }
        Assert.AreEqual(1, average.InformationSets.First().Value.Probabilities.Sum(), 1e-14);
    }

    [TestMethod]
    public async Task VersionOneArchiveRoundTripsPoliciesAndRejectsLateInvalidEntryAtomically()
    {
        var solver = await Create();
        for (int i = 0; i < 5; i++) solver.RunIteration();
        string path = Path.GetTempFileName();
        try
        {
            BenchmarkProfileArchive.Write(solver, path, new { iteration = 5 });
            var saved = BenchmarkProfileArchive.Read(solver, path);
            Assert.AreEqual(5, saved.Selection.GetProperty("iteration").GetInt32());
            solver.RunIteration();
            BenchmarkProfileArchive.LoadForReplay(solver, path);
            Assert.AreEqual(6, solver.CompletedIterations); // replay is not resume
            foreach (var node in solver.InformationSets)
            {
                var key = PolicyAccess.Key(node, solver.GameDefinition);
                CollectionAssert.AreEqual(saved.Current.InformationSets[key].Probabilities.ToArray(), node.GetCurrentProbabilitiesAsArray());
                CollectionAssert.AreEqual(saved.Average.InformationSets[key].Probabilities.ToArray(), node.GetAverageStrategiesAsArray());
            }
            JsonNode document;
            using (var input = File.OpenRead(path))
            using (var zip = new GZipStream(input, CompressionMode.Decompress)) document = JsonNode.Parse(zip);
            // The first entry is valid and different; a bad final entry must prevent all writes.
            document["informationSets"][0]["current"] = new JsonArray(0.0, 1.0);
            document["informationSets"][solver.InformationSets.Count - 1]["identity"] = "wrong";
            using (var output = File.Create(path))
            using (var zip = new GZipStream(output, CompressionLevel.Optimal)) JsonSerializer.Serialize(zip, document);
            var before = solver.CaptureState();
            Assert.ThrowsException<InvalidDataException>(() => BenchmarkProfileArchive.LoadForReplay(solver, path));
            for (int i = 0; i < solver.InformationSets.Count; i++)
                CollectionAssert.AreEqual(before.NodeInformation[i], solver.InformationSets[i].NodeInformation);
        }
        finally { File.Delete(path); }
    }
}
