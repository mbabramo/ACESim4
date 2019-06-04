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
        public List<NodeAction> NodeActions = new List<NodeAction>();
        public IGameState SuccessorInformationSet;

        public NodeActionsHistory()
        {

        }

        public NodeActionsHistory DeepClone(int skip = 0, int take = int.MaxValue)
        {
            return new NodeActionsHistory()
            {
                NodeActions = NodeActions.Select(x => x.Clone()).Skip(skip).Take(take).ToList(),
                SuccessorInformationSet = SuccessorInformationSet
            };
        }

        public override bool Equals(object obj)
        {
            NodeActionsHistory other = (NodeActionsHistory)obj;
            return other.NodeActions.SequenceEqual(NodeActions) && other.SuccessorInformationSet == SuccessorInformationSet;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NodeActions, SuccessorInformationSet);
        }

        public NodeActionsHistory WithAppended(IGameState node, byte actionAtNode, int distributorChanceInputs = -1)
        {
            var copied = NodeActions.ToList();
            copied.Add(new NodeAction(node, actionAtNode, distributorChanceInputs));
            return new NodeActionsHistory()
            {
                NodeActions = copied,
                SuccessorInformationSet = null
            };
        }

        public NodeActionsHistory WithFinalUtilitiesAppended(FinalUtilitiesNode node)
        {

            var copied = NodeActions.ToList();
            return new NodeActionsHistory()
            {
                NodeActions = copied,
                SuccessorInformationSet = node
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
            return DeepClone(indexOfLastInformationSetByPlayer == null ? 0 : (int)indexOfLastInformationSetByPlayer + 1);
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
        /// Returns another NodeActionsHistory object with all actions after the specified decision.
        /// </summary>
        /// <param name="decisionIndex"></param>
        /// <returns></returns>
        public NodeActionsHistory GetSubsequentHistory(int decisionIndex)
        {
            int? indexOfInformationSetForDecision = GetIndexOfInformationSetForDecision(decisionIndex);
            return DeepClone((int)indexOfInformationSetForDecision + 1);
        }

        /// <summary>
        /// Returns another NodeActionsHistory object with all actions after the specified decision but only to a successor information set (i.e., final utilities or another information set node for the same non-chance player), which will then be included as the successor information set.
        /// </summary>
        /// <param name="decisionIndex"></param>
        /// <param name="nonChancePlayerIndex"></param>
        /// <returns></returns>
        public NodeActionsHistory GetSubsequentHistoryToSuccessor(int decisionIndex, byte nonChancePlayerIndex)
        {
            int? indexOfInformationSetForDecision = GetIndexOfInformationSetForDecision(decisionIndex);
            int? indexOfSuccessor = GetIndexOfLastInformationSetByPlayer(nonChancePlayerIndex);
            if (indexOfSuccessor == null || indexOfSuccessor <= indexOfInformationSetForDecision)
            {
                if (SuccessorInformationSet == null)
                    throw new Exception("No successor found.");
                return DeepClone((int)indexOfInformationSetForDecision + 1); // including successor information set
            }
            // we want to skip the information set at indexOfInformationSetForDecision as well as the information set at indexOfSuccessor.
            var result = DeepClone((int)indexOfInformationSetForDecision + 1, (int) indexOfSuccessor - (int) indexOfInformationSetForDecision - 1);
            result.SuccessorInformationSet = NodeActions[(int)indexOfSuccessor].Node;
            return result;
        }

        private int? GetIndexOfInformationSetForDecision(int decisionIndex)
        {
            int? indexOfInformationSetForDecision = null;
            for (int i = NodeActions.Count - 1; i >= 0; i--)
                if (NodeActions[i].Node is InformationSetNode informationSet && informationSet.DecisionIndex == decisionIndex)
                {
                    indexOfInformationSetForDecision = i;
                    break;
                }
            if (indexOfInformationSetForDecision == null)
                throw new ArgumentException("Expected decision not found; can't get subsequent history.");
            return indexOfInformationSetForDecision;
        }

        /// <summary>
        /// Returns a list of actions, excluding a specified player as well as distributed chance decisions and distributor chance input decisions, if chance decisions are being distributed. The decisions included are thus all those that may differentiate the action taken at the current information set.
        /// </summary>
        /// <param name="excludePlayerIndex"></param>
        /// <param name="distributingChanceDecisions"></param>
        /// <returns></returns>
        public ByteList GetActionsList(byte? excludePlayerIndex, bool distributingChanceDecisions)
        {
            return new ByteList(NodeActions
                .Where(x => excludePlayerIndex == null || !(x.Node is InformationSetNode informationSet) || informationSet.PlayerIndex != excludePlayerIndex)
                .Where(x => !(x.Node is ChanceNode chanceNode) || 
                        (!(chanceNode.Decision.DistributedChanceDecision) && !(chanceNode.Decision.DistributorChanceInputDecision))
                        || !distributingChanceDecisions)
                .Select(x => x.ActionAtNode)
                );
        }
    }
}
