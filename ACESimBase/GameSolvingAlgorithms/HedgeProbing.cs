using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {
        // TODO The fundamental problem here is that relatively early iterations swamp the later iterations (even with discounting). The problem affects information sets that require extensive cooperation to get to (but do not reflect the final reward for cooperation); it's important to get the strategy correct in these information sets to allow proper backward deduction. In a very early iteration, regrets may accumulate on such an information set in conjunction with relatively high inverse pi values, because the path to this information set is no less likely than any other path. These initial regrets will support non cooperation, because the players haven't yet figured out how to cooperate in later iterations. This feeds back to earlier information sets, where players also won't cooperate. Thus, in subsequent information sets, the probability of the other player getting to such an information set is tiny. Thus, even if regrets for a cooperative move become positive, this will have only a small effect, as the earlier large negative regret was multiplied by a much higher inverse pi value. It doesn't matter if we divide all regrets by the sum of the inverse pi values; that won't affect probabilities with hedge or regret matching.
        // Discounting is only a partial solution, because the Brown-Sandholm approach does not discount in a continuous way; that is, it does not allow for the first hundred iterations to be much less important than the second hundred, and the second hundred to be much less important than the third hundred, etc. Perhaps this form of discounting would be an alternative, but it might not provide attractive bounds.
        // An alternative possibility is to make an iteration a batch of many rollouts, enough so that each information set is likely visited many times. Thus, within an iteration, we will weight the regrets by the inverse pis. As a result, in each iteration, regrets will be the same order of magnitude. In other words, because we are doing a weighted average (based on inverse pi values) within each iterations (among all the items in a batch), we can do a straight non-weighted average across iterations. 

        public unsafe double HedgeProbe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return HedgeProbe(ref historyPoint, randomProducer)[playerBeingOptimized];
        }

        public unsafe double[] HedgeProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            //if (TraceCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                if (TraceCFR && Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                    TabbedText.WriteLine($"{historyPoint.GameProgress}");
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {String.Join(",", utility)}");
                return utility;
            }
            else
            {
                if (gameStateType == GameStateTypeEnum.Chance)
                    return HedgeProbe_ChanceNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                if (gameStateType == GameStateTypeEnum.InformationSet)
                    return HedgeProbe_DecisionNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                throw new NotImplementedException();
            }
        }

        private unsafe double[] HedgeProbe_DecisionNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            InformationSetNodeTally informationSet = (InformationSetNodeTally)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetHedgeProbabilities(actionProbabilities);
            byte sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
            return CompleteHedgeProbe_InPlace(ref historyPoint, randomProducer, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
        }

        private double[] HedgeProbe_ChanceNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (chanceNodeSettings.CriticalNode)
            { // Must sample every action at this node.
                if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
                {
                    double[] combined = new double[NumNonChancePlayers]; // TODO -- can we use an array pool? Or use a pointer?
                    for (byte a = 1; a <= numPossibleActions; a++)
                    {
                        double probability = chanceNodeSettings.GetActionProbability(a);
                        IGameState gameStateOriginal = historyPoint.GameState; // TODO -- can we move this out of the for loop?
                        double[] result = CompleteHedgeProbe_InPlace(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateOriginal);
                        for (byte p = 0; p < NumNonChancePlayers; p++)
                            combined[p] += probability * result[p];
                    }
                    return combined;
                }
                else
                {
                    double[] combined = new double[NumNonChancePlayers];  // TODO -- can we use an array pool?
                    for (byte a = 1; a <= numPossibleActions; a++)
                    {
                        double probability = chanceNodeSettings.GetActionProbability(a);
                        double[] result = CompleteHedgeProbe(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        for (byte p = 0; p < NumNonChancePlayers; p++)
                            combined[p] += probability * result[p];
                    }
                    return combined;
                }
            }
            else
            { // Can sample just one path at this node.
                byte sampledAction = chanceNodeSettings.SampleAction(numPossibleActions,
                    randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNodeSettings.GetActionProbability(sampledAction)}");
                return CompleteHedgeProbe_InPlace(ref historyPoint, randomProducer, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            }
        }

        private double[] CompleteHedgeProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = HedgeProbe(ref nextHistoryPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }


        private double[] CompleteHedgeProbe_InPlace(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            historyPoint.SwitchToBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = HedgeProbe(ref historyPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }

        public unsafe double HedgeProbe_WalkTree(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, Decision nextDecision, byte nextDecisionIndex)
        {
            if (TraceCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            if (gameStateForCurrentPlayer is FinalUtilities finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
                return HedgeProbe_WalkTree_ChanceNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNodeSettings);
            else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
            {
                return HedgeProbe_WalkTree_DecisionNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet);
            }
            else
                throw new NotImplementedException();
        }

        private unsafe double HedgeProbe_WalkTree_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet)
        {
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetHedgeProbabilities(sigmaRegretMatchedActionProbabilities);
            byte playerAtPoint = informationSet.PlayerIndex;
            double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
            if (playerAtPoint != playerBeingOptimized)
                return HedgeProbe_WalkTree_DecisionNode_OtherPlayer(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, numPossibleActions, randomDouble, playerAtPoint);
            return HedgeProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, numPossibleActions, randomDouble, playerAtPoint, sigmaRegretMatchedActionProbabilities);
        }

        private unsafe double HedgeProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, byte numPossibleActions, double randomDouble, byte playerAtPoint, double* sigmaRegretMatchedActionProbabilities)
        {
            double* samplingProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetEpsilonAdjustedHedgeProbabilities(samplingProbabilities, EvolutionSettings.EpsilonForMainPlayer);
            byte sampledAction = SampleAction(samplingProbabilities, numPossibleActions, randomDouble);
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
            double* counterfactualValues = stackalloc double[numPossibleActions];
            double summation = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                {
                    IGameState gameStateOriginal = historyPoint.GameState; // TODO -- move out of loop?
                    historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    summation = HedgeProbe_CalculateCounterfactualValues(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
                    GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
                }
                else
                {
                    // must put this in a separate method to avoid cost of creating HistoryPoint in this method when not in this loop
                    summation = HedgeProbe_CalculateCounterfactualValues_NewHistoryPoint(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, summation, sampledAction, samplingProbabilities, counterfactualValues);
                }
            }
            double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
            byte bestAction = 0;
            double bestCumulativeRegretIncrement = 0;
            informationSet.InitiateHedgeUpdate();
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                   (counterfactualValues[action - 1] - summation);
                informationSet.HedgeSetLastRegret(action, cumulativeRegretIncrement);
                if (bestAction == 0 || cumulativeRegretIncrement > bestCumulativeRegretIncrement) // TODO -- delete these variables.
                {
                    bestAction = action;
                    bestCumulativeRegretIncrement = cumulativeRegretIncrement;
                }
                if (TraceCFR)
                {
                    //TabbedText.WriteLine($"Optimizing {playerBeingOptimized} Actions to here {historyPoint.GetActionsToHereString(Navigation)} information set:{historyPoint.HistoryToPoint.GetPlayerInformationString(playerBeingOptimized, null)}"); 
                    TabbedText.WriteLine(
                        $"Increasing cumulative regret for action {action} in {informationSet.InformationSetNumber} by {inverseSamplingProbabilityQ} * ({(counterfactualValues[action - 1])} - {summation}) = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                }
            }
            informationSet.ConcludeHedgeUpdate();
            return summation;
        }

        private unsafe double HedgeProbe_CalculateCounterfactualValues_NewHistoryPoint(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, double summation, byte sampledAction, double* samplingProbabilities, double* counterfactualValues)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
            summation = HedgeProbe_CalculateCounterfactualValues(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
            return summation;
        }

        private unsafe double HedgeProbe_CalculateCounterfactualValues(ref HistoryPoint nextHistoryPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, byte sampledAction, double* samplingProbabilities, double* counterfactualValues, double summation)
        {
            if (action == sampledAction)
            {
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"{action}: Sampling selected action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
                if (TraceCFR)
                    TabbedText.Tabs++;
                double samplingProbabilityQPrime = samplingProbabilityQ * samplingProbabilities[action - 1];
                // IMPORTANT: Unlike Gibson probing, we don't record the result of the walk through the tree.
                HedgeProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized,
                    samplingProbabilityQPrime, randomProducer, informationSet.Decision, informationSet.DecisionIndex);
                if (TraceCFR)
                    TabbedText.Tabs--;
            }
            // IMPORTANT: Unlike Gibson probing, we use a probe to calculate all counterfactual values. 
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{action}: HedgeProbing action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
            if (TraceCFR)
                TabbedText.Tabs++;
            counterfactualValues[action - 1] =
                HedgeProbe_SinglePlayer(nextHistoryPoint, playerBeingOptimized, randomProducer);
            double summationDelta = sigmaRegretMatchedActionProbabilities[action - 1] *
                                    counterfactualValues[action - 1];
            summation += summationDelta;
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"Counterfactual value for action {action} is {counterfactualValues[action - 1]} => increment summation by {sigmaRegretMatchedActionProbabilities[action - 1]} * {counterfactualValues[action - 1]} = {summationDelta} to {summation}");
            }
            return summation;
        }

        private unsafe double HedgeProbe_WalkTree_DecisionNode_OtherPlayer(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte numPossibleActions, double randomDouble, byte playerAtPoint)
        {
            informationSet.GetHedgeProbabilities(sigmaRegretMatchedActionProbabilities);
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double cumulativeStrategyIncrement =
                    sigmaRegretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                if (EvolutionSettings.ParallelOptimization)
                    informationSet.IncrementCumulativeStrategy_Parallel(action, cumulativeStrategyIncrement);
                else
                    informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
            }
            byte sampledAction = SampleAction(sigmaRegretMatchedActionProbabilities, numPossibleActions,
                randomDouble);
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
            if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                return HedgeProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sampledAction);
            return HedgeProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sampledAction);
        }

        private double HedgeProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, byte sampledAction)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue2 = HedgeProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue2}");
            }
            return walkTreeValue2;
        }

        private double HedgeProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNodeTally informationSet, byte sampledAction)
        {
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue = HedgeProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
            return walkTreeValue;
        }

        private double HedgeProbe_WalkTree_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNodeSettings chanceNodeSettings)
        {
            byte sampledAction;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (numPossibleActions == 1)
                sampledAction = 1;
            else
                sampledAction = chanceNodeSettings.SampleAction(numPossibleActions, randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
            // TODO: Take into account critical node status. Right now, our critical node matters only for our probes, i.e. for later decisions. But we might have an early chance node that should be critical.
            if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
            {
                return HedgeProbe_WalkTree_ChanceNode_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNodeSettings, sampledAction, numPossibleActions);
            }
            else
            {
                return HedgeProbe_WalkTree_ChanceNode_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNodeSettings, sampledAction, numPossibleActions);
            }
        }

        private double HedgeProbe_WalkTree_ChanceNode_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = HedgeProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }

        private double HedgeProbe_WalkTree_ChanceNode_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = HedgeProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateOriginal);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }

        public void HedgeProbingCFRIteration(int iteration)
        {
            bool success;
            do
            {
                try
                {
                    success = true;
                    for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                    {
                        IRandomProducer randomProducer =
                            new ConsistentRandomSequenceProducer(iteration * 997 + playerBeingOptimized * 283 + GameNumber * 719);
                        HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                        if (TraceCFR)
                        {
                            TabbedText.WriteLine($"Iteration {iteration} Optimize player {playerBeingOptimized}");
                            TabbedText.Tabs++;
                        }
                        HedgeProbe_WalkTree(ref historyPoint, playerBeingOptimized, 1.0, randomProducer, GameDefinition.DecisionsExecutionOrder[0], 0);
                        if (TraceCFR)
                            TabbedText.Tabs--;
                    }
                }
                catch (Exception e)
                { // not clear on why this is needed
                    Console.WriteLine($"Error: {e}");
                    Console.WriteLine(e.StackTrace);
                    success = false;
                }
            } while (!success);
        }

        public async Task<string> SolveHedgeProbingCFR(string reportName)
        {
            //TraceCFR = true;
            //GameProgressLogger.LoggingOn = true;
            GameProgressLogger.OutputLogMessages = true;
            string reportString = null;
            GameNumber = EvolutionSettings.GameNumber;
            Console.WriteLine($"{reportName } game number {GameNumber} ({DateTime.Now})");
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Abramowicz algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            //GameDefinition.PrintOutOrderingInformation();
            // The code can run in parallel, but we break up our parallel calls for two reasons: (1) We would like to produce reports and need to do this while pausing the main algorithm; and (2) we would like to be able differentiate early from late iterations, in case we want to change epsilon over time for example. 
            ProbingCFRIterationNum = 0;
            int iterationsThisPhase = EvolutionSettings.TotalProbingCFRIterations;
            int startingIteration = ProbingCFRIterationNum;
            int stopPhaseBefore = startingIteration + iterationsThisPhase;
            while (startingIteration < stopPhaseBefore)
            {
                int stopBefore;
                if (EvolutionSettings.ReportEveryNIterations == null)
                    stopBefore = stopPhaseBefore;
                else
                {
                    int stopToReportBefore = HedgeProbing_GetNextMultipleOf(ProbingCFRIterationNum, (int)EvolutionSettings.ReportEveryNIterations);
                    stopBefore = Math.Min(stopPhaseBefore, stopToReportBefore);
                }
                s.Start();
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, startingIteration, stopBefore, iteration =>
                {
                    //if (iteration == 125092)
                    //    TraceCFR = true;
                    //else
                    //    TraceCFR = false;
                    ProbingCFREffectiveIteration = iteration;
                    HedgeProbingCFRIteration(iteration);
                }
                );
                s.Stop();
                ProbingCFRIterationNum = startingIteration = stopBefore; // this is the iteration to run next
                reportString = await GenerateReports(ProbingCFRIterationNum,
                    () =>
                        $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
            }
            return reportString; // final report
        }

        private int HedgeProbing_GetNextMultipleOf(int value, int multiple)
        {
            int rem = value % multiple;
            int result = value - rem;
            result += multiple;
            return result;
        }
    }
}