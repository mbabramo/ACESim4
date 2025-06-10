using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    /// <summary>
    /// Records every path of decision- and chance-actions together with its reach probability.
    /// </summary>
    public sealed class RecordGamePathsProcessor : ITreeNodeProcessor<double, List<RecordGamePathsProcessor.GamePath>>
    {
        public sealed record ActionStep(IGameState FromNode, byte ActionIndex);
        public sealed record GamePath(IReadOnlyList<ActionStep> Steps, double Probability);

        private readonly Stack<ActionStep> _currentSteps = new();
        private readonly List<GamePath> _allPaths = new();

        public IReadOnlyList<GamePath> Paths => _allPaths;

        // ---------- Forward phase ----------

        public double ChanceNode_Forward(
            ChanceNode chanceNode,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            double fromPredecessor,
            int distributorChanceInputs)
        {
            double cumulative = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);

            if (predecessor != null)
                _currentSteps.Push(new ActionStep(predecessor, predecessorAction));

            return cumulative;
        }

        public double InformationSet_Forward(
            InformationSetNode informationSet,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            double fromPredecessor)
        {
            double cumulative = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);

            if (predecessor != null)
                _currentSteps.Push(new ActionStep(predecessor, predecessorAction));

            return cumulative;
        }

        // ---------- Backward phase ----------

        public List<GamePath> ChanceNode_Backward(
            ChanceNode chanceNode,
            IEnumerable<List<GamePath>> fromSuccessors,
            int distributorChanceInputs)
        {
            var merged = fromSuccessors.SelectMany(x => x).ToList();

            if (_currentSteps.Count > 0)
                _currentSteps.Pop();

            return merged;
        }

        public List<GamePath> InformationSet_Backward(
            InformationSetNode informationSet,
            IEnumerable<List<GamePath>> fromSuccessors)
        {
            var merged = fromSuccessors.SelectMany(x => x).ToList();

            if (_currentSteps.Count > 0)
                _currentSteps.Pop();

            return merged;
        }

        // ---------- Terminal node ----------

        public List<GamePath> FinalUtilities_TurnAround(
            FinalUtilitiesNode finalUtilities,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            double fromPredecessor)
        {
            if (predecessor != null)
                _currentSteps.Push(new ActionStep(predecessor, predecessorAction));

            double probability = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
            var pathCopy = _currentSteps.Reverse().ToList(); // root-to-leaf order

            var path = new GamePath(pathCopy, probability);
            _allPaths.Add(path);

            if (predecessor != null)
                _currentSteps.Pop();

            return new List<GamePath> { path };
        }

        // ---------- Helper ----------

        private static double GetCumulativeReachProbability(double fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            double cumulative = fromPredecessor;

            if (predecessor == null)
                cumulative = 1.0;
            else if (predecessor is ChanceNode c)
                cumulative *= c.GetActionProbability(predecessorAction);
            else if (predecessor is InformationSetNode i)
                cumulative *= i.GetCurrentProbability(predecessorAction, false);

            return cumulative;
        }
    }
}
