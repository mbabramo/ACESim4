using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Games.EFGFileGame;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>Replay original independent solves, never intervention transitions.</summary>
public static class ArticleEquilibriumPaths
{
    public sealed record SolveSelection(string Source, string OriginalLog, int Seed = 0);
    public sealed record PathRequest(string SourceRequest, string OutputDirectory, SolveSelection[] Equilibria, int MaxPivots = 0);
    public sealed record SetMetadata(int Index, int TreeIndex, string Key, int Number, byte Player, string Decision,
        int Signal, double SignalValue, int? ExitCommitment, int FirstAction, string[] Actions);
    public sealed record PathFrame(int Step, string Kind, ECTAPivotSnapshot Native,
        double ProjectionFlowResidual, int[] PriorCompletedInformationSets, ECTAIncentives Strategy);
    public sealed record PathResult(string Schema, string Id, string OptionSet, string Initialization,
        int Seed, bool Exact, int Steps, int Pivots, int OriginalPivots, double Seconds,
        Fingerprint[] Inputs, Fingerprint Frames, Fingerprint CoreAssembly, int ValidatedSourceRows,
        SetMetadata[] InformationSets, double InitialEpsilon, double FinalEpsilon,
        double[] FinalIndependentGains, double SavedEquilibriumEpsilon,
        double MaximumSavedPolicyDifference, double MaximumProjectionFlowResidual,
        double MaximumInitialPriorDifference, int MaxIntegralUtility, int RoundOffChanceDigits, string[] Interpretation);

    public static JsonSerializerOptions CompactJson => new(ArticleWorkedPathExtraction.JsonOptions) { WriteIndented = false };
    public static async Task<string[]> RunAsync(string requestFile, Action<string> progress = null)
    {
        requestFile = Path.GetFullPath(requestFile);
        string directory = Path.GetDirectoryName(requestFile);
        var request = JsonSerializer.Deserialize<PathRequest>(File.ReadAllText(requestFile), CompactJson)
            ?? throw new InvalidDataException("Empty path request.");
        if (request.Equilibria == null || request.Equilibria.Length == 0 ||
            request.Equilibria.Select(e => e.Source).Distinct().Count() != request.Equilibria.Length ||
            request.Equilibria.Any(e => e.Seed != 0) || request.MaxPivots < 0)
            throw new InvalidDataException("Original article solves require exact seed-zero uniform priors and unique sources.");
        string sourceFile = Path.GetFullPath(request.SourceRequest, directory);
        var sources = ReadRequest(sourceFile).Sources;
        string sourceDirectory = Path.GetDirectoryName(sourceFile);
        string output = Path.GetFullPath(request.OutputDirectory, directory);
        Directory.CreateDirectory(output);
        string completed=Path.Combine(output,"equilibrium-paths-manifest.json");
        if (File.Exists(completed))
        {
            using var manifest=JsonDocument.Parse(File.ReadAllText(completed));
            var root=manifest.RootElement;
            if(root.GetProperty("Request").GetProperty("Sha256").GetString()!=Hash(requestFile).Sha256)
                throw new IOException("Completed replay has a different request; choose a fresh output directory.");
            var cached=root.GetProperty("Results").Deserialize<Fingerprint[]>(JsonOptions);
            foreach(var f in cached)
            {
                if(Hash(f.Path).Sha256!=f.Sha256)throw new IOException("Changed completed trace metadata.");
                var metadata=JsonSerializer.Deserialize<PathResult>(File.ReadAllText(f.Path),JsonOptions);
                foreach(var input in metadata.Inputs.Append(metadata.Frames).Append(metadata.CoreAssembly))
                    if(Hash(input.Path).Sha256!=input.Sha256)throw new IOException("Changed completed replay input: "+input.Path);
            }
            progress?.Invoke($"Reused {cached.Length} completed, hash-verified replay(s).");
            return cached.Select(f=>f.Path).ToArray();
        }
        var results = new List<string>();
        foreach (var solve in request.Equilibria)
        {
            var selection = sources.Single(s => s.Id == solve.Source);
            if (!Regex.IsMatch(selection.Id, @"\A[a-z0-9-]+\z")) throw new InvalidDataException("Unsafe source ID.");
            var options = ArticleWorkedPathExtraction.CreateOptions(selection.OptionSetName); RequireProtocol(options);
            var game = (SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(options);
            string file = Path.GetFullPath(selection.EquilibriumFile, sourceDirectory);
            string actions = Path.GetFullPath(selection.ActionReportFile, sourceDirectory);
            string log = Path.GetFullPath(solve.OriginalLog, directory);
            foreach (string input in new[] { file, actions, log })
                if ((output + Path.DirectorySeparatorChar).StartsWith(Path.GetDirectoryName(input) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Output must be outside production-input directories.");
            string logText = File.ReadAllText(log);
            var pivotMatch = Regex.Matches(logText, @"Complete after (\d+) pivoting steps");
            if (pivotMatch.Count != 1 || !logText.Contains("Using exact arithmetic for initial prior") ||
                !logText.Contains("Prior 1 of 1") || !logText.Contains("Option set " + selection.OptionSetName))
                throw new InvalidDataException("Original log does not document the selected single-prior exact solve.");
            int originalPivots = int.Parse(pivotMatch[0].Groups[1].Value);
            var values = File.ReadLines(file).Skip(selection.EquilibriumNumber - 1).First()
                .Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray();
            var fallback = ArticleWorkedPathExtraction.LoadProfile(game, values);
            int rows = ArticleWorkedPathExtraction.ValidateActionReport(game, selection.EquilibriumNumber, actions);
            var saved = Capture(game, selection.Id, fallback);
            var reference = Describe(game, saved);
            double savedEpsilon = new byte[] { 0, 1 }.Max(p => Respond(game, saved, p, "saved-control").Gain);
            if (savedEpsilon > 1e-7) throw new InvalidDataException("Saved profile is not a verified equilibrium.");
            var nodes = game.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).ToArray();
            var savedValues = nodes.SelectMany(n => saved.Strategies[Key(n, game.GameDefinition)].Probabilities).ToArray();
            var uniform = nodes.SelectMany(n => Enumerable.Repeat(1.0 / n.NumPossibleActions, n.NumPossibleActions)).ToArray();
            var inputs = new[] { Hash(file), Hash(actions), Hash(log), Hash(requestFile), Hash(sourceFile) };
            string stem = Path.Combine(output, selection.Id);
            if (File.Exists(stem + ".json")) throw new IOException("A completed replay already exists: " + stem);
            progress?.Invoke($"Replay {selection.Id}: exact uniform prior, {rows} validated actions; expect {originalPivots} pivots.");
            var timer = Stopwatch.StartNew();
            int steps = 0, pivots = 0; double maxFlow = 0, initialDifference = 0;
            ECTAIncentives first = null, last = null; SetMetadata[] metadata = null;
            using (var writer = new StreamWriter(stem + ".jsonl", false))
            {
                ECTAStrategyDiagnostics<ExactValue> engine = null;
                game.TraceECTA<ExactValue>(null, tree =>
                {
                    engine = new(tree, game.TraceOutcomeUtilities());
                    int offset = 0;
                    metadata = engine.InformationSetIndices.Select((i, index) =>
                    {
                        var node = game.TraceInformationSets[i].InformationSetNode;
                        var info = reference.InformationSets.Single(s => s.Key == Key(node, game.GameDefinition));
                        var m = new SetMetadata(index, i, info.Key, info.Number, info.Player, info.Decision, info.Signal,
                            info.SignalValue, info.ExitCommitment, offset, info.Actions.Select(a => a.Label).ToArray());
                        offset += m.Actions.Length; return m;
                    }).ToArray();
                    first = engine.Evaluate(engine.PriorProbabilities);
                    initialDifference = engine.PriorProbabilities.Zip(uniform, (a, b) => Math.Abs(a - b)).Max();
                    if (initialDifference > 1e-14) throw new InvalidDataException("Original uniform initialization not reproduced.");
                    var savedCheck = engine.Evaluate(savedValues);
                    for (byte p = 0; p < 2; p++)
                    {
                        Near(reference.Utilities[p], savedCheck.Utilities[p], 1e-7, "Saved policy replay");
                        Near(Respond(game, saved, p, "saved-fast-control").BestResponseUtility,
                            savedCheck.BestResponseUtilities[p], 1e-7, "Saved unrestricted BR");
                    }
                    ArticleWorkedPathExtraction.LoadProfile(game, uniform);
                    var prior = Capture(game, "original-uniform-prior");
                    var priorReference = Describe(game, prior);
                    for (byte p = 0; p < 2; p++)
                    {
                        Near(priorReference.Utilities[p], first.Utilities[p], 1e-7, "Prior utility");
                        Near(Respond(game, prior, p, "prior-control").BestResponseUtility,
                            first.BestResponseUtilities[p], 1e-7, "Prior unrestricted BR");
                    }
                    writer.WriteLine(JsonSerializer.Serialize(new PathFrame(steps++, "initial-prior", null, 0, Array.Empty<int>(), first), CompactJson));
                }, (tree, pivot) =>
                {
                    var projected = engine.Project(pivot);
                    last = engine.Evaluate(projected.Probabilities);
                    maxFlow = Math.Max(maxFlow, projected.FlowResidual); pivots = pivot.Pivot;
                    writer.WriteLine(JsonSerializer.Serialize(new PathFrame(steps++, pivot.Final ? "final-pivot" : "pivot",
                        pivot, projected.FlowResidual, projected.PriorCompletedInformationSets, last), CompactJson));
                    if (pivots % 50 == 0 || pivot.Final)
                    {
                        writer.Flush();
                        progress?.Invoke($"  Pivot {pivots}/{originalPivots}: z0={pivot.Auxiliary:G4}, epsilon={last.Epsilon:G4}, flow residual={projected.FlowResidual:G3} ({timer.Elapsed.TotalSeconds:F1}s)");
                    }
                }, maxPivots: request.MaxPivots, seed: solve.Seed);
            }
            if (last == null || last.Epsilon > 1e-7) throw new InvalidDataException("Final profile is not a verified equilibrium.");
            if (pivots != originalPivots) throw new InvalidDataException($"Replay took {pivots} pivots; original took {originalPivots}.");
            double difference = last.Probabilities.Zip(savedValues, (a, b) => Math.Abs(a - b)).Max();
            if (difference > 1e-10) throw new InvalidDataException($"Replay did not recover the saved equilibrium: max probability difference {difference:G17}.");
            ArticleWorkedPathExtraction.LoadProfile(game, last.Probabilities);
            var finalProfile = Capture(game, "traced-final");
            var independent = new byte[] { 0, 1 }.Select(p => Respond(game, finalProfile, p, "final-control").Gain).ToArray();
            for (int p = 0; p < 2; p++) Near(last.UnilateralGains[p], independent[p], 1e-7, "Final independent BR");
            if (independent.Max() > 1e-7) throw new InvalidDataException("Final independent equilibrium check failed.");
            foreach (var input in inputs)
                if (Hash(input.Path).Sha256 != input.Sha256) throw new IOException("An input changed during tracing: " + input.Path);
            var result = new PathResult("2", selection.Id, selection.OptionSetName, "Original uniform prior (genprior(0))",
                solve.Seed, true, steps, pivots, originalPivots, timer.Elapsed.TotalSeconds, inputs,
                Hash(stem + ".jsonl"), Hash(typeof(ArticleEquilibriumPaths).Assembly.Location), rows, metadata,
                first.Epsilon, last.Epsilon, independent, savedEpsilon, difference, maxFlow, initialDifference,
                EvolutionSettings.MaxIntegralUtility, EvolutionSettings.RoundOffChanceDigits, Interpretation);
            await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(result, JsonOptions) + "\n");
            results.Add(stem + ".json");
            progress?.Invoke($"Verified original endpoint and {pivots} pivots; {steps} frames; max saved probability difference {difference:G4}.");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "equilibrium-paths-manifest.json"),
            JsonSerializer.Serialize(new { Schema = "2", Request = Hash(requestFile), Results = results.Select(Hash).ToArray() }, JsonOptions) + "\n");
        return results.ToArray();
    }

    public static readonly string[] Interpretation =
    {
        "Replay of the original independent exact ECTA/Lemke solve from the uniform covering-vector prior, with fixed game rules throughout. Not an intervention, behavioral learning, or best-response dynamics.",
        "Every actual pivot is exported, including degenerate pivots with unchanged probabilities. Step zero is the original uniform prior. No interpolated strategies are inserted. Original log pivot count and all saved action probabilities are verified at the endpoint.",
        "Raw Z/W and auxiliary z0 are retained. Displayed realization weights are x + z0 times the prior realization plan, locally normalized into behavioral probabilities. Root/flow residual is recorded; zero-weight information sets retain the uniform prior and are identified by TreeIndex.",
        "Epsilon is the larger player's full unrestricted best-response gain in rounded game utility units. NashConv is the sum of the two gains. Neither is geometric distance to the saved profile, and neither need decrease at each pivot.",
        "Action utility conditions on the acting player's information set with both parties' continuation strategies fixed at this step. Advantage is Q(action) minus the expected Q of the CURRENT mix, not a final-equilibrium payoff. LocalGap includes profitable in-support reweighting; OutsideSupportGap includes only actions with probability at most 1e-10.",
        "Counterfactual reach omits own earlier choices only. Zero opponent-and-chance reach makes action incentives undefined (null). Positive counterfactual reach can define incentives at an actually unreached own history; positive local gaps there need not contradict Nash equilibrium.",
        "Complete strategies include both private exit-commitment branches of offers. Uniform completions at unreached histories are bookkeeping, not evidence of equilibrium mixing.",
        "Raw LCP feasibility/complementarity residuals use normalized matrix units and are supplementary native diagnostics. Original saved profiles, action reports and logs are hash-checked and never overwritten."
    };
}
