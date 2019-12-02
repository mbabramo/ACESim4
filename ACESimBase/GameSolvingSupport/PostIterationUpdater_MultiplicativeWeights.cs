using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    [Serializable]
    public class PostIterationUpdater_MultiplicativeWeights : PostIterationUpdaterBase
    {
        double MultiplicativeWeightsEpsilon;

        public override void PrepareForUpdating(int iteration, EvolutionSettings evolutionSettings)
        {
            MultiplicativeWeightsEpsilon = evolutionSettings.MultiplicativeWeightsEpsilon(iteration, evolutionSettings.TotalIterations);

        }

        public override void UpdateInformationSet(InformationSetNode node, bool weightResultByInversePiForIteration)
        {
            // normalize regrets to costs between 0 and 1. the key assumption is that each iteration takes into account ALL possible outcomes (as in a vanilla hedge CFR algorithm)
            double sumWeights = 0;
            double[,] nodeInformation = node.NodeInformation;
            int numPossibleActions = node.NumPossibleActions;
            for (int a = 1; a <= numPossibleActions; a++)
            {
                double normalizedRegret = GetNormalizedRegret(node, a, weightResultByInversePiForIteration, true);
                double adjustedNormalizedRegret = 1.0 - normalizedRegret; // if regret is high (good move), this is low; bad moves are now close to 1 and good moves are close to 0
                double weightAdjustment = Math.Pow(1 - MultiplicativeWeightsEpsilon, adjustedNormalizedRegret); // if there is a good move, then this is high (relatively close to 1). For example, suppose MultiplicativeWeightsEpsilon is 0.5. Then, if adjustedNormalizedRegret is 0.9 (bad move), the weight adjustment is 0.536, but if adjustedNormalizedRegret is 0.1 (good move), the weight adjustment is only 0.933, so the bad move is discounted relative to the good move by 0.536/0.933. if MultiplicativeWeightsEpsilon is 0.1, then the weight adjustments are 0.98 and 0.90; i.e., the algorithm is much less greedy (because 1 - MultiplicativeWeightsEpsilon is relatively lose to 1). if MultiplicativeWeightsEpsilon is 0.9, the algorithm is much more greedy.
                double weight = nodeInformation[InformationSetNode.adjustedWeightsDimension, a - 1];
                weight *= weightAdjustment; // So, this weight reduces only slightly when regret is high
                if (double.IsNaN(weight) || double.IsInfinity(weight))
                    throw new Exception();
                nodeInformation[InformationSetNode.adjustedWeightsDimension, a - 1] = weight;
                sumWeights += weight;
                // DEBUG
                nodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, a - 1] = 0; // reset for next iteration
                nodeInformation[InformationSetNode.sumInversePiDimension, a - 1] = 0;
            }
            if (sumWeights < 1E-20)
            { // increase all weights to avoid all weights being round off to zero -- since this affects only relative probabilities at the information set, this won't matter
                for (int a = 1; a <= numPossibleActions; a++)
                {
                    nodeInformation[InformationSetNode.adjustedWeightsDimension, a - 1] *= 1E+15;
                }
                sumWeights *= 1E+15;
            }

            // Finally, calculate the updated probabilities, plus the average strategies
            // We set each item to its proportion of the weights, but no less than SmallestProbabilityRepresented. 
            Func<byte, double> unadjustedProbabilityFunc = a => nodeInformation[InformationSetNode.adjustedWeightsDimension, a - 1] / sumWeights;
            node.SetProbabilitiesFromFunc(InformationSetNode.currentProbabilityDimension, InformationSetNode.SmallestProbabilityRepresented, false, false, unadjustedProbabilityFunc);
        }
    }
}
