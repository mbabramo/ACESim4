using ACESimBase.Util.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESim
{

    public class SequenceFormPayoffs : ITreeNodeProcessor<SequenceFormPayoffsTreeInfo, bool /* ignored */>
    {
        public double[,] A, B, ChanceProbabilitySums;

        public SequenceFormPayoffs(int rowPlayerStrategies, int colPlayerStrategies)
        {
            A = new double[rowPlayerStrategies, colPlayerStrategies];
            B = new double[rowPlayerStrategies, colPlayerStrategies];
            ChanceProbabilitySums = new double[rowPlayerStrategies, colPlayerStrategies];
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
            A.MakeNegative(true);
            B.MakeNegative(true);
        }

        public SequenceFormPayoffsTreeInfo ProcessPredecessor(IGameState predecessor, byte predecessorAction, SequenceFormPayoffsTreeInfo fromPredecessor, int distributorChanceInputs = -1)
        {
            if (predecessor is ChanceNode chanceNode)
            {
                if (chanceNode.Decision.DistributedChanceDecision)
                    return fromPredecessor;
                double probability = chanceNode.GetActionProbability(predecessorAction, distributorChanceInputs);
                var returnVal = fromPredecessor?.WithChanceProbabilityMultiplied(probability) ?? new SequenceFormPayoffsTreeInfo(probability, 0, 0);
                return returnVal;
            }
            else if (predecessor is InformationSetNode informationSet)
            {
                int cumulativeChoice = informationSet.CumulativeChoiceNumber + predecessorAction;
                return (fromPredecessor ?? new SequenceFormPayoffsTreeInfo()).WithCumulativeChoice(informationSet.PlayerIndex == 0, cumulativeChoice); 
            }
            return fromPredecessor;
        }

        public bool ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<bool> fromSuccessors, int distributorChanceInputs)
        {
            return true; // ignored
        }

        public SequenceFormPayoffsTreeInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, SequenceFormPayoffsTreeInfo fromPredecessor, int distributorChanceInputs)
        {
            return ProcessPredecessor(predecessor, predecessorAction, fromPredecessor, predecessorDistributorChanceInputs);
        }

        public bool FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, SequenceFormPayoffsTreeInfo fromPredecessor)
        {
            var finalSequenceInfo = ProcessPredecessor(predecessor, predecessorAction, fromPredecessor);
            double incrementA = finalUtilities.Utilities[0] * finalSequenceInfo.ChanceProbability;
            A[finalSequenceInfo.RowPlayerCumulativeChoice, finalSequenceInfo.ColPlayerCumulativeChoice] += incrementA;
            double incrementB = finalUtilities.Utilities[1] * finalSequenceInfo.ChanceProbability;
            B[finalSequenceInfo.RowPlayerCumulativeChoice, finalSequenceInfo.ColPlayerCumulativeChoice] += incrementB;
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
