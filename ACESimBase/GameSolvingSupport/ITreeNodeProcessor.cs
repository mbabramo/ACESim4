using System.Collections.Generic;

namespace ACESim
{
    public interface ITreeNodeProcessor<Forward,Back>
    {
        void FinalUtilities_ReceiveFromPredecessor(FinalUtilities finalUtilities, Forward fromPredecessor);
        Back FinalUtilities_SendToPredecessor(FinalUtilities finalUtilities);

        void ChanceNode_ReceiveFromPredecessor(ChanceNode chanceNode, Forward fromPredecessor);
        Forward ChanceNode_SendToSuccessors(ChanceNode chanceNode);
        void ChanceNode_ReceiveFromSuccessors(ChanceNode chanceNode, IEnumerable<Back> fromSuccessors);
        Back ChanceNode_SendToPredecessor(ChanceNode chanceNode);


        void InformationSet_ReceiveFromPredecessor(InformationSetNodeTally informationSet, Forward fromPredecessor);
        Forward InformationSet_SendToSuccessors(InformationSetNodeTally informationSet);
        void InformationSet_ReceiveFromSuccessors(InformationSetNodeTally informationSet, IEnumerable<Back> fromSuccessors);
        Back InformationSet_SendToPredecessor(InformationSetNodeTally informationSet);
    }

}
