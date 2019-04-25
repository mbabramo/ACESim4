﻿using ACESim.Util;
using System;
using System.Diagnostics;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {

        Debug; // must also have invpi for average strategies

        public struct HedgeVanillaResult
        {
            /// <summary>
            /// The utility from the player being optimized playing Hedge against Hedge. This is used to set counterfactual values and regret.
            /// </summary>
            public double UtilityHedgeVsHedge;
            /// <summary>
            /// The utility from the player being optimized playing an average strategy against the other player playing an average strategy. This can be used to compare with best response to average strategies; in an epsilon-equilibrium, they should be very close.
            /// </summary>
            public double UtilityAverageStrategyVsAverageStrategy;
            /// <summary>
            /// The utility from the player being optimized playing an approximate best response to the other player's use of average strategies. This can be compared to average strategy performance against average strategies; this will be higher, but not much in Epsilon equilibrium. In addition, this can be used in CFR-BR for the player not being optimized, when we have skipped iterations of the player being optimized.
            /// </summary>
            public double UtilityBestResponseToAverageStrategy;

            public void IncrementBasedOnOtherPlayer(ref HedgeVanillaResult other, double averageStrategyProbability, double hedgeProbability)
            {
                UtilityHedgeVsHedge += other.UtilityHedgeVsHedge * hedgeProbability;
                UtilityAverageStrategyVsAverageStrategy += other.UtilityAverageStrategyVsAverageStrategy * averageStrategyProbability;
                UtilityBestResponseToAverageStrategy += other.UtilityBestResponseToAverageStrategy * averageStrategyProbability;
            }
        }

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe HedgeVanillaResult HedgeVanillaCFR(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues,
            bool usePruning)
        {
            if (usePruning && ShouldPruneIfPruning(piValues))
                return new HedgeVanillaResult { UtilityAverageStrategyVsAverageStrategy = 0, UtilityBestResponseToAverageStrategy = 0, UtilityHedgeVsHedge = 0 };
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                double util = finalUtilities.Utilities[playerBeingOptimized];
                return new HedgeVanillaResult { UtilityAverageStrategyVsAverageStrategy = util, UtilityBestResponseToAverageStrategy = util, UtilityHedgeVsHedge = util };
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
                return HedgeVanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, usePruning);
            }
            else
                return HedgeVanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, usePruning);
        }

        private unsafe HedgeVanillaResult HedgeVanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
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
            HedgeVanillaResult result = default;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double probabilityOfAction = actionProbabilities[action - 1];
                GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false,
                    nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                if (TraceCFR)
                {
                    TabbedText.WriteLine(
                        $"decisionNum {decisionNum} optimizing player {playerBeingOptimized}  own decision {playerMakingDecision == playerBeingOptimized} action {action} probability {probabilityOfAction} ...");
                    TabbedText.Tabs++;
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                HedgeVanillaResult innerResult = HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
                expectedValueOfAction[action - 1] = innerResult.UtilityHedgeVsHedge;
                double averageStrategyProbability = informationSet.GetHedgeSavedAverageStrategy(action);
                if (playerMakingDecision == playerBeingOptimized)
                {
                    if (informationSet.LastBestResponseAction == action)
                    {
                        // Because this is the best response action, the best response utility that we get should be propagated back directly. Meanwhile, we want to keep track of all the times that we traverse through this information set, weighing the best response results (which may vary, since our terminal nodes may vary) by the inversePi.
                        informationSet.IncrementBestResponse(action, inversePiAverageStrategies, innerResult.UtilityBestResponseToAverageStrategy);
                        result.UtilityBestResponseToAverageStrategy = innerResult.UtilityBestResponseToAverageStrategy;
                    }
                    // The other result utilities are just the probability adjusted utilities. 
                    result.UtilityHedgeVsHedge += probabilityOfAction * innerResult.UtilityHedgeVsHedge;
                    result.UtilityAverageStrategyVsAverageStrategy += averageStrategyProbability * innerResult.UtilityAverageStrategyVsAverageStrategy;
                }
                else
                {
                    // This isn't the decision being optimized, so we essentially just need to pass through the player being optimized's utilities, weighting by the probability for each action (which will depend on whether we are using average strategy or hedge to calculate the utilities).
                    result.IncrementBasedOnOtherPlayer(ref innerResult, averageStrategyProbability, probabilityOfAction);
                }
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action}{(informationSet.LastBestResponseAction == action ? "*" : "")} expected value {expectedValueOfAction[action - 1]} best response expected value {bestResponseExpectedValues[playerBeingOptimized]} cum expected value {expectedValue}{(action == numPossibleActions ? "*" : "")}");
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
            return expectedValue;
        }

        private unsafe double HedgeVanillaCFR_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning, double* bestResponseExpectedValues)
        {
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
            double expectedValue = 0;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use HedgeVanillaCFR much.
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte)(numPossibleActions + 1),
                action =>
                {
                    var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                    double* bestResponseExpectedValuesThisAction = stackalloc double[MaxNumMainPlayers];
                    double probabilityAdjustedExpectedValueParticularAction =
                        HedgeVanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues,
                            chanceNodeSettings, equalProbabilityNextPiValues, expectedValue, action, usePruning, bestResponseExpectedValuesThisAction);
                    Interlocking.Add(ref expectedValue, probabilityAdjustedExpectedValueParticularAction);
                    for (int p = 0; p < NumNonChancePlayers; p++)
                    {
                        Interlocking.Add(ref bestResponseExpectedValues[p], bestResponseExpectedValuesThisAction[p]);
                    }
                });

            return expectedValue;
        }

        private unsafe double HedgeVanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, ChanceNodeSettings chanceNodeSettings, double* equalProbabilityNextPiValues,
            double expectedValue, byte action, bool usePruning, double* probabilityAdjustedBestResponseExpectedValues)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
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
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.WriteLine(
                    $"Chance decisionNum {chanceNodeSettings.DecisionByteCode} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction =
                HedgeVanillaCFR(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning, probabilityAdjustedBestResponseExpectedValues);
            for (int p = 0; p < NumNonChancePlayers; p++)
                probabilityAdjustedBestResponseExpectedValues[p] *= actionProbability;
            var probabilityAdjustedExpectedValueParticularAction = actionProbability * expectedValueParticularAction;
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
            }

            return probabilityAdjustedExpectedValueParticularAction;
        }

        public unsafe string SolveHedgeVanillaCFR()
        {
            string reportString = null;
            for (int iteration = 0; iteration < EvolutionSettings.TotalVanillaCFRIterations; iteration++)
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
            AverageStrategyAdjustment = Math.Pow(HedgeVanillaIteration / (HedgeVanillaIteration + 1.0), EvolutionSettings.Discounting_Gamma);

            string reportString = null;
            double[] lastUtilities = new double[NumNonChancePlayers];

            bool usePruning = false; // iteration >= 100;
            ActionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;

            //if (iteration == 501)
            //{
            //    TraceCFR = true; // DEBUG
            //}

            double* bestResponseExpectedValues = stackalloc double[MaxNumMainPlayers];
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double* initialPiValues = stackalloc double[MaxNumMainPlayers];
                GetInitialPiValues(initialPiValues);
                if (TraceCFR)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized}");
                HedgeVanillaIterationStopwatch.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                lastUtilities[playerBeingOptimized] =
                    HedgeVanillaCFR(ref historyPoint, playerBeingOptimized, initialPiValues, usePruning, bestResponseExpectedValues);
                if (iteration % 10 == 0)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized} utility {lastUtilities[playerBeingOptimized]} best response value {bestResponseExpectedValues[playerBeingOptimized]}");
                HedgeVanillaIterationStopwatch.Stop();
            }

            reportString = GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((HedgeVanillaIterationStopwatch.ElapsedMilliseconds / ((double)iteration + 1.0)))}");
            return reportString;
        }
    }
}