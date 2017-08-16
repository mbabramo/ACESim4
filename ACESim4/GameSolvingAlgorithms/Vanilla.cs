using System.Diagnostics;

namespace ACESim
{
    public partial class CounterfactualRegretMaximization
    {
        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe double VanillaCFR(HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues,
            bool usePruning)
        {
            if (usePruning && ShouldPruneIfPruning(piValues))
                return 0;
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities) gameStateForCurrentPlayer;
                return finalUtilities.Utilities[playerBeingOptimized];
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
                return VanillaCFR_ChanceNode(historyPoint, playerBeingOptimized, piValues, usePruning);
            }
            else
                return VanillaCFR_DecisionNode(historyPoint, playerBeingOptimized, piValues, usePruning);
        }

        private unsafe bool ShouldPruneIfPruning(double* piValues)
        {
            // If we are pruning, then we do prune when the probability of getting to this path is 0.
            // But that doesn't mean that we should prune. The results from low probability paths can 
            // still matter.
            bool allZero = true;
            for (int i = 0; i < NumNonChancePlayers; i++)
                if (*(piValues + i) != 0)
                {
                    allZero = false;
                    break;
                }
            if (allZero)
                return true;
            return false;
        }

        private unsafe double VanillaCFR_DecisionNode(HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            var informationSet = (InformationSetNodeTally) gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction != null)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                    actionProbabilities, (byte) alwaysDoAction);
            else
            {
                if (usePruning)
                    informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                else
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            }
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false,
                    nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                if (TraceVanillaCFR)
                {
                    TabbedText.WriteLine(
                        $"decisionNum {decisionNum} optimizing player {playerBeingOptimized}  own decision {playerMakingDecision == playerBeingOptimized} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                expectedValueOfAction[action - 1] = VanillaCFR(nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceVanillaCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action} expected value {expectedValueOfAction[action - 1]} cum expected value {expectedValue}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    informationSet.IncrementCumulativeStrategy(action, pi * actionProbabilities[action - 1]);
                    if (TraceVanillaCFR)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]}");
                        TabbedText.WriteLine(
                            $"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                    }
                }
            }
            return expectedValue;
        }

        private unsafe double VanillaCFR_ChanceNode(HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning)
        {
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumPlayers];
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNodeSettings.GetActionProbability(1), true,
                    equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte) (numPossibleActions + 1),
                action =>
                {
                    //double* piValuesToPass = stackalloc double[MaxNumPlayers];
                    //for (int i = 0; i < MaxNumPlayers; i++)
                    //    *(piValuesToPass + i) = *(piValues + i);
                    //double* equalProbabilityPiValuesToPass = stackalloc double[MaxNumPlayers];
                    //if (equalProbabilityNextPiValues == null)
                    //    equalProbabilityPiValuesToPass = null;
                    //else
                    //    for (int i = 0; i < MaxNumPlayers; i++)
                    //        *(equalProbabilityPiValuesToPass + i) = *(equalProbabilityNextPiValues + i);
                    double probabilityAdjustedExpectedValueParticularAction =
                        VanillaCFR_ChanceNode_NextAction(historyPoint, playerBeingOptimized, piValues,
                            chanceNodeSettings, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    expectedValue += probabilityAdjustedExpectedValueParticularAction;
                });

            return expectedValue;
        }

        private unsafe double VanillaCFR_ChanceNode_NextAction(HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, ChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues,
            double expectedValue, byte action, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumPlayers];
            if (equalProbabilityNextPiValues != null)
            {
                double* locTarget = nextPiValues;
                double* locSource = equalProbabilityNextPiValues;
                for (int i = 0; i < NumNonChancePlayers; i++)
                {
                    (*locTarget) = (*locSource);
                    locTarget++;
                    locSource++;
                }
            }
            else // must set probability separately for each action we take
                GetNextPiValues(piValues, playerBeingOptimized, chanceNodeSettings.GetActionProbability(action), true,
                    nextPiValues);
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
            if (TraceVanillaCFR)
            {
                TabbedText.WriteLine(
                    $"Chance decisionNum {chanceNodeSettings.DecisionByteCode} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction =
                VanillaCFR(nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
            var probabilityAdjustedExpectedValueParticularAction = actionProbability * expectedValueParticularAction;
            if (TraceVanillaCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
            }

            return probabilityAdjustedExpectedValueParticularAction;
        }

        public unsafe void SolveVanillaCFR()
        {
            for (int iteration = 0; iteration < EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                VanillaCFRIteration(iteration);
            }
        }

        private unsafe void VanillaCFRIteration(int iteration)
        {
            Stopwatch s = new Stopwatch();
            double[] lastUtilities = new double[NumNonChancePlayers];

            bool usePruning = iteration >= 100;
            ActionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double* initialPiValues = stackalloc double[MaxNumPlayers];
                GetInitialPiValues(initialPiValues);
                if (TraceVanillaCFR)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized}");
                s.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                lastUtilities[playerBeingOptimized] =
                    VanillaCFR(historyPoint, playerBeingOptimized, initialPiValues, usePruning);
                s.Stop();
            }

            GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double) iteration + 1.0)))}");
        }
    }
}