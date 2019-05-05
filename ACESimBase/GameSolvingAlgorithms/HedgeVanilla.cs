using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
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

        // We can achieve considerable improvements in performance by unrolling the algorithm. Instead of traversing the tree, we simply have a series of simple commands that can be processed on an array. The challenge is that we need to create this series fo commands. 


        private ArrayCommandList Unrolled_Commands;
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
                Unroll_ExecuteUnrolledCommands(array, iteration == 1);
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
            const int max_num_commands = 50_000_000;
            Unroll_InitializeInitialArrayIndices();
            Unrolled_Commands = new ArrayCommandList(max_num_commands, null, Unrolled_InitialArrayIndex);
            ActionStrategy = ActionStrategies.NormalizedHedge;
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                if (TraceCFR)
                    TabbedText.WriteLine($"Unrolling for Player {p}");
                Unroll_HedgeVanillaCFR(ref historyPoint, p, Unrolled_InitialPiValuesIndices, Unrolled_InitialPiValuesIndices);
            }
            Unroll_SizeOfArray = Unrolled_Commands.MaxArrayIndex + 1;
        }

        private void Unroll_ExecuteUnrolledCommands(double[] array, bool firstExecution)
        {
            Unroll_CopyInformationSetsToArray(array, firstExecution);
            Unrolled_Commands.ExecuteAll(array);
            Unroll_CopyArrayToInformationSets(array);
        }

        // Let's store the index in the array at which we will place the various types of information set information.

        private const int Unroll_NumPiecesInfoPerInformationSetAction = 6;
        private const int Unroll_InformationSetPerActionOrder_AverageStrategy = 0;
        private const int Unroll_InformationSetPerActionOrder_HedgeProbability = 1;
        private const int Unroll_InformationSetPerActionOrder_LastRegret = 2;
        private const int Unroll_InformationSetPerActionOrder_BestResponseNumerator = 3;
        private const int Unroll_InformationSetPerActionOrder_BestResponseDenominator = 4;
        private const int Unroll_InformationSetPerActionOrder_CumulativeStrategy = 5;


        private int[] Unrolled_InformationSetsIndices;
        private int[] Unrolled_ChanceNodesIndices;
        private int[] Unrolled_FinalUtilitiesNodesIndices;
        private int[] Unrolled_InitialPiValuesIndices = null;
        private int Unrolled_AverageStrategyAdjustmentIndex = -1;
        private int Unrolled_InitialArrayIndex = -1;

        // The following indices correspond to the order in HedgeVanillaUtilities
        private const int Unroll_Result_HedgeVsHedgeIndex = 0;
        private const int Unroll_Result_AverageStrategyIndex = 1;
        private const int Unroll_Result_BestResponseIndex = 2;

        private int Unrolled_GetInformationSetIndex_LastBestResponse(int informationSetNumber, byte numPossibleActions) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * numPossibleActions);

        private int Unrolled_GetInformationSetIndex_AverageStrategy(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_AverageStrategy;

        private int Unrolled_GetInformationSetIndex_HedgeProbability(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_HedgeProbability;

        private int[] Unrolled_GetInformationSetIndex_HedgeProbabilities_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unrolled_InformationSetsIndices[informationSetNumber];
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_HedgeProbability;
            }
            return probabilities;
        }

        private int Unrolled_GetInformationSetIndex_LastRegret(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_LastRegret;

        private int Unrolled_GetInformationSetIndex_BestResponseNumerator(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseNumerator;

        private int Unrolled_GetInformationSetIndex_BestResponseDenominator(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseDenominator;
        private int Unrolled_GetInformationSetIndex_CumulativeStrategy(int informationSetNumber, byte action) => Unrolled_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CumulativeStrategy;

        private int Unrolled_GetChanceNodeIndex(int chanceNodeNumber) => Unrolled_ChanceNodesIndices[chanceNodeNumber];
        private int Unrolled_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, byte action) => Unrolled_ChanceNodesIndices[chanceNodeNumber] + (byte) (action - 1);
        private int[] Unrolled_GetChanceNodeIndices(int chanceNodeNumber, byte numPossibleActions)
        {
            int firstIndex = Unrolled_GetChanceNodeIndex(chanceNodeNumber);
            return Enumerable.Range(0, numPossibleActions).Select(x => firstIndex + x).ToArray();
        }
        private int Unrolled_GetFinalUtilitiesNodesIndex(int finalUtilitiesNodeNumber, byte playerBeingOptimized) => Unrolled_FinalUtilitiesNodesIndices[finalUtilitiesNodeNumber] + playerBeingOptimized;

        private void Unroll_InitializeInitialArrayIndices()
        {
            int index = 1; // skip index 0 because we want to be able to identify references to index 0 as errors
            Unrolled_ChanceNodesIndices = new int[ChanceNodes.Count];
            for (int i = 0; i < ChanceNodes.Count; i++)
            {
                Unrolled_ChanceNodesIndices[i] = index;
                int numItems = ChanceNodes[i].Decision.NumPossibleActions;
                index += numItems;
            }
            Unrolled_FinalUtilitiesNodesIndices = new int[FinalUtilitiesNodes.Count];
            for (int i = 0; i < FinalUtilitiesNodes.Count; i++)
            {
                Unrolled_FinalUtilitiesNodesIndices[i] = index;
                int numItems = FinalUtilitiesNodes[i].Utilities.Length;
                index += numItems;
            }
            Unrolled_InformationSetsIndices = new int[InformationSets.Count];
            for (int i = 0; i < InformationSets.Count; i++)
            {
                Unrolled_InformationSetsIndices[i] = index;
                int numItems = Unroll_NumPiecesInfoPerInformationSetAction * InformationSets[i].NumPossibleActions + 1; // the plus 1 is for the last best response
                index += numItems;
            }
            Unrolled_InitialPiValuesIndices = new int[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                Unrolled_InitialPiValuesIndices[p] = index++;
            Unrolled_AverageStrategyAdjustmentIndex = index++;
            Unrolled_InitialArrayIndex = index;
        }

        private void Unroll_CopyInformationSetsToArray(double[] array, bool copyChanceAndFinalUtilitiesNodes)
        {
            if (copyChanceAndFinalUtilitiesNodes)
            { // these only need to be copied once -- not every iteration
                Parallel.For(0, ChanceNodes.Count, x =>
                {
                    var chanceNode = ChanceNodes[x];
                    int initialIndex = Unrolled_GetChanceNodeIndex(chanceNode.ChanceNodeNumber);
                    for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
                    {
                        array[initialIndex++] = chanceNode.GetActionProbability(a);
                    }
                });
                Parallel.For(0, FinalUtilitiesNodes.Count, x =>
                {
                    var finalUtilitiesNode = FinalUtilitiesNodes[x];
                    int initialIndex = Unrolled_GetFinalUtilitiesNodesIndex(finalUtilitiesNode.FinalUtilitiesNodeNumber, 0);
                    for (byte p = 0; p < finalUtilitiesNode.Utilities.Length; p++)
                    {
                        array[initialIndex++] = finalUtilitiesNode.Utilities[p];
                    }
                });
            }
            Parallel.For(0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                int initialIndex = Unrolled_InformationSetsIndices[infoSet.InformationSetNumber];
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
                array[Unrolled_InitialPiValuesIndices[p]] = 1.0;
            }
            CalculateDiscountingAdjustments();
            array[Unrolled_AverageStrategyAdjustmentIndex] = AverageStrategyAdjustment;
        }

        private void Unroll_CopyArrayToInformationSets(double[] array)
        {
            Parallel.For(0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    int index = Unrolled_GetInformationSetIndex_LastRegret(infoSet.InformationSetNumber, action);
                    infoSet.NormalizedHedgeIncrementLastRegret(action, array[index]);
                    index = Unrolled_GetInformationSetIndex_CumulativeStrategy(infoSet.InformationSetNumber, action);
                    infoSet.IncrementCumulativeStrategy(action, array[index]);
                }
            });
        }

        #endregion

        #region Unrolled algorithm

        public unsafe int[] Unroll_HedgeVanillaCFR(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                // Note: An alternative approach would be to add the utility value found here to the unrolled commands, instead of looking it up in the array. But this approach makes it possible to change some game parameters and thus the final utilities without regenerating commands.
                int finalUtilIndex = Unrolled_GetFinalUtilitiesNodesIndex(finalUtilities.FinalUtilitiesNodeNumber, playerBeingOptimized);
                // Note: We must copy this so that we don't change the final utilities themselves.
                int finalUtilIndexCopy1 = Unrolled_Commands.CopyToNew(finalUtilIndex);
                int finalUtilIndexCopy2 = Unrolled_Commands.CopyToNew(finalUtilIndex);
                int finalUtilIndexCopy3 = Unrolled_Commands.CopyToNew(finalUtilIndex);
                return new int[] { finalUtilIndexCopy1, finalUtilIndexCopy2, finalUtilIndexCopy3 };
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return Unroll_HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
            }
            else
                return Unroll_HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
        }

        private unsafe int[] Unroll_HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues)
        {
            int inversePi = Unroll_GetInversePiValue(piValues, playerBeingOptimized);
            int inversePiAvgStrat = Unroll_GetInversePiValue(avgStratPiValues, playerBeingOptimized);
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNodeTally)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            int[] actionProbabilities = Unrolled_GetInformationSetIndex_HedgeProbabilities_All(informationSet.InformationSetNumber, numPossibleActions);
            int[] expectedValueOfAction = Unrolled_Commands.NewZeroArray(numPossibleActions);
            int expectedValue = Unrolled_Commands.NewZero();
            int[] result = Unrolled_Commands.NewZeroArray(3);
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int probabilityOfAction = Unrolled_GetInformationSetIndex_HedgeProbability(informationSet.InformationSetNumber, action);
                if (EvolutionSettings.PruneOnOpponentStrategy)
                    throw new NotImplementedException();
                //if (EvolutionSettings.PruneOnOpponentStrategy && playerBeingOptimized != playerMakingDecision && probabilityOfAction < EvolutionSettings.PruneOnOpponentStrategyThreshold)
                //    continue;
                int probabilityOfActionAvgStrat = Unrolled_GetInformationSetIndex_AverageStrategy(informationSet.InformationSetNumber, action);
                int[] nextPiValues = Unroll_GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false); // reduce probability associated with player being optimized, without changing probabilities for other players
                int[] nextAvgStratPiValues = Unroll_GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false);
                if (TraceCFR)
                {
                    int probabilityOfActionCopy = Unrolled_Commands.CopyToNew(probabilityOfAction);
                    TabbedText.WriteLine(
                        $"code {informationSet.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.Name}) optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability ARRAY{probabilityOfActionCopy} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                int[] innerResult = Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
                Unrolled_Commands.CopyToExisting(expectedValueOfAction[action - 1], innerResult[Unroll_Result_HedgeVsHedgeIndex]);
                if (playerMakingDecision == playerBeingOptimized)
                {
                    int lastBestResponseActionIndex = Unrolled_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNumber, (byte) informationSet.NumPossibleActions);
                    Unrolled_Commands.InsertNotEqualsValueCommand(lastBestResponseActionIndex, (int)action);
                    result[Unroll_Result_BestResponseIndex] = Unrolled_Commands.CopyToNew(result[Unroll_Result_BestResponseIndex]); // we copy this here so that we have a new value whether we execute the inner loop or not
                    int goToCommandIndex = Unrolled_Commands.InsertBlankCommand();
                    // the following is executed only if lastBestResponseActionIndex == action
                        int bestResponseNumerator = Unrolled_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNumber, action);
                        int bestResponseDenominator = Unrolled_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNumber, action);
                        Unrolled_Commands.IncrementByProduct(bestResponseNumerator, inversePiAvgStrat, innerResult[Unroll_Result_BestResponseIndex], true);
                        Unrolled_Commands.Increment(bestResponseNumerator, inversePiAvgStrat, true);
                        Unrolled_Commands.CopyToExisting(result[Unroll_Result_BestResponseIndex], innerResult[Unroll_Result_BestResponseIndex]);
                    // end of loop
                    Unrolled_Commands.ReplaceCommandWithGoToCommand(goToCommandIndex, Unrolled_Commands.NextCommandIndex); // completes the go to statement
                    Unrolled_Commands.IncrementByProduct(result[Unroll_Result_HedgeVsHedgeIndex], probabilityOfAction, innerResult[Unroll_Result_HedgeVsHedgeIndex], false);
                    Unrolled_Commands.IncrementByProduct(result[Unroll_Result_AverageStrategyIndex], probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex], false);
                }
                else
                {
                    //if (TraceCFR)
                    //{
                    //    //DEBUG
                    //    TabbedText.WriteLine(
                    //        $"... BEFORE prob ARRAY{probabilityOfAction} results: ARRAY{result[Unroll_Result_HedgeVsHedgeIndex]} ARRAY{result[Unroll_Result_AverageStrategyIndex]} ARRAY{result[Unroll_Result_BestResponseIndex]} inner:  ARRAY{innerResult[Unroll_Result_HedgeVsHedgeIndex]} ARRAY{innerResult[Unroll_Result_AverageStrategyIndex]} ARRAY{innerResult[Unroll_Result_BestResponseIndex]} ");
                    //}
                    Unrolled_Commands.IncrementByProduct(result[Unroll_Result_HedgeVsHedgeIndex], probabilityOfAction, innerResult[Unroll_Result_HedgeVsHedgeIndex], false);
                    Unrolled_Commands.IncrementByProduct(result[Unroll_Result_AverageStrategyIndex], probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex], false);
                    Unrolled_Commands.IncrementByProduct(result[Unroll_Result_BestResponseIndex], probabilityOfActionAvgStrat, innerResult[Unroll_Result_BestResponseIndex], false);

                    //if (TraceCFR)
                    //{
                    //    //DEBUG
                    //    TabbedText.WriteLine(
                    //        $"... AFTER prob ARRAY{probabilityOfAction} results: ARRAY{result[Unroll_Result_HedgeVsHedgeIndex]} ARRAY{result[Unroll_Result_AverageStrategyIndex]} ARRAY{result[Unroll_Result_BestResponseIndex]} ");
                    //}
                }
                Unrolled_Commands.IncrementByProduct(expectedValue, probabilityOfAction, expectedValueOfAction[action - 1], false);

                if (TraceCFR)
                {
                    int expectedValueOfActionCopy = Unrolled_Commands.CopyToNew(expectedValueOfAction[action - 1]);
                    int bestResponseExpectedValueCopy = Unrolled_Commands.CopyToNew(result[Unroll_Result_BestResponseIndex]);
                    int cumExpectedValueCopy = Unrolled_Commands.CopyToNew(expectedValue);
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action} expected value ARRAY{expectedValueOfActionCopy} best response expected value ARRAY{bestResponseExpectedValueCopy} cum expected value ARRAY{cumExpectedValueCopy}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    int pi = Unrolled_Commands.CopyToNew(piValues[playerBeingOptimized]);
                    int regret = Unrolled_Commands.CopyToNew(expectedValueOfAction[action - 1]);
                    Unrolled_Commands.Decrement(regret, expectedValue, false);
                    if (TraceCFR)
                    { // DEBUG
                        int piValuesZeroCopy = Unrolled_Commands.CopyToNew(piValues[0]);
                        int piValuesOneCopy = Unrolled_Commands.CopyToNew(piValues[1]);
                        int regretCopy = Unrolled_Commands.CopyToNew(regret);
                        int inversePiCopy = Unrolled_Commands.CopyToNew(inversePi);
                        int exLagCopy = Unrolled_Commands.CopyToNew(expectedValueOfAction[action - 1]);
                        int exCopy = Unrolled_Commands.CopyToNew(expectedValue);
                        TabbedText.WriteLine(
                            $"Extra regrets Action {action} regret ARRAY{regretCopy} = ARRAY{exLagCopy} - ARRAY{exCopy} ; inversePi ARRAY{inversePiCopy} avg_strat_incrememnt");
                    }
                    int lastRegret = Unrolled_GetInformationSetIndex_LastRegret(informationSet.InformationSetNumber, action);
                    Unrolled_Commands.IncrementByProduct(lastRegret, inversePi, regret, true);
                    // now contribution to average strategy
                    int contributionToAverageStrategy = Unrolled_Commands.CopyToNew(pi);
                    Unrolled_Commands.MultiplyBy(contributionToAverageStrategy, actionProbabilities[action - 1], false);
                    if (EvolutionSettings.UseRegretAndStrategyDiscounting)
                        Unrolled_Commands.MultiplyBy(contributionToAverageStrategy, Unrolled_AverageStrategyAdjustmentIndex, false);
                    int cumulativeStrategy = Unrolled_GetInformationSetIndex_CumulativeStrategy(informationSet.InformationSetNumber, action);
                    Unrolled_Commands.Increment(cumulativeStrategy, contributionToAverageStrategy, true);
                    if (TraceCFR)
                    {
                        int piCopy = Unrolled_Commands.CopyToNew(pi);
                        int piValuesZeroCopy = Unrolled_Commands.CopyToNew(piValues[0]);
                        int piValuesOneCopy = Unrolled_Commands.CopyToNew(piValues[1]);
                        int regretCopy = Unrolled_Commands.CopyToNew(regret);
                        int inversePiCopy = Unrolled_Commands.CopyToNew(inversePi);
                        int contributionToAverageStrategyCopy = Unrolled_Commands.CopyToNew(contributionToAverageStrategy);
                        int cumulativeStrategyCopy = Unrolled_Commands.CopyToNew(cumulativeStrategy);
                        TabbedText.WriteLine($"PiValues ARRAY{piValuesZeroCopy} ARRAY{piValuesOneCopy} pi for optimized ARRAY{piCopy} AvgStrategyAdjustment ARRAY{Unrolled_AverageStrategyAdjustmentIndex}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} probability ARRAY{actionProbabilities[action - 1]} regret ARRAY{regretCopy} inversePi ARRAY{inversePiCopy} avg_strat_incrememnt ARRAY{contributionToAverageStrategyCopy} cum_strategy ARRAY{cumulativeStrategyCopy}");
                    }
                }
            }
            return result;
        }

        private unsafe int[] Unroll_HedgeVanillaCFR_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues)
        {
            int[] result = Unrolled_Commands.NewZeroArray(3); // initialize to zero -- equivalent of HedgeVanillaUtilities
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = chanceNodeSettings.Decision.NumPossibleActions;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            //DEBUG -- add parallelism back
            //Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
            //    (byte)(numPossibleActions + 1),
            //    action =>
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                int[] probabilityAdjustedInnerResult = Unroll_HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues, avgStratPiValues,
                        chanceNodeSettings, action);
                Unrolled_Commands.IncrementArrayBy(result, probabilityAdjustedInnerResult, false);
            }

            return result;
        }

        private unsafe int[] Unroll_HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, ChanceNodeSettings chanceNodeSettings, byte action)
        {
            int actionProbability = Unrolled_GetChanceNodeIndex_ProbabilityForAction(chanceNodeSettings.ChanceNodeNumber, action);
            int[] nextPiValues = Unroll_GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true);
            int[] nextAvgStratPiValues = Unroll_GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);

            if (TraceCFR)
            {
                int actionProbabilityCopy = Unrolled_Commands.CopyToNew(actionProbability);
                TabbedText.WriteLine(
                    $"Chance code {chanceNodeSettings.DecisionByteCode} ({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == chanceNodeSettings.DecisionByteCode).Name}) action {action} probability ARRAY{actionProbabilityCopy} ...");
                TabbedText.Tabs++;
            }
            int[] result =
                Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
            if (TraceCFR)
            {
                // save current hedge result before multiplying
                int beforeMultipleHedgeCopy = Unrolled_Commands.CopyToNew(result[Unroll_Result_HedgeVsHedgeIndex]);
                int actionProbabilityCopy = Unrolled_Commands.CopyToNew(actionProbability);

                Unrolled_Commands.MultiplyArrayBy(result, actionProbability, false);

                int resultHedgeCopy = Unrolled_Commands.CopyToNew(result[Unroll_Result_HedgeVsHedgeIndex]);

                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} value ARRAY{beforeMultipleHedgeCopy} probability ARRAY{actionProbabilityCopy} expected value contribution ARRAY{resultHedgeCopy}");
            }
            else
                Unrolled_Commands.MultiplyArrayBy(result, actionProbability, false);

            return result;
        }

        private unsafe int[] Unroll_GetNextPiValues(int[] currentPiValues, byte playerIndex, int probabilityToMultiplyBy, bool changeOtherPlayers)
        {
            int[] nextPiValues = new int[NumNonChancePlayers]; // unlike the GetNextPiValues, we create the array here and then return it
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                int currentPiValue = currentPiValues[p];
                int nextPiValue;
                if (p == playerIndex)
                {
                    if (changeOtherPlayers)
                        nextPiValue = Unrolled_Commands.CopyToNew(currentPiValue);
                    else
                        nextPiValue = Unrolled_Commands.MultiplyToNew(currentPiValue, probabilityToMultiplyBy);
                }
                else
                {
                    if (changeOtherPlayers)
                        nextPiValue = Unrolled_Commands.MultiplyToNew(currentPiValue, probabilityToMultiplyBy);
                    else
                        nextPiValue = Unrolled_Commands.CopyToNew(currentPiValue);
                }
                nextPiValues[p] = nextPiValue;
            }
            return nextPiValues;
        }

        private unsafe int Unroll_GetInversePiValue(int[] piValues, byte playerIndex)
        {
            if (NumNonChancePlayers == 2)
                return Unrolled_Commands.CopyToNew(piValues[(byte)1 - playerIndex]);
            bool firstPlayerOtherThanMainFound = false;
            int indexForInversePiValue = -1;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != playerIndex)
                {
                    if (firstPlayerOtherThanMainFound)
                    {
                        Unrolled_Commands.MultiplyBy(indexForInversePiValue, piValues[p], false);
                    }
                    else
                    {
                        indexForInversePiValue = Unrolled_Commands.CopyToNew(piValues[p]);
                        firstPlayerOtherThanMainFound = true;
                    }
                }
            return indexForInversePiValue;
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
                HedgeVanillaUtilities result = HedgeVanillaCFR(ref historyPoint, playerBeingOptimized, initialPiValues, initialAvgStratPiValues);
                HedgeVanillaIterationStopwatch.Stop();
                if (iteration % 10 == 0)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized} {result} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            }

            UpdateInformationSets(iteration);

            reportString = GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
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
        public unsafe HedgeVanillaUtilities HedgeVanillaCFR(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, double* avgStratPiValues)
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
                return HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
            }
            else
                return HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
        }

        bool IncludeAsteriskForBestResponseInTrace = false;

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, double* avgStratPiValues)
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
            if (informationSet.DecisionByteCode == 14)
            {
                var DEBUG = 0;
            }
            for (byte action = 1; action <= numPossibleActions; action++)
            {
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
                HedgeVanillaUtilities innerResult = HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
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
                    if (TraceCFR)
                    { // DEBUG
                        TabbedText.WriteLine(
                            $"Extra regrets Action {action} regret {regret} = {expectedValueOfAction[action - 1]} - {expectedValue} ; inversePi {inversePi} avg_strat_incrememnt");
                    }
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
            double* piValues, double* avgStratPiValues)
        {
            HedgeVanillaUtilities result = default;
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte)(numPossibleActions + 1),
                action =>
                {
                    var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                    HedgeVanillaUtilities probabilityAdjustedInnerResult =  HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues, avgStratPiValues, chanceNodeSettings, action);
                    result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);
                });

            return result;
        }

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, double* avgStratPiValues, ChanceNodeSettings chanceNodeSettings, byte action)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            double* nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
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
                HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
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