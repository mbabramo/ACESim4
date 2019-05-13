using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {
        // Differences from Gibson:
        // 1. During the Probe, we visit all branches on a critical node.
        // 2. The counterfactual value of an action selected for the player being selected is determined based on a probe. The walk through the tree is used solely for purposes of sampling.
        // 3. Backup regrets set on alternate iterations. We alternate normal with exploratory iterations, where both players engage in epsilon exploration. In an exploratory iteration, we increment backup cumulative regrets, but only where the main regrets are empty. This ensures that if a node will not be visited without exploration, we still develop our best estimate of the correct value based on exploration. Note that this provides robustness but means that T in the regret bound guarantees will consist only of the normal iterations.

        // TODO: Try using https://github.com/josetr/IL.InitLocals. We are spending a lot of time resetting the stack and thus clearing everything allocated via stackalloc. But we shouldn't really need to, since we copy data into whatever we stack allocate.


        // TODO: possible speedup: Skip probes on many zero-probability moves. So, if we're exploring, and a move is zero probability based on regret matching, then it won't affect any other node's measurement of counterfactual regret. However, we still periodically want to measure this node's counterfactual regret. So, we might still explore this node as our main path, but skip using it as a probe.

        // TODO: Can we store utilities for the resolution set in the penultimate node? That is, if we see that the next nodes all contain a final utilities, then maybe we can record what those final utilities are, and thus save the need to traverse each of those possibilities.


        /// <summary>
        /// Constants to multiply cumulative regret increments by in early phases of the optimization process. This prevents early phases and the effectively random moves in those phases from having a large effect on later optimizations. 
        /// </summary>
        private List<(double discount, double endAfterIterationsProportion)> Discounts = new List<(double, double)>()
        {
          //(0.00000001, 0.01),
          //(0.00000100, 0.05),
          //(0.00010000, 0.1),
          //(0.01000000, 0.2),
          
            (0.00000001, 0.01),
            (0.00000010, 0.02),
            (0.00000100, 0.04),
            (0.00001000, 0.08),
            (0.00010000, 0.12),
            (0.00100000, 0.16),
            (0.01000000, 0.24),
            (0.1000000, 0.32),
        };

        /// <summary>
        /// Phases (out of 100) at which to subtract out the regret values from prior stages. If the last point listed here corresponds to the point at which discounting ends, then the iterations since the second-to-last phase point will still be reflected, but no earlier iterations will have any role in determining the cumulative regret increments and thus the average strategy.
        /// </summary>
        private List<int> PhasePointsToSubtractEarlierValues = new List<int>() {1, 2, 4, 8, 12, 16, 24, 32};


        public unsafe double ExploratoryProbe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return ExploratoryProbe(ref historyPoint, randomProducer)[playerBeingOptimized];
        }
        
        public unsafe double[] ExploratoryProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer)
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
                    TabbedText.WriteLine($"Utility returned {String.Join("," ,utility)}"); 
                return utility;
            }
            else
            {
                if (gameStateType == GameStateTypeEnum.Chance)
                    return ExploratoryProbe_ChanceNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                if (gameStateType == GameStateTypeEnum.InformationSet)
                    return ExploratoryProbe_DecisionNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                throw new NotImplementedException();
            }
        }

        private unsafe double[] ExploratoryProbe_DecisionNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            InformationSetNodeTally informationSet = (InformationSetNodeTally) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            byte sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
            return CompleteExploratoryProbe_InPlace(ref historyPoint, randomProducer, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
        }

        private double[] ExploratoryProbe_ChanceNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
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
                        double[] result = CompleteExploratoryProbe_InPlace(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
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
                        double[] result = CompleteExploratoryProbe(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
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
                return CompleteExploratoryProbe_InPlace(ref historyPoint, randomProducer, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            }
        }

        private double[] CompleteExploratoryProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = ExploratoryProbe(ref nextHistoryPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }
        

        private double[] CompleteExploratoryProbe_InPlace(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            historyPoint.SwitchToBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = ExploratoryProbe(ref historyPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }

        public unsafe double ExploratoryProbe_WalkTree(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, Decision nextDecision, byte nextDecisionIndex)
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
                return ExploratoryProbe_WalkTree_ChanceNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings);
            else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
            {
                return ExploratoryProbe_WalkTree_DecisionNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet);
            }
            else
                throw new NotImplementedException();
        }

        private unsafe double ExploratoryProbe_WalkTree_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet)
        {
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
            byte playerAtPoint = informationSet.PlayerIndex;
            double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
            if (playerAtPoint != playerBeingOptimized)
                return ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sigmaRegretMatchedActionProbabilities, numPossibleActions, randomDouble, playerAtPoint);
            return ExploratoryProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, numPossibleActions, randomDouble, playerAtPoint, sigmaRegretMatchedActionProbabilities);
        }

        private unsafe double ExploratoryProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte numPossibleActions, double randomDouble, byte playerAtPoint, double* sigmaRegretMatchedActionProbabilities)
        {
            double* samplingProbabilities = stackalloc double[numPossibleActions];
            if (isExploratoryIteration || EvolutionSettings.PlayerBeingOptimizedExploresOnOwnIterations)
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, EvolutionSettings.EpsilonForMainPlayer);
            else
                informationSet.GetRegretMatchingProbabilities(samplingProbabilities);
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
                    summation = CalculateCounterfactualValues(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
                    GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
                }
                else
                {
                    // must put this in a separate method to avoid cost of creating HistoryPoint in this method when not in this loop
                    summation = CalculateCounterfactualValues_NewHistoryPoint(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sigmaRegretMatchedActionProbabilities, action, summation, sampledAction, samplingProbabilities, counterfactualValues);
                }
            }
            double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
            byte bestAction = 0;
            double bestCumulativeRegretIncrement = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                   (counterfactualValues[action - 1] - summation);
                bool incrementVisits = action == numPossibleActions; // we increment visits only once per information set. This incrementing is how we keep track of whether we are accumulating backup visits without main visits, in which case we switch to that set.
                if (BackupDiscountingEnabled)
                    cumulativeRegretIncrement *= CurrentDiscount;
                if (EvolutionSettings.ParallelOptimization)
                    informationSet.IncrementCumulativeRegret_Parallel(action, cumulativeRegretIncrement, isExploratoryIteration, BackupRegretsTrigger, incrementVisits);
                else
                    informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement, isExploratoryIteration, BackupRegretsTrigger, incrementVisits);
                if (bestAction == 0 || cumulativeRegretIncrement > bestCumulativeRegretIncrement)
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
            return summation;
        }

        private unsafe double CalculateCounterfactualValues_NewHistoryPoint(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, double summation, byte sampledAction, double* samplingProbabilities, double* counterfactualValues)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
            summation = CalculateCounterfactualValues(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
            return summation;
        }

        private unsafe double CalculateCounterfactualValues(ref HistoryPoint nextHistoryPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, byte sampledAction, double* samplingProbabilities, double* counterfactualValues, double summation)
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
                ExploratoryProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized,
                    samplingProbabilityQPrime, randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
                if (TraceCFR)
                    TabbedText.Tabs--;
            }
            // IMPORTANT: Unlike Gibson probing, we use a probe to calculate all counterfactual values. 
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{action}: ExploratoryProbing action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
            if (TraceCFR)
                TabbedText.Tabs++;
            counterfactualValues[action - 1] =
                ExploratoryProbe_SinglePlayer(nextHistoryPoint, playerBeingOptimized, randomProducer);
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

        private unsafe double ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte numPossibleActions, double randomDouble, byte playerAtPoint)
        {
            if (!isExploratoryIteration)
                informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
            else // Difference from Gibson. The opponent will use epsilon exploration (but only during the exploratory phase).
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities, EvolutionSettings.EpsilonForOpponentWhenExploring);
            if (!isExploratoryIteration)
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
                return ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sampledAction);
            return ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sampledAction);
        }

        private double ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte sampledAction)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue2 = ExploratoryProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue2}");
            }
            return walkTreeValue2;
        }

        private double ExploratoryProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte sampledAction)
        {
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue = ExploratoryProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
            return walkTreeValue;
        }

        private double ExploratoryProbe_WalkTree_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings)
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
                return ExploratoryProbe_WalkTree_ChanceNode_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings, sampledAction, numPossibleActions);
            }
            else
            {
                return ExploratoryProbe_WalkTree_ChanceNode_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings, sampledAction, numPossibleActions);
            }
        }

        private double ExploratoryProbe_WalkTree_ChanceNode_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = ExploratoryProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }

        private double ExploratoryProbe_WalkTree_ChanceNode_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = ExploratoryProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateOriginal);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }


        public void ExploratoryProbingCFRIteration(int iteration)
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
                        ExploratoryProbe_WalkTree(ref historyPoint, playerBeingOptimized, 1.0, randomProducer, iteration % 2 == 1, GameDefinition.DecisionsExecutionOrder[0], 0);
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

        private static int GameNumber = 0;

        private int BackupRegretsTrigger;
        private bool BackupDiscountingEnabled;
        private double CurrentDiscount;

        public unsafe string SolveExploratoryProbingCFR(string reportName)
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
            int numPhases = 100; // should have at least 100 if we have a discount for first 1% of iterations
            int iterationsPerPhase = (EvolutionSettings.TotalProbingCFRIterations) / (numPhases);
            int iterationsFinalPhase = EvolutionSettings.TotalProbingCFRIterations - (numPhases - 1) * iterationsPerPhase; // may be greater if there is a remainder from above
            ProbingCFRIterationNum = 0;
            ProbingCFREffectiveIteration = 0;
            for (int phase = 0; phase < numPhases; phase++)
            {
                BackupRegretsTrigger = EvolutionSettings.MinBackupRegretsTrigger + (int) (EvolutionSettings.TriggerIncreaseOverTime * ((double) phase / (double) (numPhases - 1)));
                int iterationsThisPhase = phase == numPhases - 1
                    ? iterationsFinalPhase
                    : iterationsPerPhase;
                int startingIteration = ProbingCFRIterationNum;
                int stopPhaseBefore = startingIteration + iterationsThisPhase;
                BackupDiscountingEnabled = false;
                foreach (var discount in Discounts)
                {
                    if (startingIteration < discount.endAfterIterationsProportion * EvolutionSettings.TotalProbingCFRIterations)
                    {
                        BackupDiscountingEnabled = true;
                        CurrentDiscount = discount.discount;
                        break; // once we find a discount, we're done
                    }
                }
                if (PhasePointsToSubtractEarlierValues.Any())
                {
                    if (phase == PhasePointsToSubtractEarlierValues.First())
                        WalkAllInformationSetTrees(node => { node.StoreCurrentTallyValues(); });
                    else if (PhasePointsToSubtractEarlierValues.Contains(phase))
                        WalkAllInformationSetTrees(node =>
                        {
                            node.SubtractOutStoredTallyValues();
                            node.StoreCurrentTallyValues();
                        });
                    if (phase == PhasePointsToSubtractEarlierValues.Last())
                        WalkAllInformationSetTrees(node => { node.ClearAverageStrategyTally(); }); // we will be measuring average strategies from here. Thus, earlier iterations will not count in the regret bounds.
                }
                while (startingIteration < stopPhaseBefore)
                {
                    int stopBefore;
                    if (EvolutionSettings.ReportEveryNIterations == null)
                        stopBefore = stopPhaseBefore;
                    else
                    {
                        int stopToReportBefore = GetNextMultipleOf(ProbingCFRIterationNum, (int)EvolutionSettings.ReportEveryNIterations);
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
                            ExploratoryProbingCFRIteration(iteration);
                        }
                    );
                    s.Stop();
                    ProbingCFRIterationNum = startingIteration = stopBefore; // this is the iteration to run next
                    reportString = GenerateReports(ProbingCFRIterationNum,
                        () =>
                            $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
                }
            }
            return reportString; // final report
        }

        private int GetNextMultipleOf(int value, int multiple)
        {
            int rem = value % multiple;
            int result = value - rem;
            result += multiple;
            return result;
        }
    }
}