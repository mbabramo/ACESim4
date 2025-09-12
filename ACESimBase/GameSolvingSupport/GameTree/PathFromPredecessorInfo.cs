using ACESimBase.Util.Collections;
using System;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    [Serializable]
    public class PathFromPredecessorInfo
    {
        public ByteList ActionsListExcludingPlayer;
        public int IndexInPredecessorsPathsFromPredecessor;
        public NodeActionsHistory Path;
        public double Probability;
        public InformationSetNode MostRecentOpponentInformationSet;
        public double ProbabilityFromMostRecentOpponent;
        public byte ActionAtOpponentInformationSet;

        public override string ToString()
        {
            return $"Path {IndexInPredecessorsPathsFromPredecessor} {Path} probability {Probability} actions excluding player {string.Join(",", ActionsListExcludingPlayer)} (most recent opponent info set {MostRecentOpponentInformationSet?.InformationSetNodeNumber} => action {ActionAtOpponentInformationSet} probability {ProbabilityFromMostRecentOpponent})";
        }
    }
}
