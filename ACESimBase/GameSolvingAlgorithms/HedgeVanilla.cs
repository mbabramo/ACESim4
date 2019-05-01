using System;
using System.Diagnostics;
using System.Threading;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {
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
                ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
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
                informationSet.GetNormalizedHedgeProbabilities(actionProbabilities, HedgeVanillaIterationInt);
            }
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            HedgeVanillaUtilities result = default;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                if (EvolutionSettings.PruneOnOpponentStrategy && playerBeingOptimized != playerMakingDecision && probabilityOfAction < EvolutionSettings.PruneOnOpponentStrategyThreshold)
                    continue;
                if (playerBeingOptimized == playerMakingDecision)
                {
                    if (probabilityOfAction < 1E-10)
                        informationSet.DEBUG_SKIPPING = informationSet.DEBUG_SKIPPING | (int)(1 << action);
                    else if (probabilityOfAction > 1E-8 && ((informationSet.DEBUG_SKIPPING & (int)(1 << action)) != 0))
                        throw new Exception("DEBUG.");
                }
                double probabilityOfActionAvgStrat = informationSet.GetHedgeSavedAverageStrategy(action);
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
                double averageStrategyProbability = informationSet.GetHedgeSavedAverageStrategy(action);
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
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumMainPlayers];
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            bool equalProbabilities = chanceNodeSettings.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNodeSettings.GetActionProbability(1), true,
                    equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte)(numPossibleActions + 1),
                action =>
                {
                    var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                    HedgeVanillaUtilities probabilityAdjustedInnerResult =  HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues, avgStratPiValues,
                            chanceNodeSettings, equalProbabilityNextPiValues, action);
                    result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);
                });

            return result;
        }

        private unsafe HedgeVanillaUtilities HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues, double* avgStratPiValues, ChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues, byte action)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            double* nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            double actionProbability = chanceNodeSettings.GetActionProbability(action);
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
            {
                GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true,
                    nextPiValues);
                GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true,
                    nextAvgStratPiValues);
            }
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

        public unsafe string SolveHedgeVanillaCFR()
        {
            string reportString = null;
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

            bool usePruning = false; // iteration >= 100;
            ActionStrategy = ActionStrategies.NormalizedHedge;

            //if (iteration == 501)
            //{
            //    TraceCFR = true; // DEBUG
            //}
            
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

            reportString = GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }
    }
}