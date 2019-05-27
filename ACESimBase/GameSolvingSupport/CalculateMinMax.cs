using System;
using System.Collections.Generic;

namespace ACESim
{
    public class CalculateMinMax : ITreeNodeProcessor<bool, double[]>
    {
        public bool Max;
        public bool Min => !Max;

        Dictionary<int, double[]> ChanceNodePassback = new Dictionary<int, double[]>();
        int NumNonChancePlayers;

        public CalculateMinMax(bool max, int numNonChancePlayers)
        {
            Max = max;
            NumNonChancePlayers = numNonChancePlayers;
        }

        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, bool fromPredecessor)
        {
            return finalUtilities.Utilities;
        }

        void ProcessSuccessors(double[] valuesToUpdate, IEnumerable<double[]> fromSuccessors)
        {
            foreach (var fromSuccessor in fromSuccessors)
                for (int i = 0; i < NumNonChancePlayers; i++)
                    if ((Max && fromSuccessor[i] > valuesToUpdate[i]) || (Min && fromSuccessor[i] < valuesToUpdate[i]))
                        valuesToUpdate[i] = fromSuccessor[i];
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, bool fromPredecessor)
        {
            double[] d = new double[NumNonChancePlayers];
            for (int i = 0; i < NumNonChancePlayers; i++)
                d[i] = Max ? int.MinValue : int.MaxValue; // initialize to value that will be replaced
            ChanceNodePassback[chanceNode.ChanceNodeNumber] = d;
            return true; // ignored
        }

        public double[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double[]> fromSuccessors)
        {
            var d = ChanceNodePassback[chanceNode.ChanceNodeNumber];
            ProcessSuccessors(d, fromSuccessors);
            return d;
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, bool fromPredecessor)
        {
            if (Min)
            {
                informationSet.MinPossible = new double[NumNonChancePlayers];
                for (int i = 0; i < NumNonChancePlayers; i++)
                    informationSet.MinPossible[i] = int.MaxValue; // initialize to value that will be replaced
            }
            else
            {
                informationSet.MaxPossible = new double[NumNonChancePlayers];
                for (int i = 0; i < NumNonChancePlayers; i++)
                    informationSet.MaxPossible[i] = int.MinValue; // initialize to value that will be replaced
            }
            return true; // ignored
        }

        public double[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<double[]> fromSuccessors)
        {
            if (Min)
            {
                ProcessSuccessors(informationSet.MinPossible, fromSuccessors);
                informationSet.MinPossibleThisPlayer = informationSet.MinPossible[informationSet.PlayerIndex];
                return informationSet.MinPossible;
            }
            else
            {
                ProcessSuccessors(informationSet.MaxPossible, fromSuccessors);
                informationSet.MaxPossibleThisPlayer = informationSet.MaxPossible[informationSet.PlayerIndex];
                return informationSet.MaxPossible;
            }
        }
    }

}
