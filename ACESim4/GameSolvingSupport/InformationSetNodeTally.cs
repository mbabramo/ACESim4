using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class InformationSetNodeTally : IGameState
    {
        public static int InformationSetsSoFar = 0;
        public int InformationSetNumber; // could delete this once things are working, but may be useful in testing scenarios
        public byte DecisionByteCode;
        public byte DecisionIndex;
        public byte PlayerIndex;
        public byte? BinarySubdivisionLevels;
        public int LastIterationChanged = -1; // used by some algorithms
        public int NumRegretIncrements = 0;
        double[,] NodeInformation;

        int NumPossibleActions => NodeInformation.GetLength(1);
        const int totalDimensions = 5;
        const int cumulativeRegretDimension = 0;
        const int cumulativeStrategyDimension = 1;
        const int bestResponseNumeratorDimension = 2;
        const int bestResponseDenominatorDimension = 3;
        const int storageDimension = 4;

        public InformationSetNodeTally(byte decisionByteCode, byte decisionIndex, byte playerIndex, int numPossibleActions, byte? binarySubdivisionLevels)
        {
            DecisionByteCode = decisionByteCode;
            DecisionIndex = decisionIndex;
            PlayerIndex = playerIndex;
            Initialize(totalDimensions, numPossibleActions);
            InformationSetNumber = InformationSetsSoFar;
            BinarySubdivisionLevels = binarySubdivisionLevels;
            Interlocked.Increment(ref InformationSetsSoFar);
        }

        public override string ToString()
        {
            return $"Information set {InformationSetNumber}: DecisionByteCode {DecisionByteCode} (index {DecisionIndex}) PlayerIndex {PlayerIndex} Probabilities {GetRegretMatchingProbabilitiesString()} Regrets {GetCumulativeRegretsString()} Strategies {GetCumulativeStrategiesString()} RegretIncrements {NumRegretIncrements}";
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

        public void IncrementCumulativeRegret_Parallel(int action, double amount)
        {
            lock (this)
            {
                NodeInformation[cumulativeRegretDimension, action - 1] += amount;
                NumRegretIncrements++;
            }
        }

        public void IncrementCumulativeRegret(int action, double amount)
        {
            NodeInformation[cumulativeRegretDimension, action - 1] += amount;
            NumRegretIncrements++;
        }

        public void SetActionToCertainty(byte action, byte numPossibleActions)
        {
            for (byte a = 1; a <= numPossibleActions; a++)
            {
                NodeInformation[cumulativeStrategyDimension, a - 1] =
                NodeInformation[cumulativeRegretDimension, a - 1] = 
                    (a == action) ? 1.0 : 0;
            }
        }

        public double GetCumulativeStrategy(int action)
        {
            double v = NodeInformation[cumulativeStrategyDimension, action - 1];
            return v;
        }

        public void IncrementCumulativeStrategy_Parallel(int action, double amount)
        {
            lock (this)
                NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public static double ZeroOutBelow = 0.01;

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

        public (double,int) GetSumPositiveCumulativeRegrets_AndNumberPositive()
        {
            double total = 0;
            int numPositive = 0;
            for (int i = 0; i < NumPossibleActions; i++)
            {
                double cumulativeRegret = NodeInformation[cumulativeRegretDimension, i];
                if (cumulativeRegret > 0)
                {
                    total += cumulativeRegret;
                    numPositive++;
                }
            }
            return (total, numPositive);
        }


        public unsafe List<double> GetRegretMatchingProbabilities()
        {
            double* probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetRegretMatchingProbabilities(probabilitiesToSet);
            return Util.ListExtensions.GetPointerAsList(probabilitiesToSet, NumPossibleActions);

        }

        public unsafe void GetRegretMatchingProbabilities(double* probabilitiesToSet)
        {
            (double sumPositiveCumulativeRegrets, int numPositive) = GetSumPositiveCumulativeRegrets_AndNumberPositive();
            if (numPositive == 1)
            {
                for (byte a = 1; a <= NumPossibleActions; a++)
                    if (NodeInformation[cumulativeRegretDimension, a - 1] > 0)
                        probabilitiesToSet[a - 1] = 1.0;
                    else
                        probabilitiesToSet[a - 1] = 0.0;
                return;
            }
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
                    probabilitiesToSet[a - 1] = (GetPositiveCumulativeRegret(a)) / sumPositiveCumulativeRegrets;
                }
            }
        }

        public unsafe string GetCumulativeStrategiesString()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(NodeInformation[cumulativeStrategyDimension, a - 1]);
            return String.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public unsafe string GetCumulativeRegretsString()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(NodeInformation[cumulativeRegretDimension, a - 1]);
            return String.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public unsafe string GetRegretMatchingProbabilitiesString()
        {
            List<double> probs = new List<double>();
            double* probabilitiesToSet = stackalloc double[NumPossibleActions];
            double sumPositiveCumulativeRegrets = GetSumPositiveCumulativeRegrets();
            if (sumPositiveCumulativeRegrets == 0)
            {
                double equalProbability = 1.0 / (double)NumPossibleActions;
                for (byte a = 1; a <= NumPossibleActions; a++)
                    probs.Add(equalProbability);
            }
            else
            {
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    probs.Add((GetPositiveCumulativeRegret(a)) / sumPositiveCumulativeRegrets);
                }
            }
            return String.Join(",", probs.Select(x => $"{x:N2}"));
        }

        /// <summary>
        /// Get regret matching adjusted probabilities, but adjusted so that unlikely actions are sometimes sampled.
        /// </summary>
        /// <param name="probabilitiesToSet">A pointer to the probabilities to set, one per action.</param>
        /// <param name="epsilon">The weight (from 0 to 1) on equal probabilities rather than on regret-matching probabilities.</param>
        public unsafe void GetEpsilonAdjustedRegretMatchingProbabilities(double* probabilitiesToSet, double epsilon)
        {
            GetRegretMatchingProbabilities(probabilitiesToSet);
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = epsilon * equalProbabilities + (1.0 - epsilon) * probabilitiesToSet[a - 1];
        }

        // The following can be used to accomplish the same thing as epsilon adjusted regret matching probabilities. This is useful if we need to be able to determine whether we are doing epsilon exploration.
        public unsafe void GetEqualProbabilitiesRegretMatching(double* probabilitiesToSet)
        {
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = equalProbabilities;
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
        }

        public bool ChooseHigherOfTwoActionsWithRegretMatching(double randomSeed1, double randomSeed2, double epsilon)
        {
            // this should be a little faster than ChooseActionWithRegretMatching
            double firstActionRegrets = NodeInformation[cumulativeRegretDimension, 0];
            double secondActionRegrets = NodeInformation[cumulativeRegretDimension, 1];
            if (randomSeed2 < epsilon || (firstActionRegrets <= 0 && secondActionRegrets <= 0))
                return randomSeed1 > 0.5;
            else if (firstActionRegrets <= 0)
                return true;
            else if (secondActionRegrets <= 0)
                return false;
            else return (secondActionRegrets / (firstActionRegrets + secondActionRegrets)) > randomSeed1;
        }

        public double GetRegretWeightedValueFromTwoActions(double scoreAction1, double scoreAction2)
        {
            double firstActionRegrets = NodeInformation[cumulativeRegretDimension, 0];
            double secondActionRegrets = NodeInformation[cumulativeRegretDimension, 1];
            if (firstActionRegrets <= 0 & secondActionRegrets <= 0)
                return 0.5*scoreAction1 + 0.5*scoreAction2;
            else if (firstActionRegrets <= 0)
                return scoreAction2;
            else if (secondActionRegrets <= 0)
                return scoreAction1;
            else
            {
                double firstActionProbability = firstActionRegrets / (firstActionRegrets + secondActionRegrets);
                return firstActionProbability * scoreAction1 + (1.0 - firstActionProbability) * scoreAction2;
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

        public void CopyCumulativeRegretsToStorage()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[storageDimension, a] = NodeInformation[cumulativeRegretDimension, a];
        }

        public void CopyStorageToCumulativeRegrets()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretDimension, a] = NodeInformation[storageDimension, a];
        }

        public void RemoveStorageFromCumulativeRegrets(double portionToRemove = 1.0)
        {
            bool difference = false;

            if (portionToRemove == 1.0)
            {
                for (byte a = 0; a < NumPossibleActions; a++)
                    if (NodeInformation[cumulativeRegretDimension, a] != NodeInformation[storageDimension, a])
                        difference = true;
                if (!difference)
                    return; // we don't want to remove cumulative regrets if nothing has changed.
            }

            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretDimension, a] -= portionToRemove * NodeInformation[storageDimension, a];
        }

        internal void DiscountStoredCumulativeRegrets()
        {
            double maxAbs = 0;
            for (byte a = 0; a < NumPossibleActions; a++)
                if (Math.Abs(NodeInformation[cumulativeRegretDimension, a]) > maxAbs)
                    maxAbs = Math.Abs(NodeInformation[cumulativeRegretDimension, a]);
            if (maxAbs == 0)
                return;
            double portionToKeep = 1.0 / maxAbs; // in other words, scale everything so that the maximum item is worth 1 or -1, making it almost trivial.
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretDimension, a] *= portionToKeep;
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.Tally;
        }

    }
}
