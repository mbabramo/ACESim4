using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Population;

public sealed record JpsroSettings(int MaximumIterations = 100, double MaximumNormalizedGain = 1e-5,
    JointSelectionSettings Selection = null, AffinitySettings Affinity = null);
public sealed record PopulationEntry(string Name, PlayerPolicy Policy);
public sealed record JpsroIteration(int Iteration, int PlaintiffPolicies, int DefendantPolicies,
    double[] ExpectedValues, double[] RawGains, double[] NormalizedGains, double RestrictedGain,
    double? SelectorDualityGap, int SelectorTermination, int SelectorIterations);
public sealed record JpsroResult(string SolutionConcept, string Selector, string Status,
    PopulationEntry[] Plaintiffs, PopulationEntry[] Defendants, double[] JointWeights,
    double[] ExpectedValues, double[] RawGains, double[] NormalizedGains, JpsroIteration[] History,
    AffinityResult[] References, JointSelectionResult Selection);

/// <summary>Separate player populations, incremental cross-pair evaluation, CCE selection,
/// and unrestricted response expansion. No initialization counts become selection weights.</summary>
public sealed class JpsroCoordinator
{
    private readonly PolicyGameTree game;
    private readonly List<PopulationEntry>[] populations = [new(), new()];
    private readonly Dictionary<(int, int), double[]> pairs = new();
    private readonly Dictionary<PlayerPolicy, double[]> signatures = new();
    private readonly PlayerPolicy[][] probes;
    private readonly Func<double[], double[]> economicFeatures;

    public JpsroCoordinator(PolicyGameTree game, IEnumerable<PopulationEntry> plaintiffs,
        IEnumerable<PopulationEntry> defendants, PlayerPolicy[][] fixedProbes = null, Func<double[], double[]> economicFeatures = null)
    {
        this.game = game ?? throw new ArgumentNullException(nameof(game));
        foreach (var entry in plaintiffs) Add(entry, 0);
        foreach (var entry in defendants) Add(entry, 1);
        if (populations.Any(p => p.Count == 0)) throw new ArgumentException("Seed each player population.");
        probes = fixedProbes?.Select(x => x.ToArray()).ToArray() ?? populations.Select(p => p.Select(x => x.Policy).ToArray()).ToArray();
        if (probes.Length != 2 || probes.Any(p => p.Length == 0) || probes.SelectMany((ps, p) => ps.Select(x => x.Player != p)).Any(x => x)) throw new ArgumentException("Invalid fixed probes.");
        this.economicFeatures = economicFeatures ?? (v => new[] { (v[0] - game.UtilityMinimum[0]) / game.UtilityScale[0], (v[1] - game.UtilityMinimum[1]) / game.UtilityScale[1] });
    }
    private bool Add(PopulationEntry entry, byte player)
    {
        if (entry.Policy.Player != player) throw new ArgumentException("Wrong population player.");
        // Exact behavioral duplicates only; economic similarity never removes an incentive.
        if (populations[player].Any(x => Equal(x.Policy, entry.Policy))) return false;
        populations[player].Add(entry); return true;
    }
    public static bool Equal(PlayerPolicy a, PlayerPolicy b) => a.Player == b.Player && a.InformationSets.Count == b.InformationSets.Count &&
        a.InformationSets.All(x => b.InformationSets.TryGetValue(x.Key, out var y) && x.Value.Decision == y.Decision && x.Value.ActionLabels.SequenceEqual(y.ActionLabels) && x.Value.Probabilities.SequenceEqual(y.Probabilities));
    private double[] Pair(int i, int j)
    {
        if (!pairs.TryGetValue((i, j), out var values)) pairs.Add((i, j), values = game.Evaluate(populations[0][i].Policy, populations[1][j].Policy));
        return values;
    }
    private double[] Signature(PlayerPolicy policy)
    {
        if (signatures.TryGetValue(policy, out var cached)) return cached;
        var rows = probes[1 - policy.Player].Select(probe => economicFeatures(policy.Player == 0 ? game.Evaluate(policy, probe) : game.Evaluate(probe, policy))).ToArray();
        if (rows.Any(x => x.Length != rows[0].Length || x.Any(v => !double.IsFinite(v)))) throw new ArgumentException("Invalid economic features.");
        // Equal feature weights and a fixed uniform probe distribution: squared distance
        // is the mean squared standardized feature difference across probes/features.
        double scale = Math.Sqrt(rows.Length * rows[0].Length);
        cached = rows.SelectMany(x => x.Select(v => v / scale)).ToArray(); signatures.Add(policy, cached); return cached;
    }

    public JpsroResult Run(JointSelectionObjective objective, JpsroSettings settings = null, Action<JpsroIteration> progress = null)
    {
        settings ??= new();
        if (settings.MaximumIterations < 1 || !double.IsFinite(settings.MaximumNormalizedGain) || settings.MaximumNormalizedGain <= 0) throw new ArgumentException("Invalid population budget/tolerance.");
        var selectionSettings = settings.Selection ?? new();
        if (selectionSettings.IncentiveTolerance >= settings.MaximumNormalizedGain) throw new ArgumentException("Restricted tolerance must be tighter than the full-game target.");
        var history = new List<JpsroIteration>();
        for (int iteration = 0; ; iteration++)
        {
            int rows = populations[0].Count, columns = populations[1].Count;
            var utilities = new[] { new double[rows * columns], new double[rows * columns] };
            for (int i = 0; i < rows; i++) for (int j = 0; j < columns; j++)
            { var v = Pair(i, j); utilities[0][i * columns + j] = v[0]; utilities[1][i * columns + j] = v[1]; }
            AffinityResult[] references = null; double[] reference = null;
            if (objective == JointSelectionObjective.RelativeEntropy)
            {
                references = populations.Select(p => AffinityReference.FromSignatures(p.Select(x => Signature(x.Policy)).ToArray(), settings.Affinity)).ToArray();
                reference = references[0].Weights.SelectMany(p => references[1].Weights.Select(d => p * d)).ToArray();
            }
            var selected = CoarseCorrelatedSelector.Select(utilities, rows, columns, game.UtilityScale, objective, reference, selectionSettings);
            var marginal = new[] { new double[rows], new double[columns] }; var expected = new double[game.ValueCount];
            for (int i = 0; i < rows; i++) for (int j = 0; j < columns; j++)
            {
                double w = selected.Weights[i * columns + j]; marginal[0][i] += w; marginal[1][j] += w;
                var v = Pair(i, j); for (int k = 0; k < expected.Length; k++) expected[k] += w * v[k];
            }
            var responses = Enumerable.Range(0, 2).Select(p => game.BestResponse((byte)p, populations[1 - p].Select(x => x.Policy).ToArray(), marginal[1 - p])).ToArray();
            var raw = Enumerable.Range(0, 2).Select(p => responses[p].Utility - expected[p]).ToArray();
            var gains = raw.Select((g, p) => Math.Max(0, g) / game.UtilityScale[p]).ToArray();
            double restricted = Math.Max(0, selected.RestrictedGains.Max());
            var step = new JpsroIteration(iteration, rows, columns, expected, raw, gains, restricted, selected.DualityGap, selected.Termination, selected.Iterations);
            history.Add(step); progress?.Invoke(step);
            string status = gains.Max() <= settings.MaximumNormalizedGain ? "FullGameCceVerified" :
                restricted > Math.Max(selectionSettings.IncentiveTolerance * 10, settings.MaximumNormalizedGain / 10) ? "MetaSolverLimit" :
                iteration + 1 >= settings.MaximumIterations ? "IterationLimit" : null;
            if (status == null)
            {
                bool added = false;
                for (byte p = 0; p < 2; p++) if (gains[p] > settings.MaximumNormalizedGain)
                    added |= Add(new($"response-{iteration + 1}-player-{p}", responses[p].Policy), p);
                if (added) continue;
                status = "NoNewResponse";
            }
            return new("normal-form approximate CCE over complete policies", objective == JointSelectionObjective.MaximumGini ? "maximum-gini" : "liu-economic-affinity-relative-entropy",
                status, populations[0].ToArray(), populations[1].ToArray(), selected.Weights, expected, raw, gains, history.ToArray(), references, selected);
        }
    }
}
