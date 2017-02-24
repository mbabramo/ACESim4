using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class CRMInformationSetNodeTally
    {
        double[,] NodeInformation;

        int NumPossibleActions => NodeInformation.GetLength(1);
        const int regretDimension = 0;
        const int cumulativeStrategyDimension = 1;

        public CRMInformationSetNodeTally(int numPossibleActions)
        {
            Initialize(2, numPossibleActions);
        }

        private void Initialize(int numDimensions, int numPossibleActions)
        {
            var x = new double[numDimensions, numPossibleActions];
            for (int i = 0; i < numDimensions; i++)
                for (int j = 0; j < numPossibleActions; j++)
                    NodeInformation[i, j] = 0;
        }

        public void IncrementRegret(int action, double amount)
        {
            NodeInformation[regretDimension, action - 1] += amount;
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public double GetPositiveCumulativeRegret(int action)
        {
            double cumulativeRegret = NodeInformation[regretDimension, action - 1];
            if (cumulativeRegret > 0)
                return cumulativeRegret;
            return 0;
        }

        public double GetSumPositiveCumulativeRegrets()
        {
            double total = 0;
            for (int i = 0; i < NumPossibleActions; i++)
            {
                double cumulativeRegret = NodeInformation[regretDimension, i];
                if (cumulativeRegret > 0)
                    total += cumulativeRegret;
            }
            return total;
        }

        public int ChooseActionWithRegretMatching(double randomSeed)
        {
            double sumPositiveCumulativeRegrets = GetSumPositiveCumulativeRegrets();
            if (sumPositiveCumulativeRegrets == 0)
            {
                int total = (int) Math.Floor(randomSeed * NumPossibleActions);
                return total + 1; // because actions are one-based.
            }
            else
            {
                double runningSum = 0;
                int numPossibleActions = NumPossibleActions;
                for (int action = 1; action <= numPossibleActions; action++)
                {
                    runningSum += GetPositiveCumulativeRegret(action);
                    if (runningSum >= randomSeed)
                        return action;
                }
                return NumPossibleActions; // could happen because of rounding error
            }
        }


    }
}
