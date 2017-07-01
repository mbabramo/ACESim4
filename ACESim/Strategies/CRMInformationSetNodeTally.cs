using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class CRMInformationSetNodeTally
    {
        public static int InformationSetsSoFar = 0;
        public int InformationSetNumber; // DEBUG -- shouldn't need this once things are working
        public byte DecisionNum;
        public byte NonChancePlayerIndex;
        double[,] NodeInformation;

        int NumPossibleActions => NodeInformation.GetLength(1);
        const int totalDimensions = 4;
        const int cumulativeRegretDimension = 0;
        const int cumulativeStrategyDimension = 1;
        const int bestResponseNumeratorDimension = 2;
        const int bestResponseDenominatorDimension = 3;

        public CRMInformationSetNodeTally(byte decisionNum, byte nonChancePlayerIndex, int numPossibleActions)
        {
            DecisionNum = decisionNum;
            NonChancePlayerIndex = nonChancePlayerIndex;
            Initialize(totalDimensions, numPossibleActions);
            InformationSetNumber = InformationSetsSoFar++;
        }

        public override string ToString()
        {
            return $"{InformationSetNumber}: Decision {DecisionNum} NonChancePlayerIndex {NonChancePlayerIndex}";
        }

        private void Initialize(int numDimensions, int numPossibleActions)
        {
            NodeInformation = new double[numDimensions, numPossibleActions];
            for (int i = 0; i < numDimensions; i++)
                for (int j = 0; j < numPossibleActions; j++)
                    NodeInformation[i, j] = 0;
        }

        public void ResetBestResponseData()
        {
            for (int i = bestResponseNumeratorDimension; i <= bestResponseDenominatorDimension; i++)
                for (int j = 0; j < NumPossibleActions; j++)
                    NodeInformation[i, j] = 0;
        }

        public byte GetBestResponseAction()
        {
            double bestRatio = 0;
            int best = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double ratio = NodeInformation[bestResponseNumeratorDimension, a - 1] / NodeInformation[bestResponseDenominatorDimension, a - 1];
                if (a == 1 || ratio > bestRatio)
                {
                    best = a;
                    bestRatio = ratio;
                }
            }
            return (byte) best;
        }

        public void GetBestResponseProbabilities(double[] probabilities)
        {
            int bestResponse = GetBestResponseAction();
            for (int a = 1; a <= NumPossibleActions; a++)
                if (a == bestResponse)
                    probabilities[a - 1] = 1.0;
                else
                    probabilities[a - 1] = 0;
        }

        public void IncrementBestResponse(int action, double piInverse, double expectedValue)
        {
            NodeInformation[bestResponseNumeratorDimension, action - 1] += piInverse * expectedValue;
            NodeInformation[bestResponseDenominatorDimension, action - 1] += piInverse;
        }

        public double GetCumulativeRegret(int action)
        {
            return NodeInformation[cumulativeRegretDimension, action - 1];
        }

        public void IncrementCumulativeRegret(int action, double amount)
        {
            NodeInformation[cumulativeRegretDimension, action - 1] += amount;
        }

        public double GetCumulativeStrategy(int action)
        {
            double v = NodeInformation[cumulativeStrategyDimension, action - 1];
            return v;
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public static double ZeroOutBelow = 0.0001;

        public unsafe void GetAverageStrategies(double* probabilities)
        {
            double sum = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
                sum += GetCumulativeStrategy(a);

            bool zeroedOutSome = false;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double quotient = GetCumulativeStrategy(a) / sum;
                if (quotient > 0 && quotient < ZeroOutBelow)
                {
                    zeroedOutSome = true;
                    probabilities[a - 1] = 0;
                }
                else
                    probabilities[a - 1] = quotient;
            }
            if (zeroedOutSome)
            {
                sum = 0;
                for (int a = 1; a <= NumPossibleActions; a++)
                    sum += probabilities[a - 1];
                for (int a = 1; a <= NumPossibleActions; a++)
                    probabilities[a - 1] /= sum;
            }

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


        public unsafe void GetRegretMatchingProbabilities(double* probabilitiesToSet)
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
                {
                    probabilitiesToSet[a - 1] = GetPositiveCumulativeRegret(a) / sumPositiveCumulativeRegrets;
                }
            }
        }

        public unsafe void GetRegretMatchingProbabilities_WithPruning(double* probabilitiesToSet)
        {
            bool zeroOutInRegretMatching = false;
            double sumPositiveCumulativeRegrets = GetSumPositiveCumulativeRegrets();
            if (sumPositiveCumulativeRegrets == 0)
            {
                double equalProbability = 1.0 / (double)NumPossibleActions;
                for (byte a = 1; a <= NumPossibleActions; a++)
                    probabilitiesToSet[a - 1] = equalProbability;
            }
            else
            {
                bool zeroedOutSome = false;
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    var positiveCumulativeRegret = GetPositiveCumulativeRegret(a);
                    var quotient = positiveCumulativeRegret / sumPositiveCumulativeRegrets;
                    if (quotient > 0 && quotient < ZeroOutBelow && zeroOutInRegretMatching)
                    {
                        sumPositiveCumulativeRegrets -= positiveCumulativeRegret;
                        zeroedOutSome = true;
                    }
                    else if (!zeroedOutSome)
                        probabilitiesToSet[a - 1] = quotient;
                }
                if (zeroedOutSome)
                    for (byte a = 1; a <= NumPossibleActions; a++)
                    {
                        var positiveCumulativeRegret = GetPositiveCumulativeRegret(a);
                        var quotient = positiveCumulativeRegret / sumPositiveCumulativeRegrets;
                        probabilitiesToSet[a - 1] = quotient;
                    }
            }
            TabbedText.WriteLine($"DEBUG: {probabilitiesToSet[0]}, {probabilitiesToSet[1]}");
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
