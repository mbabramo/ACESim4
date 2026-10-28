using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Policies;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Evaluation;

/// <summary>
/// Detached two-player perfect-recall tree. Mixtures select a complete opponent policy
/// once, privately, before play. No training fields, report state, or policy averages are used.
/// A tree instance caches immutable policies and is intended for sequential use.
/// </summary>
public sealed class PolicyGameTree
{
    public sealed class Node
    {
        internal int Index;
        internal int InformationSet;
        internal double[] ChanceProbabilities;
        internal double[] Values;
        internal Node[] Children;
        internal byte[] Actions;
        private Node() { }
        public static Node Terminal(params double[] values) => new() { InformationSet = -2, Values = values.ToArray(), Children = [] };
        public static Node Chance(double[] probabilities, params Node[] children) => new() { InformationSet = -1, ChanceProbabilities = probabilities.ToArray(), Children = children.ToArray() };
        public static Node Decision(int informationSet, params Node[] children) => new() { InformationSet = informationSet, Children = children.ToArray() };
    }
    public sealed record Response(PlayerPolicy Policy, double Utility, double ReplayUtility);
    private readonly Node root;
    private readonly Node[] nodes;
    private readonly InformationSetPolicy[] layout;
    private readonly List<Node>[] occurrences;
    private readonly (int Info, int Action)[][] ownSequences;
    private readonly Dictionary<PlayerPolicy, double[][]> compiled = new();
    public IReadOnlyList<InformationSetPolicy> Layout => Array.AsReadOnly(layout);
    public int NodeCount => nodes.Length;
    public int ValueCount { get; }
    public double[] UtilityMinimum { get; }
    public double[] UtilityScale { get; }

    public PolicyGameTree(Node root, IEnumerable<InformationSetPolicy> informationSets)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        layout = informationSets.ToArray();
        if (layout.Any(x => x.Player > 1) || layout.Select(x => x.Key).Distinct().Count() != layout.Length)
            throw new ArgumentException("A two-player, uniquely keyed policy layout is required.");
        occurrences = layout.Select(_ => new List<Node>()).ToArray();
        ownSequences = new (int, int)[layout.Length][];
        var all = new List<Node>();
        var seen = new HashSet<Node>();
        var actions = new List<byte>();
        void Visit(Node n, List<(int, int)>[] histories)
        {
            if (!seen.Add(n)) throw new InvalidDataException("Use separate nodes for separate tree histories.");
            n.Index = all.Count; all.Add(n);
            if (n.InformationSet == -2)
            {
                if (n.Values.Length < 2 || n.Values.Any(x => !double.IsFinite(x))) throw new InvalidDataException("Invalid terminal values.");
                n.Actions = actions.ToArray();
                return;
            }
            if (n.Children.Length == 0) throw new InvalidDataException("Empty decision.");
            if (n.InformationSet == -1)
            {
                ValidateDistribution(n.ChanceProbabilities, n.Children.Length);
                for (int a = 0; a < n.Children.Length; a++)
                { actions.Add(checked((byte)(a + 1))); Visit(n.Children[a], histories); actions.RemoveAt(actions.Count - 1); }
                return;
            }
            int info = n.InformationSet;
            if (info < 0 || info >= layout.Length || n.Children.Length != layout[info].Probabilities.Count)
                throw new InvalidDataException("Inconsistent information-set actions.");
            byte player = layout[info].Player;
            if (ownSequences[info] == null) ownSequences[info] = histories[player].ToArray();
            else if (!ownSequences[info].SequenceEqual(histories[player]))
                throw new NotSupportedException("Population response oracles require perfect recall.");
            if (histories[player].Any(h => h.Item1 == info)) throw new NotSupportedException("Absent-minded information set.");
            occurrences[info].Add(n);
            for (int a = 0; a < n.Children.Length; a++)
            {
                histories[player].Add((info, a)); actions.Add(checked((byte)(a + 1)));
                Visit(n.Children[a], histories); histories[player].RemoveAt(histories[player].Count - 1); actions.RemoveAt(actions.Count - 1);
            }
        }
        Visit(root, [new(), new()]);
        nodes = all.ToArray();
        if (occurrences.Any(x => x.Count == 0)) throw new InvalidDataException("Unused information set.");
        var leaves = nodes.Where(n => n.InformationSet == -2).ToArray();
        ValueCount = leaves[0].Values.Length;
        if (leaves.Any(n => n.Values.Length != ValueCount)) throw new InvalidDataException("Inconsistent terminal value dimensions.");
        UtilityMinimum = Enumerable.Range(0, 2).Select(p => leaves.Min(n => n.Values[p])).ToArray();
        UtilityScale = Enumerable.Range(0, 2).Select(p => Math.Max(1e-12, leaves.Max(n => n.Values[p]) - UtilityMinimum[p])).ToArray();
    }

    public static PolicyGameTree Create(StrategiesDeveloperBase developer, Func<FinalUtilitiesNode, double[]> values = null)
    {
        if (developer.NumNonChancePlayers != 2) throw new NotSupportedException("Exactly two strategic players are required.");
        var informationSets = developer.InformationSets.ToArray();
        var map = informationSets.Select((n, i) => (n.InformationSetNodeNumber, i)).ToDictionary(x => x.InformationSetNodeNumber, x => x.i);
        var specs = informationSets.Select(n => new InformationSetPolicy(PolicyAccess.Key(n, developer.GameDefinition), n.PlayerIndex,
            n.DecisionByteCode, PolicyAccess.ActionLabels(n, developer.GameDefinition), n.GetCurrentProbabilitiesAsArray())).ToArray();
        return new(developer.TreeWalk_Tree(new Builder(map, values ?? (n => n.Utilities.Concat(n.CustomResult.AsDoubleArray()).ToArray()))), specs);
    }

    private sealed class Builder(Dictionary<int, int> map, Func<FinalUtilitiesNode, double[]> values) : ITreeNodeProcessor<int, Node>
    {
        public int ChanceNode_Forward(ChanceNode n, IGameState p, byte a, int f) => 0;
        public int InformationSet_Forward(InformationSetNode n, IGameState p, byte a, int f) => 0;
        public Node ChanceNode_Backward(ChanceNode n, IEnumerable<Node> children) => Node.Chance(n.GetActionProbabilities().ToArray(), children.ToArray());
        public Node InformationSet_Backward(InformationSetNode n, IEnumerable<Node> children) => Node.Decision(map[n.InformationSetNodeNumber], children.ToArray());
        public Node FinalUtilities_TurnAround(FinalUtilitiesNode n, IGameState p, byte a, int f) => Node.Terminal(values(n));
    }

    public PlayerPolicy Uniform(byte player) => MakePolicy(player, i => Enumerable.Repeat(1.0 / layout[i].Probabilities.Count, layout[i].Probabilities.Count).ToArray());
    public PlayerPolicy MakePolicy(byte player, Func<int, double[]> probabilities)
    {
        if (player > 1) throw new ArgumentOutOfRangeException(nameof(player));
        return new(player, layout.Select((s, i) => (s, i)).Where(x => x.s.Player == player)
            .Select(x => new InformationSetPolicy(x.s.Key, player, x.s.Decision, x.s.ActionLabels, probabilities(x.i))));
    }
    private double[][] Compile(PlayerPolicy policy)
    {
        if (policy == null || policy.Player > 1) throw new ArgumentException("Invalid player policy.");
        if (compiled.TryGetValue(policy, out var result)) return result;
        if (policy.InformationSets.Count != layout.Count(x => x.Player == policy.Player)) throw new InvalidDataException("Incomplete player policy.");
        result = new double[layout.Length][];
        for (int i = 0; i < layout.Length; i++) if (layout[i].Player == policy.Player)
        {
            var spec = layout[i];
            if (!policy.InformationSets.TryGetValue(spec.Key, out var s) || s.Player != spec.Player || s.Decision != spec.Decision || !s.ActionLabels.SequenceEqual(spec.ActionLabels))
                throw new InvalidDataException("Player policy has a different semantic layout.");
            result[i] = s.Probabilities.ToArray();
        }
        compiled.Add(policy, result);
        return result;
    }
    public static void ValidateDistribution(IReadOnlyList<double> values, int count)
    {
        if (values == null || values.Count != count || count == 0 || values.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(values.Sum() - 1) > 1e-10)
            throw new ArgumentException("Expected a finite nonnegative probability distribution.");
    }

    public double[] Evaluate(PlayerPolicy plaintiff, PlayerPolicy defendant)
    {
        if (plaintiff.Player != 0 || defendant.Player != 1) throw new ArgumentException("Player order must be 0, 1.");
        var p = new[] { Compile(plaintiff), Compile(defendant) };
        var reach = ArrayPool<double>.Shared.Rent(nodes.Length);
        var result = new double[ValueCount];
        reach[root.Index] = 1;
        try
        {
            foreach (var n in nodes)
            {
                if (n.InformationSet == -2)
                { for (int v = 0; v < result.Length; v++) result[v] += reach[n.Index] * n.Values[v]; }
                else
                {
                    var probabilities = n.InformationSet < 0 ? n.ChanceProbabilities : p[layout[n.InformationSet].Player][n.InformationSet];
                    for (int a = 0; a < n.Children.Length; a++) reach[n.Children[a].Index] = reach[n.Index] * probabilities[a];
                }
            }
        }
        finally { ArrayPool<double>.Shared.Return(reach); }
        return result;
    }

    /// <summary>Enumerate the outcome distribution of a joint policy mixture. Each terminal
    /// path is emitted once, with its joint-weighted probability, for game-specific reports.</summary>
    public double VisitJointTerminals(IReadOnlyList<PlayerPolicy> plaintiffs, IReadOnlyList<PlayerPolicy> defendants,
        IReadOnlyList<double> weights, Action<IReadOnlyList<byte>, double> visit)
    {
        ValidateDistribution(weights, checked(plaintiffs.Count * defendants.Count));
        if (plaintiffs.Any(p => p.Player != 0) || defendants.Any(p => p.Player != 1)) throw new ArgumentException("Wrong population player.");
        var p = plaintiffs.Select(Compile).ToArray(); var d = defendants.Select(Compile).ToArray();
        var terminalMass = new double[nodes.Length]; var reach = new double[nodes.Length];
        for (int i = 0; i < p.Length; i++) for (int j = 0; j < d.Length; j++)
        {
            double weight = weights[i * d.Length + j]; if (weight == 0) continue;
            reach[root.Index] = weight;
            foreach (var n in nodes)
            {
                if (n.InformationSet == -2) terminalMass[n.Index] += reach[n.Index];
                else
                {
                    var probabilities = n.InformationSet < 0 ? n.ChanceProbabilities :
                        layout[n.InformationSet].Player == 0 ? p[i][n.InformationSet] : d[j][n.InformationSet];
                    for (int a = 0; a < n.Children.Length; a++) reach[n.Children[a].Index] = reach[n.Index] * probabilities[a];
                }
            }
        }
        double total = 0;
        foreach (var n in nodes.Where(n => n.InformationSet == -2)) if (terminalMass[n.Index] > 0)
        { total += terminalMass[n.Index]; visit(Array.AsReadOnly(n.Actions), terminalMass[n.Index]); }
        if (Math.Abs(total - 1) > 1e-9) throw new ArithmeticException("Joint terminal mass differs from one.");
        return total;
    }

    /// <summary>Kuhn realization-equivalent behavioral policy, weighted by own sequence reach.
    /// Arithmetic averaging of probabilities would reveal/forget the hidden policy incorrectly.</summary>
    public PlayerPolicy Mix(IReadOnlyList<PlayerPolicy> policies, IReadOnlyList<double> weights)
    {
        ValidateDistribution(weights, policies.Count);
        byte player = policies[0].Player;
        if (policies.Any(p => p.Player != player)) throw new ArgumentException("A mixture contains one player's policies.");
        var entries = policies.Select(Compile).ToArray();
        return MakePolicy(player, info =>
        {
            var result = new double[layout[info].Probabilities.Count]; double total = 0;
            // Rare off-path sequences can have subnormal or underflowing reach. Work in
            // logs so their conditional mixture remains well defined; then rescale.
            var logMass = new double[entries.Length];
            for (int k = 0; k < entries.Length; k++)
            {
                logMass[k] = Math.Log(weights[k]);
                foreach (var prior in ownSequences[info]) logMass[k] += Math.Log(entries[k][prior.Info][prior.Action]);
            }
            double maximum = logMass.Max();
            if (double.IsNegativeInfinity(maximum)) return Enumerable.Repeat(1.0 / result.Length, result.Length).ToArray();
            for (int k = 0; k < entries.Length; k++)
            {
                double mass = Math.Exp(logMass[k] - maximum);
                total += mass;
                for (int a = 0; a < result.Length; a++) result[a] += mass * entries[k][info][a];
            }
            for (int a = 0; a < result.Length; a++) result[a] = Math.Clamp(result[a] / total, 0, 1);
            return result;
        });
    }

    public Response BestResponse(byte player, IReadOnlyList<PlayerPolicy> opponents, IReadOnlyList<double> weights)
    {
        if (player > 1 || opponents.Any(x => x.Player == player)) throw new ArgumentException("Wrong opponent player.");
        var mixture = Mix(opponents, weights); var opponent = Compile(mixture);
        var reach = new double[nodes.Length]; reach[root.Index] = 1;
        foreach (var n in nodes.Where(n => n.Children.Length > 0))
            for (int a = 0; a < n.Children.Length; a++)
                reach[n.Children[a].Index] = reach[n.Index] * (n.InformationSet < 0 ? n.ChanceProbabilities[a] :
                    layout[n.InformationSet].Player == player ? 1 : opponent[n.InformationSet][a]);
        var choices = Enumerable.Repeat(-1, layout.Length).ToArray();
        var solving = new bool[layout.Length];
        var values = Enumerable.Repeat(double.NaN, nodes.Length).ToArray();
        double Value(Node n)
        {
            if (!double.IsNaN(values[n.Index])) return values[n.Index];
            double v;
            if (n.InformationSet == -2) v = n.Values[player];
            else if (n.InformationSet >= 0 && layout[n.InformationSet].Player == player)
            { Solve(n.InformationSet); v = Value(n.Children[choices[n.InformationSet]]); }
            else
            {
                var probabilities = n.InformationSet < 0 ? n.ChanceProbabilities : opponent[n.InformationSet];
                v = 0; for (int a = 0; a < n.Children.Length; a++) v += probabilities[a] * Value(n.Children[a]);
            }
            return values[n.Index] = v;
        }
        void Solve(int info)
        {
            if (choices[info] >= 0) return;
            if (solving[info]) throw new NotSupportedException("Cyclic information-set dependency.");
            solving[info] = true;
            var actionValues = new double[layout[info].Probabilities.Count];
            foreach (var n in occurrences[info])
                for (int a = 0; a < n.Children.Length; a++) actionValues[a] += reach[n.Index] * Value(n.Children[a]);
            choices[info] = 0;
            for (int a = 1; a < actionValues.Length; a++) if (actionValues[a] > actionValues[choices[info]]) choices[info] = a;
            solving[info] = false;
        }
        double best = Value(root);
        // Complete even counterfactually unreachable information sets deterministically.
        for (int i = 0; i < layout.Length; i++) if (layout[i].Player == player) Solve(i);
        var response = MakePolicy(player, i => Enumerable.Range(0, layout[i].Probabilities.Count).Select(a => a == choices[i] ? 1.0 : 0).ToArray());
        double replay = 0;
        for (int k = 0; k < opponents.Count; k++) if (weights[k] > 0)
            replay += weights[k] * (player == 0 ? Evaluate(response, opponents[k]) : Evaluate(opponents[k], response))[player];
        if (Math.Abs(best - replay) > 1e-9 * Math.Max(1, UtilityScale[player])) throw new ArithmeticException("Hidden-mixture response replay differs.");
        // A transient mixture must not accumulate in the immutable-policy cache.
        compiled.Remove(mixture);
        return new(response, best, replay);
    }
}
