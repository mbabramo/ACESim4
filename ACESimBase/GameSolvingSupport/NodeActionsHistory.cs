using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class NodeActionsHistory
    {
        public double Coefficient = 1.0;
        public List<NodeAction> NodeActions = new List<NodeAction>();
        public IGameState Successor;

        public NodeActionsHistory()
        {

        }

        public override string ToString()
        {
            string coefString = Coefficient == 1.0 ? "" : $"{Coefficient.ToSignificantFigures(3)} * ";
            var s = coefString + String.Join(", ", NodeActions);
            if (Successor != null)
                s += $"{(s.Length > 0 ? " " : "")}Successor: {Successor.GetGameStateType()} {Successor.GetNodeNumber()}";
            return s;
        }

        public NodeActionsHistory DeepClone(int skip = 0, int take = int.MaxValue)
        {
            return new NodeActionsHistory()
            {
                NodeActions = NodeActions.Select(x => x.Clone()).Skip(skip).Take(take).ToList(),
                Successor = Successor
            };
        }

        public NodeActionsHistory WithoutDistributedChanceActions()
        {
            return new NodeActionsHistory()
            {
                NodeActions = NodeActions
                                .Where(x => !(x.Node is ChanceNode c) || !c.Decision.DistributedChanceDecision)
                                .Select(x => x.Clone())
                                .ToList(),
                Successor = Successor
            };
        }

        public override bool Equals(object obj)
        {
            NodeActionsHistory other = (NodeActionsHistory)obj;
            return other.NodeActions.SequenceEqual(NodeActions) && other.Successor == Successor;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NodeActions, Successor);
        }

        private bool ModifyCoefficient(IGameState node)
        {
            if (node is ChanceNodeEqualProbabilities c)
            {
                Coefficient *= 1.0 / (double)c.Decision.NumPossibleActions;
                return true;
            }
            return false;
        }

        public NodeActionsHistory WithPrepended(IGameState node, byte actionAtNode, int distributorChanceInputs = -1, bool omitDistributedChanceDecisions = false)
        {
            if (node == null)
                throw new Exception();
            var copied = NodeActions.ToList();
            var prepended = new NodeActionsHistory()
            {
                Coefficient = Coefficient,
                NodeActions = copied,
                Successor = Successor
            };
            if (!prepended.ModifyCoefficient(node) && !(omitDistributedChanceDecisions && node is ChanceNode c && c.Decision.DistributedChanceDecision))
                copied.Insert(0, new NodeAction(node, actionAtNode, distributorChanceInputs));
            return prepended;
        }

        public NodeActionsHistory WithAppended(IGameState node, byte actionAtNode, int distributorChanceInputs = -1)
        {
            if (node == null)
                throw new Exception();
            var copied = NodeActions.ToList();
            var appended = new NodeActionsHistory()
            {
                Coefficient = Coefficient,
                NodeActions = copied,
                Successor = null
            };
            if (!appended.ModifyCoefficient(node))
                copied.Add(new NodeAction(node, actionAtNode, distributorChanceInputs));
            return appended;
        }

        public NodeActionsHistory WithFinalUtilitiesAppended(FinalUtilitiesNode node)
        {
            var copied = NodeActions.ToList();
            return new NodeActionsHistory()
            {
                NodeActions = copied,
                Successor = node
            };
        }

        /// <summary>
        /// Returns another NodeActionsHistory object including all history since the last information set by the player, or all history if the player has not had an information set yet.
        /// </summary>
        /// <param name="nonChancePlayerIndex"></param>
        /// <returns></returns>
        public NodeActionsHistory GetIncrementalHistory(byte nonChancePlayerIndex)
        {
            int? indexOfLastInformationSetByPlayer = GetIndexOfLastInformationSetByPlayer(nonChancePlayerIndex);
            return DeepClone(indexOfLastInformationSetByPlayer == null ? 0 : (int)indexOfLastInformationSetByPlayer + 1).WithoutDistributedChanceActions();
        }

        public (InformationSetNode lastInformationSet, byte actionTakenAtLastInformationSet) GetLastInformationSetByPlayer(byte nonChancePlayerIndex)
        {
            int? index = GetIndexOfLastInformationSetByPlayer(nonChancePlayerIndex);
            if (index == null)
                return (null, 0);
            NodeAction nodeAction = NodeActions[(int)index];
            var informationSet = nodeAction.Node as InformationSetNode;
            if (informationSet == null)
                throw new Exception();
            return (informationSet, nodeAction.ActionAtNode);
        }

        private int? GetIndexOfLastInformationSetByPlayer(byte nonChancePlayerIndex)
        {
            int? indexOfLastInformationSetByPlayer = null;
            for (int i = NodeActions.Count - 1; i >= 0; i--)
                if (NodeActions[i].Node is InformationSetNode informationSet && informationSet.PlayerIndex == nonChancePlayerIndex)
                {
                    indexOfLastInformationSetByPlayer = i;
                    break;
                }

            return indexOfLastInformationSetByPlayer;
        }


        /// <summary>
        /// Returns a list of actions, excluding a specified player as well as distributed chance decisions, if chance decisions are being distributed. The decisions included are thus all those that may differentiate the action taken at the current information set.
        /// </summary>
        /// <param name="excludePlayerIndex"></param>
        /// <param name="distributingChanceDecisions"></param>
        /// <returns></returns>
        public ByteList GetActionsList(byte? excludePlayerIndex, bool distributingChanceDecisions)
        {
            IEnumerable<byte> nodesToInclude = NodeActions
                .Where(x => excludePlayerIndex == null || !(x.Node is InformationSetNode informationSet) || informationSet.PlayerIndex != excludePlayerIndex)
                .Where(x => !(x.Node is ChanceNode chanceNode) ||
                        (!(chanceNode.Decision.DistributedChanceDecision))
                        || !distributingChanceDecisions)
                .Select(x => x.ActionAtNode);
            return new ByteList(nodesToInclude);
        }

        public (double bestResponseValue, double averageStrategyValue) GetProbabilityAdjustedUtilityOfPath(byte playerIndex)
        {
            double bestResponseUtility = GetBestResponseUtilityAfterPathToSuccessor(playerIndex);
            double averageStrategyUtility = GetAverageStrategyUtilityAfterPathToSuccessor(playerIndex);
            double pathProbability = GetProbabilityOfPath();
            double bestResponseValue = pathProbability * bestResponseUtility;
            double averageStrategyValue = pathProbability * averageStrategyUtility;
            return (bestResponseValue, averageStrategyValue);
        }

        public double GetProbabilityOfPath()
        {
            double pathProbability = Coefficient;
            foreach (var nodeAction in NodeActions)
            {
                switch (nodeAction.Node)
                {
                    case ChanceNode c:
                        pathProbability *= c.GetActionProbability(nodeAction.ActionAtNode, nodeAction.DistributorChanceInputs);
                        break;
                    case InformationSetNode i:
                        pathProbability *= i.GetAverageStrategy(nodeAction.ActionAtNode);
                        break;
                    default: throw new NotSupportedException();
                }
            }

            return pathProbability;
        }

        public (double probabilityOfPath, double probabilitySinceOpponentInformationSet, InformationSetNode mostRecentOpponentInformationSet, byte actionAtOpponentInformationSet) GetProbabilityOfPathPlus()
        {
            bool opponentInformationSetFound = false;
            double probabilitySinceMostRecentOpponentInformationSet = 1.0;
            InformationSetNode mostRecentOpponentInformationSet = null;
            byte actionAtOpponentInformationSet = 0;
            double pathProbability = Coefficient;
            foreach (var nodeAction in NodeActions)
            {
                switch (nodeAction.Node)
                {
                    case ChanceNode c:
                        double chanceProbability = c.GetActionProbability(nodeAction.ActionAtNode, nodeAction.DistributorChanceInputs);
                        if (!opponentInformationSetFound)
                        {
                            probabilitySinceMostRecentOpponentInformationSet *= chanceProbability;
                        }
                        pathProbability *= chanceProbability;
                        break;
                    case InformationSetNode i:
                        double averageStrategy = i.GetAverageStrategy(nodeAction.ActionAtNode);
                        if (!opponentInformationSetFound)
                        {
                            opponentInformationSetFound = true;
                            mostRecentOpponentInformationSet = i;
                            probabilitySinceMostRecentOpponentInformationSet *= averageStrategy;
                            actionAtOpponentInformationSet = nodeAction.ActionAtNode;
                        }
                        pathProbability *= averageStrategy;
                        break;
                    default: throw new NotSupportedException();
                }
            }

            return (pathProbability, probabilitySinceMostRecentOpponentInformationSet, mostRecentOpponentInformationSet, actionAtOpponentInformationSet);
        }

        public double GetBestResponseUtilityAfterPathToSuccessor(byte playerIndex)
        {
            double utility;
            var successor = Successor;
            switch (successor)
            {
                case FinalUtilitiesNode f:
                    utility = f.Utilities[playerIndex];
                    break;
                case InformationSetNode i:
                    if (playerIndex != i.PlayerIndex)
                        throw new Exception();
                    utility = i.LastBestResponseValue;
                    break;
                default: throw new NotSupportedException();
            }

            return utility;
        }

        public double GetAverageStrategyUtilityAfterPathToSuccessor(byte playerIndex)
        {
            double utility;
            var successor = Successor;
            switch (successor)
            {
                case FinalUtilitiesNode f:
                    utility = f.Utilities[playerIndex];
                    break;
                case InformationSetNode i:
                    if (playerIndex != i.PlayerIndex)
                        throw new Exception();
                    utility = i.AverageStrategyResultsForPathFromPredecessor[i.NumVisitsFromPredecessorToGetAverageStrategy++]; // that is, return the average strategy result for the predecessor, on the assumption that the paths from the predecessor are being visited in order to make this request.
                    break;
                default: throw new NotSupportedException();
            }

            return utility;
        }
    }
}
