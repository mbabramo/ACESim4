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

        public void FinalUtilities_ReceiveFromPredecessor(FinalUtilities finalUtilities, bool fromPredecessor)
        {
            // ignore
        }

        public double[] FinalUtilities_SendToPredecessor(FinalUtilities finalUtilities)
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

        public void ChanceNode_ReceiveFromPredecessor(ChanceNodeSettings chanceNodeSettings, bool fromPredecessor)
        {
            double[] d = new double[NumNonChancePlayers];
            for (int i = 0; i < NumNonChancePlayers; i++)
                d[i] = Max ? int.MinValue : int.MaxValue; // initialize to value that will be replaced
            ChanceNodePassback[chanceNodeSettings.ChanceNodeNumber] = d;
        }

        public bool ChanceNode_SendToSuccessors(ChanceNodeSettings chanceNodeSettings)
        {
            return true; // ignored
        }

        public void ChanceNode_ReceiveFromSuccessors(ChanceNodeSettings chanceNodeSettings, IEnumerable<double[]> fromSuccessors)
        {
            var d = ChanceNodePassback[chanceNodeSettings.ChanceNodeNumber];
            ProcessSuccessors(d, fromSuccessors);
        }

        public double[] ChanceNode_SendToPredecessor(ChanceNodeSettings chanceNodeSettings)
        {
            return ChanceNodePassback[chanceNodeSettings.ChanceNodeNumber];
        }

        public void InformationSet_ReceiveFromPredecessor(InformationSetNodeTally informationSet, bool fromPredecessor)
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
        }

        public bool InformationSet_SendToSuccessors(InformationSetNodeTally informationSet)
        {
            return true; // ignored
        }

        public void InformationSet_ReceiveFromSuccessors(InformationSetNodeTally informationSet, IEnumerable<double[]> fromSuccessors)
        {
            if (Min)
            {
                ProcessSuccessors(informationSet.MinPossible, fromSuccessors);
                informationSet.MinPossibleThisPlayer = informationSet.MinPossible[informationSet.PlayerIndex];
            }
            else
            {
                ProcessSuccessors(informationSet.MaxPossible, fromSuccessors);
                informationSet.MaxPossibleThisPlayer = informationSet.MaxPossible[informationSet.PlayerIndex];
            }
        }

        public double[] InformationSet_SendToPredecessor(InformationSetNodeTally informationSet)
        {
            return Max ? informationSet.MaxPossible : informationSet.MinPossible;
        }
    }

}
