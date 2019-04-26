using ACESim.Util;
using ACESimBase.Util;
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
        #region Properties, members, and constants

        public static int InformationSetsSoFar = 0;
        public int InformationSetNumber; // could delete this once things are working, but may be useful in testing scenarios
        public Decision Decision;
        public byte DecisionByteCode => Decision.DecisionByteCode;
        public byte DecisionIndex;
        public byte PlayerIndex => Decision.PlayerNumber;
        public bool MustUseBackup;
        public int NumTotalIncrements = 0;
        public int NumRegretIncrements = 0;

        public int NumBackupRegretIncrements = 0;
        public int NumBackupRegretsSinceLastRegretIncrement = 0;


        double[,] NodeInformation;

        int NumPossibleActions => Decision.NumPossibleActions;
        const int totalDimensions = 8;
        const int cumulativeRegretDimension = 0;
        const int cumulativeStrategyDimension = 1;
        const int bestResponseNumeratorDimension = 2;
        const int bestResponseDenominatorDimension = 3;
        // for hedge probing
        const int hedgeProbabilityDimension = 4;
        const int lastRegretDimension = 5;
        const int temporaryDimension = 6;
        // for normalized hedge (including also hedge probability dimension)
        const int regretIncrementsDimension = 5;
        const int adjustedWeightsDimension = 6;
        const int averageStrategyProbabilityDimension = 7;
        // for exploratory probing
        const int storageDimension = 4;
        const int storageDimension2 = 5;
        const int cumulativeRegretBackupDimension = 6;

        // Normalized hedge
        public SimpleExclusiveLock UpdatingHedge;
        const double NormalizedHedgeEpsilon = 0.5;
        int LastUpdatedIteration = -1;
        public byte LastBestResponseAction = 0;
        public bool BestResponseWeightsUpdatedSinceLast = false;
        public double[] LastBestResponseExpectedValues;

        // hedge probing
        double V = 0; // V parameter in Cesa-Bianchi
        double MaxAbsRegretDiff = 0;
        double E = 1;
        double Nu;
        static double C = Math.Sqrt((2 * (Math.Sqrt(2) - 1.0)) / (Math.Exp(1.0) - 2));

        #endregion

        #region Construction and initialization

        public InformationSetNodeTally()
        {

        }

        public InformationSetNodeTally(Decision decision, byte decisionIndex)
        {
            Decision = decision;
            DecisionIndex = decisionIndex;
            Initialize(totalDimensions, decision.NumPossibleActions);
            InformationSetNumber = InformationSetsSoFar;
            Interlocked.Increment(ref InformationSetsSoFar);
        }

        public void Reinitialize()
        {
            Initialize(totalDimensions, Decision.NumPossibleActions);
            NumTotalIncrements = 0;
            NumRegretIncrements = 0;
            NumBackupRegretIncrements = 0;
            NumBackupRegretsSinceLastRegretIncrement = 0;
            V = 0;
            MaxAbsRegretDiff = 0;
            E = 1;
            UpdatingHedge = null;
        }

        public string ToStringAbbreviated()
        {
            return $"Information set {InformationSetNumber}: Probabilities {GetProbabilitiesString()} {GetBestResponseStringIfAvailable()}RegretIncrements {NumRegretIncrements}";
        }

        public override string ToString()
        {
            return $"Information set {InformationSetNumber}: DecisionByteCode {DecisionByteCode} (index {DecisionIndex}) PlayerIndex {PlayerIndex} Probabilities {GetProbabilitiesString()} {GetBestResponseStringIfAvailable()}Regrets{(MustUseBackup ? "*" : "")} {GetCumulativeRegretsString()} Strategies {GetCumulativeStrategiesString()} RegretIncrements {NumRegretIncrements} NumBackupRegretsSinceLastRegretIncrement {NumBackupRegretsSinceLastRegretIncrement} NumBackupRegretIncrements {NumBackupRegretIncrements} TotalIncrements {NumTotalIncrements}";
        }

        public string GetBestResponseStringIfAvailable()
        {
            if (LastBestResponseAction == 0)
                return "";
            return $"BestResponse {LastBestResponseAction} ";
            //return $"BestResponse {LastBestResponseAction} {NodeInformation[bestResponseNumeratorDimension, PlayerIndex]}/{NodeInformation[bestResponseDenominatorDimension, PlayerIndex]}";
        }

        public string GetProbabilitiesString()
        {
            if (UpdatingHedge != null)
            {
                var probs = GetHedgeProbabilitiesAsArray();
                return String.Join(",", probs.Select(x => $"{x:N6}"));
            }
            return GetRegretMatchingProbabilitiesString();
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

        public void ClearAverageStrategyTally()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeStrategyDimension, a] = 0;
        }

        public void ClearMainTally()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretDimension, a] = 0;
            MustUseBackup = true; // as long as the main tally is cleared, we have to use the backup tally -- if we set something in the main tally, then this will automatically change
        }

        public void ClearBackupTally()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretBackupDimension, a] = 0;
        }

        public void CopyFromOneDimensionToAnother(byte dimensionCopyingFrom, byte dimensionCopyingTo)
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[dimensionCopyingTo, a] = NodeInformation[dimensionCopyingFrom, a];
        }

        public void SubtractOutValues(byte dimensionSubtractingFrom, byte dimensionWithValuesToSubtract)
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[dimensionSubtractingFrom, a] -= NodeInformation[dimensionWithValuesToSubtract, a];
        }

        public void CopyBackupTallyToMainTally()
        {
            CopyFromOneDimensionToAnother(cumulativeRegretBackupDimension, cumulativeRegretDimension);
        }

        public void CopyMainTallyToBackupTally()
        {
            CopyFromOneDimensionToAnother(cumulativeRegretDimension, cumulativeRegretBackupDimension);
        }

        public void StoreCurrentTallyValues()
        {
            CopyFromOneDimensionToAnother(cumulativeRegretDimension, storageDimension);
            CopyFromOneDimensionToAnother(cumulativeRegretBackupDimension, storageDimension2);
        }

        public void SubtractOutStoredTallyValues()
        {
            SubtractOutValues(cumulativeRegretDimension, storageDimension);
            SubtractOutValues(cumulativeRegretBackupDimension, storageDimension2);
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.Tally;
        }

        #endregion

        #region Best response

        public byte GetBestResponseAction()
        {
            if (BestResponseWeightsUpdatedSinceLast == false)
                return LastBestResponseAction;
            double bestRatio = 0;
            int best = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double denominator = NodeInformation[bestResponseDenominatorDimension, a - 1];
                if (denominator == 0)
                    return 0; // no best response data available
                double ratio = NodeInformation[bestResponseNumeratorDimension, a - 1] / denominator;
                if (a == 1 || ratio > bestRatio)
                {
                    best = a;
                    bestRatio = ratio;
                }
            }
            LastBestResponseAction = (byte)best;
            BestResponseWeightsUpdatedSinceLast = false;
            ResetBestResponseData();
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
            BestResponseWeightsUpdatedSinceLast = true;
        }

        #endregion

        #region Cumulative regrets and backup regrets

        public string GetCumulativeRegretsString()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(GetCumulativeRegret(a));
            return String.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public double GetCumulativeRegret(int action)
        {
            return MustUseBackup ? NodeInformation[cumulativeRegretBackupDimension, action - 1] : NodeInformation[cumulativeRegretDimension, action - 1];
        }

        public double GetPositiveCumulativeRegret(int action)
        {
            double cumulativeRegret = MustUseBackup ? NodeInformation[cumulativeRegretBackupDimension, action - 1] : NodeInformation[cumulativeRegretDimension, action - 1];
            if (cumulativeRegret > 0)
                return cumulativeRegret;
            return 0;
        }

        public double GetSumPositiveCumulativeRegrets()
        {
            double total = 0;
            for (int i = 0; i < NumPossibleActions; i++)
            {
                double cumulativeRegret = MustUseBackup ? NodeInformation[cumulativeRegretBackupDimension, i] : NodeInformation[cumulativeRegretDimension, i];
                if (cumulativeRegret > 0)
                    total += cumulativeRegret;
            }
            return total;
        }

        public (double, int) GetSumPositiveCumulativeRegrets_AndNumberPositive()
        {
            double total = 0;
            int numPositive = 0;
            for (int i = 0; i < NumPossibleActions; i++)
            {
                double cumulativeRegret = MustUseBackup ? NodeInformation[cumulativeRegretBackupDimension, i] : NodeInformation[cumulativeRegretDimension, i];
                if (cumulativeRegret > 0)
                {
                    total += cumulativeRegret;
                    numPositive++;
                }
            }
            return (total, numPositive);
        }

        public void IncrementCumulativeRegret_Parallel(int action, double amount, bool incrementBackup, int backupRegretsTrigger = int.MaxValue, bool incrementVisits = false)
        {
            Interlocked.Increment(ref NumTotalIncrements);
            if (incrementBackup)
            {
                Interlocking.Add(ref NodeInformation[cumulativeRegretBackupDimension, action - 1], amount);
                if (incrementVisits)
                {
                    Interlocked.Increment(ref NumBackupRegretIncrements);
                    Interlocked.Increment(ref NumBackupRegretsSinceLastRegretIncrement);
                    SetMustUseBackup(backupRegretsTrigger);
                }
                return;
            }
            Interlocking.Add(ref NodeInformation[cumulativeRegretDimension, action - 1], amount);
            if (incrementVisits)
            {
                Interlocked.Increment(ref NumRegretIncrements);
                NumBackupRegretsSinceLastRegretIncrement = 0;
                SetMustUseBackup(backupRegretsTrigger);
            }
        }

        public void IncrementCumulativeRegret(int action, double amount, bool incrementBackup, int backupRegretsTrigger = int.MaxValue, bool incrementVisits = false)
        {
            NumTotalIncrements++; // we always keep track of this
            if (incrementBackup)
            {
                NodeInformation[cumulativeRegretBackupDimension, action - 1] += amount;
                if (incrementVisits)
                {
                    NumBackupRegretIncrements++;
                    NumBackupRegretsSinceLastRegretIncrement++;
                    SetMustUseBackup(backupRegretsTrigger);
                }
                return;
            }
            NodeInformation[cumulativeRegretDimension, action - 1] += amount;
            if (incrementVisits)
            {
                NumRegretIncrements++;
                NumBackupRegretsSinceLastRegretIncrement = 0;
                SetMustUseBackup(backupRegretsTrigger);
            }
        }

        private void SetMustUseBackup(int backupRegretsTrigger)
        {
            MustUseBackup = (NumRegretIncrements == 0) || NumBackupRegretsSinceLastRegretIncrement >= backupRegretsTrigger;
        }

        #endregion

        #region Cumulative strategies

        public unsafe string GetCumulativeStrategiesString()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(NodeInformation[cumulativeStrategyDimension, a - 1]);
            return String.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public double GetCumulativeStrategy(int action)
        {
            double v = NodeInformation[cumulativeStrategyDimension, action - 1];
            return v;
        }

        public void IncrementCumulativeStrategy_Parallel(int action, double amount)
        {
            Interlocking.Add(ref NodeInformation[cumulativeStrategyDimension, action - 1], amount);
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            NodeInformation[cumulativeStrategyDimension, action - 1] += amount;
        }

        public static double ZeroOutBelow = 1E-50;

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
            if (sum == 0)
                GetEqualProbabilitiesRegretMatching(probabilities);

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

        #endregion

        #region Regret matching

        public unsafe List<double> GetRegretMatchingProbabilities()
        {
            double* probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetRegretMatchingProbabilities(probabilitiesToSet);
            return Util.ListExtensions.GetPointerAsList(probabilitiesToSet, NumPossibleActions);
        }

        public unsafe List<double> GetRegretMatchingProbabilities_IgnoreBackup()
        {
            // NOTE: Not thread-safe
            bool mustUseBackupPrevious = MustUseBackup;
            MustUseBackup = false;
            double* probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetRegretMatchingProbabilities(probabilitiesToSet);
            List<double> returnVal = Util.ListExtensions.GetPointerAsList(probabilitiesToSet, NumPossibleActions);
            MustUseBackup = mustUseBackupPrevious;
            return returnVal;
        }

        public unsafe List<double> GetRegretMatchingProbabilities_WithEvenProbabilitiesIfUsingBackup()
        {
            // NOTE: Not thread-safe
            bool mustUseBackupPrevious = MustUseBackup;
            MustUseBackup = false;
            double* probabilitiesToSet = stackalloc double[NumPossibleActions];
            if (mustUseBackupPrevious)
                GetEpsilonAdjustedRegretMatchingProbabilities(probabilitiesToSet, 1.0);
            else
                GetRegretMatchingProbabilities(probabilitiesToSet);
            List<double> returnVal = Util.ListExtensions.GetPointerAsList(probabilitiesToSet, NumPossibleActions);
            MustUseBackup = mustUseBackupPrevious;
            return returnVal;
        }

        public unsafe void GetRegretMatchingProbabilities(double* probabilitiesToSet)
        {
            bool done = false;
            while (!done)
            { // without this outer loop, there is a risk that when using parallel code, our regret matching probabilities will not add up to 1
                (double sumPositiveCumulativeRegrets, int numPositive) = GetSumPositiveCumulativeRegrets_AndNumberPositive();
                if (numPositive == 1)
                {
                    int numSet = 0;
                    for (byte action = 1; action <= NumPossibleActions; action++)
                        if (GetCumulativeRegret(action) > 0)
                        {
                            probabilitiesToSet[action - 1] = 1.0;
                            numSet++;
                        }
                        else
                            probabilitiesToSet[action - 1] = 0.0;
                    done = numSet == 1;
                }
                if (sumPositiveCumulativeRegrets == 0)
                {
                    double equalProbability = 1.0 / (double)NumPossibleActions;
                    for (byte a = 1; a <= NumPossibleActions; a++)
                        probabilitiesToSet[a - 1] = equalProbability;
                    done = true;
                }
                else
                {
                    double total = 0;
                    for (byte a = 1; a <= NumPossibleActions; a++)
                    {
                        probabilitiesToSet[a - 1] = (GetPositiveCumulativeRegret(a)) / sumPositiveCumulativeRegrets;
                        total += probabilitiesToSet[a - 1];
                    }
                    done = Math.Abs(1.0 - total) < 1E-7;
                }
            }
        }

        public string GetRegretMatchingProbabilitiesString()
        {
            var probs = GetRegretMatchingProbabilitiesList();
            return String.Join(",", probs.Select(x => $"{x:N2}"));
        }

        public unsafe List<double> GetRegretMatchingProbabilitiesList()
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
            return probs;
        }

        public byte GetRegretMatchingHighestRatedAction()
        {
            var regretMatchingProbabilitiesList = GetRegretMatchingProbabilitiesList();
            int highestIndex = regretMatchingProbabilitiesList.Select((v, i) => new { item = v, index = i }).OrderByDescending(x => x.item).First().index;
            byte actionWithHighestProbability = (byte)(highestIndex + 1);
            return actionWithHighestProbability;
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
            double firstActionRegrets = GetCumulativeRegret(1);
            double secondActionRegrets = GetCumulativeRegret(2);
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
            double firstActionRegrets = GetCumulativeRegret(1);
            double secondActionRegrets = GetCumulativeRegret(2);
            if (firstActionRegrets <= 0 & secondActionRegrets <= 0)
                return 0.5 * scoreAction1 + 0.5 * scoreAction2;
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

        #endregion

        #region Normalized Hedge

        private void UpdateNormalizedHedgeIfNecessary(int iteration)
        {
            if (iteration > LastUpdatedIteration)
            {
                InitializeNormalizedHedgeIfNecessary();
                UpdatingHedge.Enter();
                if (iteration > LastUpdatedIteration)
                {
                    GetBestResponseAction(); // calculate best response, if data is available
                    // remember last regret
                    double minLastRegret = 0, maxLastRegret = 0;
                    for (int a = 1; a <= NumPossibleActions; a++)
                    {
                        double lastRegret = NodeInformation[regretIncrementsDimension, a - 1];
                        if (a == 1)
                            minLastRegret = maxLastRegret = lastRegret;
                        else if (lastRegret > maxLastRegret)
                            maxLastRegret = lastRegret;
                        else if (lastRegret < minLastRegret)
                            minLastRegret = lastRegret;
                    }
                    // normalize regrets to costs between 0 and 1. the key assumption is that each iteration takes into account ALL possible outcomes (as in a vanilla hedge CFR algorithm)
                    double sumWeights = 0, sumCumulativeStrategies = 0;
                    for (int a = 1; a <= NumPossibleActions; a++)
                    {
                        double regretIncrements = NodeInformation[regretIncrementsDimension, a - 1];
                        double normalizedCost = maxLastRegret == minLastRegret ? 0.5 : 1.0 - (regretIncrements - minLastRegret) / (maxLastRegret - minLastRegret);
                        double weightAdjustment = Math.Pow(1 - NormalizedHedgeEpsilon, normalizedCost);
                        double weight = NodeInformation[adjustedWeightsDimension, a - 1];
                        weight *= weightAdjustment;
                        if (double.IsNaN(weight))
                            throw new Exception();
                        NodeInformation[adjustedWeightsDimension, a - 1] = weight;
                        sumWeights += weight;
                        NodeInformation[cumulativeRegretDimension, a - 1] += NodeInformation[regretIncrementsDimension, a - 1];
                        NodeInformation[regretIncrementsDimension, a - 1] = 0; // reset for next iteration
                        sumCumulativeStrategies += NodeInformation[cumulativeStrategyDimension, a - 1];
                    }
                    if (sumWeights < 1E-20)
                    { // increase all weights to avoid all weights being round off to zero -- since this affects only relative probabilities at the information set, this won't matter
                        for (int a = 1; a <= NumPossibleActions; a++)
                        {
                            NodeInformation[adjustedWeightsDimension, a - 1] *= 1E+15;
                        }
                        sumWeights *= 1E+15;
                    }
                    // Finally, calculate the hedge adjusted probabilities
                    for (int a = 1; a <= NumPossibleActions; a++)
                    {
                        double probabilityHedge = NodeInformation[adjustedWeightsDimension, a - 1] / sumWeights;
                        if (probabilityHedge == 0)
                            probabilityHedge = Double.Epsilon; // always maintain at least the smallest possible positive probability
                        if (double.IsNaN(probabilityHedge))
                            throw new Exception();
                        NodeInformation[hedgeProbabilityDimension, a - 1] = probabilityHedge;
                        if (sumCumulativeStrategies > 0)
                        {
                            double probabilityAverageStrategy = NodeInformation[cumulativeStrategyDimension, a - 1] / sumCumulativeStrategies;
                            if (probabilityAverageStrategy == 0)
                                probabilityAverageStrategy = Double.Epsilon; // always maintain at least the smallest possible positive probability
                            if (double.IsNaN(probabilityAverageStrategy))
                                throw new Exception();
                            NodeInformation[averageStrategyProbabilityDimension, a - 1] = probabilityAverageStrategy;
                        }
                    }
                    LastUpdatedIteration = iteration;
                }
                UpdatingHedge.Exit();
            }
        }

        private void InitializeNormalizedHedgeIfNecessary()
        {
            if (UpdatingHedge == null)
            {
                lock (this)
                {
                    if (UpdatingHedge == null)
                    { // Initialize
                        if (NumPossibleActions == 0)
                            throw new Exception("NumPossibleActions not initialized");
                        double probability = 1.0 / (double)NumPossibleActions;
                        if (double.IsNaN(probability))
                            throw new Exception(); // DEBUG
                        for (int a = 1; a <= NumPossibleActions; a++)
                        {
                            NodeInformation[adjustedWeightsDimension, a - 1] = 1.0;
                            NodeInformation[hedgeProbabilityDimension, a - 1] = probability;
                            NodeInformation[averageStrategyProbabilityDimension, a - 1] = probability;
                        }
                        UpdatingHedge = new SimpleExclusiveLock();
                    }
                }
            }
        }

        public void NormalizedHedgeIncrementLastRegret(byte action, double regretTimesInversePi)
        {
            IncrementDouble(ref NodeInformation[lastRegretDimension, action - 1], regretTimesInversePi);
            Interlocked.Increment(ref NumRegretIncrements);
        }

        public double GetHedgeSavedAverageStrategy(byte action)
        {
            return NodeInformation[averageStrategyProbabilityDimension, action - 1];
        }

        private static double IncrementDouble(ref double location1, double value)
        {
            double newCurrentValue = location1; // non-volatile read, so may be stale
            while (true)
            {
                double currentValue = newCurrentValue;
                double newValue = currentValue + value;
                newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
                if (newCurrentValue == currentValue)
                    return newValue;
            }
        }

        public unsafe void GetEpsilonAdjustedNormalizedHedgeProbabilities(double* probabilitiesToSet, double epsilon, int iteration)
        {
            GetNormalizedHedgeProbabilities(probabilitiesToSet, iteration);
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = epsilon * equalProbabilities + (1.0 - epsilon) * probabilitiesToSet[a - 1];
        }

        public unsafe void GetNormalizedHedgeProbabilities(double* probabilitiesToSet, int iteration)
        {
            UpdateNormalizedHedgeIfNecessary(iteration);
            bool done = false;
            while (!done)
            { // without this outer loop, there is a risk that when using parallel code, our probabilities will not add up to 1
                double total = 0;
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    probabilitiesToSet[a - 1] = NodeInformation[hedgeProbabilityDimension, a - 1];
                    total += probabilitiesToSet[a - 1];
                }
                done = Math.Abs(1.0 - total) < 1E-7;
            }
        }

        public unsafe double[] GetNormalizedHedgeProbabilitiesAsArray(int iteration)
        {
            double[] array = new double[NumPossibleActions];

            double* actionProbabilities = stackalloc double[NumPossibleActions];
            GetNormalizedHedgeProbabilities(actionProbabilities, iteration);
            for (int a = 0; a < NumPossibleActions; a++)
                array[a] = actionProbabilities[a];
            return array;
        }

        #endregion

        #region Hedge

        public void InitiateHedgeUpdate()
        {
            InitializeHedgeIfNecessary();
            UpdatingHedge.Enter();
        }

        private void InitializeHedgeIfNecessary()
        {
            if (UpdatingHedge == null)
            {
                lock (this)
                {
                    if (UpdatingHedge == null)
                    { // Initialize
                        if (NumPossibleActions == 0)
                            throw new Exception("NumPossibleActions not initialized");
                        double probability = 1.0 / (double)NumPossibleActions;
                        if (double.IsNaN(probability))
                            throw new Exception(); // DEBUG
                        for (int a = 1; a <= NumPossibleActions; a++)
                            NodeInformation[hedgeProbabilityDimension, a - 1] = probability;
                        UpdatingHedge = new SimpleExclusiveLock();
                    }
                }
            }
        }

        public void ConcludeHedgeUpdate()
        {
            UpdateHedgeInfoAfterIteration();
            UpdatingHedge.Exit();
        }

        public void HedgeSetLastRegret(byte action, double regret)
        {
            NodeInformation[lastRegretDimension, action - 1] = regret;
            NumRegretIncrements++;
        }

        private unsafe void UpdateHedgeInfoAfterIteration()
        {
            double firstSum = 0, secondSum = 0;
            double minLastRegret = 0, maxLastRegret = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double lastPi = NodeInformation[hedgeProbabilityDimension, a - 1];
                double lastRegret = NodeInformation[lastRegretDimension, a - 1];
                if (a == 1)
                    minLastRegret = maxLastRegret = lastRegret;
                else if (lastRegret > maxLastRegret)
                    maxLastRegret = lastRegret;
                else if (lastRegret < minLastRegret)
                    minLastRegret = lastRegret;
                double product = lastPi * lastRegret;
                firstSum += product * lastRegret; // i.e., pi * regret^2
                secondSum += product;
            }
            double varZt = firstSum - secondSum * secondSum; // see Cesa-Bianchi-2007 p. 333
            if (varZt < 0)
                varZt = 0; // rounding error
            V += varZt; // p. 334
            // update e, if necessary (p. 336)
            double absRegretDiff = Math.Abs(maxLastRegret - minLastRegret);
            if (absRegretDiff > MaxAbsRegretDiff)
            {
                MaxAbsRegretDiff = absRegretDiff;
                if (MaxAbsRegretDiff > 0)
                {
                    int k = (int)Math.Ceiling(Math.Log(MaxAbsRegretDiff, 2.0));
                    E = Math.Pow(2.0, k);
                }
            }
            // Now, calculate Nu
            Nu = Math.Min(1.0 / E, C * Math.Sqrt(Math.Log(NumPossibleActions) / V));
            if (double.IsNaN(Nu))
                throw new Exception();
            // Great, we can now calculate the p values. p. 333. First, we'll store the numerators, and then we'll divide by the denominator.
            double denominatorForAllActions = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                NodeInformation[cumulativeRegretDimension, a - 1] += NodeInformation[lastRegretDimension, a - 1];
                double numeratorForThisAction = Math.Exp(Nu * NodeInformation[cumulativeRegretDimension, a - 1]);
                NodeInformation[temporaryDimension, a - 1] = numeratorForThisAction; // alternative implementation would reuse lastRegretDimension
                if (double.IsNaN(numeratorForThisAction))
                    throw new Exception("Regrets too high. Must scale all regrets.");
                denominatorForAllActions += numeratorForThisAction;
            }
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double quotient = NodeInformation[temporaryDimension, a - 1] / denominatorForAllActions;
                NodeInformation[hedgeProbabilityDimension, a - 1] = quotient;
                if (double.IsNaN(quotient))
                    throw new Exception("Regrets too high. Must scale all regrets");
            }
        }

        public unsafe void GetEpsilonAdjustedHedgeProbabilities(double* probabilitiesToSet, double epsilon)
        {
            GetHedgeProbabilities(probabilitiesToSet);
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = epsilon * equalProbabilities + (1.0 - epsilon) * probabilitiesToSet[a - 1];
        }

        public unsafe void GetHedgeProbabilities(double* probabilitiesToSet)
        {
            if (UpdatingHedge == null)
            {
                InitiateHedgeUpdate(); // will initialize if still necessary
                ConcludeHedgeUpdate();
            }
            bool done = false;
            while (!done)
            { // without this outer loop, there is a risk that when using parallel code, our probabilities will not add up to 1
                double total = 0;
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    probabilitiesToSet[a - 1] = NodeInformation[hedgeProbabilityDimension, a - 1];
                    total += probabilitiesToSet[a - 1];
                }
                done = Math.Abs(1.0 - total) < 1E-7;
            }
        }

        public unsafe double[] GetHedgeProbabilitiesAsArray()
        {
            double[] array = new double[NumPossibleActions];

            double* actionProbabilities = stackalloc double[NumPossibleActions];
            GetHedgeProbabilities(actionProbabilities);
            for (int a = 0; a < NumPossibleActions; a++)
                array[a] = actionProbabilities[a];
            return array;
        }


        #endregion

    }
}
