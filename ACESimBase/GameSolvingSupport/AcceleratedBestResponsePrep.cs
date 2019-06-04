using System;
using System.Collections.Generic;
using System.Text;
using ACESim;

namespace ACESimBase.GameSolvingSupport
{
    public class AcceleratedBestResponsePrep : ITreeNodeProcessor<NodeActionsHistory, NodeActionsHistory>
    {


        public NodeActionsHistory InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor)
        {
            NodeActions
        }

        public NodeActionsHistory InformationSet_Backward(InformationSetNode informationSet, IEnumerable<NodeActionsHistory> fromSuccessors)
        {
            throw new NotImplementedException();
        }

        public NodeActionsHistory ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<NodeActionsHistory> fromSuccessors, int distributorChanceInputs)
        {
            throw new NotImplementedException();
        }

        public NodeActionsHistory ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor, int distributorChanceInputs)
        {
            throw new NotImplementedException();
        }

        public NodeActionsHistory FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor)
        {
            throw new NotImplementedException();
        }
    }
}
