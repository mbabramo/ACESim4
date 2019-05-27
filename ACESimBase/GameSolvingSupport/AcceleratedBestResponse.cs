using System;
using System.Collections.Generic;

namespace ACESim
{
    public class AcceleratedBestResponse : ITreeNodeProcessor<bool, double>
    {
        byte CalculatingForPlayer;

        public AcceleratedBestResponse(byte calculatingForPlayer)
        {
            CalculatingForPlayer = calculatingForPlayer;
        }

        public double FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, bool fromPredecessor)
        {
            return finalUtilities.Utilities[CalculatingForPlayer];
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, bool fromPredecessor)
        {
            return true; // ignored
        }

        public double ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double> fromSuccessors)
        {
            double value = 0;
            byte a = 1;
            foreach (double utility in fromSuccessors)
            {
                value += chanceNode.GetActionProbability(a - 1) * utility;
                a++;
            }
            return value;
        }

        public bool DistributeDistributableDistributorChanceInputs(ChanceNode chanceNode) => chanceNode.Decision.ProvidesPrivateInformationFor != CalculatingForPlayer;

        public bool InformationSet_Forward(InformationSetNode informationSet, bool fromPredecessor)
        {
            if (CalculatingForPlayer == informationSet.PlayerIndex)
                informationSet.LastBestResponseAction = 0;
            return true; // ignored
        }

        public double InformationSet_Backward(InformationSetNode informationSet, IEnumerable<double> fromSuccessors)
        {
            if (informationSet.PlayerIndex == CalculatingForPlayer)
            {
                byte a = 1;
                byte bestA = 0;
                double best = 0;
                foreach (double utility in fromSuccessors)
                {
                    if (bestA == 0 || utility > best)
                    {
                        bestA = a;
                        best = utility;
                    }
                    a++;
                }
                informationSet.LastBestResponseAction = bestA;
                return best;
            }
            else
            {
                // other player's information set
                double value = 0;
                byte a = 1;
                double[] averageStrategies = informationSet.GetAverageStrategiesAsArray();
                foreach (double utility in fromSuccessors)
                {
                    value += averageStrategies[a - 1] * utility;
                    a++;
                }
                return value;
            }
        }
    }

}
