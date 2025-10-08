using ACESim;
using ACESimBase.GameSolvingSupport.PostIterationUpdater;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Randomization;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.TaskManagement;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.GameTree
{

    [Serializable]
    public class InformationSetNode : IGameState, IAnyNode
    {

        #region Properties, members, and constants

        public const double SmallestProbabilityRepresented = 1E-16; // We make this considerably greater than Double.Epsilon (but still very small), because (1) otherwise when we multiply the value by anything < 1, we get 0, and this makes it impossible to climb out of being a zero-probability action, (2) we want to be able to represent 1 - probability.
        public const double SmallestProbabilityInAverageStrategy = 1E-5; // This is greater still -- when calculating average strategies, we disregard very small probabilities (which presumably are on their way to zero)

        public int InformationSetNodeNumber;
        public int OverallIndexAmongActions;
        public bool IsChanceNode => false;
        public bool IsUtilitiesNode => false;

        public int GetInformationSetNodeNumber() => InformationSetNodeNumber;
        public Decision Decision { get; set; }

        public double[] GetNodeValues() => GetCurrentProbabilitiesAsArray();
        public int? AltNodeNumber { get; set; }
        public int GetNumPossibleActions() => Decision.NumPossibleActions;
        public EvolutionSettings EvolutionSettings;
        public byte[] InformationSetContents;
        public string InformationSetContentsString => string.Join(",", InformationSetContents.Select(x => $"{x}").ToArray());
        public List<(byte decisionIndex, byte information)> LabeledInformationSet;
        public string InformationSetWithLabels(GameDefinition gd) => string.Join(";", LabeledInformationSet.Select(x => $"{gd.DecisionsExecutionOrder[x.decisionIndex].Name}: {x.information}"));
        public string InformationSetWithAlphabeticalLabels(GameDefinition gd) => string.Join(";", LabeledInformationSet.OrderBy(x => gd.DecisionsExecutionOrder[x.decisionIndex].Name).Select(x => $"{gd.DecisionsExecutionOrder[x.decisionIndex].Name}: {x.information}"));
        public byte[] InformationSetContentsSinceParent => ParentInformationSet == null ? InformationSetContents : InformationSetContents.Skip(ParentInformationSet.InformationSetContents.Length).ToArray();
        public string InformationSetContentsSinceParentString => string.Join(",", InformationSetContentsSinceParent);
        public byte DecisionByteCode => Decision.DecisionByteCode;
        public byte DecisionIndex;

        // the following are for sequence form
        /// <summary>
        /// An information set number starting numbering at 1 for this player only.
        /// </summary>
        public int PerPlayerNodeNumber;
        /// <summary>
        /// A number that, when added to a one-based action, will yield a cumulative choice action across all of a player's information sets, with the first cumulative choice action (ignoring the empty sequence) is 1 (i.e., the CumulativeChoiceNumber of the first information set is 0).
        /// </summary>
        public int CumulativeChoiceNumber;

        public byte PlayerIndex => Decision.PlayerIndex;

        public double[,] NodeInformation { get; set; }
        public double[,] BackupNodeInformation;

        public double[] MaxPossible, MinPossible;
        public double MaxPossibleThisPlayer, MinPossibleThisPlayer;

        public int LastPastValueIndexRecorded = -1;
        public List<double[]> PastValues;
        public List<double> PastValuesCumulativeStrategyDiscounts;

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
        
        private double[] _cumulativeStrategyCompensation;


        #endregion

        #region Hooks for NodeInformation mutations

        int _DEBUGcounter;

        /// <summary>
        /// Called right before a NodeInformation cell is about to be mutated.
        /// dimension: the first index (row). actionIndex: the second index (column, zero-based action).
        /// </summary>
        protected virtual void OnNodeInformationMutating(int dimension, int actionIndex) 
        {
            if (InformationSetNodeNumber == 0 && dimension == currentProbabilityDimension && actionIndex == 0)
            {
                // This is just a convenient place to put a breakpoint to see when the first information set's first action's current probability is changed.
                Console.WriteLine($"Changing from {NodeInformation[dimension, actionIndex]} (counter {_DEBUGcounter++})"); // DEBUG
            }
        }

        /// <summary>
        /// Called immediately after a NodeInformation cell has been mutated.
        /// dimension: the first index (row). actionIndex: the second index (column, zero-based action). value: the final value written.
        /// </summary>
        protected virtual void OnNodeInformationMutated(int dimension, int actionIndex, double value) 
        {
            if (InformationSetNodeNumber == 0 && dimension == currentProbabilityDimension && actionIndex == 0)
            {
                // This is just a convenient place to put a breakpoint to see when the first information set's first action's current probability is changed.
                Console.WriteLine($"Changing to {value}"); // DEBUG
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetNI(int dimension, int actionIndex, double value)
        {
            OnNodeInformationMutating(dimension, actionIndex);
            NodeInformation[dimension, actionIndex] = value;
            OnNodeInformationMutated(dimension, actionIndex, NodeInformation[dimension, actionIndex]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddNI(int dimension, int actionIndex, double delta)
        {
            OnNodeInformationMutating(dimension, actionIndex);
            NodeInformation[dimension, actionIndex] += delta;
            OnNodeInformationMutated(dimension, actionIndex, NodeInformation[dimension, actionIndex]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InterlockedAddNI(int dimension, int actionIndex, double delta)
        {
            OnNodeInformationMutating(dimension, actionIndex);
            Interlocking.Add(ref NodeInformation[dimension, actionIndex], delta);
            OnNodeInformationMutated(dimension, actionIndex, NodeInformation[dimension, actionIndex]);
        }

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
                PastValues = new List<double[]>();
                PastValuesCumulativeStrategyDiscounts = new List<double>();
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

            foreach (InformationSetNode x in all)
                x.ChildInformationSets = x.ChildInformationSets.OrderBy(x => x.InformationSetContentsSinceParentString).ToList();

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

        public string ToStringWithoutValues()
        {
            return $"ISet {GetInformationSetNodeNumber()} Alt {AltNodeNumber} {Decision.Name} ({Decision.Abbreviation})";
        }

        public override string ToString()
        {
            return $"Information set {GetInformationSetNodeNumber()}{(AltNodeNumber == null ? "" : $" (alt {AltNodeNumber})")} {Decision.Name} ({Decision.Abbreviation}): DecisionByteCode {DecisionByteCode} (index {DecisionIndex}) PlayerIndex {PlayerIndex} Probabilities {GetCurrentProbabilitiesAsString()} {GetBestResponseStringIfAvailable()}Average {GetAverageStrategiesAsString()} Regrets {GetCumulativeRegretsString()} Strategies {GetCumulativeStrategiesString()} InformationSetContents {string.Join(";", LabeledInformationSet.Select(x => $"(index {x.decisionIndex}, info {x.information})"))}";
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
            double probability = 1.0 / NumPossibleActions;
            if (double.IsNaN(probability) || NumPossibleActions == 0)
                throw new Exception();
            BestResponseAction = 1;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                SetNI(currentProbabilityDimension, a - 1, probability);
                SetNI(currentProbabilityForOpponentDimension, a - 1, probability);
                SetNI(averageStrategyProbabilityDimension, a - 1, probability);
                SetNI(bestResponseNumeratorDimension, a - 1, 0);
                SetNI(bestResponseDenominatorDimension, a - 1, 0);
                SetNI(cumulativeRegretDimension, a - 1, 0);
                SetNI(cumulativeStrategyDimension, a - 1, 0);
                SetNI(adjustedWeightsDimension, a - 1, 1.0);
                SetNI(sumRegretTimesInversePiDimension, a - 1, 0);
                SetNI(sumInversePiDimension, a - 1, 0);
                SetNI(lastCumulativeStrategyIncrementsDimension, a - 1, 0);
                SetNI(scratchDimension, a - 1, 0);
            }
            if (EvolutionSettings.RecordPastValues && clearPastValues)
            {
                PastValues = new List<double[]>();
                PastValuesCumulativeStrategyDiscounts = new List<double>();
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
                SetNI(cumulativeStrategyDimension, j, 0);
        }

        public void ClearBestResponse()
        {
            for (int i = bestResponseNumeratorDimension; i <= bestResponseDenominatorDimension; i++)
                for (int j = 0; j < NumPossibleActions; j++)
                    SetNI(i, j, 0);
            BestResponseDeterminedFromIncrements = false;
        }

        public void ClearAverageStrategyTally()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                SetNI(cumulativeStrategyDimension, a, 0);
        }

        public void ClearCumulativeRegrets()
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                SetNI(cumulativeRegretDimension, a, 0);
        }

        public void CopyFromOneDimensionToAnother(byte dimensionCopyingFrom, byte dimensionCopyingTo)
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                SetNI(dimensionCopyingTo, a, NodeInformation[dimensionCopyingFrom, a]);
        }

        public void SubtractOutValues(byte dimensionSubtractingFrom, byte dimensionWithValuesToSubtract)
        {
            for (byte a = 0; a < NumPossibleActions; a++)
                SetNI(dimensionSubtractingFrom, a, NodeInformation[dimensionSubtractingFrom, a] - NodeInformation[dimensionWithValuesToSubtract, a]);
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.InformationSet;
        }

        #endregion

        #region Best response

        // Note: See also BestResponse.cs file for best response algorithm, including tracing options.

        private bool LogBestResponseCalculation = false; // Note: This is for extra logging, since there is also logging from TraceGEBR and TraceAcceleratedBestResponsePrep

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
                {
                    if (LogBestResponseCalculation)
                        TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}: Denom is 0; cannot compute ratio.");
                    return; // no best response data available
                }
                double numerator = NodeInformation[bestResponseNumeratorDimension, action - 1];
                double ratio = numerator / denominator;
                BestResponseOptions[action - 1] = ratio;
                if (LogBestResponseCalculation)
                    TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}: Numerator = {numerator:F10}, Denom = {denominator:F10}, Ratio = {ratio:F10}");
                if (action == 1 || ratio > bestRatio)
                {
                    best = action;
                    bestRatio = ratio;
                }
            }
            BestResponseAction = (byte)best;
            if (LogBestResponseCalculation)
                TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Chosen BestResponseAction = {BestResponseAction} with ratio = {bestRatio:F10}");
            BestResponseDeterminedFromIncrements = true;
        }

        public void GetBestResponseProbabilities(Span<double> probabilities)
        {
            int bestResponse = BestResponseAction;
            for (int a = 1; a <= NumPossibleActions; a++)
                probabilities[a - 1] = a == bestResponse ? 1.0 : 0;
        }

        public double[] GetBestResponseProbabilities()
        {
            double[] probabilities = new double[NumPossibleActions];
            int bestResponse = BestResponseAction;
            for (int a = 1; a <= NumPossibleActions; a++)
                probabilities[a - 1] = a == bestResponse ? 1.0 : 0;
            return probabilities;
        }

        public void IncrementBestResponse(int action, double piInverse, double expectedValue)
        {
            if (LogBestResponseCalculation)
                TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}: Adding piInverse {piInverse:F3}, expectedValue {expectedValue:F3}.");
            double oldNumerator = NodeInformation[bestResponseNumeratorDimension, action - 1];
            double oldDenom = NodeInformation[bestResponseDenominatorDimension, action - 1];
            AddNI(bestResponseNumeratorDimension, action - 1, piInverse * expectedValue);
            AddNI(bestResponseDenominatorDimension, action - 1, piInverse);
            double newNumerator = NodeInformation[bestResponseNumeratorDimension, action - 1];
            double newDenom = NodeInformation[bestResponseDenominatorDimension, action - 1];
            if (LogBestResponseCalculation)
            {
                double? newQuotient = newDenom == 0 ? null : newNumerator / newDenom;
                TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}: Numerator: {oldNumerator:F3} -> {newNumerator:F3}, Denom: {oldDenom:F3} -> {newDenom:F3} ==> {newQuotient:F3}");
            }
            BestResponseDeterminedFromIncrements = false;
        }

        public void SetBestResponse_NumeratorAndDenominator(int action, double numerator, double denominator)
        {
            SetNI(bestResponseNumeratorDimension, action - 1, numerator);
            SetNI(bestResponseDenominatorDimension, action - 1, denominator);
            BestResponseDeterminedFromIncrements = false;
        }

        #endregion

        #region AcceleratedBestResponse

        public double SelfReachProbability, OpponentsReachProbability;
        public InformationSetNode PredecessorInformationSetForPlayer;
        public byte ActionTakenAtPredecessorSet;
        public bool BestResponseMayReachHere;
        public bool LastBestResponseMayReachHere;
        public ByteList LastActionsList => PathsFromPredecessor.Last().ActionsListExcludingPlayer;

        public List<PathFromPredecessorInfo> PathsFromPredecessor;
        /// <summary>
        /// A list for each action of the list of histories (one for each path from predecessor) from this node to possible successors given that action.
        /// </summary>
        public List<List<NodeActionsMultipleHistories>> PathsToSuccessors;
        public (bool consideredForPruning, bool prunable)[] PrunableActions;
        public double LastBestResponseValue
        {
            get { return BestResponseOptions?[BestResponseAction - 1] ?? 0; }
        }
        public double[] BestResponseOptions;
        public double[] AverageStrategyResultsForPathFromPredecessor;
        public FloatSet[] CustomResultForPathFromPredecessor;
        public int NumVisitsFromPredecessorToGetAverageStrategy;
        public int NumVisitsFromPredecessorToGetCustomResult;

        public void AcceleratedBestResponse_CalculateReachProbabilities(bool determinePrunability, bool useCurrentStrategyForBestResponse)
        {
            if (PredecessorInformationSetForPlayer == null)
                SelfReachProbability = 1.0;
            else
                SelfReachProbability = PredecessorInformationSetForPlayer.SelfReachProbability *
                                       PredecessorInformationSetForPlayer.GetAverageOrCurrentStrategy(ActionTakenAtPredecessorSet, !useCurrentStrategyForBestResponse);
            OpponentsReachProbability = 0;
            if (PrunableActions == null)
                PrunableActions = new (bool consideredForPruning, bool prunable)[NumPossibleActions];
            for (int i = 0; i < NumPossibleActions; i++)
                PrunableActions[i] = (false, true);
            double sumProbabilitiesSinceOpponentInformationSets = 0;
            for (int i = 0; i < PathsFromPredecessor.Count; i++)
            {
                PathFromPredecessorInfo pathFromPredecessor = PathsFromPredecessor[i];
                double predecessorOpponentsReachProbability = PredecessorInformationSetForPlayer?.PathsFromPredecessor[pathFromPredecessor.IndexInPredecessorsPathsFromPredecessor].Probability ?? 1.0;
                double pathProbabilityFromPredecessor;
                if (determinePrunability)
                {
                    (double p, double probSinceOpponent, InformationSetNode mostRecentOpponent, byte actionAtOpponent) =
                        pathFromPredecessor.Path.GetProbabilityOfPathPlus();
                    pathProbabilityFromPredecessor = p;
                    pathFromPredecessor.MostRecentOpponentInformationSet = mostRecentOpponent;
                    pathFromPredecessor.ActionAtOpponentInformationSet = actionAtOpponent;
                    pathFromPredecessor.ProbabilityFromMostRecentOpponent = probSinceOpponent;
                    sumProbabilitiesSinceOpponentInformationSets += probSinceOpponent;
                }
                else
                    pathProbabilityFromPredecessor = pathFromPredecessor.Path.GetProbabilityOfPath(useCurrentStrategyForBestResponse);
                double cumulativePathProbability = predecessorOpponentsReachProbability * pathProbabilityFromPredecessor;
                pathFromPredecessor.Probability = cumulativePathProbability;
                if (LogBestResponseCalculation)
                    TabbedText.WriteLine($"Calculating node {InformationSetNodeNumber} path {i} ({pathFromPredecessor.Path}) calculating probability as {predecessorOpponentsReachProbability} * {pathProbabilityFromPredecessor} = {cumulativePathProbability}");
                // No aggregated logging here.
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
                            pathFromPredecessor.MostRecentOpponentInformationSet.PrunableActions[pathFromPredecessor.ActionAtOpponentInformationSet - 1].prunable = false;
                    }
                }
            }
        }

        public void AcceleratedBestResponse_CalculateBestResponseValues(byte numNonChancePlayers, bool useCurrentStrategyForBestResponse)
        {
            if (BestResponseOptions == null)
                BestResponseOptions = new double[Decision.NumPossibleActions];
            int numPathsFromPredecessor = PathsFromPredecessor.Count();
            if (AverageStrategyResultsForPathFromPredecessor == null)
                AverageStrategyResultsForPathFromPredecessor = new double[numPathsFromPredecessor];
            else
                for (int i = 0; i < numPathsFromPredecessor; i++)
                    AverageStrategyResultsForPathFromPredecessor[i] = 0;
            if (CustomResultForPathFromPredecessor == null)
                CustomResultForPathFromPredecessor = new FloatSet[numPathsFromPredecessor];
            else
                for (int i = 0; i < numPathsFromPredecessor; i++)
                    CustomResultForPathFromPredecessor[i] = new FloatSet();
            for (byte action = 1; action <= Decision.NumPossibleActions; action++)
            {
                var pathsToSuccessorsForAction = PathsToSuccessors[action - 1];
                if (pathsToSuccessorsForAction.Count() != numPathsFromPredecessor)
                    throw new Exception();
                double averageStrategyProbability = GetAverageOrCurrentStrategy(action, !useCurrentStrategyForBestResponse);
                // For each path, log an input update analogous to IncrementBestResponse.
                double accumulatedBestResponseNumerator = 0, accumulatedBestResponseDenom = 0;
                for (int i = 0; i < numPathsFromPredecessor; i++)
                {
                    (double unweightedValue, double avgStratValue, FloatSet customResult) =
                        pathsToSuccessorsForAction[i].GetProbabilityAdjustedValueOfPaths(PlayerIndex, useCurrentStrategyForBestResponse);
                    double oppReachProb = PathsFromPredecessor[i].Probability;
                    double weighted = unweightedValue * oppReachProb;
                    if (LogBestResponseCalculation)
                        TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}, average strategy {averageStrategyProbability}, Path {i} [{PathsFromPredecessor[i].ToString().Replace("\n", ";").Replace("\r", "")}]: Adding oppReachProb {oppReachProb:F10}, unweightedValue {unweightedValue:F10} (weighted = {weighted:F10})");
                    accumulatedBestResponseNumerator += weighted;
                    accumulatedBestResponseDenom += oppReachProb;
                    AverageStrategyResultsForPathFromPredecessor[i] += avgStratValue * averageStrategyProbability;
                    CustomResultForPathFromPredecessor[i] = CustomResultForPathFromPredecessor[i].Plus(customResult.Times((float)averageStrategyProbability));
                }
                double ratio = accumulatedBestResponseDenom == 0 ? 0 : accumulatedBestResponseNumerator / accumulatedBestResponseDenom;
                BestResponseOptions[action - 1] = ratio;
                if (LogBestResponseCalculation)
                    TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Action {action}: Numerator = {accumulatedBestResponseNumerator:F10}, Denom = {accumulatedBestResponseDenom:F10}, Ratio = {ratio:F10}");
                if (action == 1 || BestResponseOptions[action - 1] > BestResponseOptions[BestResponseAction - 1])
                    BestResponseAction = action;
            }
            if (LogBestResponseCalculation)
                TabbedText.WriteLine($"InformationSet {InformationSetNodeNumber}, Decision {Decision.Name} | Chosen BestResponseAction = {BestResponseAction} with ratio = {BestResponseOptions[BestResponseAction - 1]:F10}");
            NumVisitsFromPredecessorToGetAverageStrategy = 0;
        }

        public void AcceleratedBestResponse_DetermineWhetherReachable()
        {
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
            const double InitialWeightMultiplier = 1.0;
            const double Curvature = 10.0;
            double weightMultiplier = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(InitialWeightMultiplier, 1.0, Curvature, iteration / (double)maxIterations);
            double weightOnBestResponse = weightMultiplier / iteration;
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
                double bestResponseProbability = BestResponseAction == action ? 1.0 : 0.0;
                double successorValue = (1.0 - weightOnBestResponse) * currentAverageStrategyProbability + weightOnBestResponse * bestResponseProbability;
                if (double.IsNaN(successorValue))
                    throw new Exception();
                SetNI(cumulativeStrategyDimension, action - 1, successorValue);
                SetNI(averageStrategyProbabilityDimension, action - 1, successorValue);
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
                double bestResponseProbability = BestResponseAction == action ? 1.0 : 0.0;
                SetNI(cumulativeStrategyDimension, action - 1, bestResponseProbability);
                SetNI(averageStrategyProbabilityDimension, action - 1, bestResponseProbability);
                if (double.IsNaN(bestResponseProbability))
                    throw new Exception();
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
                double v = minValueForEachAction + NodeInformation[averageStrategyProbabilityDimension, action - 1] * remainingAfterPerturbation;
                if (double.IsNaN(v))
                    throw new Exception();
                SetNI(averageStrategyProbabilityDimension, action - 1, v);
                if (includeCumulativeStrategy)
                    SetNI(cumulativeStrategyDimension, action - 1, NodeInformation[averageStrategyProbabilityDimension, action - 1]);
            }
        }

        #endregion



        #region Regrets

        public void IncrementLastRegret(byte action, double regretTimesInversePi, double inversePi)
        {
            AddNI(sumRegretTimesInversePiDimension, action - 1, regretTimesInversePi);
            AddNI(sumInversePiDimension, action - 1, inversePi);
        }
        public void IncrementLastRegret_Parallel(byte action, double regretTimesInversePi, double inversePi)
        {
            InterlockedAddNI(sumRegretTimesInversePiDimension, action - 1, regretTimesInversePi);
            InterlockedAddNI(sumInversePiDimension, action - 1, inversePi);
        }

        public double NormalizeRegret(double regret, bool makeStrictlyPositive)
        {
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
            return string.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public double GetCumulativeRegret(int action)
        {
            return NodeInformation[cumulativeRegretDimension, action - 1];
        }

        public void SetCumulativeRegret(int action, double regret)
        {
            SetNI(cumulativeRegretDimension, action - 1, regret);
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
            InterlockedAddNI(cumulativeRegretDimension, action - 1, amount);
        }

        public void IncrementCumulativeRegret(int action, double amount, int backupRegretsTrigger = int.MaxValue, bool incrementVisits = false)
        {
            ref double sum = ref NodeInformation[cumulativeRegretDimension, action - 1];

            sum += amount;
            sum = Quantize15Digits(sum);
            OnNodeInformationMutated(cumulativeRegretDimension, action - 1, sum);
        }


        #endregion

        #region Cumulative strategies

        public void IncrementLastCumulativeStrategyIncrements(byte action, double strategyProbabilityTimesSelfReachProbability)
        {
            AddNI(lastCumulativeStrategyIncrementsDimension, action - 1, strategyProbabilityTimesSelfReachProbability);
        }

        public void IncrementLastCumulativeStrategyIncrements_Parallel(byte action, double strategyProbabilityTimesSelfReachProbability)
        {
            InterlockedAddNI(lastCumulativeStrategyIncrementsDimension, action - 1, strategyProbabilityTimesSelfReachProbability);
        }

        public double GetLastCumulativeStrategyIncrement(byte action) => NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1];

        private void UpdateCumulativeAndAverageStrategies(int iteration, double averageStrategyAdjustment, bool normalizeCumulativeStrategyIncrements, bool resetPreviousCumulativeStrategyIncrements, double continuousRegretsDiscountingAdjustment)
        {

            RecordProbabilitiesAsPastValues_BasedOnIteration(iteration, averageStrategyAdjustment); // these are the average strategies played, and thus shouldn't reflect the updates below

            UpdateCumulativeRegrets(continuousRegretsDiscountingAdjustment);
            if (resetPreviousCumulativeStrategyIncrements)
                ClearCumulativeStrategy();
            UpdateCumulativeStrategy(averageStrategyAdjustment, normalizeCumulativeStrategyIncrements);

            UpdateAverageStrategy();

            AverageStrategyAdjustmentsSum += averageStrategyAdjustment;
        }

        public void ResetCumulativeRegrets()
        {
            UpdateCumulativeRegrets(0);
        }

        public void ShrinkCumulativeRegrets()
        {
            UpdateCumulativeRegrets(0.0000001);
        }

        private void UpdateCumulativeRegrets(double continuousDiscountingAdjustment)
        {
            if (continuousDiscountingAdjustment != 1.0)
            {
                for (byte a = 1; a <= NumPossibleActions; a++)
                    SetNI(cumulativeRegretDimension, a - 1, NodeInformation[cumulativeRegretDimension, a - 1] * continuousDiscountingAdjustment);
            }
        }
        private void UpdateCumulativeStrategy(double averageStrategyAdjustment, bool normalizeCumulativeStrategyIncrements)
        {
            if (_cumulativeStrategyCompensation == null || _cumulativeStrategyCompensation.Length != NumPossibleActions)
                _cumulativeStrategyCompensation = new double[NumPossibleActions];

            double totalLastIncrement = 0.0;
            double totalCompensation = 0.0;

            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double inc = NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1];
                KahanAdd(ref totalLastIncrement, ref totalCompensation, inc);
            }

            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double normalizedIncrement;

                if (totalLastIncrement == 0.0)
                {
                    normalizedIncrement = normalizeCumulativeStrategyIncrements
                        ? NodeInformation[currentProbabilityDimension, action - 1]
                        : 0.0;
                }
                else
                {
                    normalizedIncrement = NodeInformation[lastCumulativeStrategyIncrementsDimension, action - 1];
                    if (normalizeCumulativeStrategyIncrements)
                        normalizedIncrement /= totalLastIncrement;
                }

                double adjustedIncrement = averageStrategyAdjustment * normalizedIncrement;

                ref double sum = ref NodeInformation[cumulativeStrategyDimension, action - 1];
                ref double comp = ref _cumulativeStrategyCompensation[action - 1];
                KahanAdd(ref sum, ref comp, adjustedIncrement);

                sum = Quantize15Digits(sum);
                OnNodeInformationMutated(cumulativeStrategyDimension, action - 1, sum);

                SetNI(lastCumulativeStrategyIncrementsDimension, action - 1, 0.0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void KahanAdd(ref double runningSum, ref double compensation, double addend)
        {
            double y = addend - compensation;
            double t = runningSum + y;
            compensation = (t - runningSum) - y;
            runningSum = t;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Quantize15Digits(double x)
        {
            return Math.Round(x, 15, MidpointRounding.AwayFromZero);
        }


        private void UpdateAverageStrategy()
        {
            double sumCumulativeStrategies = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                sumCumulativeStrategies += NodeInformation[cumulativeStrategyDimension, a - 1];
            }
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
            return string.Join(", ", probs.Select(x => $"{x:N2}"));
        }

        public double GetCumulativeStrategy(int action)
        {
            double v = NodeInformation[cumulativeStrategyDimension, action - 1];
            return v;
        }

        public void IncrementCumulativeStrategy_Parallel(int action, double amount)
        {
            InterlockedAddNI(cumulativeStrategyDimension, action - 1, amount);
        }

        public void IncrementCumulativeStrategy(int action, double amount)
        {
            AddNI(cumulativeStrategyDimension, action - 1, amount);
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
                array[a - 1] = GetLastCumulativeStrategyIncrement((byte)a);
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
            return string.Join(", ", GetAverageStrategiesAsArray().Select(x => x.ToSignificantFigures(3)));
        }

        public void SetActionToCertainty(byte action, byte numPossibleActions)
        {
            for (byte a = 1; a <= numPossibleActions; a++)
            {
                double v = a == action ? 1.0 : 0;
                SetNI(cumulativeStrategyDimension, a - 1, v);
                SetNI(cumulativeRegretDimension, a - 1, v);
            }
        }

        #endregion

        #region Regret matching

        public List<double> GetRegretMatchingProbabilitiesList()
        {
            Span<double> probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetRegretMatchingProbabilities(probabilitiesToSet);
            return ListExtensions.GetSpanAsList(probabilitiesToSet, NumPossibleActions);
        }

        public List<double> GetEqualProbabilitiesList()
        {
            Span<double> probabilitiesToSet = stackalloc double[NumPossibleActions];
            GetEqualProbabilitiesRegretMatching(probabilitiesToSet);
            return ListExtensions.GetSpanAsList(probabilitiesToSet, NumPossibleActions);
        }

        public void GetRegretMatchingProbabilities(Span<double> probabilitiesToSet)
        {
            const double eps = 1e-14;

            double sumPositive = 0.0;
            int numPositive = 0;
            int lastPositiveIndex = -1;

            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double r = NodeInformation[cumulativeRegretDimension, action - 1];
                if (r > eps)
                {
                    sumPositive += r;
                    numPositive++;
                    lastPositiveIndex = action - 1;
                }
            }

            if (numPositive == 1)
            {
                for (byte action = 1; action <= NumPossibleActions; action++)
                    probabilitiesToSet[action - 1] = (action - 1) == lastPositiveIndex ? 1.0 : 0.0;
                return;
            }

            if (sumPositive == 0.0)
            {
                double equalProbability = 1.0 / NumPossibleActions;
                for (byte action = 1; action <= NumPossibleActions; action++)
                    probabilitiesToSet[action - 1] = equalProbability;
                return;
            }

            int argmax = 0;
            for (byte action = 1; action <= NumPossibleActions; action++)
            {
                double r = NodeInformation[cumulativeRegretDimension, action - 1];
                double p = r > eps ? (r / sumPositive) : 0.0;
                probabilitiesToSet[action - 1] = p;
                if (p > probabilitiesToSet[argmax])
                    argmax = action - 1;
            }

            if (probabilitiesToSet[argmax] >= 1.0 - 1e-12)
            {
                for (int a = 0; a < NumPossibleActions; a++)
                    probabilitiesToSet[a] = a == argmax ? 1.0 : 0.0;
            }
        }


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
                double equalProbability = 1.0 / NumPossibleActions;
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

        private void NormalizeAndSnapProbabilities(int probabilityDimension, double zeroThreshold, double snapThreshold)
        {
            double sum = 0.0;
            int n = NumPossibleActions;
            for (int a = 0; a < n; a++)
            {
                double v = NodeInformation[probabilityDimension, a];
                if (v < zeroThreshold) v = 0.0;
                SetNI(probabilityDimension, a, v);
                sum += v;
            }

            if (sum == 0.0)
            {
                double equal = 1.0 / n;
                for (int a = 0; a < n; a++)
                    SetNI(probabilityDimension, a, equal);
                return;
            }

            for (int a = 0; a < n; a++)
                SetNI(probabilityDimension, a, NodeInformation[probabilityDimension, a] / sum);

            int argmax = 0;
            for (int a = 1; a < n; a++)
                if (NodeInformation[probabilityDimension, a] > NodeInformation[probabilityDimension, argmax])
                    argmax = a;

            if (NodeInformation[probabilityDimension, argmax] >= snapThreshold)
            {
                for (int a = 0; a < n; a++)
                    SetNI(probabilityDimension, a, a == argmax ? 1.0 : 0.0);
            }
        }

        public void PostIterationUpdates(
            int iteration,
            PostIterationUpdaterBase updater,
            double averageStrategyAdjustment,
            bool normalizeCumulativeStrategyIncrements,
            bool resetPreviousCumulativeStrategyIncrements,
            double continuousRegretsDiscountingAdjustment,
            double? pruneOpponentStrategyBelow,
            bool pruneOpponentStrategyIfDesignatedPrunable,
            bool addOpponentTremble,
            bool weightResultByInversePiForIteration,
            double? randomNumberToSelectSingleOpponentAction)
        {
            UpdateCumulativeAndAverageStrategies(iteration, averageStrategyAdjustment, normalizeCumulativeStrategyIncrements, resetPreviousCumulativeStrategyIncrements, continuousRegretsDiscountingAdjustment);

            DetermineBestResponseAction();
            ClearBestResponse();

            updater.UpdateInformationSet(this, weightResultByInversePiForIteration);

            NormalizeAndSnapProbabilities(averageStrategyProbabilityDimension, 1e-15, 1.0 - 1e-12);
            NormalizeAndSnapProbabilities(currentProbabilityDimension,           1e-15, 1.0 - 1e-12);

            UpdateOpponentProbabilities(iteration, pruneOpponentStrategyBelow, pruneOpponentStrategyIfDesignatedPrunable, addOpponentTremble, randomNumberToSelectSingleOpponentAction);
        }


        private void UpdateOpponentProbabilities(int iteration, double? pruneOpponentStrategyBelow, bool pruneOpponentStrategyIfDesignatedPrunable, bool addOpponentTremble, double? randomNumberToSelectSingleOpponentAction)
        {
            if (iteration <= Decision.WarmStartThroughIteration)
            {
                for (int a = 1; a <= NumPossibleActions; a++)
                {
                    SetNI(currentProbabilityForOpponentDimension, a - 1, a == Decision.WarmStartValue ? 1.0 : 0);
                }
                return;
            }
            bool pruning = pruneOpponentStrategyIfDesignatedPrunable || pruneOpponentStrategyBelow != null && pruneOpponentStrategyBelow != 0;
            double probabilityThreshold = pruning && !pruneOpponentStrategyIfDesignatedPrunable ? (double)pruneOpponentStrategyBelow : SmallestProbabilityRepresented;
            Func<byte, double> currentProbabilityFunc;
            if (EvolutionSettings.CFR_OpponentPlaysAverageStrategy)
                currentProbabilityFunc = a => NodeInformation[averageStrategyProbabilityDimension, a - 1];
            else
                currentProbabilityFunc = a => NodeInformation[currentProbabilityDimension, a - 1];
            SetProbabilitiesFromFunc(currentProbabilityForOpponentDimension, probabilityThreshold, pruning, pruneOpponentStrategyIfDesignatedPrunable, currentProbabilityFunc);

            if (addOpponentTremble)
                AddTrembleToOpponentProbabilities(0.1);

            if (randomNumberToSelectSingleOpponentAction != null)
            {
                double p = (double)randomNumberToSelectSingleOpponentAction;
                double total = 0;
                byte a;
                for (a = 1; a <= NumPossibleActions - 1; a++)
                {
                    total += currentProbabilityFunc(a);
                    if (total > p)
                        break;
                }
                for (byte a2 = 1; a2 <= NumPossibleActions; a2++)
                {
                    SetNI(currentProbabilityForOpponentDimension, a2 - 1, a == a2 ? 1.0 : 0);
                }
            }
        }

        public void RoundOffLowProbabilities(bool currentProbabilityDimension, double probabilityThreshold) => RoundOffLowProbabilities(currentProbabilityDimension ? InformationSetNode.currentProbabilityDimension : averageStrategyProbabilityDimension, probabilityThreshold);

        public void RoundOffLowProbabilities(int probabilityDimension, double probabilityThreshold)
        {
            double total = 0;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double v = NodeInformation[probabilityDimension, a - 1];
                if (v < probabilityThreshold)
                    SetNI(probabilityDimension, a - 1, 0);
                else
                    total += v;
            }
            for (byte a = 1; a <= NumPossibleActions; a++)
                SetNI(probabilityDimension, a - 1, NodeInformation[probabilityDimension, a - 1] / total);
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
                if (double.IsNaN(p) || double.IsNaN(setBelowThresholdTo))
                    throw new Exception();
                if (!usePrunabilityInsteadOfThreshold && p <= probabilityThreshold || usePrunabilityInsteadOfThreshold && PrunableActions != null && PrunableActions[a - 1].consideredForPruning && PrunableActions[a - 1].prunable)
                    p = setBelowThresholdTo;
                SetNI(probabilityDimension, a - 1, p);
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
            double remainingProbability = 1.0 - sumExcludingLargest;
            if (double.IsNaN(remainingProbability))
                throw new Exception();
            SetNI(probabilityDimension, largestAction - 1, remainingProbability);
        }

        private void AddTrembleToOpponentProbabilities(double trembleProportion)
        {
            if (!Decision.IsContinuousAction)
                return;
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                SetNI(scratchDimension, a - 1, NodeInformation[currentProbabilityForOpponentDimension, a - 1]);
            }
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double original = NodeInformation[scratchDimension, a - 1];
                double trembleSizeEachDirection = original * trembleProportion * 0.5;
                if (original > SmallestProbabilityRepresented)
                {
                    if (a > 1)
                    {
                        AddNI(currentProbabilityForOpponentDimension, a - 2, trembleSizeEachDirection);
                        AddNI(currentProbabilityForOpponentDimension, a - 1, -trembleSizeEachDirection);
                    }
                    if (a < NumPossibleActions)
                    {
                        AddNI(currentProbabilityForOpponentDimension, a, trembleSizeEachDirection);
                        AddNI(currentProbabilityForOpponentDimension, a - 1, -trembleSizeEachDirection);
                    }
                }
            }
        }

        private void RemoveTremble()
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                SetNI(currentProbabilityForOpponentDimension, a - 1, NodeInformation[scratchDimension, a - 1]);
            }
        }

        private void RecordProbabilitiesAsPastValues_BasedOnIteration(int iteration, double averageStrategyAdjustment)
        {
            if (EvolutionSettings.RecordPastValues_AtIteration(iteration))
            {
                PastValuesCumulativeStrategyDiscounts.Add(averageStrategyAdjustment);
                RecordProbabilitiesAsPastValues();
            }
        }

        public void RecordProbabilitiesAsPastValues()
        {
            LastPastValueIndexRecorded++;
            PastValues.Add(new double[NumPossibleActions]);
            for (byte a = 1; a <= NumPossibleActions; a++)
                PastValues[LastPastValueIndexRecorded][a - 1] = GetCurrentProbability(a, false);
        }

        public void SetAverageStrategyToPastValue(int pastValueIndex)
        {
            CreateBackup();
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double v = PastValues[pastValueIndex][a - 1];
                SetNI(averageStrategyProbabilityDimension, a - 1, v);
                if (double.IsNaN(v))
                    throw new Exception();
            }
        }

        #endregion

        #region Hedge

        double V = 0;
        double MaxAbsRegretDiff = 0;
        double E = 1;
        double Nu;
        static double C = Math.Sqrt(2 * (Math.Sqrt(2) - 1.0) / (Math.Exp(1.0) - 2));

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
                firstSum += product * lastRegret;
                secondSum += product;
            }
            double varZt = firstSum - secondSum * secondSum;
            if (varZt < 0)
                varZt = 0;
            V += varZt;
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
            Nu = Math.Min(1.0 / E, C * Math.Sqrt(Math.Log(NumPossibleActions) / V));
            if (double.IsNaN(Nu))
                throw new Exception();
            double denominatorForAllActions = 0;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                AddNI(cumulativeRegretDimension, a - 1, NodeInformation[sumRegretTimesInversePiDimension, a - 1]);
                double numeratorForThisAction = Math.Exp(Nu * NodeInformation[cumulativeRegretDimension, a - 1]);
                SetNI(scratchDimension, a - 1, numeratorForThisAction);
                if (double.IsNaN(numeratorForThisAction))
                    throw new Exception("Regrets too high. Must scale all regrets.");
                denominatorForAllActions += numeratorForThisAction;
            }
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double quotient = NodeInformation[scratchDimension, a - 1] / denominatorForAllActions;
                SetNI(currentProbabilityDimension, a - 1, quotient);
                if (double.IsNaN(quotient))
                    throw new Exception("Regrets too high. Must scale all regrets");
            }
        }

        #endregion

        #region Get probabilities

        public double GetAverageOrCurrentStrategy(byte action, bool averageStrategy)
        {
            return NodeInformation[averageStrategy ? averageStrategyProbabilityDimension : currentProbabilityDimension, action - 1];
        }

        public double GetAverageStrategy(byte action)
        {
            var result = NodeInformation[averageStrategyProbabilityDimension, action - 1];
            if (double.IsNaN(result))
                throw new Exception("Average strategy is set to infinite / NaN.");
            return result;
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
            int pastValuesCount = LastPastValueIndexRecorded + 1;
            double cumulativeDiscountLevelToSeek = pastValuesCount * randomNumberToChooseIteration;
            if (PastValuesCumulativeStrategyDiscounts == null || PastValuesCumulativeStrategyDiscounts.All(x => x == 0))
            {
                int index2 = (int)(randomNumberToChooseIteration * pastValuesCount);
                for (int a = 0; a < NumPossibleActions; a++)
                    probabilities[a] = PastValues[index2][a];
                return;
            }

            int index = PastValuesCumulativeStrategyDiscounts.BinarySearch(cumulativeDiscountLevelToSeek, Comparer<double>.Default);
            if (index < 0)
                index = ~index;
            if (index == pastValuesCount)
                index--;
            for (int a = 0; a < NumPossibleActions; a++)
                probabilities[a] = PastValues[index][a];
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
            return string.Join(", ", GetCurrentProbabilitiesAsArray().Select(x => x.ToSignificantFigures(3)));
        }

        public void GetCurrentProbabilities(Span<double> probabilitiesToSet)
        {
            bool done = false;
            int i = 0;
            while (!done)
            {
                double total = 0;
                for (byte a = 1; a <= NumPossibleActions; a++)
                {
                    probabilitiesToSet[a - 1] = NodeInformation[currentProbabilityDimension, a - 1];
                    total += probabilitiesToSet[a - 1];
                }
                done = Math.Abs(1.0 - total) < 1E-4;
                if (i++ > 100)
                    return;
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

        public void SetCurrentProbabilities(double[] probabilities)
        {
            for (int index = 0; index < NumPossibleActions; index++)
                SetNI(currentProbabilityDimension, index, probabilities[index]);
        }

        #endregion

        #region Symmetry

        public byte[] GetSymmetricInformationSet(GameDefinition gameDefinition) => GetSymmetricInformationSet(gameDefinition,
 LabeledInformationSet);

        public static byte[] GetSymmetricInformationSet(GameDefinition gameDefinition, List<(byte decisionIndex, byte information)> labeledInformationSet)
        {
            byte[] symmetric = labeledInformationSet.Select(x => x.information).ToArray();
            for (int i = 0; i < symmetric.Length; i++)
            {
                byte decisionIndex = labeledInformationSet[i].decisionIndex;
                Decision decision = gameDefinition.DecisionPointsExecutionOrder[decisionIndex].Decision;
                var symmetryMap = decision.SymmetryMap;
                if (symmetryMap.information == SymmetryMapInput.NotInInformationSet)
                    throw new Exception("Information supposedly not in information set found in information set");
                if (symmetryMap.information == SymmetryMapInput.NotCompatibleWithSymmetry)
                    throw new Exception("Decision not compatible with symmetry");
                byte playerIndex = decision.PlayerIndex;
                if (playerIndex == 0)
                {
                    byte? nextDecisionIndex = labeledInformationSet.Count() > i + 1 ? labeledInformationSet[i + 1].decisionIndex : null;
                    Decision nextDecision = nextDecisionIndex == null ? null : gameDefinition.DecisionPointsExecutionOrder[(byte)nextDecisionIndex].Decision;
                    bool nextDecisionIsOpponent = nextDecision != null && nextDecision.PlayerIndex == 1;
                    if (nextDecisionIsOpponent && nextDecision.SymmetryMap != decision.SymmetryMap)
                        throw new Exception();
                    if (nextDecisionIsOpponent)
                    {
                        byte temp = symmetric[i + 1];
                        symmetric[i + 1] = symmetric[i];
                        symmetric[i] = temp;
                    }
                    if (symmetryMap.information == SymmetryMapInput.ReverseInfo)
                    {
                        symmetric[i] = (byte)(decision.NumPossibleActions - symmetric[i] + 1);
                        if (nextDecisionIsOpponent)
                            symmetric[i + 1] = (byte)(decision.NumPossibleActions - symmetric[i + 1] + 1);
                    }
                }
                else if (decision.IsChance)
                {
                    if (symmetryMap.information == SymmetryMapInput.ReverseInfo)
                        symmetric[i] = (byte)(decision.NumPossibleActions - symmetric[i] + 1);
                }
            }
            return symmetric;
        }


        public static List<(byte decisionIndex, byte information)> GetSymmetricLabeledInformationSet_FromLaterDecisionToEarlier(GameDefinition gameDefinition, List<(byte decisionIndex, byte information)> labeledInformationSet)

        {
            List<(byte decisionIndex, byte information)> symmetric = labeledInformationSet.ToList();
            for (int i = 0; i < symmetric.Count; i++)
            {
                byte decisionIndex = labeledInformationSet[i].decisionIndex;
                Decision decision = gameDefinition.DecisionPointsExecutionOrder[decisionIndex].Decision;
                var symmetryMap = decision.SymmetryMap;
                if (symmetryMap.information == SymmetryMapInput.NotInInformationSet)
                    throw new Exception("Information supposedly not in information set found in information set");
                if (symmetryMap.information == SymmetryMapInput.NotCompatibleWithSymmetry)
                    throw new Exception("Decision not compatible with symmetry");
                byte playerIndex = decision.PlayerIndex;
                if (playerIndex == 1)
                {
                    byte? previousDecisionIndex = labeledInformationSet.Count() > i - 1 ? labeledInformationSet[i - 1].decisionIndex : null;
                    Decision previousDecision = previousDecisionIndex == null ? null : gameDefinition.DecisionPointsExecutionOrder[(byte)previousDecisionIndex].Decision;
                    bool previousDecisionInInformationSetIsOpponents = previousDecision != null && previousDecision.PlayerIndex == 0 && previousDecision.DecisionByteCode == decision.DecisionByteCode - 1;
                    if (previousDecisionInInformationSetIsOpponents && previousDecision.SymmetryMap != decision.SymmetryMap)
                        throw new Exception();
                    if (previousDecisionInInformationSetIsOpponents)
                    {
                        var temp = symmetric[i - 1].information;
                        symmetric[i - 1] = (symmetric[i - 1].decisionIndex, symmetric[i].information);
                        symmetric[i] = (symmetric[i].decisionIndex, temp);
                    }
                    else
                    {
                        symmetric[i] = ((byte)(symmetric[i].decisionIndex - 1), symmetric[i].information);
                    }
                    if (symmetryMap.information == SymmetryMapInput.ReverseInfo)
                    {
                        symmetric[i] = (symmetric[i].decisionIndex, (byte)(decision.NumPossibleActions - symmetric[i].information + 1));
                        if (previousDecisionInInformationSetIsOpponents)
                            symmetric[i - 1] = (symmetric[i - 1].decisionIndex, (byte)(decision.NumPossibleActions - symmetric[i - 1].information + 1));
                    }
                }
                else if (decision.IsChance)
                {
                    bool isOnlyForPlayer1 = decision.PlayersToInform.Contains((byte)1) && !decision.PlayersToInform.Contains((byte)0);
                    if (isOnlyForPlayer1)
                        symmetric[i] = ((byte)(symmetric[i].decisionIndex - 1), symmetric[i].information);
                    if (symmetryMap.information == SymmetryMapInput.ReverseInfo)
                        symmetric[i] = (symmetric[i].decisionIndex, (byte)(decision.NumPossibleActions - symmetric[i].information + 1));
                }
            }
            return symmetric;
        }

        public static Dictionary<InformationSetNode, InformationSetNode> IdentifySymmetricInformationSets(List<InformationSetNode> nodes, GameDefinition gameDefinition)
        {
            if (!gameDefinition.GameIsSymmetric() || gameDefinition.DecisionsExecutionOrder.Any(x => x.SymmetryMap.information == SymmetryMapInput.NotCompatibleWithSymmetry))
                throw new Exception("This game or a decision in it is not compatible with symmetry.");
            Dictionary<string, InformationSetNode> reverseInformationSetToNodeMap = new Dictionary<string, InformationSetNode>();
            Dictionary<InformationSetNode, InformationSetNode> result = new Dictionary<InformationSetNode, InformationSetNode>();
            foreach (InformationSetNode node in nodes)
            {
                if (node.PlayerIndex == 0)
                {
                    byte[] reverse = node.GetSymmetricInformationSet(gameDefinition);
                    reverseInformationSetToNodeMap[string.Join(",", reverse)] = node;
                }
            }
            foreach (InformationSetNode node in nodes)
            {
                if (node.PlayerIndex == 1)
                {
                    InformationSetNode symmetricNode = reverseInformationSetToNodeMap[string.Join(",", node.InformationSetContents)];
                    result[node] = symmetricNode;
                }
            }
            return result;
        }

        public void CopyFromSymmetricInformationSet(InformationSetNode sourceInformationSet, SymmetryMapOutput symmetryMapOutput, bool verifySymmetry)
        {
            if (symmetryMapOutput == SymmetryMapOutput.ChanceDecision)
                throw new Exception("Cannot copy to chance decision information set.");
            for (byte a = 1; a <= Decision.NumPossibleActions; a++)
            {
                byte source = (byte)(a - 1);
                byte target;
                if (symmetryMapOutput == SymmetryMapOutput.SameAction)
                    target = (byte)(a - 1);
                else
                    target = (byte)(Decision.NumPossibleActions - a + 1 - 1);
                if (verifySymmetry)
                {
                    if (Math.Abs(NodeInformation[sumInversePiDimension, target] - sourceInformationSet.NodeInformation[sumInversePiDimension, source]) > 1E-6)
                        throw new Exception("Symmetry verification failed.");
                    if (Math.Abs(NodeInformation[sumRegretTimesInversePiDimension, target] - sourceInformationSet.NodeInformation[sumRegretTimesInversePiDimension, source]) > 1E-6)
                        throw new Exception("Symmetry verification failed.");
                }
                for (int dimension = 0; dimension <= scratchDimension; dimension++)
                    SetNI(dimension, target, sourceInformationSet.NodeInformation[dimension, source]);
            }
        }

        #endregion

        #region General manipulation

        public void SetToMixedStrategyBasedOnRegretMatchingCumulativeRegrets(bool setAverageAndCumulativeStrategy)
        {
            double[] strategy = new double[NumPossibleActions];
            GetRegretMatchingProbabilities(strategy);
            SetToMixedStrategy(strategy, setAverageAndCumulativeStrategy);
        }

        public void SetToMixedStrategy(double[] strategy, bool setAverageAndCumulativeStrategy)
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double probabilityValue = strategy[a - 1];
                SetCurrentStrategyValue(a, probabilityValue, setAverageAndCumulativeStrategy);
            }
        }

        public void SetCurrentStrategyValue(byte a, double probabilityValue, bool setAverageAndCumulativeStrategy)
        {
            if (double.IsNaN(probabilityValue))
                throw new Exception("Probability value is not a number");
            SetNI(currentProbabilityDimension, a - 1, probabilityValue);
            SetNI(currentProbabilityForOpponentDimension, a - 1, probabilityValue);
            if (setAverageAndCumulativeStrategy)
            {
                SetNI(averageStrategyProbabilityDimension, a - 1, probabilityValue);
                SetNI(cumulativeStrategyDimension, a - 1, probabilityValue);
            }
        }
        public void SetCurrentAndAverageStrategyValues(byte a, double current, double average)
        {
            if (double.IsNaN(current) || double.IsNaN(average))
                throw new Exception();
            SetNI(currentProbabilityDimension, a - 1, current);
            SetNI(currentProbabilityForOpponentDimension, a - 1, current);
            SetNI(averageStrategyProbabilityDimension, a - 1, average);
            SetNI(cumulativeStrategyDimension, a - 1, average);
        }

        public void SetCurrentAndAverageProbabilities(double[] probabilities)
        {
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double p = probabilities[a - 1];
                if (double.IsNaN(p))
                    throw new Exception();
                SetNI(currentProbabilityDimension, a - 1, p);
                SetNI(currentProbabilityForOpponentDimension, a - 1, p);
                SetNI(averageStrategyProbabilityDimension, a - 1, p);
                SetNI(cumulativeStrategyDimension, a - 1, p);
            }
        }

        public void SetToPureStrategy(byte action, bool setAverageAndCumulativeStrategy)
        {
            for (byte a = 1; a <= NumPossibleActions; a++)
            {
                double v = action == a ? 1.0 : 0;
                SetNI(currentProbabilityDimension, a - 1, v);
                SetNI(currentProbabilityForOpponentDimension, a - 1, v);
                if (setAverageAndCumulativeStrategy)
                {
                    SetNI(averageStrategyProbabilityDimension, a - 1, v);
                    SetNI(cumulativeStrategyDimension, a - 1, v);
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
                    SetNI(averageStrategyProbabilityDimension, action - 1, 0);
                    SetNI(cumulativeStrategyDimension, action - 1, 0);
                }
            }
            if (reallocated > 0)
            {
                double multiplyBy = 1.0 / (1.0 - reallocated);
                if (double.IsNaN(multiplyBy))
                    throw new Exception();
                for (byte action = 1; action < NumPossibleActions; action++)
                    SetNI(averageStrategyProbabilityDimension, action - 1, NodeInformation[averageStrategyProbabilityDimension, action - 1] * multiplyBy);
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
                if (double.IsNaN(reallocation))
                    throw new Exception();
                AddNI(averageStrategyProbabilityDimension, highest.index, -reallocation);
                AddNI(averageStrategyProbabilityDimension, secondHighest.index, reallocation);
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

        public double PVP(int iteration, byte action, Func<double> multiplyByFn)
        {
            double d = PastValues[iteration][action - 1];
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
                        total += PastValues[p][a - 1];
                    }

                    double v = total / (LastPastValueIndexRecorded + 1);
                    SetNI(averageStrategyProbabilityDimension, a - 1, v);
                    if (double.IsNaN(v))
                        throw new Exception();
                }
        }
        public void SetCurrentAndAverageStrategyToPastValue(int pastValueIndex, byte? playerMatchRequirement = null)
        {
            if (playerMatchRequirement is byte p && p != PlayerIndex)
                return;
            for (int a = 1; a <= NumPossibleActions; a++)
            {
                double pv = PastValues[pastValueIndex][a - 1];
                SetNI(currentProbabilityDimension, a - 1, pv);
                SetNI(currentProbabilityForOpponentDimension, a - 1, pv);
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
                    int j0 = RandomGenerator.Next(total);
                    int j1 = RandomGenerator.Next(total);
                    double sumSqDiffs = 0;
                    for (int k = 0; k < NumPossibleActions; k++)
                    {
                        double diff = PastValues[j0][k] - PastValues[j1][k];
                        sumSqDiffs += diff * diff;
                    }
                    double distance = Math.Sqrt(sumSqDiffs);
                    sumDistances += distance;
                }
                double avgDistance = sumDistances / numToTest;
                avgDistanceString = avgDistance.ToSignificantFigures(3);

                ranges = new List<(int startIteration, int endIteration, int significantActions)>();
                int activeRangeStart = total / 2;
                int significantActionsInRange = 0;

                for (int i = activeRangeStart; i < total; i++)
                {
                    int significantActions = 0;
                    for (int k = 0; k < NumPossibleActions; k++)
                    {
                        if (PastValues[i][k] >= 0.01)
                        {
                            significantActions |= 1 << k;
                        }
                    }
                    if (significantActions != significantActionsInRange || i == total - 1)
                    {
                        if (i > 0)
                            ranges.Add((activeRangeStart, i - 1, significantActionsInRange));
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
                        if ((sigActionsBits & 1 << i) != 0)
                            sigActions.Add(i);
                    return string.Join(",", sigActions);
                }

                rangesString = string.Join("; ", ranges.Select(x => $"({x.startIteration}-{x.endIteration}): {GetActionsAsString(x.significantActions)}"));
            }
            string hedgeString = GetCurrentProbabilitiesAsString();
            double[] averageStrategies = GetAverageStrategiesAsArray();
            string avgStratString = GetAverageStrategiesAsString();
            bool avgStratSameAsBestResponse = averageStrategies[BestResponseAction - 1] > 0.9999999;
            TabbedText.WriteLine($"{(avgStratSameAsBestResponse ? "*" : "")} decision {Decision.Name} Information set {InformationSetNodeNumber} bestrespon {BestResponseAction} hedge {hedgeString} avg {avgStratString} avg distance {avgDistanceString} ranges: {rangesString}");
        }

        #endregion

    }
}
