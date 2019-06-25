using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using System;

namespace ACESim
{
    [Serializable]
    public class PathFromPredecessorInfo
    {
        public ByteList ActionsList;
        public int IndexInPredecessorsPathsFromPredecessor;
        public NodeActionsHistory Path;
        public double Probability;
        public InformationSetNode MostRecentOpponentInformationSet;
        public double ProbabilityFromMostRecentOpponent;
        public byte ActionAtOpponentInformationSet;
    }
}
