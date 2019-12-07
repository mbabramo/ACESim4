using ACESim.Util;
using ACESimBase.GameSolvingSupport;
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
    public class InformationSetNode : IGameState
    {
        #region Properties, members, and constants

        public const double SmallestProbabilityRepresented = 1E-16; // We make this considerably greater than Double.Epsilon (but still very small), because (1) otherwise when we multiply the value by anything < 1, we get 0, and this makes it impossible to climb out of being a zero-probability action, (2) we want to be able to represent 1 - probability.
        public const double SmallestProbabilityInAverageStrategy = 1E-5; // This is greater still -- when calculating average strategies, we disregard very small probabilities (which presumably are on their way to zero)

        public int InformationSetNodeNumber; // could delete this once things are working, but may be useful in testing scenarios
        public int GetNodeNumber() => InformationSetNodeNumber;
        public Decision Decision;
        public EvolutionSettings EvolutionSettings;
        public byte[] InformationSetContents;
        public byte DecisionByteCode => Decision.DecisionByteCode;
        public byte DecisionIndex;
        public byte PlayerIndex => Decision.PlayerNumber;

        public double[,] NodeInformation;
        public double[,] BackupNodeInformation;

        public double[] MaxPossible, MinPossible;
        public double MaxPossibleThisPlayer, MinPossibleThisPlayer;
        
        public int LastPastValueIndexRecorded = -1;
        public double[,] PastValues;
        public double[] PastValuesCumulativeStrategyDiscounts;

        public List<InformationSetNode> AncestorInformationSets;
        public List<InformationSetNode> DescendantInformationSets;
        public List<InformationSetNode> ChildInformationSets;
        public int SizeNeededToDisplayDescendants = 1;
        public int NumGenerationsFromHere = 1;
        public InformationSetNode ParentInformationSet;
        double PastValuesLastCumulativeStrategyDiscount => PastValuesCumulativeStrategyDiscounts[LastPastValueIndexRecorded];

        public int NumPossibleActions => Decision.NumPossibleActions;
        public const int totalDimensions = 12; // one more than highest dimension below

        // Current information
        public const int currentProbabilityDimension = 0;
        public const int currentProbabilityForOpponentDimension = 1; // may be different from current probability because of pruning
        public const int averageStrategyProbabilityDimension = 2;
        public const int bestResponseNumeratorDimension = 3;
        public const int bestResponseDenominatorDimension = 4;
        // Accumulated information
        public const int cumulativeRegretDimension = 5;
        public const int cumulativeStrategyDimension = 6;
        public const int adjustedWeightsDimension = 7;
        // Most recent information -- to be accumulated after iteration
        public const int sumRegretTimesInversePiDimension = 8;
        public const int sumInversePiDimension = 9;
        public const int lastCumulativeStrategyIncrementsDimension = 10;
        // Scratch space (i.e., for algorithm to write some information for each action)
        public const int scratchDimension = 11;

        // Best response
        public byte BestResponseAction = 0;
        public byte BackupBestResponseAction = 0;
        public bool BestResponseDeterminedFromIncrements = false; // this is used by the generalized best response algorithm to determine whether it needs to recalculate best response

        // sum of average strategy adjustments
        public double AverageStrategyAdjustmentsSum = 0;
        public double BackupAverageStrategyAdjustmentsSum = 0;

        #endregion

        #region Construction and initialization

        public InformationSetNode()
        {

        }

        public InformationSetNode(Decision decision, byte decisionIndex, EvolutionSettings evolutionSettings, int informationSetNodeNumber)
        {
            Decision = decision;
            DecisionIndex = decisionIndex;
            EvolutionSettings = evolutionSettings;
            InformationSetNodeNumber = informationSetNodeNumber;
            if (EvolutionSettings.RecordPastValues)
            {
                int totalValuesToRecord = EvolutionSettings.RecordPastValues_TargetNumberToRecord;
                PastValues = new double[totalValuesToRecord, NumPossibleActions];
                PastValuesCumulativeStrategyDiscounts = new double[totalValuesToRecord];
                LastPastValueIndexRecorded = -1;
            }
            Initialize();
        }

        public void Reinitialize()
        {
            Initialize(false);
            V = 0;
            MaxAbsRegretDiff = 0;
            E = 1;
        }

        public static void IdentifyNodeRelationships(List<InformationSetNode> all)
        {
            foreach (InformationSetNode x in all)
            {
                x.AncestorInformationSets = new List<InformationSetNode>();
                x.DescendantInformationSets = new List<InformationSetNode>();
                x.ChildInformationSets = new List<InformationSetNode>();
            }

            foreach (InformationSetNode x in all)
            {
                foreach (InformationSetNode y in all)
                {
                    if (x.IsAncestorOf(y))
                    {
                        x.DescendantInformationSets.Add(y);
                        y.AncestorInformationSets.Add(x);
                    }
                }
            }

            foreach (InformationSetNode x in all)
            {
                if (x.AncestorInformationSets.Any())
                {
                    int maxLength = x.AncestorInformationSets.Max(y => y.InformationSetContents.Length);
                    x.ParentInformationSet = x.AncestorInformationSets.First(y => y.InformationSetContents.Length == maxLength);
                    x.ParentInformationSet.ChildInformationSets.Add(x);
                }
            }

            bool changeMade = true;
            while (changeMade)
            {
                changeMade = false;
                foreach (InformationSetNode x in all)
                {
                    if (x.ChildInformationSets.Any())
                    {
                        int revisedSize = x.ChildInformationSets.Sum(y => y.SizeNeededToDisplayDescendants);
                        if (x.SizeNeededToDisplayDescendants != revisedSize)
                        {
                            changeMade = true;
                            x.SizeNeededToDisplayDescendants = revisedSize;
                        }
                        int revisedGenerations = x.ChildInformationSets.Max(y => y.NumGenerationsFromHere) + 1;
                        if (x.NumGenerationsFromHere != revisedGenerations)
                        {
                            changeMade = true;
                            x.NumGenerationsFromHere = revisedGenerations;
                        }
                    }
                }
            }
        }

        public bool IsAncestorOf(InformationSetNode other)
        {
            return this != other && other.PlayerIndex == PlayerIndex && other.InformationSetContents.Length > InformationSetContents.Length && InformationSetContents.SequenceEqual(other.InformationSetContents.Take(InformationSetContents.Length));
        }

        public string ToStringAbbreviated()
        {
            return $"Information set {InformationSetNodeNumber}: Probabilities {GetCurrentProbabilitiesAsString()} {GetBestResponseStringIfAvailable()}";
        }

        public override string ToString()
        {
            return $"Information set {InformationSetNodeNumber} ({Decision.Name}): DecisionByteCode {DecisionByteCode} (index {DecisionIndex}) PlayerIndex {PlayerIndex} Probabilities {GetCurrentProbabilitiesAsString()} {GetBestResponseStringIfAvailable()}Average {GetAverageStrategiesAsString()} Regrets {GetCumulativeRegretsString()} Strategies {GetCumulativeStrategiesString()}";
        }

        public string GetBestResponseStringIfAvailable()
        {
            if (BestResponseAction == 0)
                return "";
            return $"BestResponse {BestResponseAction} ";
            //return $"BestResponse {BackupBestResponseAction} {NodeInformation[bestResponseNumeratorDimension, PlayerIndex]}/{NodeInformation[bestResponseDenominatorDimension, PlayerIndex]}";
        }

        public void Initialize(bool clearPastValues = false)
        {
            ResetNodeInformation(totalDimensions, NumPossibleActions);
            BackupNodeInformation = null;
            double probability = 1.0 / (double)NumPossibleActions;
            if (double.IsNaN(probability))
                throw new Exception();
            BestResponseAction = 1;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                NodeInformation[currentProbabilityDimension, a - 1] = probability;
                NodeInformation[currentProbabilityForOpponentDimension, a - 1] = probability;
                NodeInformation[averageStrategyProbabilityDimension, a - 1] = probability;
                NodeInformation[bestResponseNumeratorDimension, a - 1] = 0;
                NodeInformation[bestResponseDenominatorDimension, a - 1] = 0;
                NodeInformation[cumulativeRegretDimension, a - 1] = 0;
                NodeInformation[cumulativeStrategyDimension, a - 1] = 0;
                NodeInformation[adjustedWeightsDimension, a - 1] = 1.0;
                NodeInformation[sumRegretTimesInversePiDimension, a - 1] = 0;
                NodeInformation[sumInversePiDimension, a - 1] = 0;
                NodeInformation[lastCumulativeStrategyIncrementsDimension, a - 1] = 0;
                NodeInformation[scratchDimension, a - 1] = 0;

            }
            if (EvolutionSettings.RecordPastValues && clearPastValues) 
            {
                PastValues = new double[EvolutionSettings.RecordPastValues_TargetNumberToRecord, NumPossibleActions];
                PastValuesCumulativeStrategyDiscounts = new double[EvolutionSettings.RecordPastValues_TargetNumberToRecord];
            }
        }

        private void ResetNodeInformation(int numDimensions, int numPossibleActions)
        {
            NodeInformation = new double[numDimensions, numPossibleActions];
            for (int i = 0; i < numDimensions; i++)
                for (int j = 0; j < numPossibleActions; j++)
                    NodeInformation[i, j] = 0;
        }

        public void ClearCumulativeStrategy()
        {
            for (int j = 0; j < NumPossibleActions; j++)
                NodeInformation[cumulativeStrategyDimension, j] = 0;
        }

        public void ClearBestResponse()
        {
            for (int i = bestResponseNumeratorDimension; i <= bestResponseDenominatorDimension; i++)
                for (int j = 0; j < NumPossibleActions; j++)
                    NodeInformation[i, j] = 0;
            BestResponseDeterminedFromIncrements = false;
        }

        public void ClearAverageStrategyTally()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeStrategyDimension, a] = 0;
        }

        public void ClearCumulativeRegrets()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                NodeInformation[cumulativeRegretDimension, a] = 0;
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

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.InformationSet;
        }

        #endregion

        #region Best response

        public void DetermineBestResponseAction()
        {
            double bestRatio = 0;
            int best = 0;
            if (BestResponseOptions == null)
                BestResponseOptions = new double[NumPossibleActions];
            for (int action = 1; action <= NumPossibleActions; action++)
            {
                double denominator = NodeInformation[bestResponseDenominatorDimension, action - 1];
                if (denominator == 0)
                    return; // no best response data available
                double ratio = NodeInformation[bestResponseNumeratorDimension, action - 1] / denominator;
                BestResponseOptions[action - 1] = ratio;
                if (action == 1 || ratio > bestRatio)
                {
                    best = action;
                    bestRatio = ratio;
                }
            }
            BestResponseAction = (byte)best;
            BestResponseDeterminedFromIncrements = true;
        }

        public void GetBestResponseProbabilities(Span<double> probabilities)
        {
            int bestResponse = BestResponseAction;
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
            BestResponseDeterminedFromIncrements = false;
        }

        public void SetBestResponse_NumeratorAndDenominator(int action, double numerator, double denominator)
        {
            NodeInformation[bestResponseNumeratorDimension, action - 1] = numerator;
            NodeInformation[bestResponseDenominatorDimension, action - 1] = denominator;
            BestResponseDeterminedFromIncrements = false;
        }

        #endregion

        #region AcceleratedBestResponse

        // information for accelerated best response
        public double SelfReachProbability, OpponentsReachProbability;
        public InformationSetNode PredecessorInformationSetForPlayer;
        public byte ActionTakenAtPredecessorSet;
        public bool BestResponseMayReachHere;
        public bool LastBestResponseMayReachHere;
        public ByteList LastActionsList => PathsFromPredecessor.Last().ActionsList; // so that we can find corresponding path in predecessor
        
        public List<PathFromPredecessorInfo> PathsFromPredecessor;
        /// <summary>
        /// A list for each action of the list of histories (one for each path from predecessor) from this node to possible successors given that action.
        /// </summary>
        public List<List<NodeActionsMultipleHistories>> PathsToSuccessors;
        public (bool consideredForPruning, bool prunable)[] PrunableActions;
        public double LastBestResponseValue => BestResponseOptions?[BestResponseAction - 1] ?? 0; 
        public double[] BestResponseOptions; // one per action for this player
        public double[] AverageStrategyResultsForPathFromPredecessor;
        public FloatSet[] CustomResultForPathFromPredecessor;
        public int NumVisitsFromPredecessorToGetAverageStrategy;
        public int NumVisitsFromPredecessorToGetCustomResult;

        public void AcceleratedBestResponse_CalculateReachProbabilities(bool determinePrunability)
        {
            if (PredecessorInformationSetForPlayer == null)
                SelfReachProbability = 1.0;
            else
                SelfReachProbability = PredecessorInformationSetForPlayer.SelfReachProbability * PredecessorInformationSetForPlayer.GetAverageStrategy(ActionTakenAtPredecessorSet);

            OpponentsReachProbability = 0;

            if (PrunableActions == null)
                PrunableActions = new (bool consideredForPruning, bool prunable)[NumPossibleActions];
            for (int i = 0; i < NumPossibleActions; i++)
                PrunableActions[i] = (false, true); // not considered, but assume prunable if considered until shown otherwise. This may then be changed in a later information set by the code immediately below.

            double sumProbabilitiesSinceOpponentInformationSets = 0;
            foreach (var pathFromPredecessor in PathsFromPredecessor)
            {
                double predecessorOpponentsReachProbability = PredecessorInformationSetForPlayer?.PathsFromPredecessor[pathFromPredecessor.IndexInPredecessorsPathsFromPredecessor].Probability ?? 1.0;
                double pathProbabilityFromPredecessor;
                if (determinePrunability)
                {
                    (double pathProbabilityFromPredecessor2, double probabilitySinceOpponentInformationSet, InformationSetNode mostRecentOpponentInformationSet, byte actionAtOpponentInformationSet) = pathFromPredecessor.Path.GetProbabilityOfPathPlus();
                    pathProbabilityFromPredecessor = pathProbabilityFromPredecessor2;
                    pathFromPredecessor.MostRecentOpponentInformationSet = mostRecentOpponentInformationSet;
                    pathFromPredecessor.ActionAtOpponentInformationSet = actionAtOpponentInformationSet;
                    pathFromPredecessor.ProbabilityFromMostRecentOpponent = probabilitySinceOpponentInformationSet;
                    sumProbabilitiesSinceOpponentInformationSets += probabilitySinceOpponentInformationSet;
                }
                else
                    pathProbabilityFromPredecessor = pathFromPredecessor.Path.GetProbabilityOfPath();
                double cumulativePathProbability = predecessorOpponentsReachProbability * pathProbabilityFromPredecessor;
                pathFromPredecessor.Probability = cumulativePathProbability;
                OpponentsReachProbability += cumulativePathProbability;
            }
            if (determinePrunability)
            {
                double prunabilityThreshold = EvolutionSettings.PruneOnOpponentStrategyThreshold;
                foreach (var pathFromPredecessor in PathsFromPredecessor)
                {
                    if (pathFromPredecessor.MostRecentOpponentInformationSet != null)
                    {
                        pathFromPredecessor.MostRecentOpponentInformationSet.PrunableActions[pathFromPredecessor.ActionAtOpponentInformationSet - 1].consideredForPruning = true;
                        if (pathFromPredecessor.ProbabilityFromMostRecentOpponent / sumProbabilitiesSinceOpponentInformationSets > prunabilityThreshold)
                        {
                            pathFromPredecessor.MostRecentOpponentInformationSet.PrunableActions[pathFromPredecessor.ActionAtOpponentInformationSet - 1].prunable = false;

                        }
                    }
                }
            }
        }

        public void AcceleratedBestResponse_CalculateBestResponseValues(byte numNonChancePlayers)
        {
            if (BestResponseOptions == null)
                BestResponseOptions = new double[Decision.NumPossibleActions];
            // Note: Each time the information set was visited in the initial tree walk set up, the algorithm will have recorded one or more paths to one or more successors, combined into a single NodeActionsMultipleHistories that reflects their relative probability weight. Thus, we have one NodeActionsMultipleHistories for each possible prior history. Each path includes opponent and chance actions, and each culminates in a successor -- either a final utilities for the player or a later information set for the player that has already been calculated. To calculate the value of an action, we need to calculate the averages of the NodeActionsMultipleHistories weighted by the probability that opponents play to here. We need calculate only this player's best response value, as the best response values for other players will be calculated on their own information sets.

            int numPathsFromPredecessor = PathsFromPredecessor.Count();
            if (AverageStrategyResultsForPathFromPredecessor == null)
                AverageStrategyResultsForPathFromPredecessor = new double[numPathsFromPredecessor];
            else for (int pathToHere = 0; pathToHere < numPathsFromPredecessor; pathToHere++)
                AverageStrategyResultsForPathFromPredecessor[pathToHere] = 0;
            if (CustomResultForPathFromPredecessor == null)
                CustomResultForPathFromPredecessor = new FloatSet[numPathsFromPredecessor];
            else for (int pathToHere = 0; pathToHere < numPathsFromPredecessor; pathToHere++)
                CustomResultForPathFromPredecessor[pathToHere] = new FloatSet();
            for (byte action = 1; action <= Decision.NumPossibleActions; action++)
            {
                var pathsToSuccessorsForAction = PathsToSuccessors[action - 1];
                if (pathsToSuccessorsForAction.Count() != numPathsFromPredecessor)
                    throw new Exception();
                double averageStrategyProbability = GetAverageStrategy(action);
                double accumulatedBestResponseNumerator = 0, accumulatedBestResponseDenominatorDenominator = 0;
                for (int pathToHere = 0; pathToHere < numPathsFromPredecessor; pathToHere++)
                {
                    (double unweightedSuccessorBestResponseValue, double averageStrategyValue, FloatSet customResult) = pathsToSuccessorsForAction[pathToHere].GetProbabilityAdjustedValueOfPaths(PlayerIndex);
                    AverageStrategyResultsForPathFromPredecessor[pathToHere] += averageStrategyValue * averageStrategyProbability; // for average strategy, we are weighting the path to successors for each path from predecessors by the average strategy probability.
                    CustomResultForPathFromPredecessor[pathToHere] = CustomResultForPathFromPredecessor[pathToHere].Plus(customResult.Times((float) averageStrategyProbability));
                    double opponentsReachProbabilityForPath = PathsFromPredecessor[pathToHere].Probability;
                    double weighted = unweightedSuccessorBestResponseValue * opponentsReachProbabilityForPath;
                    accumulatedBestResponseNumerator += weighted;
                    accumulatedBestResponseDenominatorDenominator += opponentsReachProbabilityForPath;
                }
                BestResponseOptions[action - 1] = accumulatedBestResponseDenominatorDenominator == 0 ? 0 : accumulatedBestResponseNumerator / accumulatedBestResponseDenominatorDenominator;
                if (action == 1 || BestResponseOptions[action - 1] > LastBestResponseValue)
                {
                    BestResponseAction = action;
                }
            }
            NumVisitsFromPredecessorToGetAverageStrategy = 0; // reset this so that we know which average strategy value to return
        }

        public void AcceleratedBestResponse_DetermineWhetherReachable()
        {
            // This assumes that we have calculated best responses and already run this on prior nodes.
            if (PredecessorInformationSetForPlayer == null)
                BestResponseMayReachHere = true;
            else
                BestResponseMayReachHere = PredecessorInformationSetForPlayer.BestResponseAction == ActionTakenAtPredecessorSet;
        }

        public void MoveAverageStrategyTowardBestResponse(int iteration, int maxIterations, double perturbation = 0)
        {
            if (!BestResponseMayReachHere && iteration > 2)
            {
                LastBestResponseMayReachHere = BestResponseMayReachHere;
                return;
            }
            const double InitialWeightMultiplier = 1.0; // greater than 1 means that we're discounting
            const double Curvature = 10.0;
            double weightMultiplier = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(InitialWeightMultiplier, 1.0, Curvature, (double)iteration / (double)maxIterations);
            double weightOnBestResponse = weightMultiplier / (double)iteration;
            const double maxWeightOnBestResponse = 0.5;
            if (weightOnBestResponse > maxWeightOnBestResponse || !LastBestResponseMayReachHere)
                weightOnBestResponse = maxWeightOnBestResponse;
            MoveAverageStrategyTowardBestResponse(weightOnBestResponse, perturbation);
            LastBestResponseMayReachHere = BestResponseMayReachHere;
        }

        private void MoveAverageStrategyTowardBestResponse(double weightOnBestResponse, double perturbation = 0)
        {
            double total = 0;
            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double currentAverageStrategyProbability = GetAverageStrategy(action);
                double bestResponseProbability = (BestResponseAction == action) ? 1.0 : 0.0;
                // double difference = bestResponseProbability - currentAverageStrategyProbability;
                double successorValue = (1.0 - weightOnBestResponse) * currentAverageStrategyProbability + weightOnBestResponse * bestResponseProbability;
                NodeInformation[cumulativeStrategyDimension, action - 1] = NodeInformation[averageStrategyProbabilityDimension, action - 1] = successorValue;
                total += successorValue;
            }
            if (Math.Abs(total - 1.0) > 1E-8)
                throw new Exception();
            if (perturbation != 0)
                PerturbAverageStrategy(perturbation, true);
        }

        public void SetAverageStrategyToBestResponse(double perturbation = 0)
        {
            double total = 0;
            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double currentAverageStrategyProbability = GetAverageStrategy(action);
                double bestResponseProbability = (BestResponseAction == action) ? 1.0 : 0.0;
                NodeInformation[cumulativeStrategyDimension, action - 1] = NodeInformation[averageStrategyProbabilityDimension, action - 1] = bestResponseProbability;
                total += bestResponseProbability;
            }
            if (Math.Abs(total - 1.0) > 1E-8)
                throw new Exception();
            if (perturbation != 0)
                PerturbAverageStrategy(perturbation, true);
        }

        public void PerturbAverageStrategy(double minValueForEachAction, bool includeCumulativeStrategy)
        {
            double totalPerturbation = NumPossibleActions * minValueForEachAction;
            if (totalPerturbation > 1.0)
            {
                totalPerturbation = 1.0;
                minValueForEachAction = 1.0 / NumPossibleActions;
            }
            double remainingAfterPerturbation = 1.0 - totalPerturbation;
            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                NodeInformation[averageStrategyProbabilityDimension, action - 1] = minValueForEachAction + NodeInformation[averageStrategyProbabilityDimension, action - 1] * remainingAfterPerturbation;
                if (includeCumulativeStrategy)
                    NodeInformation[cumulativeStrategyDimension, action - 1] = NodeInformation[averageStrategyProbabilityDimension, action - 1];
            }
        }

        #endregion

        #region Regrets

        public void IncrementLastRegret(byte action, double regretTimesInversePi, double inversePi)
        {
            NodeInformation[sumRegretTimesInversePiDimension, action - 1] += regretTimesInversePi;
            NodeInformation[sumInversePiDimension, action - 1] += inversePi;
        }
        public void IncrementLastRegret_Parallel(byte action, double regretTimesInversePi, double inversePi)
        {
            Interlocking.Add(ref NodeInformation[sumRegretTimesInversePiDimension, action - 1], regretTimesInversePi);
            Interlocking.Add(ref NodeInformation[sumInversePiDimension, action - 1], inversePi);
            //    Debug.WriteLine($"after increment: regret*invpi {regretTimesInversePi} inversePi {inversePi} numerator {NodeInformation[lastRegretNumeratorDimension, action - 1]} denominator {NodeInformation[lastRegretDenominatorDimension, action - 1]} fraction {NodeInformation[lastRegretNumeratorDimension, action - 1] / NodeInformation[lastRegretDenominatorDimension, action - 1]}");
        }

        public double NormalizeRegret(double regret, bool makeStrictlyPositive)
        {
            // best performance possible occurs if expected value is MaxPossibleThisPlayer when overall expected value is MinPossibleThisPlayer. worst performance possible occurs if regret is MinPossibleThisPlayer when overall expected value is MaxPossibleThisPlayer. Regret can range from -(MaxPossible - MinPossible) to +(MaxPossible - MinPossible). Thus, Regret + (MaxPossible - MinPossible) can range from 0 to 2*(MaxPossible - MinPossible). So, we can normalize regret to be from 0 to 1 by calculating (regret + range) / (2 * range).
            double range = MaxPossibleThisPlayer - MinPossibleThisPlayer;
            if (makeStrictlyPositive)
            {
                double normalizedRegret = (regret + range) / (2 * range);
                if (normalizedRegret < 0 || normalizedRegret > 1.0)
                    throw new Exception("Invalid normalized regret");
                return normalizedRegret;
            }
            else
                return regret / range;
        }

        public string GetCumulativeRegretsString()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(GetCumulativeRegret(a));
            return String.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public double GetCumulativeRegret(int action)
        {
            return NodeInformation[cumulativeRegretDimension, action - 1];
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

        public (double, int) GetSumPositiveCumulativeRegrets_AndNumberPositive()
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

        public void IncrementCumulativeRegret_Parallel(int action, double amount, bool incrementBackup, int backupRegretsTrigger = int.MaxValue, bool incrementVisits = false)
        {
            Interlocking.Add(ref NodeInformation[cumulativeRegretDimension, action - 1], amount);
        }

        public void IncrementCumulativeRegret(int action, double amount, int backupRegretsTrigger = int.MaxValue, bool incrementVisits = false)
        {
            NodeInformation[cumulativeRegretDimension, action - 1] += amount;
        }

        #endregion

        #region Cumulative strategies

        public void IncrementLastCumulativeStrategyIncrements(byte action, double strategyProbabilityTimesSelfReachProbability)
        {
            NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1] += strategyProbabilityTimesSelfReachProbability;
        }

        public void IncrementLastCumulativeStrategyIncrements_Parallel(byte action, double strategyProbabilityTimesSelfReachProbability)
        {
            Interlocking.Add(ref NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1], strategyProbabilityTimesSelfReachProbability);
            //Interlocked.Increment(ref NumRegretIncrements);
        }

        public double GetLastCumulativeStrategyIncrement(byte action) => NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1];

        private void UpdateCumulativeAndAverageStrategies(int iteration, double averageStrategyAdjustment, bool normalizeCumulativeStrategyIncrements, bool resetPreviousCumulativeStrategyIncrements)
        {

            RecordProbabilitiesAsPastValues(iteration, averageStrategyAdjustment); // these are the average strategies played, and thus shouldn't reflect the updates below

            if (resetPreviousCumulativeStrategyIncrements)
                ClearCumulativeStrategy();
            UpdateCumulativeStrategy(averageStrategyAdjustment, normalizeCumulativeStrategyIncrements);

            UpdateAverageStrategy();

            AverageStrategyAdjustmentsSum += averageStrategyAdjustment;
        }

        private void UpdateCumulativeStrategy(double averageStrategyAdjustment, bool normalizeCumulativeStrategyIncrements)
        {
            double lastCumulativeStrategyIncrementSum = 0;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                // double lastRegret = NodeInformation[lastRegretDimension, a - 1];
                lastCumulativeStrategyIncrementSum += NodeInformation[lastCumulativeStrategyIncrementsDimension, a - 1];
            }
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double normalizedCumulativeStrategyIncrement = 0;
                if (lastCumulativeStrategyIncrementSum == 0) // can be zero if pruning means that an information set is never reached -- in this case we still need to update the average strategy if normalizing.
                {
                    if (normalizeCumulativeStrategyIncrements)
                        normalizedCumulativeStrategyIncrement = NodeInformation[currentProbabilityDimension, a - 1];
                }
                else
                {
                    normalizedCumulativeStrategyIncrement = NodeInformation[lastCumulativeStrategyIncrementsDimension, a - 1];
                    if (normalizeCumulativeStrategyIncrements)
                        normalizedCumulativeStrategyIncrement /= lastCumulativeStrategyIncrementSum; // This is the key effect of normalizing. This will make all probabilities add up to 1, so that even if this is an iteration where it is very unlikely that we reach the information set, this iteration will not be discounted relative to iterations where we do reach the information set. It is useful to do this when discounting, since otherwise it may take trillions of iterations to make up for a few early iterations. But later on, we want to be giving greater weight to iterations in which the self-play probability is higher.
                }
                double adjustedIncrement = averageStrategyAdjustment * normalizedCumulativeStrategyIncrement; // ... but here we do our regular discounting so later iterations can count more than earlier ones
                NodeInformation[cumulativeStrategyDimension, a - 1] += adjustedIncrement;
                NodeInformation[lastCumulativeStrategyIncrementsDimension, a - 1] = 0;
            }
        }

        private void UpdateAverageStrategy()
        {
            double sumCumulativeStrategies = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                sumCumulativeStrategies += NodeInformation[cumulativeStrategyDimension, a - 1];
            }
            // Also, calculate average strategies
            if (sumCumulativeStrategies > 0)
            {
                Func<byte, double> avgStrategyFunc = a => NodeInformation[cumulativeStrategyDimension, a - 1] / sumCumulativeStrategies;
                SetProbabilitiesFromFunc(averageStrategyProbabilityDimension, SmallestProbabilityInAverageStrategy, true, false, avgStrategyFunc);
            }
        }
        public double[] GetCumulativeStrategiesAsArray()
        {
            List<double> probs = new List<double>();
            for (byte a = 1; a <= NumPossibleActions; a++)
                probs.Add(NodeInformation[cumulativeStrategyDimension, a - 1]);
            return probs.ToArray();
        }

        public string GetCumulativeStrategiesString()
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

        public static bool ZeroOutInCalculatingAverageStrategies = false;
        public static double ZeroOutBelow = 1E-50;

        public void CalculateAverageStrategyFromCumulative(Span<double> probabilities)
        {
            double sum = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
                sum += GetCumulativeStrategy(a);

            bool zeroedOutSome = false;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double quotient = GetCumulativeStrategy(a) / sum;
                if (quotient > 0 && ZeroOutInCalculatingAverageStrategies && quotient < ZeroOutBelow)
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

        public double[] GetLastRegretIncrementsAsArray()
        {
            double[] array = new double[NumPossibleActions];
            for (int a = 1; a <= NumPossibleActions; a++)
                array[a - 1] = NodeInformation[sumInversePiDimension, a - 1] == 0 ? 0 : NodeInformation[sumRegretTimesInversePiDimension, a - 1] / NodeInformation[sumInversePiDimension, a - 1];
            return array;
        }

        public double[] GetLastCumulativeStrategyIncrementAsArray()
        {
            double[] array = new double[NumPossibleActions];
            for (int a = 1; a <= NumPossibleActions; a++)
                array[a - 1] = GetLastCumulativeStrategyIncrement((byte) a);
            return array;
        }

        public double[] GetAverageStrategiesAsArray()
        {
            double[] array = new double[NumPossibleActions];

            Span<double> actionProbabilities = stackalloc double[NumPossibleActions];
            CalculateAverageStrategyFromCumulative(actionProbabilities);
            for (int a = 0; a < NumPossibleActions; a++)
                array[a] = actionProbabilities[a];
            return array;
        }

        public string GetAverageStrategiesAsString()
        {
            return String.Join(", ", GetAverageStrategiesAsArray().Select(x => x.ToSignificantFigures(3)));
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

        public List<double> GetRegretMatchingProbabilitiesList()
        {
            Span<double> probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetRegretMatchingProbabilities(probabilitiesToSet);
            return Util.ListExtensions.GetSpanAsList(probabilitiesToSet, NumPossibleActions);
        }

        public List<double> GetEqualProbabilitiesList()
        {
            // NOTE: Not thread-safe
            Span<double> probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetEqualProbabilitiesRegretMatching(probabilitiesToSet);
            return Util.ListExtensions.GetSpanAsList(probabilitiesToSet, NumPossibleActions);
        }

        public void GetRegretMatchingProbabilities(Span<double> probabilitiesToSet)
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

        /// <summary>
        /// Get regret matching adjusted probabilities, but adjusted so that unlikely actions are sometimes sampled.
        /// </summary>
        /// <param name="probabilitiesToSet">A pointer to the probabilities to set, one per action.</param>
        /// <param name="epsilon">The weight (from 0 to 1) on equal probabilities rather than on regret-matching probabilities.</param>
        public void GetEpsilonAdjustedRegretMatchingProbabilities(Span<double> probabilitiesToSet, double epsilon)
        {
            GetRegretMatchingProbabilities(probabilitiesToSet);
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = epsilon * equalProbabilities + (1.0 - epsilon) * probabilitiesToSet[a - 1];
        }

        public void GetEqualProbabilitiesRegretMatching(Span<double> probabilitiesToSet)
        {
            double equalProbabilities = 1.0 / NumPossibleActions;
            for (byte a = 1; a <= NumPossibleActions; a++)
                probabilitiesToSet[a - 1] = equalProbabilities;
        }

        public void GetRegretMatchingProbabilities_WithPruning(Span<double> probabilitiesToSet)
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

        #endregion

        #region Post-iteration updates

        // Note: The first two methods must be used if we don't have a guarantee that updating will take place before each iteration.

        public void PostIterationUpdates(int iteration, PostIterationUpdaterBase updater, double averageStrategyAdjustment, bool normalizeCumulativeStrategyIncrements, bool resetPreviousCumulativeStrategyIncrements, double? pruneOpponentStrategyBelow, bool pruneOpponentStrategyIfDesignatedPrunable, bool addOpponentTremble, bool weightResultByInversePiForIteration, double? randomNumberToSelectSingleOpponentAction)
        {
            UpdateCumulativeAndAverageStrategies(iteration, averageStrategyAdjustment, normalizeCumulativeStrategyIncrements, resetPreviousCumulativeStrategyIncrements);
            DetermineBestResponseAction();
            ClearBestResponse();
            updater.UpdateInformationSet(this, weightResultByInversePiForIteration);
            UpdateOpponentProbabilities(iteration, pruneOpponentStrategyBelow, pruneOpponentStrategyIfDesignatedPrunable, addOpponentTremble, randomNumberToSelectSingleOpponentAction);
        }

        private void UpdateOpponentProbabilities(int iteration, double? pruneOpponentStrategyBelow, bool pruneOpponentStrategyIfDesignatedPrunable, bool addOpponentTremble, double? randomNumberToSelectSingleOpponentAction)
        {
            if (iteration <= Decision.WarmStartThroughIteration)
            {
                for (int a = 1; a <= NumPossibleActions; a++)
                {
                    NodeInformation[currentProbabilityForOpponentDimension, a - 1] = (a == Decision.WarmStartValue) ? 1.0 : 0;
                }
                return;
            }
            // The opponent's probability is the probability to use when traversing an opponent information set during optimization. 
            bool pruning = pruneOpponentStrategyIfDesignatedPrunable || (pruneOpponentStrategyBelow != null && pruneOpponentStrategyBelow != 0);
            double probabilityThreshold = pruning && !pruneOpponentStrategyIfDesignatedPrunable ? (double)pruneOpponentStrategyBelow : SmallestProbabilityRepresented;
            Func<byte, double> currentProbabilityFunc;
            if (EvolutionSettings.CFR_OpponentPlaysAverageStrategy)
                currentProbabilityFunc = a => NodeInformation[averageStrategyProbabilityDimension, a - 1];
            else
                currentProbabilityFunc = a => NodeInformation[currentProbabilityDimension, a - 1];
            SetProbabilitiesFromFunc(currentProbabilityForOpponentDimension, probabilityThreshold, pruning, pruneOpponentStrategyIfDesignatedPrunable, currentProbabilityFunc);

            if (addOpponentTremble)
                AddTrembleToOpponentProbabilities(0.1); // note: doesn't seem to make much difference

            if (randomNumberToSelectSingleOpponentAction != null)
            {
                double p = (double) randomNumberToSelectSingleOpponentAction;
                // find corresponding action based on cumulative probabilities
                double total = 0;
                byte a;
                for (a = 1; a <= NumPossibleActions - 1; a++) // look at all actions but last (if we don't break, then it's the last action)
                {
                    total += currentProbabilityFunc(a);
                    if (total > p)
                        break;
                }
                // set probabilities to 1 or 0
                for (byte a2 = 1; a2 <= NumPossibleActions; a2++)
                {
                    NodeInformation[currentProbabilityForOpponentDimension, a2 - 1] = (a == a2) ? 1.0 : 0;
                }
            }
        }

        public void SetProbabilitiesFromFunc(int probabilityDimension, double probabilityThreshold, bool setBelowThresholdToZero, bool usePrunabilityInsteadOfThreshold, Func<byte, double> initialProbabilityFunc)
        {
            double setBelowThresholdTo = setBelowThresholdToZero ? 0 : probabilityThreshold;
            byte largestAction = 0;
            double largestValue = 0;
            double sumExcludingLargest = 0;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double p = initialProbabilityFunc(a);
                if (double.IsNaN(p))
                    throw new Exception();
                if ((!usePrunabilityInsteadOfThreshold && p <= probabilityThreshold) || (usePrunabilityInsteadOfThreshold && PrunableActions != null && PrunableActions[a - 1].consideredForPruning && PrunableActions[a - 1].prunable))
                    p = setBelowThresholdTo;
                NodeInformation[probabilityDimension, a - 1] = p;
                if (a == 1 || p > largestValue)
                {
                    if (a != 1)
                        sumExcludingLargest += largestValue;
                    largestAction = a;
                    largestValue = p;
                }
                else
                    sumExcludingLargest += p;
            }
            double remainingProbability = 1.0 - sumExcludingLargest; // note: still not guaranteed to create sum of exactly 1
            if (double.IsNaN(remainingProbability))
                throw new Exception();
            NodeInformation[probabilityDimension, largestAction - 1] = remainingProbability;
        }

        // TODO: Consider deleting this.
        private void AddTrembleToOpponentProbabilities(double trembleProportion)
        {
            if (!Decision.IsContinuousAction)
                return;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                NodeInformation[scratchDimension, a - 1] = NodeInformation[currentProbabilityForOpponentDimension, a - 1];
            }
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double original = NodeInformation[scratchDimension, a - 1];
                double trembleSizeEachDirection = original * trembleProportion * 0.5;
                if (original > SmallestProbabilityRepresented)
                {
                    if (a > 1)
                    {
                        NodeInformation[currentProbabilityForOpponentDimension, a - 2] += trembleSizeEachDirection;
                        NodeInformation[currentProbabilityForOpponentDimension, a - 1] -= trembleSizeEachDirection;
                    }
                    if (a < NumPossibleActions)
                    {
                        NodeInformation[currentProbabilityForOpponentDimension, a] += trembleSizeEachDirection;
                        NodeInformation[currentProbabilityForOpponentDimension, a - 1] -= trembleSizeEachDirection;
                    }
                }
            }
        }

        private void RemoveTremble()
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                NodeInformation[currentProbabilityForOpponentDimension, a - 1] = NodeInformation[scratchDimension, a - 1];
            }
        }

        private void RecordProbabilitiesAsPastValues(int iteration, double averageStrategyAdjustment)
        {
            if (EvolutionSettings.RecordPastValues_AtIteration(iteration))
            {
                if (LastPastValueIndexRecorded + 1 < PastValuesCumulativeStrategyDiscounts.Length)
                {
                    LastPastValueIndexRecorded++;
                    if (LastPastValueIndexRecorded == 0)
                        PastValuesCumulativeStrategyDiscounts[0] = averageStrategyAdjustment;
                    else
                        PastValuesCumulativeStrategyDiscounts[LastPastValueIndexRecorded] = PastValuesCumulativeStrategyDiscounts[LastPastValueIndexRecorded] + averageStrategyAdjustment;
                    for (byte a = 1; a <= NumPossibleActions; a++)
                        PastValues[LastPastValueIndexRecorded, a - 1] = GetCurrentProbability(a, false);
                }
            }
        }

        public void SetAverageStrategyToPastValue(int pastValueIndex)
        {
            CreateBackup();
            for (byte a = 1; a <= NumPossibleActions; a++)
                NodeInformation[averageStrategyProbabilityDimension, a - 1] = PastValues[pastValueIndex, a - 1];
        }

        #endregion

        #region Hedge

        // Note: This requires further testing

        // hedge probing
        double V = 0; // V parameter in Cesa-Bianchi
        double MaxAbsRegretDiff = 0;
        double E = 1;
        double Nu;
        static double C = Math.Sqrt((2 * (Math.Sqrt(2) - 1.0)) / (Math.Exp(1.0) - 2));

        private void UpdateHedgeInfoAfterIteration()
        {
            double firstSum = 0, secondSum = 0;
            double minLastRegret = 0, maxLastRegret = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double lastPi = NodeInformation[currentProbabilityDimension, a - 1];
                double lastRegret = NodeInformation[sumRegretTimesInversePiDimension, a - 1];
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
                NodeInformation[cumulativeRegretDimension, a - 1] += NodeInformation[sumRegretTimesInversePiDimension, a - 1];
                double numeratorForThisAction = Math.Exp(Nu * NodeInformation[cumulativeRegretDimension, a - 1]);
                NodeInformation[scratchDimension, a - 1] = numeratorForThisAction; // alternative implementation would reuse lastRegretDimension
                if (double.IsNaN(numeratorForThisAction))
                    throw new Exception("Regrets too high. Must scale all regrets.");
                denominatorForAllActions += numeratorForThisAction;
            }
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double quotient = NodeInformation[scratchDimension, a - 1] / denominatorForAllActions;
                NodeInformation[currentProbabilityDimension, a - 1] = quotient;
                if (double.IsNaN(quotient))
                    throw new Exception("Regrets too high. Must scale all regrets");
            }
        }

        #endregion

        #region Get probabilities

        public double GetAverageStrategy(byte action)
        {
            return NodeInformation[averageStrategyProbabilityDimension, action - 1];
        }

        public void GetAverageStrategyProbabilities(Span<double> probabilitiesToSet)
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                probabilitiesToSet[a - 1] = NodeInformation[averageStrategyProbabilityDimension, a - 1];
            }
        }

        public void GetCorrelatedEquilibriumProbabilities(double randomNumberToChooseIteration, Span<double> probabilities)
        {
            int pastValuesCount = LastPastValueIndexRecorded;
            double cumulativeDiscountLevelToSeek = pastValuesCount * randomNumberToChooseIteration;
            if (PastValuesCumulativeStrategyDiscounts == null || PastValuesCumulativeStrategyDiscounts.All(x => x == 0))
            {
                int index2 = (int)(randomNumberToChooseIteration * LastPastValueIndexRecorded);
                for (int a = 0; a < NumPossibleActions; a++)
                    probabilities[a] = PastValues[index2, a];
                return;
            }

            Span<double> pastValueDiscounts = new Span<double>(PastValuesCumulativeStrategyDiscounts, 0, pastValuesCount);
            int index = pastValueDiscounts.BinarySearch(cumulativeDiscountLevelToSeek, Comparer<double>.Default);
            if (index < 0)
                index = ~index; // when negative, index is the bitwise complement of the first index larger than the value sought
            if (index == pastValuesCount)
                index--; // should be very rare.
            for (int a = 0; a < NumPossibleActions; a++)
                probabilities[a] = PastValues[index, a];
        }

        public void GetCurrentProbabilities(Span<double> probabilitiesToSet, bool opponentProbabilities)
        {
            int probabilityDimension = opponentProbabilities ? currentProbabilityForOpponentDimension : currentProbabilityDimension;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                probabilitiesToSet[a - 1] = NodeInformation[probabilityDimension, a - 1];
            }
        }

        public double GetCurrentProbability(byte a, bool opponentProbabilities)
        {
            int probabilityDimension = opponentProbabilities ? currentProbabilityForOpponentDimension : currentProbabilityDimension;
            return NodeInformation[probabilityDimension, a - 1];
        }

        public string GetCurrentProbabilitiesAsString()
        {
            return String.Join(", ", GetCurrentProbabilitiesAsArray().Select(x => x.ToSignificantFigures(3)));
        }

        public void GetCurrentProbabilities(Span<double> probabilitiesToSet)
        {
            bool done = false;
            while (!done)
            { // without this outer loop, there is a risk that when using parallel code, our probabilities will not add up to 1
                double total = 0;
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    probabilitiesToSet[a - 1] = NodeInformation[currentProbabilityDimension, a - 1];
                    total += probabilitiesToSet[a - 1];
                }
                done = Math.Abs(1.0 - total) < 1E-7;
            }
        }

        public double[] GetCurrentProbabilitiesAsArray()
        {
            double[] array = new double[NumPossibleActions];

            Span<double> actionProbabilities = stackalloc double[NumPossibleActions];
            GetCurrentProbabilities(actionProbabilities);
            for (int a = 0; a < NumPossibleActions; a++)
                array[a] = actionProbabilities[a];
            return array;
        }

        #endregion

        #region General manipulation

        public void SetToMixedStrategy(double[] strategy, bool setAverageAndCumulativeStrategy)
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                NodeInformation[currentProbabilityDimension, a - 1] = strategy[a - 1];
                NodeInformation[currentProbabilityForOpponentDimension, a - 1] = strategy[a - 1];
                if (setAverageAndCumulativeStrategy)
                {
                    NodeInformation[averageStrategyProbabilityDimension, a - 1] = strategy[a - 1];
                    NodeInformation[cumulativeStrategyDimension, a - 1] = strategy[a - 1];
                }
            }
        }

        public void SetToPureStrategy(byte action, bool setAverageAndCumulativeStrategy)
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double v = (action == a) ? 1.0 : 0;
                NodeInformation[currentProbabilityDimension, a - 1] = v;
                NodeInformation[currentProbabilityForOpponentDimension, a - 1] = v;
                if (setAverageAndCumulativeStrategy)
                {
                    NodeInformation[averageStrategyProbabilityDimension, a - 1] = v;
                    NodeInformation[cumulativeStrategyDimension, a - 1] = v;
                }
            }
        }

        public byte GetPureStrategy()
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                if (NodeInformation[currentProbabilityDimension, a - 1] == 1.0)
                    return a;
            }
            throw new Exception("No pure strategy found.");
        }

        public void CreateBackup()
        {
            BackupNodeInformation = (double[,])NodeInformation.Clone();
            BackupBestResponseAction = BestResponseAction;
            BackupAverageStrategyAdjustmentsSum = AverageStrategyAdjustmentsSum;
        }

        public void RestoreBackup()
        {
            NodeInformation = (double[,])BackupNodeInformation.Clone();
            BestResponseAction = BackupBestResponseAction;
            AverageStrategyAdjustmentsSum = BackupAverageStrategyAdjustmentsSum;
        }



        public void ZeroLowProbabilities(double threshold)
        {
            double reallocated = 0;
            for (byte action = 1; action < NumPossibleActions; action++)
            {
                double p = GetAverageStrategy(action);
                if (p < threshold)
                {
                    reallocated += p;
                    NodeInformation[averageStrategyProbabilityDimension, action - 1] = 0;
                    NodeInformation[cumulativeStrategyDimension, action - 1] = 0;
                }
            }
            if (reallocated > 0)
            {
                double multiplyBy = 1.0 / (1.0 - reallocated);
                for (byte action = 1; action < NumPossibleActions; action++)
                    NodeInformation[averageStrategyProbabilityDimension, action - 1] *= multiplyBy;
            }
        }

        public void AddMixedness(double minProbabilitySecondBest, bool continuousDecisionsOnly)
        {
            if (continuousDecisionsOnly && !Decision.IsContinuousAction)
                return;
            double[] averageStrategyProbabilities = GetAverageStrategiesAsArray();
            var indexed = averageStrategyProbabilities.Select((value, index) => (value, index)).ToList();
            var highest = indexed.OrderByDescending(x => x.value).First();
            var secondHighest = indexed.OrderByDescending(x => x.value).Skip(1).First();
            if (highest.value < minProbabilitySecondBest * 2)
                minProbabilitySecondBest = highest.value / 2.0;
            if (secondHighest.value < minProbabilitySecondBest)
            {
                double reallocation = minProbabilitySecondBest - secondHighest.value;
                NodeInformation[averageStrategyProbabilityDimension, highest.index] -= reallocation;
                NodeInformation[averageStrategyProbabilityDimension, secondHighest.index] += reallocation;
            }
            var revisedAverageStrategyProbabilities = GetAverageStrategiesAsArray();
            if (Math.Abs(revisedAverageStrategyProbabilities.Sum() - 1) > 1E-8)
                throw new Exception();
        }

        public void AddNoiseToBestResponse(double probabilityChange, int iteration)
        {

            if (!Decision.IsContinuousAction)
                return;
            double r = new ConsistentRandomSequenceProducer(InformationSetNodeNumber).GetDoubleAtIndex(iteration);
            if (r < probabilityChange)
            {
                bool up;
                if (BestResponseAction == 1)
                    up = true;
                else if (BestResponseAction == NumPossibleActions)
                    up = false;
                else
                    up = r < probabilityChange / 2.0;
                if (up)
                    BestResponseAction++;
                else
                    BestResponseAction--;
            }
        }

        // past value product -- used in autogenerated correlated equilibrium code (referenced by string, so it appears to have zero references.
        public double PVP(int iteration, byte action, Func<double> multiplyByFn)
        {
            double d = PastValues[iteration, action - 1];
            if (d < 1E-8)
                return 0;
            return d * multiplyByFn();
        }

        public void SetAverageStrategyFromPastValues()
        {
            if (PastValues != null)
                for (int a = 1; a <= NumPossibleActions; a++)
                {
                    double total = 0;
                    for (int p = 0; p <= LastPastValueIndexRecorded; p++)
                    {
                        total += PastValues[p, a - 1];
                    }
                    NodeInformation[averageStrategyProbabilityDimension, a - 1] = (total / (double)(LastPastValueIndexRecorded + 1));
                }
        }

        public void PastValuesAnalyze()
        {
            string avgDistanceString = null, rangesString = null;
            List<(int startIteration, int endIteration, int significantActions)> ranges = null;
            if (PastValues != null && LastPastValueIndexRecorded > -1)
            {
                int total = LastPastValueIndexRecorded;
                int numToTest = 1000;
                double sumDistances = 0;
                for (int i = 0; i < numToTest; i++)
                {
                    // Note: we're not weighting these here
                    int j0 = RandomGenerator.Next(total);
                    int j1 = RandomGenerator.Next(total);
                    double sumSqDiffs = 0;
                    for (int k = 0; k < NumPossibleActions; k++)
                    {
                        double diff = PastValues[j0, k] - PastValues[j1, k];
                        sumSqDiffs += diff * diff;
                    }
                    double distance = Math.Sqrt(sumSqDiffs);
                    sumDistances += distance;
                }
                double avgDistance = sumDistances / (double)numToTest;
                avgDistanceString = avgDistance.ToSignificantFigures(3);

                ranges = new List<(int startIteration, int endIteration, int significantActions)>();
                int activeRangeStart = total / 2  /* focus on second half */;
                int significantActionsInRange = 0;

                for (int i = activeRangeStart; i < total; i++)
                {
                    int significantActions = 0;
                    for (int k = 0; k < NumPossibleActions; k++)
                    {
                        if (PastValues[i, k] >= 0.01)
                        {
                            significantActions |= (1 << k);
                        }
                    }
                    if (significantActions != significantActionsInRange || i == total - 1)
                    {
                        if (i > 0)
                            ranges.Add(((int)activeRangeStart, i - 1, significantActionsInRange));
                        activeRangeStart = i;
                        significantActionsInRange = significantActions;
                    }
                }
                int minNumIterations = 1;
                ranges = ranges.Where(x => x.endIteration - x.startIteration + 1 >= minNumIterations).ToList();

                string GetActionsAsString(int sigActionsBits)
                {
                    List<int> sigActions = new List<int>();
                    for (int i = 0; i < 32; i++)
                        if ((sigActionsBits & (1 << i)) != 0)
                            sigActions.Add(i);
                    return String.Join(",", sigActions);
                }

                rangesString = String.Join("; ", ranges.Select(x => $"({x.startIteration}-{x.endIteration}): {GetActionsAsString(x.significantActions)}"));
            }
            string hedgeString = GetCurrentProbabilitiesAsString();
            double[] averageStrategies = GetAverageStrategiesAsArray();
            string avgStratString = GetAverageStrategiesAsString();
            bool avgStratSameAsBestResponse = averageStrategies[BestResponseAction - 1] > 0.9999999;
            //if (ranges.Count() > 1)
            TabbedText.WriteLine($"{(avgStratSameAsBestResponse ? "*" : "")} decision {Decision.Name} Information set {InformationSetNodeNumber} bestrespon {BestResponseAction} hedge {hedgeString} avg {avgStratString} avg distance {avgDistanceString} ranges: {rangesString}");
        }

        #endregion

    }
}
