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

        public NodeActionsHistory DeepClone(int skip = 0)
        {
            return new NodeActionsHistory()
            {
                NodeActions = NodeActions.Select(x => x.Clone()).Skip(skip).ToList(),
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

        public NodeActionsHistory GetIncrementalHistory(byte nonChancePlayerIndex)
        {
            int? indexOfLastInformationSetByPlayer = null;
            for (int i = NodeActions.Count - 1; i >= 0; i--)
                if (NodeActions[i].Node is InformationSetNode informationSet && informationSet.PlayerIndex == nonChancePlayerIndex)
                {
                    indexOfLastInformationSetByPlayer = i;
                    break;
                }
            return DeepClone(indexOfLastInformationSetByPlayer == null ? 0 : (int)indexOfLastInformationSetByPlayer + 1);
        }

        public NodeActionsHistory GetSubsequentHistory(int decisionIndex)
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
            return DeepClone((int)indexOfInformationSetForDecision + 1);
        }

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
