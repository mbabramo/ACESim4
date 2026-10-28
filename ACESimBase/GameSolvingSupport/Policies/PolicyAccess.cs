using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using System;
using System.IO;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Policies;

/// <summary>Semantic policy mapping shared by solvers, diagnostics and persisted-profile adapters.</summary>
public static class PolicyAccess
{
    public static string Key(InformationSetNode node, GameDefinition game) =>
        node.PlayerIndex + "/" + node.DecisionByteCode + "/" + string.Join(";",
            node.LabeledInformationSet.Select(x => game.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode + ":" + x.information));

    public static string[] ActionLabels(InformationSetNode node, GameDefinition game) =>
        Enumerable.Range(1, node.NumPossibleActions).Select(a => game.GetActionString((byte)a, node.DecisionByteCode)).ToArray();

    public static BehavioralProfile CaptureCurrent(StrategiesDeveloperBase developer, string name) =>
        new(name, developer.GameDefinition.OptionSetName, developer.InformationSets.Select(node =>
            new InformationSetPolicy(Key(node, developer.GameDefinition), node.PlayerIndex, node.DecisionByteCode,
                ActionLabels(node, developer.GameDefinition), node.GetCurrentProbabilitiesAsArray())));

    public static BehavioralProfile CaptureAverage(StrategiesDeveloperBase developer, string name) =>
        new(name, developer.GameDefinition.OptionSetName, developer.InformationSets.Select(node =>
            new InformationSetPolicy(Key(node, developer.GameDefinition), node.PlayerIndex, node.DecisionByteCode,
                ActionLabels(node, developer.GameDefinition), node.GetAverageStrategiesAsArray())));

    public static void ValidateLayout(StrategiesDeveloperBase developer, BehavioralProfile profile)
    {
        if (profile.InformationSets.Count != developer.InformationSets.Count)
            throw new InvalidDataException("Profile has a different information-set count.");
        foreach (var node in developer.InformationSets)
            if (!profile.InformationSets.TryGetValue(Key(node, developer.GameDefinition), out var policy) ||
                policy.Player != node.PlayerIndex || policy.Decision != node.DecisionByteCode ||
                !policy.ActionLabels.SequenceEqual(ActionLabels(node, developer.GameDefinition)))
                throw new InvalidDataException("Incomplete or inconsistent semantic profile mapping.");
    }

    /// <summary>Validate the complete profile before writing current and opponent-facing probabilities.
    /// Regrets, average accumulators and training clocks are deliberately not changed.</summary>
    public static void ApplyCurrent(StrategiesDeveloperBase developer, BehavioralProfile profile)
    {
        ValidateLayout(developer, profile);
        foreach (var node in developer.InformationSets)
        {
            var probabilities = profile.InformationSets[Key(node, developer.GameDefinition)].Probabilities.ToArray();
            node.SetCurrentProbabilities(probabilities);
            for (int a = 0; a < probabilities.Length; a++)
                node.NodeInformation[InformationSetNode.currentProbabilityForOpponentDimension, a] = probabilities[a];
        }
    }
}
