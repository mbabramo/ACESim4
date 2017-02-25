﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class CRMInformationSetNodeTally
    {
        public byte DecisionNum;
        public byte NonChancePlayerIndex;
        double[,] NodeInformation;

        int NumPossibleActions => NodeInformation.GetLength(1);
        const int cumulativeRegretDimension = 0;
        const int cumulativeStrategyDimension = 1;

        public CRMInformationSetNodeTally(byte decisionNum, byte nonChancePlayerIndex, int numPossibleActions)
        {
            DecisionNum = decisionNum;
            NonChancePlayerIndex = nonChancePlayerIndex;
            Initialize(2, numPossibleActions);
        }

        private void Initialize(int numDimensions, int numPossibleActions)
        {
            NodeInformation = new double[numDimensions, numPossibleActions];
            for (int i = 0; i < numDimensions; i++)
                for (int j = 0; j < numPossibleActions; j++)
                    NodeInformation[i, j] = 0;
        }

        public void IncrementCumulativeRegret(int action, double amount)
        {
            NodeInformation[cumulativeRegretDimension, action - 1] += amount;
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public double GetPositiveCumulativeRegret(int action)
        {
            double cumulativeRegret = NodeInformation[cumulativeRegretDimension, action - 1];
            if (cumulativeRegret > 0)
                return cumulativeRegret;
            return 0;
        }

        public double GetSumPositiveCumulativeRegrets()
        {
            double total = 0;
            for (int i = 0; i < NumPossibleActions; i++)
            {
                double cumulativeRegret = NodeInformation[cumulativeRegretDimension, i];
                if (cumulativeRegret > 0)
                    total += cumulativeRegret;
            }
            return total;
        }

        public void SetRegretMatchingProbabilities(double[] probabilitiesToSet)
        {
            double sumPositiveCumulativeRegrets = GetSumPositiveCumulativeRegrets();
            if (sumPositiveCumulativeRegrets == 0)
            {
                double equalProbability = 1.0 / (double)NumPossibleActions;
                for (byte a = 1; a <= NumPossibleActions; a++)
                    probabilitiesToSet[a - 1] = equalProbability;
            }
            else
            {
                for (byte a = 1; a <= NumPossibleActions; a++)
                    probabilitiesToSet[a - 1] = GetPositiveCumulativeRegret(a) / sumPositiveCumulativeRegrets;
            }
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
