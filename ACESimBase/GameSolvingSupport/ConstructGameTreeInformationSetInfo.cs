using ACESim;
using Microsoft.FSharp.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class ConstructGameTreeInformationSetInfo : ITreeNodeProcessor<ConstructGameTreeInformationSetInfo.ForwardInfo, ConstructGameTreeInformationSetInfo.MoveProbabilityTracker<(byte decisionByteCode, byte move)>>
    {
        Dictionary<int, double> ProbabilityOfReachingInformationSetForNonChancePlayer = new Dictionary<int, double>();
        Dictionary<int, double> ProbabilityOfReachingInformationSetForChance = new Dictionary<int, double>();
        Dictionary<int, double> ProbabilityOfReachingInformationSet(bool forNonChancePlayer) => forNonChancePlayer ? ProbabilityOfReachingInformationSetForNonChancePlayer : ProbabilityOfReachingInformationSetForChance;
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForNonChancePlayerNodes = new Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>>();
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForChanceNodes = new Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>>();
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForInformationSets(bool forNonChancePlayers) => forNonChancePlayers ? StatisticsForNonChancePlayerNodes : StatisticsForChanceNodes;

        Dictionary<(bool chancePlayer, int nodeNumber), IAnyNode> InformationSets = new Dictionary<(bool chancePlayer, int nodeNumber), IAnyNode>();
        NWayTreeStorageInternal<GamePointNode> TreeRoot;
        Stack<NWayTreeStorageInternal<GamePointNode>> ParentNodes = new Stack<NWayTreeStorageInternal<GamePointNode>>();
        Stack<double> ProbabilitiesToNode = new Stack<double>();

        public record GamePointNode(IAnyNode anyNode, double gamePointReachProbability)
        {
        }

        public record ForwardInfo(MoveProbabilityTracker<(byte decisionByteCode, byte move)> moveProbabilities, double reachProbability)
        {
        }

        public ConstructGameTreeInformationSetInfo()
        {
        }

        public Dictionary<string, double[]> GetProbabilitiesOfOtherInformationSetMoves(bool nonChancePlayer, int atNodeNumber, List<Decision> decisions)
        {
            Dictionary<string, double[]> d = new Dictionary<string, double[]>();
            foreach (var decision in decisions)
            {
                double[] probabilities = GetProbabilitiesOfOtherInformationSetMoves(nonChancePlayer, atNodeNumber, decision);
                if (probabilities != null)
                    d[decision.Name] = probabilities;
            }
            return d;
        }

        public double[] GetProbabilitiesOfOtherInformationSetMoves(bool nonChancePlayer, int atNodeNumber, Decision sourceDecision)
        {
            double[] results = new double[sourceDecision.NumPossibleActions];
            var statistics = StatisticsForInformationSets(nonChancePlayer);
            var statisticsAtNode = statistics.GetValueOrDefault(atNodeNumber);
            if (statisticsAtNode == null)
                return null;
            for (byte a = 1; a <= sourceDecision.NumPossibleActions; a++)
                results[a - 1] = statisticsAtNode.GetWeight((sourceDecision.DecisionByteCode, a));
            double sum = results.Sum();
            return results.Select(x => x / sum).ToArray();
        }

        public class MoveProbabilityTracker<T>
        {
            Dictionary<T, double> Values = new Dictionary<T, double>();


            public MoveProbabilityTracker()
            {

            }
            public MoveProbabilityTracker(List<MoveProbabilityTracker<T>> laterMoves, double[] weights)
            {
                for (int i = 0; i < weights.Count(); i++)
                    Aggregate(laterMoves[i], weights[i]);
            }

            public MoveProbabilityTracker<T> CloneWithWeight(double w)
            {
                var tracker = new MoveProbabilityTracker<T>();
                foreach (var v in Values)
                    tracker.AddMove(v.Key, v.Value * w);
                return tracker;
            }

            public void Aggregate(MoveProbabilityTracker<T> other, double weight)
            {
                foreach (var v in other.Values)
                    AddMove(v.Key, v.Value * weight);
            }

            public void AddMove(T t, double weight)
            {
                if (!Values.ContainsKey(t))
                    Values[t] = 0;
                Values[t] += weight;
            }

            public double GetWeight(T t)
            {
                if (!Values.ContainsKey(t))
                    Values[t] = 0;
                return Values[t];
            }

            public double[] GetWeights()
            {
                return Values.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
            }
        }

        private MoveProbabilityTracker<(byte decisionByteCode, byte move)> AddToTracker(bool nonChancePlayer, int nodeNumber, double reachProbability, MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker)
        {
            var tracker = nonChancePlayer ? StatisticsForNonChancePlayerNodes : StatisticsForChanceNodes;
            var moveProbabilityTracker = tracker.GetValueOrDefault(nodeNumber, new MoveProbabilityTracker<(byte decisionByteCode, byte move)>());
            moveProbabilityTracker.Aggregate(toAddToTracker, reachProbability);
            tracker[nodeNumber] = moveProbabilityTracker.CloneWithWeight(1.0);
            return moveProbabilityTracker;
        }

        private static double GetCumulativeReachProbability(double fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            double cumulativeProbability = fromPredecessor;
            if (predecessor == null)
                cumulativeProbability = 1.0;
            else if (predecessor is ChanceNode c)
                cumulativeProbability *= c.GetActionProbability(predecessorAction);
            else if (predecessor is InformationSetNode i)
                cumulativeProbability *= i.GetCurrentProbability(predecessorAction, false);
            return cumulativeProbability;
        }

        private ForwardInfo AnyNode_Forward(IAnyNode anyNode, IGameState predecessor, byte predecessorAction, ForwardInfo fromPredecessor)
        {
            double reachProbability = fromPredecessor == null ? 1.0 : GetCumulativeReachProbability(fromPredecessor.reachProbability, predecessor, predecessorAction);
            ProbabilitiesToNode.Push(reachProbability);

            AddNodeToTree(anyNode, predecessorAction, reachProbability);

            ProbabilityOfReachingInformationSet(!anyNode.IsChanceNode)[anyNode.GetInformationSetNodeNumber()] = ProbabilityOfReachingInformationSet(!anyNode.IsChanceNode).GetValueOrDefault(anyNode.GetInformationSetNodeNumber()) + reachProbability;
            MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker = fromPredecessor == null ? new MoveProbabilityTracker<(byte decisionByteCode, byte move)>() : fromPredecessor.moveProbabilities.CloneWithWeight(1.0);
            for (int a = 1; a <= anyNode.Decision.NumPossibleActions; a++)
            {
                toAddToTracker.AddMove((anyNode.Decision.DecisionByteCode, (byte)a), 1.0);
            }
            int nodeNumber = anyNode.GetInformationSetNodeNumber();
            return new ForwardInfo(AddToTracker(!anyNode.IsChanceNode, nodeNumber, reachProbability, toAddToTracker), reachProbability);
        }

        private void AddNodeToTree(IAnyNode anyNode, byte predecessorAction, double reachProbability)
        {
            NWayTreeStorageInternal<GamePointNode> treeNode = null;
            if (TreeRoot == null)
                treeNode = TreeRoot = new NWayTreeStorageInternal<GamePointNode>(null, anyNode.Decision.NumPossibleActions);
            else
            {
                var parentNode = ParentNodes.Peek();
                parentNode.SetBranch(predecessorAction, new NWayTreeStorageInternal<GamePointNode>(parentNode, anyNode.Decision?.NumPossibleActions ?? 0));
                treeNode = (NWayTreeStorageInternal<GamePointNode>)parentNode.GetBranch(predecessorAction);
            }
            treeNode.StoredValue = new GamePointNode(anyNode, reachProbability);

            if (!anyNode.IsUtilitiesNode)
                ParentNodes.Push(treeNode);
        }

        public ForwardInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor, int distributorChanceInputs) => AnyNode_Forward(chanceNode, predecessor, predecessorAction, fromPredecessor);

        public ForwardInfo InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor) => AnyNode_Forward(informationSet, predecessor, predecessorAction, fromPredecessor);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor)
        {
            double reachProbability = GetCumulativeReachProbability(fromPredecessor.reachProbability, predecessor, predecessorAction);
            AddNodeToTree(finalUtilities, predecessorAction, reachProbability);
            return new MoveProbabilityTracker<(byte decisionByteCode, byte move)>();
        }

        private MoveProbabilityTracker<(byte decisionByteCode, byte move)> AnyNode_Backward(IAnyNode node, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors)
        {
            ParentNodes.Pop();
            double reachProbability = ProbabilitiesToNode.Pop();
            var probabilitiesFromHere = node.GetNodeValues();
            MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker = new MoveProbabilityTracker<(byte decisionByteCode, byte move)>(fromSuccessors.ToList(), probabilitiesFromHere);
            for (int a = 1; a <= node.Decision.NumPossibleActions; a++)
            {
                toAddToTracker.AddMove((node.Decision.DecisionByteCode, (byte)a), probabilitiesFromHere[a - 1]);
            }
            int nodeNumber = node.GetInformationSetNodeNumber();
            return AddToTracker(!node.IsChanceNode, nodeNumber, reachProbability, toAddToTracker);
        }

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors, int distributorChanceInputs) => AnyNode_Backward(chanceNode, fromSuccessors);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> InformationSet_Backward(InformationSetNode informationSet, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors) => AnyNode_Backward(informationSet, fromSuccessors);
    }
}
