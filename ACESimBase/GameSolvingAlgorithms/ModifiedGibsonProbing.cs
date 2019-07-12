using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class ModifiedGibsonProbing : CounterfactualRegretMinimization
    {
        // Differences from Gibson:
        // 1. During the Probe, we visit all branches on a critical node.
        // 2. The counterfactual value of an action selected for the player being selected is determined based on a probe. The walk through the tree is used solely for purposes of sampling.

        // TODO: Try using https://github.com/josetr/IL.InitLocals. We are spending a lot of time resetting the stack and thus clearing everything allocated via stackalloc. But we shouldn't really need to, since we copy data into whatever we stack allocate.

        // TODO: Can we store utilities for the resolution set in the penultimate node? That is, if we see that the next nodes all contain a final utilities, then maybe we can record what those final utilities are, and thus save the need to traverse each of those possibilities.


        public ModifiedGibsonProbing(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new ModifiedGibsonProbing(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }
        public unsafe double ModifiedGibsonProbe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return ModifiedGibsonProbe(ref historyPoint, randomProducer)[playerBeingOptimized];
        }

        public unsafe double[] ModifiedGibsonProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            //if (TraceCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                if (TraceCFR && Navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                    TabbedText.WriteLine($"{historyPoint.GameProgress}");
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {String.Join(",", utility)}");
                return utility;
            }
            else
            {
                if (gameStateType == GameStateTypeEnum.Chance)
                    return ModifiedGibsonProbe_ChanceNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                if (gameStateType == GameStateTypeEnum.InformationSet)
                    return ModifiedGibsonProbe_DecisionNode(ref historyPoint, randomProducer, gameStateForCurrentPlayer);
                throw new NotImplementedException();
            }
        }

        private unsafe double[] ModifiedGibsonProbe_DecisionNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            InformationSetNode informationSet = (InformationSetNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            byte sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
            return CompleteModifiedGibsonProbe_InPlace(ref historyPoint, randomProducer, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
        }

        private double[] ModifiedGibsonProbe_ChanceNode(ref HistoryPoint historyPoint, IRandomProducer randomProducer, IGameState gameStateForCurrentPlayer)
        {
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            if (chanceNode.CriticalNode)
            { // Must sample every action at this node.
                if (historyPoint.BranchingIsReversible(Navigation, chanceNode.Decision))
                {
                    double[] combined = new double[NumNonChancePlayers]; // TODO -- can we use an array pool? Or use a pointer?
                    for (byte a = 1; a <= numPossibleActions; a++)
                    {
                        double probability = chanceNode.GetActionProbability(a);
                        IGameState gameStateOriginal = historyPoint.GameState; // TODO -- can we move this out of the for loop?
                        double[] result = CompleteModifiedGibsonProbe_InPlace(ref historyPoint, randomProducer, a, chanceNode.Decision, chanceNode.DecisionIndex);
                        GameDefinition.ReverseDecision(chanceNode.Decision, ref historyPoint, gameStateOriginal);
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
                        double probability = chanceNode.GetActionProbability(a);
                        double[] result = CompleteModifiedGibsonProbe(ref historyPoint, randomProducer, a, chanceNode.Decision, chanceNode.DecisionIndex);
                        for (byte p = 0; p < NumNonChancePlayers; p++)
                            combined[p] += probability * result[p];
                    }
                    return combined;
                }
            }
            else
            { // Can sample just one path at this node.
                byte sampledAction = chanceNode.SampleAction(numPossibleActions,
                    randomProducer.GetDoubleAtIndex(chanceNode.DecisionIndex));
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNode.GetActionProbability(sampledAction)}");
                return CompleteModifiedGibsonProbe_InPlace(ref historyPoint, randomProducer, sampledAction, chanceNode.Decision, chanceNode.DecisionIndex);
            }
        }

        private double[] CompleteModifiedGibsonProbe(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = ModifiedGibsonProbe(ref nextHistoryPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }


        private double[] CompleteModifiedGibsonProbe_InPlace(ref HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction, Decision nextDecision, byte nextDecisionIndex)
        {
            historyPoint.SwitchToBranch(Navigation, sampledAction, nextDecision, nextDecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double[] probeResult = ModifiedGibsonProbe(ref historyPoint, randomProducer);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                //TabbedText.WriteLine($"Actions to here: {nextHistoryPoint.GetActionsToHereString(Navigation)}");
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }

        public unsafe double ModifiedGibsonProbe_WalkTree(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, Decision nextDecision, byte nextDecisionIndex)
        {
            if (TraceCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            if (gameStateForCurrentPlayer is FinalUtilitiesNode finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is ChanceNode chanceNode)
                return ModifiedGibsonProbe_WalkTree_ChanceNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNode);
            else if (gameStateForCurrentPlayer is InformationSetNode informationSet)
            {
                return ModifiedGibsonProbe_WalkTree_DecisionNode(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet);
            }
            else
                throw new NotImplementedException();
        }

        private unsafe double ModifiedGibsonProbe_WalkTree_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet)
        {
            byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
            double* sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
            byte playerAtPoint = informationSet.PlayerIndex;
            double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
            if (playerAtPoint != playerBeingOptimized)
                return ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, numPossibleActions, randomDouble, playerAtPoint);
            return ModifiedGibsonProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, numPossibleActions, randomDouble, playerAtPoint, sigmaRegretMatchedActionProbabilities);
        }

        private unsafe double ModifiedGibsonProbe_WalkTree_DecisionNode_PlayerBeingOptimized(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, byte numPossibleActions, double randomDouble, byte playerAtPoint, double* sigmaRegretMatchedActionProbabilities)
        {
            double* samplingProbabilities = stackalloc double[numPossibleActions];
            informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, EvolutionSettings.EpsilonForMainPlayer);
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
                    summation = ModifiedGibsonProbe_CalculateCounterfactualValues(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
                    GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
                }
                else
                {
                    // must put this in a separate method to avoid cost of creating HistoryPoint in this method when not in this loop
                    summation = ModifiedGibsonProbe_CalculateCounterfactualValues_NewHistoryPoint(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, summation, sampledAction, samplingProbabilities, counterfactualValues);
                }
            }
            double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
            byte bestAction = 0;
            double bestCumulativeRegretIncrement = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                   (counterfactualValues[action - 1] - summation);
                if (EvolutionSettings.ParallelOptimization)
                    informationSet.IncrementCumulativeRegret_Parallel(action, cumulativeRegretIncrement, false);
                else
                    informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement);
                if (bestAction == 0 || cumulativeRegretIncrement > bestCumulativeRegretIncrement)
                {
                    bestAction = action;
                    bestCumulativeRegretIncrement = cumulativeRegretIncrement;
                }
                if (TraceCFR)
                {
                    //TabbedText.WriteLine($"Optimizing {playerBeingOptimized} Actions to here {historyPoint.GetActionsToHereString(Navigation)} information set:{historyPoint.HistoryToPoint.GetPlayerInformationString(playerBeingOptimized, null)}"); 
                    TabbedText.WriteLine(
                        $"Increasing cumulative regret for action {action} in {informationSet.InformationSetNodeNumber} by {inverseSamplingProbabilityQ} * ({(counterfactualValues[action - 1])} - {summation}) = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                }
            }
            return summation;
        }

        private unsafe double ModifiedGibsonProbe_CalculateCounterfactualValues_NewHistoryPoint(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, double summation, byte sampledAction, double* samplingProbabilities, double* counterfactualValues)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
            summation = ModifiedGibsonProbe_CalculateCounterfactualValues(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sigmaRegretMatchedActionProbabilities, action, sampledAction, samplingProbabilities, counterfactualValues, summation);
            return summation;
        }

        private unsafe double ModifiedGibsonProbe_CalculateCounterfactualValues(ref HistoryPoint nextHistoryPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, double* sigmaRegretMatchedActionProbabilities, byte action, byte sampledAction, double* samplingProbabilities, double* counterfactualValues, double summation)
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
                ModifiedGibsonProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized,
                    samplingProbabilityQPrime, randomProducer, informationSet.Decision, informationSet.DecisionIndex);
                if (TraceCFR)
                    TabbedText.Tabs--;
            }
            // IMPORTANT: Unlike Gibson probing, we use a probe to calculate all counterfactual values. 
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{action}: ModifiedGibsonProbing action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
            if (TraceCFR)
                TabbedText.Tabs++;
            counterfactualValues[action - 1] =
                ModifiedGibsonProbe_SinglePlayer(nextHistoryPoint, playerBeingOptimized, randomProducer);
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

        private unsafe double ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, double* sigmaRegretMatchedActionProbabilities, byte numPossibleActions, double randomDouble, byte playerAtPoint)
        {
            informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
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
                return ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sampledAction);
            return ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, informationSet, sampledAction);
        }

        private double ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, byte sampledAction)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue2 = ModifiedGibsonProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue2}");
            }
            return walkTreeValue2;
        }

        private double ModifiedGibsonProbe_WalkTree_DecisionNode_OtherPlayer_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, InformationSetNode informationSet, byte sampledAction)
        {
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            double walkTreeValue = ModifiedGibsonProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, informationSet.Decision, informationSet.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateOriginal);
            return walkTreeValue;
        }

        private double ModifiedGibsonProbe_WalkTree_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNode chanceNode)
        {
            byte sampledAction;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            if (numPossibleActions == 1)
                sampledAction = 1;
            else
                sampledAction = chanceNode.SampleAction(numPossibleActions, randomProducer.GetDoubleAtIndex(chanceNode.DecisionIndex));
            // TODO: Take into account critical node status. Right now, our critical node matters only for our probes, i.e. for later decisions. But we might have an early chance node that should be critical.
            if (historyPoint.BranchingIsReversible(Navigation, chanceNode.Decision))
            {
                return ModifiedGibsonProbe_WalkTree_ChanceNode_Reversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNode, sampledAction, numPossibleActions);
            }
            else
            {
                return ModifiedGibsonProbe_WalkTree_ChanceNode_NotReversible(ref historyPoint, playerBeingOptimized, samplingProbabilityQ, randomProducer, chanceNode, sampledAction, numPossibleActions);
            }
        }

        private double ModifiedGibsonProbe_WalkTree_ChanceNode_NotReversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNode chanceNode, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNode.DecisionIndex}");
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNode.Decision, chanceNode.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = ModifiedGibsonProbe_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, chanceNode.Decision, chanceNode.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }

        private double ModifiedGibsonProbe_WalkTree_ChanceNode_Reversible(ref HistoryPoint historyPoint, byte playerBeingOptimized, double samplingProbabilityQ, IRandomProducer randomProducer, ChanceNode chanceNode, byte sampledAction, byte numPossibleActions)
        {
            if (TraceCFR)
                TabbedText.WriteLine(
                    $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNode.DecisionIndex}");
            IGameState gameStateOriginal = historyPoint.GameState;
            historyPoint.SwitchToBranch(Navigation, sampledAction, chanceNode.Decision, chanceNode.DecisionIndex);
            if (TraceCFR)
                TabbedText.Tabs++;
            // var actionsToHere = nextHistoryPoint.GetActionsToHereString(Navigation);
            double walkTreeValue = ModifiedGibsonProbe_WalkTree(ref historyPoint, playerBeingOptimized, samplingProbabilityQ,
                randomProducer, chanceNode.Decision, chanceNode.DecisionIndex);
            GameDefinition.ReverseDecision(chanceNode.Decision, ref historyPoint, gameStateOriginal);
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
            }
            return walkTreeValue;
        }


        public void ModifiedGibsonProbingCFRIteration(int iteration)
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
                        ModifiedGibsonProbe_WalkTree(ref historyPoint, playerBeingOptimized, 1.0, randomProducer, GameDefinition.DecisionsExecutionOrder[0], 0);
                        if (TraceCFR)
                            TabbedText.Tabs--;
                    }
                }
                catch (Exception e)
                { // not clear on why this is needed
                    TabbedText.WriteLine($"Error: {e}");
                    TabbedText.WriteLine(e.StackTrace);
                    success = false;
                }
            } while (!success);
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            //TraceCFR = true;
            //GameProgressLogger.LoggingOn = true;
            GameProgressLogger.OutputLogMessages = true;
            ReportCollection reportCollection = new ReportCollection();
            GameNumber = EvolutionSettings.GameNumber;
            TabbedText.WriteLine($"{optionSetName } game number {GameNumber} ({DateTime.Now})");
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Abramowicz algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            //GameDefinition.PrintOutOrderingInformation();
            // The code can run in parallel, but we break up our parallel calls for two reasons: (1) We would like to produce reports and need to do this while pausing the main algorithm; and (2) we would like to be able differentiate early from late iterations, in case we want to change epsilon over time for example. 
            IterationNum = 0;
            int iterationsThisPhase = EvolutionSettings.TotalProbingCFRIterations;
            int startingIteration = IterationNum;
            int stopPhaseBefore = startingIteration + iterationsThisPhase;
            while (startingIteration < stopPhaseBefore)
            {
                int stopBefore;
                if (EvolutionSettings.ReportEveryNIterations == null)
                    stopBefore = stopPhaseBefore;
                else
                {
                    int stopToReportBefore = ModifiedGibsonProbing_GetNextMultipleOf(IterationNum, (int)EvolutionSettings.ReportEveryNIterations);
                    stopBefore = Math.Min(stopPhaseBefore, stopToReportBefore);
                }
                s.Start();
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, startingIteration, stopBefore, iteration =>
                {
                    //if (iteration == 125092)
                    //    TraceCFR = true;
                    //else
                    //    TraceCFR = false;
                    ModifiedGibsonProbingCFRIteration(iteration);
                }
                );
                s.Stop();
                IterationNum = startingIteration = stopBefore; // this is the iteration to run next
                var result = await GenerateReports(IterationNum,
                    () =>
                        $"Iteration {IterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(IterationNum + 1))))}");
                reportCollection.Add(result);
            }
            return reportCollection;
        }

        private int ModifiedGibsonProbing_GetNextMultipleOf(int value, int multiple)
        {
            int rem = value % multiple;
            int result = value - rem;
            result += multiple;
            return result;
        }
    }
}