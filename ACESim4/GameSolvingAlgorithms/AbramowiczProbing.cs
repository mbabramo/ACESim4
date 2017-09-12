using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESim
{
    public partial class CounterfactualRegretMaximization
    {
        // Differences from Gibson:
        // 1. During the Probe, we visit all branches on a critical node.
        // 2. The counterfactual value of an action selected for the player being selected is determined based on a probe. The walk through the tree is used solely for purposes of sampling.
        // 3. Backup regrets set on alternate iterations. We alternate normal with exploratory iterations, where both players engage in epsilon exploration. In an exploratory iteration, we increment backup cumulative regrets, but only where the main regrets are empty. This ensures that if a node will not be visited without exploration, we still develop our best estimate of the correct value based on exploration. Note that this provides robustness but means that T in the regret bound guarantees will consist only if the normal iterations.

        // TODO -- optimization. Reducing unnecessary probes. Sometimes, we may have an action that leads to a particular result that will then necessarily be the same for either all higher or lower actions. For example, a plaintiff gives a particular offer that results in rejection of the defendant's settlement offer. In many litigation games (where there is no punishment for unreasonable offers), this means that all higher plaintiff offers will lead to the same result. So, in this case, we should start with the lowest possible offer. As soon as we get one that leads to rejection of the next settlement, then we should automatically set all higher offers. This should eliminate about half of all probes if implemented. But what if one's decision will enter into the other player's information set? Then this won't work. Every action will lead potentially to different consequences. 
        // The game definition might have a function that has IdentifySameGroups. It reports this information using a boolean indicating whether an action will produce the same result as the one below it. For our game, when we're forgetting earlier offers, this will be all offers leading to rejection of the settlement (unless we are in a bargaining round where we're penalizing unreasonable settlements). Note that sampling one item in this group will essentially have the same effect as sampling any other. Meanwhile, accepting offers will lead to quick termination of the game, so those probes won't take long to resolve. 
        // Further optimization might be to store utilities in the penultimate node, in a dictionary based on a hash of the resolution set. This would take up a fair bit of space, but it might speed things up. Can we also do a similar speed up at the beginning of the game for chance actions? That is, we'd like to be able to go from litigation quality to the parties signals without going through all of the noise values.


        // Comment: Note that we're already incrementing cumulative regret for all possible actions at each information set visited. So, the only benefit that we can get occurs if we have decisions that lead to the same result, thus speeding up our probes. Perhaps we can have a custom hook that allows us to figure this out quickly. That is, the game definition will tell us, given the custom random numbers that we have and the HistoryPoint, which actions can be grouped with the action that we are currently exploring for the player being optimized. For example, we might be able to group all actions that would lead to rejection of settlement, depending on the information that will be added to players' information sets; it might also report whether there could be another group beyond this. This should be very straightforward when we're forgetting earlier bargaining rounds. 
        


        private bool AlsoDisablePlayerOwnExplorationOnNonExploratoryIterations = true;
        private List<(double discount, double endAfterIterationsProportion)> Discounts = new List<(double, double)>()
        {
          (0.00000001, 0.01),
          (0.00000100, 0.05),
          (0.00010000, 0.1),
          (0.01000000, 0.2),
        };

        private List<int> PhasePointsToSubtractEarlierValues = new List<int>() {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20};

        // DEBUG: See if it will work well with just PhasePoints, no discounts. Could leave feature in though.


        public unsafe double AbramowiczProbe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return AbramowiczProbe(ref historyPoint, randomProducer)[playerBeingOptimized];
        }

        public unsafe double[] AbramowiczProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            //if (TraceProbingCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                if (TraceProbingCFR && Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                    TabbedText.WriteLine($"{historyPoint.GameProgress}");
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceProbingCFR)
                    TabbedText.WriteLine($"Utility returned {String.Join("," ,utility)}"); 
                return utility;
            }
            else
            {
                if (gameStateType == GameStateTypeEnum.Chance)
                    return AbramowiczProbe_ChanceNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                if (gameStateType == GameStateTypeEnum.Tally)
                    return AbramowiczProbe_DecisionNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                throw new NotImplementedException();
            }
        }

        private unsafe double[] AbramowiczProbe_DecisionNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            InformationSetNodeTally informationSet = (InformationSetNodeTally) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            byte sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
            return CompleteAbramowiczProbe_InPlace(ref historyPoint, randomProducer, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
        }

        private double[] AbramowiczProbe_ChanceNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (chanceNodeSettings.CriticalNode)
            {
                if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
                {
                    double[] combined = new double[NumNonChancePlayers];
                    for (byte a = 1; a <= numPossibleActions; a++)
                    {
                        double probability = chanceNodeSettings.GetActionProbability(a);
                        IGameState gameStateOriginal = historyPoint.GameState;
                        double[] result = CompleteAbramowiczProbe_InPlace(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateOriginal);
                        for (byte p = 0; p < NumNonChancePlayers; p++)
                            combined[p] += probability * result[p];
                    }
                    return combined;
                }
                else
                {
                    double[] combined = new double[NumNonChancePlayers];
                    for (byte a = 1; a <= numPossibleActions; a++)
                    {
                        double probability = chanceNodeSettings.GetActionProbability(a);
                        double[] result = CompleteAbramowiczProbe(ref historyPoint, randomProducer, a, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                        for (byte p = 0; p < NumNonChancePlayers; p++)
                            combined[p] += probability * result[p];
                    }
                    return combined;
                }
            }
            else
            {
                byte sampledAction = chanceNodeSettings.SampleAction(numPossibleActions,
                    randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
                if (TraceProbingCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNodeSettings.GetActionProbability(sampledAction)}");
                return CompleteAbramowiczProbe_InPlace(ref historyPoint, randomProducer, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            }
        }

        private double[] CompleteAbramowiczProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            double[] probeResult = AbramowiczProbe(ref nextHistoryPoint, randomProducer);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }
        

        private double[] CompleteAbramowiczProbe_InPlace(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            historyPoint.SwitchToBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            double[] probeResult = AbramowiczProbe(ref historyPoint, randomProducer);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }

        public unsafe double AbramowiczProbe_WalkTree(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, Decision nextDecision, byte nextDecisionIndex)
        {
            if (TraceProbingCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            if (gameStateForCurrentPlayer is FinalUtilities finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceProbingCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
                return AbramowiczProbe_WalkTree_ChanceNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings);
            else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
            {
                return AbramowiczProbe_WalkTree_DecisionNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet);
            }
            else
                throw new NotImplementedException();
        }

        private unsafe double AbramowiczProbe_WalkTree_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet)
        {
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
            byte playerAtPoint = informationSet.PlayerIndex;
            double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
            if (playerAtPoint != playerBeingOptimized)
                return AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sigmaRegretMatchedActionProbabilities, numPossibleActions, randomDouble, playerAtPoint);
            return AbramowiczProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, numPossibleActions, randomDouble, playerAtPoint, sigmaRegretMatchedActionProbabilities);
        }

        private unsafe double AbramowiczProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte numPossibleActions, double randomDouble, byte playerAtPoint, double* sigmaRegretMatchedActionProbabilities)
        {
            double* samplingProbabilities = stackalloc double[numPossibleActions];
            if (!isExploratoryIteration && AlsoDisablePlayerOwnExplorationOnNonExploratoryIterations) // with this change, the main player will also explore only on odd iterations
                informationSet.GetRegretMatchingProbabilities(samplingProbabilities);
            else
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, EvolutionSettings.EpsilonForMainPlayer);
            byte sampledAction = SampleAction(samplingProbabilities, numPossibleActions, randomDouble);
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
            double* counterfactualValues = stackalloc double[numPossibleActions];
            double summation = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                {
                    IGameState gameStateOriginal = historyPoint.GameState;
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
                if (DiscountingEnabled)
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
                if (TraceProbingCFR)
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
                if (TraceProbingCFR)
                    TabbedText.WriteLine(
                        $"{action}: Sampling selected action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
                if (TraceProbingCFR)
                    TabbedText.Tabs++;
                double samplingProbabilityQPrime = samplingProbabilityQ * samplingProbabilities[action - 1];
                // IMPORTANT: Unlike Gibson probing, we don't record the result of the walk through the tree.
                AbramowiczProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized,
                    samplingProbabilityQPrime, randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
                if (TraceProbingCFR)
                    TabbedText.Tabs--;
            }
            // IMPORTANT: Unlike Gibson probing, we use a probe to calculate all counterfactual values. 
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{action}: AbramowiczProbing action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            counterfactualValues[action - 1] =
                AbramowiczProbe_SinglePlayer(nextHistoryPoint, playerBeingOptimized, randomProducer);
            double summationDelta = sigmaRegretMatchedActionProbabilities[action - 1] *
                                    counterfactualValues[action - 1];
            summation += summationDelta;
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"Counterfactual value for action {action} is {counterfactualValues[action - 1]} => increment summation by {sigmaRegretMatchedActionProbabilities[action - 1]} * {counterfactualValues[action - 1]} = {summationDelta} to {summation}");
            }
            return summation;
        }

        private unsafe double AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, double* sigmaRegretMatchedActionProbabilities, byte numPossibleActions, double randomDouble, byte playerAtPoint)
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
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                }
            byte sampledAction = SampleAction(sigmaRegretMatchedActionProbabilities, numPossibleActions,
                randomDouble);
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
            if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                return AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sampledAction);
            return AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, informationSet, sampledAction);
        }

        private double AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte sampledAction)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            double walkTreeValue2 = AbramowiczProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue2}");
            }
            return walkTreeValue2;
        }

        private double AbramowiczProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, InformationSetNodeTally informationSet, byte sampledAction)
        {
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            double walkTreeValue = AbramowiczProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
            return walkTreeValue;
        }

        private double AbramowiczProbe_WalkTree_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings)
        {
            byte sampledAction;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (numPossibleActions == 1)
                sampledAction = 1;
            else
                sampledAction = chanceNodeSettings.SampleAction(numPossibleActions, randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
            if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
            {
                return AbramowiczProbe_WalkTree_ChanceNode_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings, sampledAction, numPossibleActions);
            }
            else
            {
                return AbramowiczProbe_WalkTree_ChanceNode_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, isExploratoryIteration, chanceNodeSettings, sampledAction, numPossibleActions);
            }
        }

        private double AbramowiczProbe_WalkTree_ChanceNode_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = AbramowiczProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }

        private double AbramowiczProbe_WalkTree_ChanceNode_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, bool isExploratoryIteration, ChanceNodeSettings chanceNodeSettings, byte sampledAction, byte numPossibleActions)
        {
            if (TraceProbingCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNodeSettings.DecisionIndex}");
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = AbramowiczProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, isExploratoryIteration, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
            GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateOriginal);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }


        public void AbramowiczProbingCFRIteration(int iteration)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                IRandomProducer randomProducer =
                    new ConsistentRandomSequenceProducer(iteration * 997 + playerBeingOptimized * 283 + GameNumber * 719);
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceProbingCFR)
                {
                    TabbedText.WriteLine($"Iteration {iteration} Optimize player {playerBeingOptimized}");
                    TabbedText.Tabs++;
                }
                AbramowiczProbe_WalkTree(ref historyPoint, playerBeingOptimized, 1.0, randomProducer, iteration % 2 == 1, GameDefinition.DecisionsExecutionOrder[0], 0);
                if (TraceProbingCFR)
                    TabbedText.Tabs--;
            }
        }

        private static int GameNumber = 0;

        private int BackupRegretsTrigger;
        private bool DiscountingEnabled;
        private double CurrentDiscount;

        public unsafe void SolveAbramowiczProbingCFR()
        {
            if (GameNumber == 0)
                GameNumber = EvolutionSettings.InitialRandomSeed;
            Console.WriteLine($"Game number {GameNumber}");
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Abramowicz algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            GameDefinition.PrintOutOrderingInformation();
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
                DiscountingEnabled = false;
                foreach (var discount in Discounts)
                {
                    if (startingIteration < discount.endAfterIterationsProportion * EvolutionSettings.TotalProbingCFRIterations)
                    {
                        DiscountingEnabled = true;
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
                            //    TraceProbingCFR = true;
                            //else
                            //    TraceProbingCFR = false;
                            ProbingCFREffectiveIteration = iteration;
                            AbramowiczProbingCFRIteration(iteration);
                        }
                    );
                    s.Stop();
                    ProbingCFRIterationNum = startingIteration = stopBefore; // this is the iteration to run next
                    GenerateReports(ProbingCFRIterationNum,
                        () =>
                            $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
                }
            }
            GameNumber++;
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