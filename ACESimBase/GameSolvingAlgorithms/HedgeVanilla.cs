﻿using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{

    // DEBUG TODO: Make generalized best response work with distributed chance actions

    // DEBUG TODO: Fix the reporting -- after generating reports every n iterations including best response, the within-iteration best response values are no longer valid. Perhaps relatedly, the best response values for player 0 are always 0 and those for player 1 are wrong (maybe that represents the improvement, but it is wrong too -- so maybe we should just delete it).

    // DEBUG TODO 1. add pruning to unrolled.

    public partial class CounterfactualRegretMinimization
    {
        #region Game state management

        public void InitializeInformationSets()
        {
            int numInformationSets = InformationSets.Count;
            Parallel.For(0, numInformationSets, n => InformationSets[n].InitializeNormalizedHedge());
        }

        public void UpdateInformationSets(int iteration)
        {
            int numInformationSets = InformationSets.Count;
            Parallel.For(0, numInformationSets, n => InformationSets[n].UpdateNormalizedHedge(iteration));
        }

        #endregion

        #region Unrolled preparation

        // We can achieve considerable improvements in performance by unrolling the algorithm. Instead of traversing the tree, we simply have a series of simple commands that can be processed on an array. The challenge is that we need to create this series of commands. This section prepares for the copying of data between information sets and the array. We can compare the outcomes of the regular algorithm and the unrolled version (which should always be equal) by using TraceCFR = true.

        private ArrayCommandList Unroll_Commands;
        private int Unroll_SizeOfArray;

        public unsafe string Unroll_SolveHedgeVanillaCFR()
        {
            string reportString = null;
            InitializeInformationSets();
            Unroll_CreateUnrolledCommandList();
            double[] array = new double[Unroll_SizeOfArray];
            for (int iteration = 1; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                HedgeVanillaIteration = iteration;
                HedgeVanillaIterationInt = iteration;
                HedgeVanillaIterationStopwatch.Start();
                Unroll_ExecuteUnrolledCommands(array, iteration == 1);
                HedgeVanillaIterationStopwatch.Stop();
                MiniReport(iteration, Unroll_IterationResultForPlayers);
                UpdateInformationSets(iteration);
                reportString = GenerateReports(iteration,
                    () =>
                        $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                if (TraceCFR)
                { // only trace through iteration
                    TraceCommandList(array);
                    return "";
                }
            }
            return reportString;
        }

        public void TraceCommandList(double[] array)
        {
            string traceStringWithArrayStubs = TabbedText.AccumulatedText.ToString();
            string replaced = StringUtil.ReplaceArrayDesignationWithArrayItem(traceStringWithArrayStubs, array);
        }

        private void Unroll_CreateUnrolledCommandList()
        {
            const int max_num_commands = 150_000_000;
            Unroll_InitializeInitialArrayIndices();
            Unroll_Commands = new ArrayCommandList(max_num_commands, Unroll_InitialArrayIndex, EvolutionSettings.ParallelOptimization);
            ActionStrategy = ActionStrategies.NormalizedHedge;
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            List<int> resultIndices = new List<int>();
            var initialPiValuesCopied = Unroll_Commands.CopyToNew(Unroll_InitialPiValuesIndices, true);
            var initialPiValues2Copied = Unroll_Commands.CopyToNew(Unroll_InitialPiValuesIndices, true);

            Unroll_Commands.StartCommandChunk(false, null, "Iteration");
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                Unroll_Commands.StartCommandChunk(false, null, "Optimizing player " + p.ToString());
                if (TraceCFR)
                    TabbedText.WriteLine($"Unrolling for Player {p}");
                Unroll_HedgeVanillaCFR(ref historyPoint, p, initialPiValuesCopied, initialPiValues2Copied, Unroll_IterationResultForPlayersIndices[p], true, 0);
                Unroll_Commands.EndCommandChunk();
            }
            Unroll_Commands.EndCommandChunk();

            Unroll_SizeOfArray = Unroll_Commands.FullArraySize;
        }

        private void Unroll_ExecuteUnrolledCommands(double[] array, bool firstExecution)
        {
            Unroll_CopyInformationSetsToArray(array, firstExecution);
            Unroll_Commands.ExecuteAll(array);
            Unroll_CopyArrayToInformationSets(array);
            Unroll_DetermineIterationResultForEachPlayer(array);
        }

        private void Unroll_DetermineIterationResultForEachPlayer(double[] array)
        {
            Unroll_IterationResultForPlayers = new HedgeVanillaUtilities[NumNonChancePlayers]; // array items from indices above will be copied here
            for (byte p = 0; p < NumNonChancePlayers; p++)
                Unroll_IterationResultForPlayers[p] = new HedgeVanillaUtilities()
                {
                    HedgeVsHedge = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_HedgeVsHedgeIndex]],
                    AverageStrategyVsAverageStrategy = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_AverageStrategyIndex]],
                    BestResponseToAverageStrategy = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_BestResponseIndex]],
                };
        }

        // Let's store the index in the array at which we will place the various types of information set information.

        private const int Unroll_NumPiecesInfoPerInformationSetAction = 6;
        private const int Unroll_InformationSetPerActionOrder_AverageStrategy = 0;
        private const int Unroll_InformationSetPerActionOrder_HedgeProbability = 1;
        private const int Unroll_InformationSetPerActionOrder_LastRegret = 2;
        private const int Unroll_InformationSetPerActionOrder_BestResponseNumerator = 3;
        private const int Unroll_InformationSetPerActionOrder_BestResponseDenominator = 4;
        private const int Unroll_InformationSetPerActionOrder_CumulativeStrategy = 5;

        private int[][] Unroll_IterationResultForPlayersIndices;
        private HedgeVanillaUtilities[] Unroll_IterationResultForPlayers;
        private int[] Unroll_InformationSetsIndices;
        private int[] Unroll_ChanceNodesIndices;
        private Dictionary<(int chanceNodeNumber, int nondistributedActions), int> Unroll_ChanceNodesIndices_NondistributedActions;
        private int[] Unroll_FinalUtilitiesNodesIndices;
        private int[] Unroll_InitialPiValuesIndices = null;
        private int Unroll_One = -1;
        private int Unroll_AverageStrategyAdjustmentIndex = -1;
        private int Unroll_InitialArrayIndex = -1;

        // The following indices correspond to the order in HedgeVanillaUtilities
        private const int Unroll_Result_HedgeVsHedgeIndex = 0;
        private const int Unroll_Result_AverageStrategyIndex = 1;
        private const int Unroll_Result_BestResponseIndex = 2;

        private int Unroll_GetInformationSetIndex_LastBestResponse(int informationSetNumber, byte numPossibleActions) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * numPossibleActions);

        private int Unroll_GetInformationSetIndex_AverageStrategy(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_AverageStrategy;

        private int Unroll_GetInformationSetIndex_HedgeProbability(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_HedgeProbability;

        private int[] Unroll_GetInformationSetIndex_HedgeProbabilities_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unroll_InformationSetsIndices[informationSetNumber];
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_HedgeProbability;
            }
            return probabilities;
        }

        private int Unroll_GetInformationSetIndex_LastRegret(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_LastRegret;

        private int Unroll_GetInformationSetIndex_BestResponseNumerator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseNumerator;

        private int Unroll_GetInformationSetIndex_BestResponseDenominator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseDenominator;
        private int Unroll_GetInformationSetIndex_CumulativeStrategy(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CumulativeStrategy;

        private int Unroll_GetChanceNodeIndex(int chanceNodeNumber) => Unroll_ChanceNodesIndices[chanceNodeNumber];
        private int Unroll_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, byte action) => Unroll_ChanceNodesIndices[chanceNodeNumber] + (byte) (action - 1);

        private int Unroll_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, int nondistributedActions, byte action)
        {
            var key = (chanceNodeNumber, nondistributedActions);
            if (nondistributedActions == -1 || Unroll_ChanceNodesIndices_NondistributedActions == null || !Unroll_ChanceNodesIndices_NondistributedActions.ContainsKey(key))
            {
                return Unroll_GetChanceNodeIndex_ProbabilityForAction(chanceNodeNumber, action);
            }
            else
                return Unroll_ChanceNodesIndices_NondistributedActions[key] + (byte)(action - 1);
        }

        private int[] Unroll_GetChanceNodeIndices(int chanceNodeNumber, byte numPossibleActions)
        {
            int firstIndex = Unroll_GetChanceNodeIndex(chanceNodeNumber);
            return Enumerable.Range(0, numPossibleActions).Select(x => firstIndex + x).ToArray();
        }
        private int Unroll_GetFinalUtilitiesNodesIndex(int finalUtilitiesNodeNumber, byte playerBeingOptimized) => Unroll_FinalUtilitiesNodesIndices[finalUtilitiesNodeNumber] + playerBeingOptimized;

        private void Unroll_InitializeInitialArrayIndices()
        {
            int index = 1; // skip index 0 because we want to be able to identify references to index 0 as errors
            Unroll_ChanceNodesIndices = new int[ChanceNodes.Count];
            if (EvolutionSettings.DistributeChanceDecisions)
                Unroll_ChanceNodesIndices_NondistributedActions = new Dictionary<(int chanceNodeNumber, int nondistributedActions), int>();
            for (int i = 0; i < ChanceNodes.Count; i++)
            {
                int numItems = ChanceNodes[i].Decision.NumPossibleActions;
                Unroll_ChanceNodesIndices[i] = index;
                index += numItems;
                if (ChanceNodes[i] is ChanceNodeSettingsUnequalProbabilities unequal && unequal.ProbabilitiesForNondistributedActions != null)
                {
                    // This node has information relevant to distributed chance actions. We thus need to remember where the indices are for the chance node 
                    var keys = unequal.ProbabilitiesForNondistributedActions.Keys.OrderBy(x => x).ToList();
                    foreach (int nondistributedActions in keys)
                    {
                        Unroll_ChanceNodesIndices_NondistributedActions[(i /* == ChanceNodes[i].ChanceNodeNumber */, nondistributedActions)] = index;
                        index += numItems;
                    }
                }
            }
            Unroll_FinalUtilitiesNodesIndices = new int[FinalUtilitiesNodes.Count];
            for (int i = 0; i < FinalUtilitiesNodes.Count; i++)
            {
                Unroll_FinalUtilitiesNodesIndices[i] = index;
                int numItems = FinalUtilitiesNodes[i].Utilities.Length;
                index += numItems;
            }
            Unroll_InformationSetsIndices = new int[InformationSets.Count];
            for (int i = 0; i < InformationSets.Count; i++)
            {
                Unroll_InformationSetsIndices[i] = index;
                int numItems = Unroll_NumPiecesInfoPerInformationSetAction * InformationSets[i].NumPossibleActions + 1; // the plus 1 is for the last best response
                index += numItems;
            }
            Unroll_InitialPiValuesIndices = new int[NumNonChancePlayers];
            Unroll_IterationResultForPlayersIndices = new int[NumNonChancePlayers][];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                Unroll_InitialPiValuesIndices[p] = index++;
                Unroll_IterationResultForPlayersIndices[p] = new int[3];
                for (int i = 0; i < 3; i++)
                    Unroll_IterationResultForPlayersIndices[p][i] = index++;
            }
            Unroll_One = index++;
            Unroll_AverageStrategyAdjustmentIndex = index++;
            Unroll_InitialArrayIndex = index;
        }

        private void Unroll_CopyInformationSetsToArray(double[] array, bool copyChanceAndFinalUtilitiesNodes)
        {
            if (copyChanceAndFinalUtilitiesNodes)
            { // these only need to be copied once -- not every iteration
                Parallel.For(0, ChanceNodes.Count, i =>
                {
                    var chanceNode = ChanceNodes[i];
                    int initialIndex = Unroll_GetChanceNodeIndex(chanceNode.ChanceNodeNumber);
                    for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
                    {
                        array[initialIndex++] = chanceNode.GetActionProbability(a);
                    }
                    if (ChanceNodes[i] is ChanceNodeSettingsUnequalProbabilities unequal && unequal.ProbabilitiesForNondistributedActions != null)
                    {
                        // This node has information relevant to distributed chance actions. We thus need to remember where the indices are for the chance node 
                        var keys = unequal.ProbabilitiesForNondistributedActions.Keys.OrderBy(x => x).ToList();
                        foreach (int nondistributedActions in keys)
                        {
                            for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
                            {
                                array[initialIndex++] = chanceNode.GetActionProbability(a, nondistributedActions);
                            }
                        }
                    }
                });
                Parallel.For(0, FinalUtilitiesNodes.Count, x =>
                {
                    var finalUtilitiesNode = FinalUtilitiesNodes[x];
                    int initialIndex = Unroll_GetFinalUtilitiesNodesIndex(finalUtilitiesNode.FinalUtilitiesNodeNumber, 0);
                    for (byte p = 0; p < finalUtilitiesNode.Utilities.Length; p++)
                    {
                        array[initialIndex++] = finalUtilitiesNode.Utilities[p];
                    }
                });
            }
            Parallel.For(0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                int initialIndex = Unroll_InformationSetsIndices[infoSet.InformationSetNumber];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    array[initialIndex++] = infoSet.GetNormalizedHedgeAverageStrategy(action);
                    array[initialIndex++] = infoSet.GetNormalizedHedgeProbability(action);
                    array[initialIndex++] = 0; // initialize last regret to zero
                    array[initialIndex++] = 0; // initialize best response numerator to zero
                    array[initialIndex++] = 0; // initialize best response denominator to zero
                    array[initialIndex++] = 0; // initialize cumulative strategy increment to zero
                }
                array[initialIndex] = infoSet.LastBestResponseAction;
            });
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                array[Unroll_InitialPiValuesIndices[p]] = 1.0;
                for (int i = 0; i < 3; i++)
                    array[Unroll_IterationResultForPlayersIndices[p][i]] = 0;
            }
            CalculateDiscountingAdjustments();
            array[Unroll_One] = 1.0;
            array[Unroll_AverageStrategyAdjustmentIndex] = AverageStrategyAdjustment;
        }

        private void Unroll_CopyArrayToInformationSets(double[] array)
        {
            //for (int i = 0; i < array.Length; i++)
            //    System.Diagnostics.Debug.WriteLine($"{i}: {array[i]}");
            Parallel.For(0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    int index = Unroll_GetInformationSetIndex_LastRegret(infoSet.InformationSetNumber, action);
                    infoSet.NormalizedHedgeIncrementLastRegret(action, array[index]);
                    index = Unroll_GetInformationSetIndex_CumulativeStrategy(infoSet.InformationSetNumber, action);
                    infoSet.IncrementCumulativeStrategy(action, array[index]);
                }
            });
        }

        #endregion

        #region Unrolled algorithm

        public unsafe void Unroll_HedgeVanillaCFR(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool isUltimateResult, int nondistributedActions)
        {
            Unroll_Commands.IncrementDepth(false);
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                // Note: An alternative approach would be to add the utility value found here to the unrolled commands, instead of looking it up in the array. But this approach makes it possible to change some game parameters and thus the final utilities without regenerating commands.
                int finalUtilIndex = Unroll_Commands.CopyToNew(Unroll_GetFinalUtilitiesNodesIndex(finalUtilities.FinalUtilitiesNodeNumber, playerBeingOptimized), true);
                // Note: We must copy this so that we don't change the final utilities themselves.
                Unroll_Commands.CopyToExisting(resultArray[0], finalUtilIndex);
                Unroll_Commands.CopyToExisting(resultArray[1], finalUtilIndex);
                Unroll_Commands.CopyToExisting(resultArray[2], finalUtilIndex);
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                Unroll_HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues, resultArray, isUltimateResult, nondistributedActions);
            }
            else
                Unroll_HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues, resultArray, isUltimateResult, nondistributedActions);
            Unroll_Commands.DecrementDepth(false, playerBeingOptimized == NumNonChancePlayers - 1);
        }

        private unsafe void Unroll_HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool isUltimateResult, int nondistributedActions)
        {
            Unroll_Commands.IncrementDepth(false);

            int inversePi = Unroll_Commands.NewUninitialized();
            Unroll_GetInversePiValue(piValues, playerBeingOptimized, inversePi);
            int inversePiAvgStrat = Unroll_Commands.NewUninitialized();
            Unroll_GetInversePiValue(avgStratPiValues, playerBeingOptimized, inversePiAvgStrat);
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNodeTally)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            int[] actionProbabilities = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_HedgeProbabilities_All(informationSet.InformationSetNumber, numPossibleActions), true);
            int[] expectedValueOfAction = Unroll_Commands.NewUninitializedArray(numPossibleActions);
            int expectedValue = Unroll_Commands.NewZero();
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int nondistributedActionsNext = nondistributedActions;
                if (informationSet.Decision.NondistributedDecision)
                    nondistributedActionsNext += action * informationSet.Decision.NondistributedDecisionMultiplier;
                int probabilityOfAction = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_HedgeProbability(informationSet.InformationSetNumber, action), true);
                if (EvolutionSettings.PruneOnOpponentStrategy)
                    throw new NotImplementedException();
                //if (EvolutionSettings.PruneOnOpponentStrategy && playerBeingOptimized != playerMakingDecision && probabilityOfAction < EvolutionSettings.PruneOnOpponentStrategyThreshold)
                //    continue;
                int probabilityOfActionAvgStrat = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_AverageStrategy(informationSet.InformationSetNumber, action), true);
                int[] nextPiValues = Unroll_Commands.NewUninitializedArray(NumNonChancePlayers);
                Unroll_GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                int[] nextAvgStratPiValues = Unroll_Commands.NewUninitializedArray(NumNonChancePlayers);
                Unroll_GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);
                if (TraceCFR)
                {
                    int probabilityOfActionCopy = Unroll_Commands.CopyToNew(probabilityOfAction, false);
                    TabbedText.WriteLine(
                        $"code {informationSet.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.Name}) optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability ARRAY{probabilityOfActionCopy} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                int[] innerResult = Unroll_Commands.NewZeroArray(3);
                Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, innerResult, false, nondistributedActionsNext);
                Unroll_Commands.CopyToExisting(expectedValueOfAction[action - 1], innerResult[Unroll_Result_HedgeVsHedgeIndex]);
                if (playerMakingDecision == playerBeingOptimized)
                {
                    int lastBestResponseActionIndex = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNumber, (byte) informationSet.NumPossibleActions), true);
                    Unroll_Commands.InsertEqualsValueCommand(lastBestResponseActionIndex, (int)action);
                    Unroll_Commands.InsertIfCommand();
                        int bestResponseNumerator = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNumber, action), true);
                        int bestResponseDenominator = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNumber, action), true);
                        Unroll_Commands.IncrementByProduct(bestResponseNumerator, true, inversePiAvgStrat, innerResult[Unroll_Result_BestResponseIndex]);
                        Unroll_Commands.Increment(bestResponseDenominator, true, inversePiAvgStrat);
                        Unroll_Commands.CopyToExisting(resultArray[Unroll_Result_BestResponseIndex], innerResult[Unroll_Result_BestResponseIndex]);
                    // end of loop
                    Unroll_Commands.InsertEndIfCommand();
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_HedgeVsHedgeIndex], false, probabilityOfAction, innerResult[Unroll_Result_HedgeVsHedgeIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_AverageStrategyIndex], false, probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex]);
                }
                else
                {
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_HedgeVsHedgeIndex], false, probabilityOfAction, innerResult[Unroll_Result_HedgeVsHedgeIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_AverageStrategyIndex], false, probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_BestResponseIndex], false, probabilityOfActionAvgStrat, innerResult[Unroll_Result_BestResponseIndex]);
                }
                Unroll_Commands.IncrementByProduct(expectedValue, false, probabilityOfAction, expectedValueOfAction[action - 1]);

                if (TraceCFR)
                {
                    int expectedValueOfActionCopy = Unroll_Commands.CopyToNew(expectedValueOfAction[action - 1], false);
                    int bestResponseExpectedValueCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_BestResponseIndex], false);
                    int cumExpectedValueCopy = Unroll_Commands.CopyToNew(expectedValue, false);
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action} expected value ARRAY{expectedValueOfActionCopy} best response expected value ARRAY{bestResponseExpectedValueCopy} cum expected value ARRAY{cumExpectedValueCopy}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    int pi = Unroll_Commands.CopyToNew(piValues[playerBeingOptimized], false);
                    int regret = Unroll_Commands.CopyToNew(expectedValueOfAction[action - 1], false);
                    Unroll_Commands.Decrement(regret, expectedValue);
                    int lastRegret = Unroll_GetInformationSetIndex_LastRegret(informationSet.InformationSetNumber, action);
                    Unroll_Commands.IncrementByProduct(lastRegret, true, inversePi, regret);
                    // now contribution to average strategy
                    int contributionToAverageStrategy = Unroll_Commands.CopyToNew(pi, false);
                    Unroll_Commands.MultiplyBy(contributionToAverageStrategy, actionProbabilities[action - 1]);
                    if (EvolutionSettings.UseRegretAndStrategyDiscounting)
                        Unroll_Commands.MultiplyBy(contributionToAverageStrategy, Unroll_Commands.CopyToNew(Unroll_AverageStrategyAdjustmentIndex, true));
                    int cumulativeStrategy = Unroll_GetInformationSetIndex_CumulativeStrategy(informationSet.InformationSetNumber, action);
                    Unroll_Commands.Increment(cumulativeStrategy, true, contributionToAverageStrategy);
                    if (TraceCFR)
                    {
                        int piCopy = Unroll_Commands.CopyToNew(pi, false);
                        int piValuesZeroCopy = Unroll_Commands.CopyToNew(piValues[0], false);
                        int piValuesOneCopy = Unroll_Commands.CopyToNew(piValues[1], false);
                        int regretCopy = Unroll_Commands.CopyToNew(regret, false);
                        int inversePiCopy = Unroll_Commands.CopyToNew(inversePi, false);
                        int contributionToAverageStrategyCopy = Unroll_Commands.CopyToNew(contributionToAverageStrategy, false);
                        int cumulativeStrategyCopy = Unroll_Commands.CopyToNew(cumulativeStrategy, false);
                        TabbedText.WriteLine($"PiValues ARRAY{piValuesZeroCopy} ARRAY{piValuesOneCopy} pi for optimized ARRAY{piCopy}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} probability ARRAY{actionProbabilities[action - 1]} regret ARRAY{regretCopy} inversePi ARRAY{inversePiCopy} avg_strat_incrememnt ARRAY{contributionToAverageStrategyCopy} cum_strategy ARRAY{cumulativeStrategyCopy}");
                    }
                }
            }
            Unroll_Commands.DecrementDepth(false);
        }

        private unsafe void Unroll_HedgeVanillaCFR_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool isUltimateResult, int nondistributedActions)
        {
            Unroll_Commands.IncrementDepth(false);
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = chanceNodeSettings.Decision.NumPossibleActions;
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                numPossibleActionsToExplore = 1;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            int[] probabilityAdjustedInnerResult = Unroll_Commands.NewUninitializedArray(3); // must allocate this outside the parallel loop, because if we have commands writing to an array created in the parallel loop, the array indices will change
            if (chanceNodeSettings.Decision.Unroll_Parallelize)
                Unroll_Commands.StartCommandChunk(true, null, chanceNodeSettings.Decision.Name);
            int? firstCommandToRepeat = null;
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                if (chanceNodeSettings.Decision.Unroll_Parallelize)
                {
                    Unroll_Commands.StartCommandChunk(false /* inner commands are run sequentially */, firstCommandToRepeat, chanceNodeSettings.Decision.Name + "=" + action.ToString());
                    if (action == 1 && chanceNodeSettings.Decision.Unroll_Parallelize_Identical)
                        firstCommandToRepeat = Unroll_Commands.NextCommandIndex;
                }
                var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                Unroll_Commands.ZeroExisting(probabilityAdjustedInnerResult);
                Unroll_HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, 
                    playerBeingOptimized, piValues, avgStratPiValues,
                        chanceNodeSettings, action, probabilityAdjustedInnerResult, false, nondistributedActions);
                Unroll_Commands.IncrementArrayBy(resultArray, isUltimateResult, probabilityAdjustedInnerResult);
                
                if (chanceNodeSettings.Decision.Unroll_Parallelize)
                    Unroll_Commands.EndCommandChunk(isUltimateResult ? null : resultArray, action != 1);
            }
            if (chanceNodeSettings.Decision.Unroll_Parallelize)
                Unroll_Commands.EndCommandChunk();

            Unroll_Commands.DecrementDepth(false);
        }

        private unsafe void Unroll_HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, ChanceNodeSettings chanceNodeSettings, byte action, int[] resultArray, bool isUltimateResult, int nondistributedActions)
        {
            Unroll_Commands.IncrementDepth(false);
            int actionProbabilityIndex = Unroll_GetChanceNodeIndex_ProbabilityForAction(chanceNodeSettings.ChanceNodeNumber, nondistributedActions, action);
            int actionProbability = Unroll_Commands.CopyToNew(actionProbabilityIndex, true);
            int nondistributedActionsNext = nondistributedActions;
            if (chanceNodeSettings.Decision.NondistributedDecision)
                nondistributedActionsNext += action * chanceNodeSettings.Decision.NondistributedDecisionMultiplier;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                actionProbability = Unroll_Commands.CopyToNew(Unroll_One, true);
            int[] nextPiValues = Unroll_Commands.NewUninitializedArray(NumNonChancePlayers);
            Unroll_GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true, nextPiValues);
            int[] nextAvgStratPiValues = Unroll_Commands.NewUninitializedArray(NumNonChancePlayers);
            Unroll_GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true, nextAvgStratPiValues);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);

            if (TraceCFR)
            {
                int actionProbabilityCopy = Unroll_Commands.CopyToNew(actionProbability, false);
                TabbedText.WriteLine(
                    $"Chance code {chanceNodeSettings.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == chanceNodeSettings.DecisionByteCode).Name}) action {action} probability ARRAY{actionProbabilityCopy} ...");
                TabbedText.Tabs++;
            }
            int[] innerResult = Unroll_Commands.NewZeroArray(3);
            Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, innerResult, false, nondistributedActionsNext);
            Unroll_Commands.CopyToExisting(resultArray, innerResult);
            if (TraceCFR)
            {
                // save current hedge result before multiplying
                int beforeMultipleHedgeCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_HedgeVsHedgeIndex], false);
                int actionProbabilityCopy = Unroll_Commands.CopyToNew(actionProbability, false);

                Unroll_Commands.MultiplyArrayBy(resultArray, actionProbability);

                int resultHedgeCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_HedgeVsHedgeIndex], false);

                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} value ARRAY{beforeMultipleHedgeCopy} probability ARRAY{actionProbabilityCopy} expected value contribution ARRAY{resultHedgeCopy}");
            }
            else
                Unroll_Commands.MultiplyArrayBy(resultArray, actionProbability);

            Unroll_Commands.DecrementDepth(false);
        }

        private unsafe void Unroll_GetNextPiValues(int[] currentPiValues, byte playerIndex, int probabilityToMultiplyBy, bool changeOtherPlayers, int[] resultArray)
        {
            Unroll_Commands.IncrementDepth(false);
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                int currentPiValue = currentPiValues[p];
                Unroll_Commands.CopyToExisting(resultArray[p], currentPiValue);
                if (p == playerIndex)
                {
                    if (!changeOtherPlayers)
                        Unroll_Commands.MultiplyBy(resultArray[p], probabilityToMultiplyBy);
                }
                else
                {
                    if (changeOtherPlayers)
                        Unroll_Commands.MultiplyBy(resultArray[p], probabilityToMultiplyBy);
                }
            }
            Unroll_Commands.DecrementDepth(false);
        }

        private unsafe void Unroll_GetInversePiValue(int[] piValues, byte playerIndex, int inversePiValueResult)
        {
            Unroll_Commands.IncrementDepth(false);
            if (NumNonChancePlayers == 2)
                Unroll_Commands.CopyToExisting(inversePiValueResult, piValues[(byte)1 - playerIndex]);
            else
            {
                bool firstPlayerOtherThanMainFound = false;
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    if (p != playerIndex)
                    {
                        if (firstPlayerOtherThanMainFound)
                        {
                            Unroll_Commands.MultiplyBy(inversePiValueResult, piValues[p]);
                        }
                        else
                        {
                            Unroll_Commands.CopyToExisting(inversePiValueResult, piValues[p]);
                            firstPlayerOtherThanMainFound = true;
                        }
                    }
            }
            Unroll_Commands.DecrementDepth(false);
        }

        #endregion

        #region Core algorithm

        public unsafe string SolveHedgeVanillaCFR()
        {
            if (EvolutionSettings.UnrollAlgorithm)
                return Unroll_SolveHedgeVanillaCFR();
            string reportString = null;
            InitializeInformationSets();
            for (int iteration = 1; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                reportString = HedgeVanillaCFRIteration(iteration);
            }
            return reportString;
        }

        double HedgeVanillaIteration;
        int HedgeVanillaIterationInt;
        Stopwatch HedgeVanillaIterationStopwatch = new Stopwatch();
        private unsafe string HedgeVanillaCFRIteration(int iteration)
        {
            HedgeVanillaIteration = iteration;
            HedgeVanillaIterationInt = iteration;
            CalculateDiscountingAdjustments();

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            ActionStrategy = ActionStrategies.NormalizedHedge;

            HedgeVanillaUtilities[] results = new HedgeVanillaUtilities[NumNonChancePlayers];
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double* initialPiValues = stackalloc double[MaxNumMainPlayers];
                double* initialAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
                GetInitialPiValues(initialPiValues);
                GetInitialPiValues(initialAvgStratPiValues);
                if (TraceCFR)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized}");
                HedgeVanillaIterationStopwatch.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                results[playerBeingOptimized] = HedgeVanillaCFR(ref historyPoint, playerBeingOptimized, initialPiValues, initialAvgStratPiValues, 0);
                HedgeVanillaIterationStopwatch.Stop();
            }
            MiniReport(iteration, results);

            UpdateInformationSets(iteration);

            reportString = GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }

        private unsafe void MiniReport(int iteration, HedgeVanillaUtilities[] results)
        {
            const int MiniReportEveryPIterations = 10;
            if (iteration % MiniReportEveryPIterations == 0)
            {
                TabbedText.WriteLine($"Iteration {iteration}");
                TabbedText.Tabs++;
                for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                    TabbedText.WriteLine($"Player {playerBeingOptimized} {results[playerBeingOptimized]} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                TabbedText.Tabs--;
            }
        }

        private unsafe void CalculateDiscountingAdjustments()
        {
            double positivePower = Math.Pow(HedgeVanillaIteration, EvolutionSettings.Discounting_Alpha);
            double negativePower = Math.Pow(HedgeVanillaIteration, EvolutionSettings.Discounting_Beta);
            PositiveRegretsAdjustment = positivePower / (positivePower + 1.0);
            NegativeRegretsAdjustment = negativePower / (negativePower + 1.0);
            AverageStrategyAdjustment = Math.Pow(HedgeVanillaIteration / (HedgeVanillaIteration), EvolutionSettings.Discounting_Gamma);
        }

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe HedgeVanillaUtilities HedgeVanillaCFR(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, double* avgStratPiValues, int nondistributedActions)
        {
            //if (usePruning && ShouldPruneIfPruning(piValues))
            //    return new HedgeVanillaUtilities { AverageStrategyVsAverageStrategy = 0, BestResponseToAverageStrategy = 0, HedgeVsHedge = 0 };
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                double util = finalUtilities.Utilities[playerBeingOptimized];
                return new HedgeVanillaUtilities { AverageStrategyVsAverageStrategy = util, BestResponseToAverageStrategy = util, HedgeVsHedge = util };
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues, nondistributedActions);
            }
            else
                return HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues, nondistributedActions);
        }

        bool IncludeAsteriskForBestResponseInTrace = false;

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, double* avgStratPiValues, int nondistributedActions)
        {
            double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
            double inversePiAvgStrat = GetInversePiValue(avgStratPiValues, playerBeingOptimized);
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            double* nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNodeTally)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction != null)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                    actionProbabilities, (byte)alwaysDoAction);
            else
            {
                // TODO: Consider pruning here
                informationSet.GetNormalizedHedgeProbabilities(actionProbabilities);
            }
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            HedgeVanillaUtilities result = default;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int nondistributedActionsNext = nondistributedActions;
                if (informationSet.Decision.NondistributedDecision)
                    nondistributedActionsNext += action * informationSet.Decision.NondistributedDecisionMultiplier;
                double probabilityOfAction = actionProbabilities[action - 1];
                if (EvolutionSettings.PruneOnOpponentStrategy && playerBeingOptimized != playerMakingDecision && probabilityOfAction < EvolutionSettings.PruneOnOpponentStrategyThreshold)
                    continue;
                double probabilityOfActionAvgStrat = informationSet.GetNormalizedHedgeAverageStrategy(action);
                GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false,
                    nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);
                if (TraceCFR)
                {
                    TabbedText.WriteLine(
                        $"code {informationSet.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.Name}) optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                HedgeVanillaUtilities innerResult = HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, nondistributedActionsNext);
                expectedValueOfAction[action - 1] = innerResult.HedgeVsHedge;
                if (playerMakingDecision == playerBeingOptimized)
                {
                    if (informationSet.LastBestResponseAction == action)
                    {
                        // Because this is the best response action, the best response utility that we get should be propagated back directly. Meanwhile, we want to keep track of all the times that we traverse through this information set, weighing the best response results (which may vary, since our terminal nodes may vary) by the inversePi.
                        informationSet.IncrementBestResponse(action, inversePiAvgStrat, innerResult.BestResponseToAverageStrategy);
                        result.BestResponseToAverageStrategy = innerResult.BestResponseToAverageStrategy;
                    }
                    // The other result utilities are just the probability adjusted utilities. 
                    result.HedgeVsHedge += probabilityOfAction * innerResult.HedgeVsHedge;
                    result.AverageStrategyVsAverageStrategy += probabilityOfActionAvgStrat * innerResult.AverageStrategyVsAverageStrategy;
                }
                else
                {
                    // This isn't the decision being optimized, so we essentially just need to pass through the player being optimized's utilities, weighting by the probability for each action (which will depend on whether we are using average strategy or hedge to calculate the utilities).
                    result.IncrementBasedOnNotYetProbabilityAdjusted(ref innerResult, probabilityOfActionAvgStrat, probabilityOfAction);
                }
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action}{(informationSet.LastBestResponseAction == action && IncludeAsteriskForBestResponseInTrace ? "*" : "")} expected value {expectedValueOfAction[action - 1]} best response expected value {result.BestResponseToAverageStrategy} cum expected value {expectedValue}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    // NOTE: With normalized hedge, we do NOT discount regrets, because we're normalizing regrets at the end of each iteration.
                    informationSet.NormalizedHedgeIncrementLastRegret(action, inversePi * regret);
                    double contributionToAverageStrategy = pi * actionProbabilities[action - 1];
                    if (EvolutionSettings.UseRegretAndStrategyDiscounting)
                        contributionToAverageStrategy *=  AverageStrategyAdjustment;
                    if (EvolutionSettings.ParallelOptimization)
                        informationSet.IncrementCumulativeStrategy_Parallel(action, contributionToAverageStrategy);
                    else
                        informationSet.IncrementCumulativeStrategy(action, contributionToAverageStrategy);
                    if (TraceCFR)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]} pi for optimized {pi}");
                        //TabbedText.WriteLine($"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} probability {actionProbabilities[action - 1]} regret {regret} inversePi {inversePi} avg_strat_incrememnt {contributionToAverageStrategy} cum_strategy {informationSet.GetCumulativeStrategy(action)}");
                    }
                }
            }
            return result;
        }

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, double* avgStratPiValues, int nondistributedActions)
        {
            HedgeVanillaUtilities result = default;
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                numPossibleActionsToExplore = 1;
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte)(numPossibleActionsToExplore + 1),
                action =>
                {
                    var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                    HedgeVanillaUtilities probabilityAdjustedInnerResult =  HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues, avgStratPiValues, chanceNodeSettings, action, nondistributedActions);
                    result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);
                });

            return result;
        }

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, double* avgStratPiValues, ChanceNodeSettings chanceNodeSettings, byte action, int nondistributedActions)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            double* nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            double actionProbability = chanceNodeSettings.GetActionProbability(action, nondistributedActions);
            int nondistributedActionsNext = nondistributedActions;
            if (chanceNodeSettings.Decision.NondistributedDecision)
                nondistributedActionsNext += action * chanceNodeSettings.Decision.NondistributedDecisionMultiplier;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                actionProbability = 1.0;
            GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true,
                nextPiValues);
            GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true,
                nextAvgStratPiValues);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.WriteLine(
                    $"Chance code {chanceNodeSettings.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == chanceNodeSettings.DecisionByteCode).Name}) action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            HedgeVanillaUtilities result =
                HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, nondistributedActionsNext);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} value {result.HedgeVsHedge} probability {actionProbability} expected value contribution {result.HedgeVsHedge * actionProbability}");
            }
            result.MakeProbabilityAdjusted(actionProbability);

            return result;
        }

        #endregion

    }
}