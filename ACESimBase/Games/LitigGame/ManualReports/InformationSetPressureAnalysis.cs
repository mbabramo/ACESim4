using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>
/// Diagnostic wrapper around the existing unrestricted, information-set GEBR routine.
/// Never solves a new equilibrium or mutates a saved profile. All continuation decisions
/// of the responding player are optimized; opponent components are mapped semantically.
/// </summary>
public static class InformationSetPressureAnalysis
{
    public const double ReachTolerance = 1e-15;
    public sealed record Tolerances(double Numerical = 1e-7, double Tie = 1e-10, double NearTie = 1e-6);
    public sealed record Strategy(string Key, byte Player, byte Decision, string Labels,
        string[] ActionLabels, double[] Probabilities, double DonorReach, bool UniformFallback,
        string Donor, bool DonorCounterfactuallyUnreachable = false);
    public sealed record Profile(string Name, string OptionSet, Dictionary<string, Strategy> Strategies);
    public sealed record ActionValue(int Action, string Label, double Probability,
        double? CounterfactualConditionalUtility, double? Loss, bool? Best, bool? NearBest);
    public sealed record InformationSet(string Key, int Number, byte Player, string Decision,
        string Labels, int Signal, double SignalValue, int? ExitCommitment,
        double ActualReach, double CounterfactualReach, bool ActualOffPath, bool CounterfactuallyUnreachable,
        double[] OpponentSignalBeliefs, double? ConditionalUtility, ActionValue[] Actions,
        double? ReferenceActionLoss, double? ActionGap);
    public sealed record CompletionWarning(string Key, string Labels, string Donor,
        double DonorReach, double Reach, bool UniformFallback, bool DonorCounterfactuallyUnreachable,
        double FocalDeviationReachWeight);
    public sealed record Result(string Name, string OptionSet, byte Player, string TieSelection,
        double ReferenceUtility, double ResponseUtility, double BestResponseUtility, double Gain,
        double MaxActionValueError, double TerminalProbability, Profile Response,
        InformationSet[] InformationSets, CompletionWarning[] ExposedOpponentSets)
    {
        public CompletionWarning[] NewlyReachedOpponentSets => ExposedOpponentSets.Where(x => x.Reach > ReachTolerance).ToArray();
    }
    public sealed record Reference(string Name, string OptionSet, double[] Utilities,
        double TerminalProbability, InformationSet[] InformationSets);

    public static string Key(InformationSetNode node, GameDefinition game) =>
        node.PlayerIndex + "/" + node.DecisionByteCode + "/" + string.Join(";",
            node.LabeledInformationSet.Select(x => game.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode + ":" + x.information));

    public static string Component(byte decision) => (LitigGameDecisions)decision switch
    {
        LitigGameDecisions.PFile or LitigGameDecisions.DAnswer => "participation",
        LitigGameDecisions.POffer or LitigGameDecisions.DOffer => "offers",
        LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault => "exit",
        _ => throw new NotSupportedException("Not a supported article decision: " + decision)
    };

    public static Profile Capture(StrategiesDeveloperBase developer, string name,
        HashSet<int> fallbacks = null, IReadOnlyDictionary<int, double> reaches = null)
    {
        var strategies = developer.InformationSets.ToDictionary(n => Key(n, developer.GameDefinition), n =>
            new Strategy(Key(n, developer.GameDefinition), n.PlayerIndex, n.DecisionByteCode,
                n.InformationSetWithLabels(developer.GameDefinition),
                Enumerable.Range(1, n.NumPossibleActions).Select(a => developer.GameDefinition.GetActionString((byte)a, n.DecisionByteCode)).ToArray(),
                n.GetCurrentProbabilitiesAsArray(), reaches?.GetValueOrDefault(n.InformationSetNodeNumber) ?? 0,
                fallbacks?.Contains(n.InformationSetNodeNumber) ?? false, name));
        return new(name, developer.GameDefinition.OptionSetName, strategies);
    }

    public static void Apply(StrategiesDeveloperBase developer, Profile profile)
    {
        var nodes = developer.InformationSets;
        if (profile.Strategies.Count != nodes.Count)
            throw new InvalidDataException("Profile has a different information-set count.");
        // Validate the entire mapping before mutating anything.
        foreach (var node in nodes)
        {
            if (!profile.Strategies.TryGetValue(Key(node, developer.GameDefinition), out var strategy) ||
                strategy.Player != node.PlayerIndex || strategy.Decision != node.DecisionByteCode ||
                strategy.Probabilities.Length != node.NumPossibleActions ||
                !strategy.ActionLabels.SequenceEqual(Enumerable.Range(1, node.NumPossibleActions)
                    .Select(a => developer.GameDefinition.GetActionString((byte)a, node.DecisionByteCode))))
                throw new InvalidDataException("Incomplete or inconsistent semantic profile mapping.");
            if (strategy.Probabilities.Any(p => !double.IsFinite(p) || p < 0 || p > 1) ||
                Math.Abs(strategy.Probabilities.Sum() - 1) > 1e-10)
                throw new InvalidDataException("Invalid behavioral strategy at " + strategy.Key);
        }
        foreach (var node in nodes)
        {
            var probabilities = profile.Strategies[Key(node, developer.GameDefinition)].Probabilities;
            node.SetCurrentProbabilities(probabilities);
            // GEBR requests opponent-facing current probabilities, not just display values.
            for (int a = 0; a < probabilities.Length; a++)
                node.NodeInformation[InformationSetNode.currentProbabilityForOpponentDimension, a] = probabilities[a];
        }
    }

    public static Profile Hybrid(Profile baseline, Profile donor, byte opponent, string component, string name)
    {
        if (opponent > 1 || !new[] { "none", "participation", "offers", "exit", "all" }.Contains(component))
            throw new ArgumentException("Invalid opponent/component.");
        if (!baseline.Strategies.Keys.OrderBy(x => x).SequenceEqual(donor.Strategies.Keys.OrderBy(x => x)))
            throw new InvalidDataException("Source and donor have different histories.");
        var entries = baseline.Strategies.ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, value) in entries.ToArray())
            if (value.Player == opponent && (component == "all" || Component(value.Decision) == component))
            {
                var replacement = donor.Strategies[key];
                if (replacement.Player != value.Player || replacement.Decision != value.Decision ||
                    !replacement.ActionLabels.SequenceEqual(value.ActionLabels))
                    throw new InvalidDataException("Incompatible donor action menu.");
                entries[key] = replacement;
            }
        return new(name, baseline.OptionSet, entries);
    }

    public static Reference Describe(StrategiesDeveloperBase developer, Profile profile)
    {
        Apply(developer, profile);
        var walkP = Walk(developer, 0);
        var walkD = Walk(developer, 1);
        return new(profile.Name, developer.GameDefinition.OptionSetName, walkP.RootUtility,
            walkP.TerminalProbability, DescribeSets(developer, 0, walkP, null, new())
                .Concat(DescribeSets(developer, 1, walkD, null, new())).ToArray());
    }

    public static Profile WithObservedReach(Profile profile, Reference reference) => profile with
    {
        Strategies = profile.Strategies.ToDictionary(x => x.Key,
            x => x.Value with { DonorReach = reference.InformationSets.Single(i => i.Key == x.Key).ActualReach })
    };

    public static Result Respond(StrategiesDeveloperBase developer, Profile input, byte player,
        string name, Tolerances tolerances = null, bool highTie = false)
    {
        tolerances ??= new();
        ValidateTolerances(tolerances);
        if (player > 1) throw new ArgumentOutOfRangeException(nameof(player));
        Apply(developer, input);
        var original = Capture(developer, "restore");
        try
        {
            double referenceUtility = Walk(developer, player).RootUtility[player];
            // GEBR leaves an old BestResponseAction in sets with zero opponent reach.
            // Clear those scratch fields, then explicitly preserve the input policy there.
            foreach (var node in developer.InformationSets.Where(n => n.PlayerIndex == player))
            {
                node.BestResponseAction = 1;
                node.BestResponseOptions = null;
            }
            double best = developer.CalculateBestResponse(player, ActionStrategies.CurrentProbability);
            var algorithmValues = new Dictionary<int, double[]>();
            var undefined = new HashSet<string>();
            foreach (var node in developer.InformationSets.Where(n => n.PlayerIndex == player))
            {
                double denominator = node.NodeInformation[InformationSetNode.bestResponseDenominatorDimension, 0];
                if (denominator <= ReachTolerance)
                {
                    undefined.Add(Key(node, developer.GameDefinition));
                    continue;
                }
                node.DetermineBestResponseAction();
                var values = node.BestResponseOptions.ToArray();
                algorithmValues[node.InformationSetNodeNumber] = values;
                int choice = node.BestResponseAction - 1;
                if (highTie) choice = Enumerable.Range(0, values.Length)
                    .Last(a => values.Max() - values[a] <= tolerances.Tie);
                var probabilities = new double[node.NumPossibleActions];
                probabilities[choice] = 1;
                node.SetCurrentProbabilities(probabilities);
            }
            var walk = Walk(developer, player);
            Near(best, walk.RootUtility[player], tolerances.Numerical,
                "Best-response policy replay (including tie sensitivity)");
            if (walk.RootUtility[player] + tolerances.Numerical < referenceUtility)
                throw new InvalidDataException("Best response is worse than the input policy.");
            double maxError = 0;
            if (!highTie)
                foreach (var node in developer.InformationSets.Where(n => n.PlayerIndex == player))
                    if (algorithmValues.TryGetValue(node.InformationSetNodeNumber, out var values))
                    {
                        var info = walk.Sets[node.InformationSetNodeNumber];
                        for (int a = 0; a < values.Length; a++)
                        {
                            double independent = info.ActionNumerators[a] / info.CounterfactualReach;
                            maxError = Math.Max(maxError, Math.Abs(independent - values[a]));
                            Near(values[a], independent, tolerances.Numerical, "Optimized information-set action value");
                        }
                    }
            var captured = Capture(developer, name, reaches: walk.Sets.ToDictionary(x => x.Key, x => x.Value.ActualReach));
            captured = captured with { Strategies = captured.Strategies.ToDictionary(x => x.Key, x =>
                x.Value.Player == player
                    ? x.Value with { DonorCounterfactuallyUnreachable = undefined.Contains(x.Key),
                        UniformFallback = undefined.Contains(x.Key) && input.Strategies[x.Key].UniformFallback }
                    : input.Strategies[x.Key]) };
            var warnings = developer.InformationSets.Where(n => n.PlayerIndex != player).Select(n =>
                (Node: n, Donor: input.Strategies[Key(n, developer.GameDefinition)],
                    Reach: walk.Sets[n.InformationSetNodeNumber].ActualReach,
                    DeviationWeight: walk.Sets[n.InformationSetNodeNumber].FocalDeviationReachWeight))
                .Where(x => x.DeviationWeight > ReachTolerance && x.Donor.DonorReach <= ReachTolerance)
                .Select(x => new CompletionWarning(x.Donor.Key, x.Donor.Labels, x.Donor.Donor,
                    x.Donor.DonorReach, x.Reach, x.Donor.UniformFallback, x.Donor.DonorCounterfactuallyUnreachable, x.DeviationWeight)).ToArray();
            return new(name, developer.GameDefinition.OptionSetName, player,
                highTie ? "highest action within tie tolerance" : "GEBR strict maximum; first action on exact ties",
                referenceUtility, walk.RootUtility[player], best, walk.RootUtility[player] - referenceUtility,
                maxError, walk.TerminalProbability, captured,
                DescribeSets(developer, player, walk, input, tolerances), warnings);
        }
        finally { Apply(developer, original); }
    }

    public static void ValidateTolerances(Tolerances t)
    {
        if (!double.IsFinite(t.Numerical) || !double.IsFinite(t.Tie) || !double.IsFinite(t.NearTie) ||
            t.Tie < 0 || t.Numerical <= 0 || t.NearTie < t.Tie || t.Numerical < t.Tie * 4)
            throw new ArgumentException("Invalid diagnostic tolerances.");
    }

    private static InformationSet[] DescribeSets(StrategiesDeveloperBase developer, byte player,
        DiagnosticWalk walk, Profile reference, Tolerances tolerances)
    {
        var game = developer.GameDefinition;
        int signalCount = ((LitigGameDefinition)game).Options.NumLiabilitySignals;
        return developer.InformationSets.Where(n => n.PlayerIndex == player)
            .OrderBy(n => n.DecisionIndex).ThenBy(n => n.InformationSetNodeNumber).Select(n =>
            {
                var info = walk.Sets[n.InformationSetNodeNumber];
                string key = Key(n, game);
                bool undefined = info.CounterfactualReach <= ReachTolerance;
                double[] q = undefined ? null : info.ActionNumerators.Select(v => v / info.CounterfactualReach).ToArray();
                double[] probabilities = n.GetCurrentProbabilitiesAsArray();
                double[] beliefs = undefined ? null : info.OpponentSignalWeights.Select(v => v / info.CounterfactualReach).ToArray();
                if (beliefs != null) Near(1, beliefs.Sum(), 1e-10, "Conditional opponent-signal probabilities");
                double? best = q?.Max();
                int signal = n.LabeledInformationSet.Single(x => game.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode ==
                    (byte)(player == 0 ? LitigGameDecisions.PLiabilitySignal : LitigGameDecisions.DLiabilitySignal)).information;
                var exit = n.LabeledInformationSet.Where(x => game.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode ==
                    (byte)(player == 0 ? LitigGameDecisions.PAbandon : LitigGameDecisions.DDefault)).ToArray();
                double? gap = q == null ? null : q.OrderByDescending(x => x).First() - q.OrderByDescending(x => x).Skip(1).First();
                double? referenceLoss = q == null || reference == null ? null : best - q.Zip(reference.Strategies[key].Probabilities, (v, p) => v * p).Sum();
                return new InformationSet(key, n.InformationSetNodeNumber, player, n.Decision.Name,
                    n.InformationSetWithLabels(game), signal, (signal - .5) / signalCount,
                    exit.Length == 0 ? null : exit.Single().information,
                    info.ActualReach, info.CounterfactualReach, info.ActualReach <= ReachTolerance, undefined,
                    beliefs, info.ActualReach <= ReachTolerance ? null : q.Zip(probabilities, (v, p) => v * p).Sum(),
                    Enumerable.Range(0, probabilities.Length).Select(a => new ActionValue(a + 1,
                        game.GetActionString((byte)(a + 1), n.DecisionByteCode), probabilities[a], q?[a],
                        q == null ? null : best - q[a], q == null ? null : best - q[a] <= tolerances.Tie,
                        q == null ? null : best - q[a] <= tolerances.NearTie)).ToArray(), referenceLoss, gap);
            }).ToArray();
    }

    public static DiagnosticWalk Walk(StrategiesDeveloperBase developer, byte player)
    {
        var walk = new DiagnosticWalk(player, ((LitigGameDefinition)developer.GameDefinition).Options.NumLiabilitySignals);
        developer.TreeWalk_Tree(walk);
        Near(1, walk.TerminalProbability, 1e-10, "Terminal probability");
        return walk;
    }

    public static void Near(double expected, double actual, double tolerance, string message)
    {
        if (!double.IsFinite(expected) || !double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidDataException($"{message}: expected {expected:G17}; actual {actual:G17}; tolerance {tolerance:G17}.");
    }

    public sealed class SetValues
    {
        public double ActualReach, CounterfactualReach, FocalDeviationReachWeight;
        public double[] ActionNumerators, OpponentSignalWeights;
    }
    public sealed record Frame(double Actual, double Counterfactual, int OpponentSignal);

    /// <summary>
    /// Independent policy evaluator. Counterfactual reach omits the focal player's
    /// earlier action probabilities, but never omits chance or opponent actions.
    /// With perfect recall, normalization gives the same beliefs as actual reach
    /// whenever the information set is reached. Off-path counterfactual values are
    /// explicitly distinct from undefined actual conditional utilities.
    /// </summary>
    public sealed class DiagnosticWalk : ITreeNodeProcessor<Frame, double[]>
    {
        private readonly byte player;
        private readonly int signals;
        private readonly Stack<(InformationSetNode Node, Frame Frame)> stack = new();
        public Dictionary<int, SetValues> Sets { get; } = new();
        public double[] RootUtility { get; private set; }
        public double TerminalProbability { get; private set; }
        public DiagnosticWalk(byte player, int signals) { this.player = player; this.signals = signals; }
        private Frame Advance(IGameState predecessor, byte action, Frame frame)
        {
            if (predecessor == null) return new(1, 1, 0);
            if (predecessor is ChanceNode c)
            {
                double p = c.GetActionProbability(action);
                int signal = c.DecisionByteCode == (byte)(player == 0 ? LitigGameDecisions.DLiabilitySignal : LitigGameDecisions.PLiabilitySignal)
                    ? action : frame.OpponentSignal;
                return new(frame.Actual * p, frame.Counterfactual * p, signal);
            }
            var n = (InformationSetNode)predecessor;
            double probability = n.GetCurrentProbability(action, false);
            return new(frame.Actual * probability, frame.Counterfactual * (n.PlayerIndex == player ? 1 : probability), frame.OpponentSignal);
        }
        public Frame ChanceNode_Forward(ChanceNode node, IGameState predecessor, byte action, Frame frame) => Advance(predecessor, action, frame);
        public Frame InformationSet_Forward(InformationSetNode node, IGameState predecessor, byte action, Frame frame)
        {
            var advanced = Advance(predecessor, action, frame);
            if (!Sets.TryGetValue(node.InformationSetNodeNumber, out var values))
                Sets[node.InformationSetNodeNumber] = values = new() { ActionNumerators = new double[node.NumPossibleActions], OpponentSignalWeights = new double[signals] };
            values.ActualReach += advanced.Actual;
            // At opponent sets this sum is an exposure weight, not a probability:
            // different focal action histories may meet in the opponent's information set.
            values.FocalDeviationReachWeight += advanced.Counterfactual;
            if (node.PlayerIndex == player)
            {
                values.CounterfactualReach += advanced.Counterfactual;
                if (advanced.OpponentSignal < 1 || advanced.OpponentSignal > signals)
                    throw new InvalidDataException("Missing opponent signal in the history.");
                values.OpponentSignalWeights[advanced.OpponentSignal - 1] += advanced.Counterfactual;
            }
            stack.Push((node, advanced));
            return advanced;
        }
        public double[] InformationSet_Backward(InformationSetNode node, IEnumerable<double[]> children)
        {
            var (_, frame) = stack.Pop();
            var values = children.ToArray();
            if (node.PlayerIndex == player)
                for (int a = 0; a < values.Length; a++) Sets[node.InformationSetNodeNumber].ActionNumerators[a] += frame.Counterfactual * values[a][player];
            return Aggregate(values, node.GetCurrentProbabilitiesAsArray());
        }
        public double[] ChanceNode_Backward(ChanceNode node, IEnumerable<double[]> children) => Aggregate(children.ToArray(), node.GetActionProbabilities().ToArray());
        private double[] Aggregate(double[][] children, double[] probabilities)
        {
            var utility = new double[children[0].Length];
            for (int a = 0; a < children.Length; a++)
                for (int p = 0; p < utility.Length; p++) utility[p] += probabilities[a] * children[a][p];
            RootUtility = utility;
            return utility;
        }
        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode terminal, IGameState predecessor, byte action, Frame frame)
        {
            TerminalProbability += Advance(predecessor, action, frame).Actual;
            return terminal.Utilities.ToArray();
        }
    }
}
