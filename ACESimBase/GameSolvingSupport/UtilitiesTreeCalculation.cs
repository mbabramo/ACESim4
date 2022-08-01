using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class UtilitiesTreeCalculation : ITreeNodeProcessor<bool, double[]>
    {
        bool UseCurrentStrategy;

        public UtilitiesTreeCalculation(bool useCurrentStrategy)
        {
            UseCurrentStrategy = useCurrentStrategy;
        }

        public double[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double[]> fromSuccessors, int distributorChanceInputs)
        {
            int numPlayers = fromSuccessors.First().Length;
            double[] utilitiesToPassBack = new double[numPlayers];
            List<double[]> fromSuccessorsList = fromSuccessors.ToList();
            if (fromSuccessorsList.Count() != chanceNode.Decision.NumPossibleActions)
                throw new Exception();
            for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
            {
                double probability = chanceNode.GetActionProbability(a, distributorChanceInputs);
                for (int playerIndex = 0; playerIndex < numPlayers; playerIndex++)
                    utilitiesToPassBack[playerIndex] += probability * fromSuccessorsList[a - 1][playerIndex];
            }
            return utilitiesToPassBack;
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor, int distributorChanceInputs)
        {
            return true; // ignored
        }

        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            return finalUtilities.Utilities;
        }

        public double[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<double[]> fromSuccessors)
        {
            int numPlayers = fromSuccessors.First().Length;
            double[] utilitiesToPassBack = new double[numPlayers];
            List<double[]> fromSuccessorsList = fromSuccessors.ToList();
            if (fromSuccessorsList.Count() != informationSet.Decision.NumPossibleActions)
                throw new Exception();
            for (byte a = 1; a <= informationSet.Decision.NumPossibleActions; a++)
            {
                double probability = UseCurrentStrategy ? informationSet.GetCurrentProbability(a, false) : informationSet.GetAverageStrategy(a);
                for (int playerIndex = 0; playerIndex < numPlayers; playerIndex++)
                    utilitiesToPassBack[playerIndex] += probability * fromSuccessorsList[a - 1][playerIndex];
            }
            return utilitiesToPassBack;
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            return true;
        }
    }
}
