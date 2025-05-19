using ACESimBase.Util.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public class UtilitiesAndCustomResultTreeCalculation : ITreeNodeProcessor<bool, (double[] utilities, FloatSet customResult)>
    {
        bool DistributingChanceActions;
        bool UseCurrentStrategy;

        public UtilitiesAndCustomResultTreeCalculation(bool distributingChanceActions, bool useCurrentStrategy)
        {
            DistributingChanceActions = distributingChanceActions;
            UseCurrentStrategy = useCurrentStrategy;
        }

        public (double[] utilities, FloatSet customResult) ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<(double[] utilities, FloatSet customResult)> fromSuccessors, int distributorChanceInputs)
        {
            if (DistributingChanceActions && chanceNode.Decision.DistributedChanceDecision)
                return fromSuccessors.Single();
            int numPlayers = fromSuccessors.First().utilities.Length;
            double[] utilitiesToPassBack = new double[numPlayers];
            List<(double[] utilities, FloatSet customResult)> fromSuccessorsList = fromSuccessors.ToList();
            if (fromSuccessorsList.Count() != chanceNode.Decision.NumPossibleActions)
                throw new Exception();
            FloatSet customResultToPassBack = default;
            for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
            {
                double probability = chanceNode.GetActionProbability(a, distributorChanceInputs);
                customResultToPassBack = customResultToPassBack.Plus(fromSuccessorsList[a - 1].customResult.Times((float)probability));
                for (int playerIndex = 0; playerIndex < numPlayers; playerIndex++)
                    utilitiesToPassBack[playerIndex] += probability * fromSuccessorsList[a - 1].utilities[playerIndex];
            }
            return (utilitiesToPassBack, customResultToPassBack);
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor, int distributorChanceInputs)
        {
            return true; // ignored
        }

        public (double[] utilities, FloatSet customResult) FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            return (finalUtilities.Utilities, finalUtilities.CustomResult);
        }

        public (double[] utilities, FloatSet customResult) InformationSet_Backward(InformationSetNode informationSet, IEnumerable<(double[] utilities, FloatSet customResult)> fromSuccessors)
        {
            int numPlayers = fromSuccessors.First().utilities.Length;
            double[] utilitiesToPassBack = new double[numPlayers];
            FloatSet customResultToPassBack = default;
            List<(double[] utilities, FloatSet customResult)> fromSuccessorsList = fromSuccessors.ToList();
            if (fromSuccessorsList.Count() != informationSet.Decision.NumPossibleActions)
                throw new Exception();
            for (byte a = 1; a <= informationSet.Decision.NumPossibleActions; a++)
            {
                double probability = UseCurrentStrategy ? informationSet.GetCurrentProbability(a, false) : informationSet.GetAverageStrategy(a);
                customResultToPassBack = customResultToPassBack.Plus(fromSuccessorsList[a - 1].customResult.Times((float)probability));
                for (int playerIndex = 0; playerIndex < numPlayers; playerIndex++)
                    utilitiesToPassBack[playerIndex] += probability * fromSuccessorsList[a - 1].utilities[playerIndex];
            }
            return (utilitiesToPassBack, customResultToPassBack);
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            return true;
        }
    }
}
