using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public class CalculateMinMax : ITreeNodeProcessor<bool, double[]>
    {
        public bool Max;

        public bool Min => !Max;
        public HashSet<int> InitializedInformationSets = new HashSet<int>();

        Dictionary<int, double[]> ChanceNodePassback = new Dictionary<int, double[]>();

        int NumNonChancePlayers;

        /// <summary>
        /// If null, the min-max is calculated for all players at once. Otherwise, the min-max is calculated only for the specified player (and the other player's hidden information is distributed, thus saving time).
        /// </summary>
        byte? CalculatingForPlayer;

        IEnumerable<byte> CalculatingForPlayers => CalculatingForPlayer == null ? Enumerable.Range(0, NumNonChancePlayers).Select(x => (byte)x) : new byte[] { (byte)CalculatingForPlayer };

        public CalculateMinMax(bool max, int numNonChancePlayers, byte? calculatingForPlayer)
        {
            Max = max;
            NumNonChancePlayers = numNonChancePlayers;
            CalculatingForPlayer = calculatingForPlayer;
        }

        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            return finalUtilities.Utilities;
        }

        void ProcessSuccessors(double[] valuesToUpdate, IEnumerable<double[]> fromSuccessors)
        {
            foreach (var fromSuccessor in fromSuccessors)
                foreach (int i in CalculatingForPlayers)
                    if (Max && fromSuccessor[i] > valuesToUpdate[i] || Min && fromSuccessor[i] < valuesToUpdate[i])
                        valuesToUpdate[i] = fromSuccessor[i];
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor, int distributorChanceInputs)
        {
            double[] d = new double[NumNonChancePlayers];
            foreach (int i in CalculatingForPlayers)
                d[i] = Max ? int.MinValue : int.MaxValue; // initialize to value that will be replaced
            ChanceNodePassback[chanceNode.ChanceNodeNumber] = d;
            return true; // ignored
        }

        public double[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double[]> fromSuccessors, int distributorChanceInputs)
        {
            var d = ChanceNodePassback[chanceNode.ChanceNodeNumber];
            ProcessSuccessors(d, fromSuccessors);
            return d;
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            if (InitializedInformationSets.Contains(informationSet.InformationSetNodeNumber))
                return true;
            InitializedInformationSets.Add(informationSet.InformationSetNodeNumber);

            if (Min)
            {
                informationSet.MinPossible = new double[NumNonChancePlayers];
                foreach (int i in CalculatingForPlayers)
                    informationSet.MinPossible[i] = int.MaxValue; // initialize to value that will be replaced
            }
            else
            {
                informationSet.MaxPossible = new double[NumNonChancePlayers];
                foreach (int i in CalculatingForPlayers)
                    informationSet.MaxPossible[i] = int.MinValue; // initialize to value that will be replaced
            }
            return true; // ignored
        }

        public double[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<double[]> fromSuccessors)
        {
            if (Min)
            {
                ProcessSuccessors(informationSet.MinPossible, fromSuccessors);
                if (CalculatingForPlayer == null || CalculatingForPlayer == informationSet.PlayerIndex)
                {
                    informationSet.MinPossibleThisPlayer = informationSet.MinPossible[informationSet.PlayerIndex];
                }
                return informationSet.MinPossible;
            }
            else
            {
                ProcessSuccessors(informationSet.MaxPossible, fromSuccessors);
                if (CalculatingForPlayer == null || CalculatingForPlayer == informationSet.PlayerIndex)
                {
                    informationSet.MaxPossibleThisPlayer = informationSet.MaxPossible[informationSet.PlayerIndex];
                    if (informationSet.MinPossibleThisPlayer == informationSet.MaxPossibleThisPlayer)
                        throw new Exception("Utility is invariant at information set"); // could be a zero-sum game (e.g., if costs are 0 in litigation game) where player is full taking into account opponent's utility
                }
                return informationSet.MaxPossible;
            }
        }
    }

}
