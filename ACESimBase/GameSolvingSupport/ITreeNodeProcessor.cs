using System.Collections.Generic;

namespace ACESim
{
    public interface ITreeNodeProcessor<Forward,Back>
    {
        Back FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, Forward fromPredecessor);

        Forward ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, Forward fromPredecessor, int distributorChanceInputs);
        Back ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<Back> fromSuccessors, int distributorChanceInputs);

        Forward InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, Forward fromPredecessor);
        Back InformationSet_Backward(InformationSetNode informationSet, IEnumerable<Back> fromSuccessors);
    }

}
