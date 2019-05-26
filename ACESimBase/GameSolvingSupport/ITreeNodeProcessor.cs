using System.Collections.Generic;

namespace ACESim
{
    public interface ITreeNodeProcessor<Forward,Back>
    {
        void FinalUtilities_ReceiveFromPredecessor(FinalUtilitiesNode finalUtilities, Forward fromPredecessor);
        Back FinalUtilities_SendToPredecessor(FinalUtilitiesNode finalUtilities);

        void ChanceNode_ReceiveFromPredecessor(ChanceNode chanceNode, Forward fromPredecessor);
        Forward ChanceNode_SendToSuccessors(ChanceNode chanceNode);
        void ChanceNode_ReceiveFromSuccessors(ChanceNode chanceNode, IEnumerable<Back> fromSuccessors);
        Back ChanceNode_SendToPredecessor(ChanceNode chanceNode);


        void InformationSet_ReceiveFromPredecessor(InformationSetNode informationSet, Forward fromPredecessor);
        Forward InformationSet_SendToSuccessors(InformationSetNode informationSet);
        void InformationSet_ReceiveFromSuccessors(InformationSetNode informationSet, IEnumerable<Back> fromSuccessors);
        Back InformationSet_SendToPredecessor(InformationSetNode informationSet);
    }

}
