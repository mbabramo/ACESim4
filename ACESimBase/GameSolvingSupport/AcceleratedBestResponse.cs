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
            throw new NotSupportedException();
            // Accelerated best response is incomplete. We have figured out how to adjust our chance node probabilities so that they reflect (1) distributed chance actions; and (2) nondistributed chance actions that we are actually playing. But we have not yet made it so that our non-chance information sets reflect (1) distributed chance actions; and (2) distributed actions of the other player (e.g., DSignal or PSignal actions). 
            // The long-term cleanup for this might involve rethinking our strategy for distributing other decisions. For each information set (including chance nodes), we would need to calculate the cumulative probability that chance would play to this information set. We would have a distinct chance node for each piece of player hidden signals produced by that point in the game, even where irrelevant to actually calculating the chance node result. Then, as we currently do, for chance nodes, we would determine the corresponding chance node in which the distributed chance values were 1 (but containing the same hidden signals). However, instead of placing in this corresponding chance node a dictionary, we would have a list of chance nodes along with the other side probabilities. In addition, for chance nodes, we would determine, to distribute chance nodes across players' hidden signals (as in this algorithm), the corresponding chance node in which the distributed chance values were 1 AND the hidden signals of the player being distributed (i.e., not the player being optimized) were 1. Again, we would store a list of these, along with reach probabilities, in those nodes.
            // Finally, for information sets, we would determine the corresponding information set if the distributed chance values were 1 AND that player's hidden signals were 1. We would then store links to all of these information sets in this corresponding information set, again with probabilities. 
            // With this approach, we don't need to pass forward the nondistributed actions. We just need to know what mode we are in -- i.e., whether we are distributing chance nodes and whether we are distributing any player's hidden signals.
        }

        public double FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, bool fromPredecessor)
        {
            double result = finalUtilities.Utilities[CalculatingForPlayer];
            TabbedText.WriteLine(result);
            return result;
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, bool fromPredecessor, DistributorChanceInputs distributorChanceInputs)
        {
            return true; // ignored
        }

        public double ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double> fromSuccessors, DistributorChanceInputs distributorChanceInputs)
        {
            double value = 0;
            byte a = 1;
            double probabilitiesTotal = 0;
            foreach (double utility in fromSuccessors)
            {
                double probability = chanceNode.GetActionProbability(a, distributorChanceInputs);
                probabilitiesTotal += probability;
                TabbedText.WriteLine($"chance {chanceNode.Decision.Name} action {a} value {utility} probability {probability}");
                value += probability * utility;
                a++;
            }
            value /= probabilitiesTotal;
            TabbedText.WriteLine($"chance {chanceNode.Decision.Name} overall value {value}");
            return value;
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, bool fromPredecessor)
        {
            TabbedText.WriteLine($"{informationSet.Decision.Name}...");
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
                TabbedText.WriteLine($"Setting best response for {informationSet.Decision.Name} to {bestA} => {best}");
                return best;
            }
            else
            {
                // other player's information set
                double value = 0;
                byte a = 1;
                double[] averageStrategies = informationSet.GetAverageStrategiesAsArray();
                double probabilitiesTotal = 0;
                foreach (double utility in fromSuccessors)
                {
                    double probability = averageStrategies[a - 1];
                    probabilitiesTotal += probability;
                    TabbedText.WriteLine($"Opponent's {informationSet.Decision.Name} action {a} probability {probability} own utility {utility}");
                    value += probability * utility;
                    a++;
                }
                value /= probabilitiesTotal; // if this is a decision where we're distributing actions, we need to adjust for that
                TabbedText.WriteLine($"Opponent's {informationSet.Decision.Name} returning best response of {value}");
                return value;
            }
        }
    }

}
