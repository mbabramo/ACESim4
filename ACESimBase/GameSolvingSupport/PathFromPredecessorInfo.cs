using ACESimBase.GameSolvingSupport;
using ACESimBase.Util.Collections;
using System;

namespace ACESim
{
    [Serializable]
    public class PathFromPredecessorInfo
    {
        public ByteList ActionsListExcludingPlayerAndDistributedChance;
        public int IndexInPredecessorsPathsFromPredecessor;
        public NodeActionsHistory Path;
        public double Probability;
        public InformationSetNode MostRecentOpponentInformationSet;
        public double ProbabilityFromMostRecentOpponent;
        public byte ActionAtOpponentInformationSet;

        public override string ToString()
        {
            return $"Path {IndexInPredecessorsPathsFromPredecessor} {Path} probability {Probability} actions excluding player {String.Join(",", ActionsListExcludingPlayerAndDistributedChance)} (most recent opponent info set {MostRecentOpponentInformationSet?.InformationSetNodeNumber} => action {ActionAtOpponentInformationSet} probability {ProbabilityFromMostRecentOpponent})";
        }
    }
}
