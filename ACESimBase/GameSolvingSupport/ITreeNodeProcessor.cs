using System.Collections.Generic;

namespace ACESim
{
    public interface ITreeNodeProcessor<Forward,Back>
    {
        Back FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, Forward fromPredecessor);

        Forward ChanceNode_Forward(ChanceNode chanceNode, Forward fromPredecessor, int distributorChanceInputs);
        Back ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<Back> fromSuccessors, int distributorChanceInputs);

        Forward InformationSet_Forward(InformationSetNode informationSet, Forward fromPredecessor);
        Back InformationSet_Backward(InformationSetNode informationSet, IEnumerable<Back> fromSuccessors);
    }

}
