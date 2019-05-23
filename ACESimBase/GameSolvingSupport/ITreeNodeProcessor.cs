using System.Collections.Generic;

namespace ACESim
{
    public interface ITreeNodeProcessor<Forward,Back>
    {
        void FinalUtilities_ReceiveFromPredecessor(FinalUtilities finalUtilities, Forward fromPredecessor);
        Back FinalUtilities_SendToPredecessor(FinalUtilities finalUtilities);

        void ChanceNode_ReceiveFromPredecessor(ChanceNodeSettings chanceNodeSettings, Forward fromPredecessor);
        Forward ChanceNode_SendToSuccessors(ChanceNodeSettings chanceNodeSettings);
        void ChanceNode_ReceiveFromSuccessors(ChanceNodeSettings chanceNodeSettings, IEnumerable<Back> fromSuccessors);
        Back ChanceNode_SendToPredecessor(ChanceNodeSettings chanceNodeSettings);


        void InformationSet_ReceiveFromPredecessor(InformationSetNodeTally informationSet, Forward fromPredecessor);
        Forward InformationSet_SendToSuccessors(InformationSetNodeTally informationSet);
        void InformationSet_ReceiveFromSuccessors(InformationSetNodeTally informationSet, IEnumerable<Back> fromSuccessors);
        Back InformationSet_SendToPredecessor(InformationSetNodeTally informationSet);
    }

}
