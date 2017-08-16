﻿using System;
using System.Diagnostics;

namespace ACESim
{
    public partial class CounterfactualRegretMaximization
    {
        public unsafe double Probe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return Probe(historyPoint, randomProducer)[playerBeingOptimized];
        }

        public unsafe double[] Probe(HistoryPoint historyPoint, IRandomProducer randomProducer)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            //if (TraceProbingCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities) gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceProbingCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else
            {
                byte sampledAction = 0;
                if (gameStateType == GameStateTypeEnum.Chance)
                {
                    ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    sampledAction = chanceNodeSettings.SampleAction(numPossibleActions,
                        randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNodeSettings.GetActionProbability(sampledAction)}");
                }
                else if (gameStateType == GameStateTypeEnum.Tally)
                {
                    InformationSetNodeTally informationSet = (InformationSetNodeTally) gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double* actionProbabilities = stackalloc double[numPossibleActions];
                    // DEBUG: No epsilon exploration during the probe.
                    //if (!EstimatingImprovementOverTimeMode && UseEpsilonOnPolicyForOpponent && ProbingCFRIterationNum <= LastOpponentEpsilonIteration)
                    //    informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(actionProbabilities, CurrentEpsilonValue);
                    //else
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                    sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                        randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                if (TraceProbingCFR)
                    TabbedText.Tabs++;
                double[] probeResult = Probe(nextHistoryPoint, randomProducer);
                if (TraceProbingCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Returning probe result {probeResult}");
                }
                return probeResult;
            }
        }

        public unsafe double Probe_WalkTree(HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, bool alreadyUsedEpsilonExploration)
        {
            if (TraceProbingCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            byte sampledAction = 0;
            if (gameStateForCurrentPlayer is FinalUtilities finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceProbingCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                sampledAction = chanceNodeSettings.SampleAction(numPossibleActions,
                    randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
                if (TraceProbingCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                if (TraceProbingCFR)
                    TabbedText.Tabs++;
                double walkTreeValue = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                    randomProducer, alreadyUsedEpsilonExploration);
                if (TraceProbingCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                }
                return walkTreeValue;
            }
            else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                double* sigma_regretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                byte playerAtPoint = informationSet.PlayerIndex;
                bool usingEpsilonExploration = false;
                double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
                if (playerAtPoint != playerBeingOptimized)
                {
                    // the use of epsilon-on-policy for early iterations of opponent's strategy is a deviation from Gibson.
                    usingEpsilonExploration =
                    (!EstimatingImprovementOverTimeMode && EvolutionSettings.UseEpsilonOnPolicyForOpponent &&
                     ProbingCFRIterationNum <= EvolutionSettings.LastOpponentEpsilonIteration &&
                     ((!alreadyUsedEpsilonExploration || !EvolutionSettings.MaxOneEpsilonExploration) &&
                      (Math.Floor(randomDouble * 1000.0) < CurrentEpsilonValue)));
                    if (usingEpsilonExploration)
                        informationSet.GetEqualProbabilitiesRegretMatching(sigma_regretMatchedActionProbabilities);
                    else
                        informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeStrategyIncrement =
                            sigma_regretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                        informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                        if (TraceProbingCFR)
                            TabbedText.WriteLine(
                                $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                    }
                    sampledAction = SampleAction(sigma_regretMatchedActionProbabilities, numPossibleActions,
                        randomDouble);
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                    if (TraceProbingCFR)
                        TabbedText.Tabs++;
                    double walkTreeValue = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                        randomProducer, alreadyUsedEpsilonExploration || usingEpsilonExploration);
                    if (TraceProbingCFR)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                }
                double* samplingProbabilities = stackalloc double[numPossibleActions];
                const double epsilonForProbeWalk = 0.5;
                usingEpsilonExploration = (!alreadyUsedEpsilonExploration
                                           || !EvolutionSettings.MaxOneEpsilonExploration) &&
                                          (Math.Floor(randomDouble * 1000.0) <
                                           epsilonForProbeWalk
                                          ); // look at late digits for random bool, without needing to get another random number
                // we can't just call GetEpsilonAdjustedRegretMatchingProbabilities, because we need to know whether we are epsilon-adjusting
                if (usingEpsilonExploration)
                    informationSet.GetEqualProbabilitiesRegretMatching(samplingProbabilities);
                else
                    informationSet.GetRegretMatchingProbabilities(samplingProbabilities);
                sampledAction = SampleAction(samplingProbabilities, numPossibleActions, randomDouble);
                if (TraceProbingCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                double* counterfactualValues = stackalloc double[numPossibleActions];
                double summation = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action);
                    if (action == sampledAction)
                    {
                        if (TraceProbingCFR)
                            TabbedText.WriteLine(
                                $"{action}: Sampling selected action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
                        if (TraceProbingCFR)
                            TabbedText.Tabs++;
                        double samplingProbabilityQPrime = samplingProbabilityQ * samplingProbabilities[action - 1];
                        counterfactualValues[action - 1] = Probe_WalkTree(nextHistoryPoint, playerBeingOptimized,
                            samplingProbabilityQPrime, randomProducer,
                            alreadyUsedEpsilonExploration || usingEpsilonExploration);
                    }
                    else
                    {
                        if (TraceProbingCFR)
                            TabbedText.WriteLine(
                                $"{action}: Probing unselected action {action} for player {informationSet.PlayerIndex}decision {informationSet.DecisionIndex}");
                        if (TraceProbingCFR)
                            TabbedText.Tabs++;
                        counterfactualValues[action - 1] =
                            Probe_SinglePlayer(nextHistoryPoint, playerBeingOptimized, randomProducer);
                    }
                    double summationDelta = sigma_regretMatchedActionProbabilities[action - 1] *
                                            counterfactualValues[action - 1];
                    summation += summationDelta;
                    if (TraceProbingCFR)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine(
                            $"Counterfactual value for action {action} is {counterfactualValues[action - 1]} => increment summation by {sigma_regretMatchedActionProbabilities[action - 1]} * {counterfactualValues[action - 1]} = {summationDelta} to {summation}");
                    }
                }
                double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                       (counterfactualValues[action - 1] - summation);
                    informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement);
                    if (TraceProbingCFR)
                    {
                        //TabbedText.WriteLine($"Optimizing {playerBeingOptimized} Iteration {ProbingCFRIterationNum} Actions to here {historyPoint.GetActionsToHereString(Navigation)}");
                        TabbedText.WriteLine(
                            $"Increasing cumulative regret for action {action} by {inverseSamplingProbabilityQ} * {(counterfactualValues[action - 1])} - {summation} = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                    }
                }
                return summation;
            }
            else
                throw new NotImplementedException();
        }

        private unsafe byte SampleAction(double* actionProbabilities, byte numPossibleActions, double randomNumber)
        {
            double cumulative = 0;
            byte action = 1;
            do
            {
                cumulative += actionProbabilities[action - 1];
                if (cumulative >= randomNumber || action == numPossibleActions)
                    return action;
                else
                    action++;
            } while (true);
        }

        public void ProbingCFRIteration(int iteration)
        {
            CurrentEpsilonValue = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(
                EvolutionSettings.FirstOpponentEpsilonValue, EvolutionSettings.LastOpponentEpsilonValue, 0.75,
                (double) iteration / (double) EvolutionSettings.TotalProbingCFRIterations);
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                IRandomProducer randomProducer =
                    new ConsistentRandomSequenceProducer(iteration * 1000 + playerBeingOptimized);
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceProbingCFR)
                {
                    TabbedText.WriteLine($"Optimize player {playerBeingOptimized}");
                    TabbedText.Tabs++;
                }
                Probe_WalkTree(historyPoint, playerBeingOptimized, 1.0, randomProducer, false);
                if (TraceProbingCFR)
                    TabbedText.Tabs--;
            }
        }

        private int ProbingCFRIterationNum;

        public unsafe void SolveProbingCFR()
        {
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Gibson algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            for (ProbingCFRIterationNum = 0;
                ProbingCFRIterationNum < EvolutionSettings.TotalProbingCFRIterations;
                ProbingCFRIterationNum++)
            {
                s.Start();
                ProbingCFRIteration(ProbingCFRIterationNum);
                s.Stop();
                GenerateReports(ProbingCFRIterationNum,
                    () =>
                        $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double) (ProbingCFRIterationNum + 1))))}");
            }
        }
    }
}