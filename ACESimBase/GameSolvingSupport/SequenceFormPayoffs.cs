using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{

    public class SequenceFormPayoffs : ITreeNodeProcessor<SequenceFormPayoffsTreeInfo, bool /* ignored */>
    {
        public double[,] A, B, ChanceProbabilitySums;

        public SequenceFormPayoffs(int rowPlayerCumulativeChoices, int colPlayerCumulativeChoices)
        {
            A = new double[rowPlayerCumulativeChoices + 1, colPlayerCumulativeChoices + 1];
            B = new double[rowPlayerCumulativeChoices + 1, colPlayerCumulativeChoices + 1];
            ChanceProbabilitySums = new double[rowPlayerCumulativeChoices + 1, colPlayerCumulativeChoices + 1];
        }

        public void FinalizeMatrices()
        {
            int rowStrategies = A.GetLength(0);
            int colStrategies = B.GetLength(1);
            for (int r = 0; r < rowStrategies; r++)
            {
                for (int c = 0; c < colStrategies; c++)
                {
                    double sumOfChanceProbabilityWeights = ChanceProbabilitySums[r, c];
                    if (sumOfChanceProbabilityWeights > 0)
                    {
                        A[r, c] /= sumOfChanceProbabilityWeights;
                        B[r, c] /= sumOfChanceProbabilityWeights;
                    }
                }
            }
        }

        public SequenceFormPayoffsTreeInfo ProcessPredecessor(IGameState predecessor, byte predecessorAction, SequenceFormPayoffsTreeInfo fromPredecessor)
        {
            if (predecessor is ChanceNode chanceNode)
            {
                double probability = chanceNode.GetActionProbability(predecessorAction);
                return fromPredecessor.WithChanceProbabilityMultiplied(probability);
            }
            else if (predecessor is InformationSetNode informationSet)
            {
                int cumulativeChoice = informationSet.CumulativeChoiceNumber + predecessorAction;
                return fromPredecessor.WithCumulativeChoice(informationSet.PlayerIndex == 0, cumulativeChoice); 
            }
            return fromPredecessor;
        }

        public bool ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<bool> fromSuccessors, int distributorChanceInputs)
        {
            return true; // ignored
        }

        public SequenceFormPayoffsTreeInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, SequenceFormPayoffsTreeInfo fromPredecessor, int distributorChanceInputs)
        {
            return ProcessPredecessor(predecessor, predecessorAction, fromPredecessor);
        }

        public bool FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, SequenceFormPayoffsTreeInfo fromPredecessor)
        {
            var finalSequenceInfo = ProcessPredecessor(predecessor, predecessorAction, fromPredecessor);
            A[finalSequenceInfo.RowPlayerCumulativeChoice, finalSequenceInfo.ColPlayerCumulativeChoice] += finalUtilities.Utilities[0] * finalSequenceInfo.ChanceProbability;
            B[finalSequenceInfo.RowPlayerCumulativeChoice, finalSequenceInfo.ColPlayerCumulativeChoice] += finalUtilities.Utilities[1] * finalSequenceInfo.ChanceProbability;
            ChanceProbabilitySums[finalSequenceInfo.RowPlayerCumulativeChoice, finalSequenceInfo.ColPlayerCumulativeChoice] += finalSequenceInfo.ChanceProbability;
            return true; // ignored
        }

        public bool InformationSet_Backward(InformationSetNode informationSet, IEnumerable<bool> fromSuccessors)
        {
            return true; // ignored
        }

        public SequenceFormPayoffsTreeInfo InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, SequenceFormPayoffsTreeInfo fromPredecessor)
        {
            return ProcessPredecessor(predecessor, predecessorAction, fromPredecessor);
        }
    }

}
