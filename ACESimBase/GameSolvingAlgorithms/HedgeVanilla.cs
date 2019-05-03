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

        private void Unroll_CreateUnrolledCommandList()
        {
            const int max_num_commands = 10_000_000;
            Unroll_InitializeInitialIndexes();
            Unrolled_Commands = new ArrayCommandList(max_num_commands, null, InitialArrayIndex);
            ActionStrategy = ActionStrategies.NormalizedHedge;
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                Unroll_HedgeVanillaCFR(ref historyPoint, p, Unrolled_InitialPiValues, Unrolled_InitialPiValues);
            }
        }

        private void Unroll_ExecuteUnrolledCommands(bool firstExecution)
        {
            double[] array = new double[Unroll_SizeOfArray];
            Unroll_CopyInformationSetsToArray(array, firstExecution);
            Unrolled_Commands.ExecuteAll(array);
            Unroll_CopyArrayToInformationSets(array);
        }

        // Let's store the index in the array at which we will place the various types of information set information.

        private const int Unroll_NumPiecesInfoPerInformationSet = 3;
        private int[] Unrolled_InformationSetsIndices;
        private int[] Unrolled_ChanceNodesIndices;
        private int[] Unrolled_FinalUtilitiesNodesIndices;
        private int InitialPiValuesIndex = -1;
        private int InitialArrayIndex = -1;
        private int[] Unrolled_InitialPiValues = null;

        private int Unrolled_GetInformationSetIndex_InitialIndex(int informationSetNumber) => Unrolled_InformationSetsIndices[Unroll_NumPiecesInfoPerInformationSet * informationSetNumber];

        private int Unrolled_GetInformationSetIndex_AverageStrategy(int informationSetNumber, byte action) => Unrolled_GetInformationSetIndex_InitialIndex(informationSetNumber) + (Unroll_NumPiecesInfoPerInformationSet * (action - 1));
        private int Unrolled_GetInformationSetIndex_HedgeProbability(int informationSetNumber, byte action) => Unrolled_GetInformationSetIndex_InitialIndex(informationSetNumber) + (Unroll_NumPiecesInfoPerInformationSet * (action - 1)) + 1;

        private int[] Unrolled_GetInformationSetIndex_HedgeProbabilities_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unrolled_GetInformationSetIndex_InitialIndex(informationSetNumber);
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSet * (action - 1)) + 1;
            }
            return probabilities;
        }

        private int Unrolled_GetInformationSetIndex_LastRegret(int informationSetNumber, byte action) => Unrolled_GetInformationSetIndex_InitialIndex(informationSetNumber) + (Unroll_NumPiecesInfoPerInformationSet * (action - 1)) + 2;

        private int Unrolled_GetChanceNodeIndex(int chanceNodeNumber) => Unrolled_ChanceNodesIndices[chanceNodeNumber];
        private int Unrolled_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, byte action) => Unrolled_ChanceNodesIndices[chanceNodeNumber] + (byte) (action - 1);
        private int[] Unrolled_GetChanceNodeIndices(int chanceNodeNumber, byte numPossibleActions)
        {
            int firstIndex = Unrolled_GetChanceNodeIndex(chanceNodeNumber);
            return Enumerable.Range(0, numPossibleActions).Select(x => firstIndex + x).ToArray();
        }
        private int Unrolled_GetFinalUtilitiesNodesIndex(int finalUtilitiesNodeNumber, byte playerBeingOptimized) => Unrolled_FinalUtilitiesNodesIndices[finalUtilitiesNodeNumber] + playerBeingOptimized;

        private void Unroll_InitializeInitialIndexes()
        {
            int index = 0;
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
                int numItems = Unroll_NumPiecesInfoPerInformationSet * InformationSets[i].NumPossibleActions;
                index += numItems;
            }
            InitialPiValuesIndex = index;
            InitialArrayIndex = index + NumNonChancePlayers;
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
                    int initialIndex = Unrolled_GetChanceNodeIndex(finalUtilitiesNode.FinalUtilitiesNodeNumber);
                    for (byte p = 0; p < finalUtilitiesNode.Utilities.Length; p++)
                    {
                        array[initialIndex++] = finalUtilitiesNode.Utilities[p];
                    }
                });
            }
            Parallel.For(0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                int initialIndex = Unrolled_GetInformationSetIndex_InitialIndex(infoSet.InformationSetNumber);
                for (byte a = 1; a <= infoSet.NumPossibleActions; a++)
                {
                    array[initialIndex++] = infoSet.GetNormalizedHedgeAverageStrategy(a);
                    array[initialIndex++] = infoSet.GetNormalizedHedgeProbability(a);
                    array[initialIndex++] = 0; // initialize last regret to zero
                }
            });
            Unrolled_InitialPiValues = new int[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                array[InitialPiValuesIndex + p] = 1.0;
                Unrolled_InitialPiValues[p] = InitialPiValuesIndex + p;
            }
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
                return new int[] { finalUtilIndex, finalUtilIndex, finalUtilIndex };
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return Unroll_HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
            }
            else
                return Unroll_HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
        }

        private unsafe int[] Unroll_HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            int[] piValues, int[] avgStratPiValues)
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
            int[] expectedValueOfAction = new int[numPossibleActions];
            double expectedValue = 0;
            int[] result = new int[3];
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
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                int[] innerResult = Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
                expectedValueOfAction[action - 1] = innerResult[0]; // hedge vs. hedge
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
                    result.AverageStrategyVsAverageStrategy += averageStrategyProbability * innerResult.AverageStrategyVsAverageStrategy;
                }
                else
                {
                    // This isn't the decision being optimized, so we essentially just need to pass through the player being optimized's utilities, weighting by the probability for each action (which will depend on whether we are using average strategy or hedge to calculate the utilities).
                    result.IncrementBasedOnNotYetProbabilityAdjusted(ref innerResult, averageStrategyProbability, probabilityOfAction);
                }
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action}{(informationSet.LastBestResponseAction == action ? "*" : "")} expected value {expectedValueOfAction[action - 1]} best response expected value {result.BestResponseToAverageStrategy} cum expected value {expectedValue}{(action == numPossibleActions ? "*" : "")}");
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
                        contributionToAverageStrategy *= AverageStrategyAdjustment;
                    if (EvolutionSettings.ParallelOptimization)
                        informationSet.IncrementCumulativeStrategy_Parallel(action, contributionToAverageStrategy);
                    else
                        informationSet.IncrementCumulativeStrategy(action, contributionToAverageStrategy);
                    if (TraceCFR)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
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
                Unrolled_Commands.IncrementArrayBy(result, probabilityAdjustedInnerResult);
            }

            return result;
        }

        private unsafe int[] Unroll_HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, ChanceNodeSettings chanceNodeSettings, byte action)
        {
            int actionProbability = Unrolled_GetChanceNodeIndex_ProbabilityForAction(chanceNodeSettings.ChanceNodeNumber, action);
            int[] nextPiValues = Unroll_GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true);
            int[] nextAvgStratPiValues = Unroll_GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            int[] result =
                Unroll_HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
            Unrolled_Commands.MultiplyArrayBy(result, actionProbability);

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
                        nextPiValue = currentPiValue;
                    else
                        nextPiValue = Unrolled_Commands.MultiplyToNew(currentPiValue, probabilityToMultiplyBy);
                }
                else
                {
                    if (changeOtherPlayers)
                        nextPiValue = Unrolled_Commands.MultiplyToNew(currentPiValue, probabilityToMultiplyBy);
                    else
                        nextPiValue = currentPiValue;
                }
                nextPiValues[p] = nextPiValue;
            }
            return nextPiValues;
        }

        private unsafe int Unroll_GetInversePiValue(int[] piValues, byte playerIndex)
        {
            if (NumNonChancePlayers == 2)
                return piValues[(byte)1 - playerIndex];
            bool firstPlayerOtherThanMainFound = false;
            int indexForInversePiValue = -1;
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != playerIndex)
                {
                    if (firstPlayerOtherThanMainFound)
                    {
                        Unrolled_Commands.MultiplyBy(indexForInversePiValue, piValues[p]);
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

            double positivePower = Math.Pow(HedgeVanillaIteration, EvolutionSettings.Discounting_Alpha);
            double negativePower = Math.Pow(HedgeVanillaIteration, EvolutionSettings.Discounting_Beta);
            PositiveRegretsAdjustment = positivePower / (positivePower + 1.0);
            NegativeRegretsAdjustment = negativePower / (negativePower + 1.0);
            AverageStrategyAdjustment = Math.Pow(HedgeVanillaIteration / (HedgeVanillaIteration), EvolutionSettings.Discounting_Gamma);

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
                        $"decisionNum {decisionNum} optimizing player {playerBeingOptimized}  own decision {playerMakingDecision == playerBeingOptimized} action {action} probability {probabilityOfAction} ...");
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
                        $"... action {action}{(informationSet.LastBestResponseAction == action ? "*" : "")} expected value {expectedValueOfAction[action - 1]} best response expected value {result.BestResponseToAverageStrategy} cum expected value {expectedValue}{(action == numPossibleActions ? "*" : "")}");
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
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
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
                    $"Chance decisionNum {chanceNodeSettings.DecisionByteCode} action {action} probability {actionProbability} ...");
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