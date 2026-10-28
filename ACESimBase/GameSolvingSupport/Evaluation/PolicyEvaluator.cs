using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Evaluation;

/// <summary>Evaluate a complete profile against the initialized game's utilities/chance law without
/// installing it in the solver. The result is not an equilibrium certificate.</summary>
public static class PolicyEvaluator
{
    public sealed record Result(double[] Utilities, double TerminalProbability);

    public static Result Evaluate(StrategiesDeveloperBase developer, BehavioralProfile profile)
    {
        PolicyAccess.ValidateLayout(developer, profile);
        var probabilities = developer.InformationSets.ToDictionary(n => n.InformationSetNodeNumber,
            n => profile.InformationSets[PolicyAccess.Key(n, developer.GameDefinition)].Probabilities);
        var walk = new EvaluationWalk(probabilities);
        developer.TreeWalk_Tree(walk);
        return new(walk.RootUtility.ToArray(), walk.TerminalProbability);
    }

    private sealed class EvaluationWalk : ITreeNodeProcessor<double, double[]>
    {
        private readonly IReadOnlyDictionary<int, IReadOnlyList<double>> probabilities;
        public double[] RootUtility { get; private set; }
        public double TerminalProbability { get; private set; }
        public EvaluationWalk(IReadOnlyDictionary<int, IReadOnlyList<double>> probabilities) => this.probabilities = probabilities;

        private double Advance(IGameState predecessor, byte action, double reach) => predecessor switch
        {
            null => 1,
            ChanceNode chance => reach * chance.GetActionProbability(action),
            InformationSetNode node => reach * probabilities[node.InformationSetNodeNumber][action - 1],
            _ => throw new InvalidOperationException("Unexpected predecessor.")
        };
        public double ChanceNode_Forward(ChanceNode node, IGameState predecessor, byte action, double reach) => Advance(predecessor, action, reach);
        public double InformationSet_Forward(InformationSetNode node, IGameState predecessor, byte action, double reach) => Advance(predecessor, action, reach);
        public double[] InformationSet_Backward(InformationSetNode node, IEnumerable<double[]> children) => Aggregate(children.ToArray(), probabilities[node.InformationSetNodeNumber]);
        public double[] ChanceNode_Backward(ChanceNode node, IEnumerable<double[]> children) => Aggregate(children.ToArray(), node.GetActionProbabilities().ToArray());
        private double[] Aggregate(double[][] children, IReadOnlyList<double> weights)
        {
            var utilities = new double[children[0].Length];
            for (int a = 0; a < children.Length; a++)
                for (int p = 0; p < utilities.Length; p++) utilities[p] += weights[a] * children[a][p];
            return RootUtility = utilities;
        }
        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode node, IGameState predecessor, byte action, double reach)
        {
            TerminalProbability += Advance(predecessor, action, reach);
            // Also supports a game whose root is already terminal.
            return RootUtility = node.Utilities.ToArray();
        }
    }
}
