using System;
using System.Diagnostics;

namespace ACESim
{
    public partial class CounterfactualRegretMaximization
    {
        // Differences from Gibson:Increm
        // 1. During the Probe, we visit all branches on a critical node.
        // 2. The counterfactual value of an action selected for the player being selected is determined based on a probe. The walk through the tree is used solely for purposes of sampling.
        // 3. Alternating phases. We alternate normal with exploratory phases. In the exploratory phase, we increment cumulative regret only where it was not incremented during the prior normal phase and do not increment cumulative strategies. This should slow down our regret bounds by half.
        // 4. Remove old regrets. We discount past regrets almost entirely at the beginning of a new phase -- keeping just enough so that the old behavior will control in case the information set is not visited.

        public unsafe double AbramowiczProbe_SinglePlayer(HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer)
        {
            return AbramowiczProbe(historyPoint, randomProducer)[playerBeingOptimized];
        }

        public unsafe double[] AbramowiczProbe(HistoryPoint historyPoint, IRandomProducer randomProducer)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(historyPoint);
            //if (TraceProbingCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilities finalUtilities = (FinalUtilities)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceProbingCFR)
                    TabbedText.WriteLine($"Utility returned {String.Join("," ,utility)}");
                return utility;
            }
            else
            {
                byte sampledAction = 0;
                if (gameStateType == GameStateTypeEnum.Chance)
                {
                    ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    if (chanceNodeSettings.CriticalNode)
                    {
                        double[] combined = new double[NumNonChancePlayers];
                        for (byte a = 1; a <= numPossibleActions; a++)
                        {
                            double probability = chanceNodeSettings.GetActionProbability(a);
                            double[] result = CompleteAbramowiczProbe(historyPoint, randomProducer, a);
                            for (byte p = 0; p < NumNonChancePlayers; p++)
                                combined[p] += probability * result[p];
                        }
                        return combined;
                    }
                    else
                    {
                        sampledAction = chanceNodeSettings.SampleAction(numPossibleActions,
                            randomProducer.GetDoubleAtIndex(chanceNodeSettings.DecisionIndex));
                        if (TraceProbingCFR)
                            TabbedText.WriteLine(
                                $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNodeSettings.GetActionProbability(sampledAction)}");
                        return CompleteAbramowiczProbe(historyPoint, randomProducer, sampledAction);
                    }
                }
                else if (gameStateType == GameStateTypeEnum.Tally)
                {
                    InformationSetNodeTally informationSet = (InformationSetNodeTally)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double* actionProbabilities = stackalloc double[numPossibleActions];
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                    sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                        randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
                    return CompleteAbramowiczProbe(historyPoint, randomProducer, sampledAction);
                }
                else
                    throw new NotImplementedException();
            }
        }


        private double[] CompleteAbramowiczProbe(HistoryPoint historyPoint, IRandomProducer randomProducer, byte sampledAction)
        {
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
            if (TraceProbingCFR)
                TabbedText.Tabs++;
            double[] probeResult = AbramowiczProbe(nextHistoryPoint, randomProducer);
            if (TraceProbingCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine($"Returning probe result {String.Join(",", probeResult)}");
            }
            return probeResult;
        }

        public unsafe double AbramowiczProbe_WalkTree(HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer)
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
                double walkTreeValue = AbramowiczProbe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                    randomProducer);
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
                double* sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
                byte playerAtPoint = informationSet.PlayerIndex;
                double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
                // OTHER PLAYER:
                if (playerAtPoint != playerBeingOptimized)
                {
                    if (IsNormalPhase)
                        informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
                    else // Difference from Gibson. The opponent will use epsilon exploration (but only during the exploratory phase).
                        informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities, CurrentEpsilonValue);
                    if (IsNormalPhase)
                        for (byte action = 1; action <= numPossibleActions; action++)
                        {
                            double cumulativeStrategyIncrement =
                                sigmaRegretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                            informationSet.IncrementCumulativeStrategy_Parallel(action, cumulativeStrategyIncrement);
                            if (TraceProbingCFR)
                                TabbedText.WriteLine(
                                    $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                        }
                    sampledAction = SampleAction(sigmaRegretMatchedActionProbabilities, numPossibleActions,
                        randomDouble);
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction);
                    if (TraceProbingCFR)
                        TabbedText.Tabs++;
                    double walkTreeValue = AbramowiczProbe_WalkTree(nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                        randomProducer);
                    if (TraceProbingCFR)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                }
                // PLAYER BEING OPTIMIZED:
                double* samplingProbabilities = stackalloc double[numPossibleActions];
                const double epsilonForProbeWalk = 0.5;
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, epsilonForProbeWalk);
                sampledAction = SampleAction(samplingProbabilities, numPossibleActions, randomDouble);
                if (TraceProbingCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
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
                        // IMPORTANT: Unlike Gibson probing, we don't record the result of the walk through the tree.
                        AbramowiczProbe_WalkTree(nextHistoryPoint, playerBeingOptimized,
                            samplingProbabilityQPrime, randomProducer);
                    }
                    // IMPORTANT: Unlike Gibson probing, we use a probe to calculate all counterfactual values. 
                    if (TraceProbingCFR)
                        TabbedText.WriteLine(
                            $"{action}: AbramowiczProbing unselected action {action} for player {informationSet.PlayerIndex}decision {informationSet.DecisionIndex}");
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
                }
                double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    // In the exploratory phase, we increment only the information sets that were not incremented in the normal phase. That's because we're using opponent exploration and don't want to distort the normal phase.
                    if (IsNormalPhase || informationSet.LastIterationChanged < BeginningOfLastNormalPhase)
                    {
                        double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                           (counterfactualValues[action - 1] - summation);
                        informationSet.IncrementCumulativeRegret_Parallel(action, cumulativeRegretIncrement);
                        if (IsNormalPhase)
                            informationSet.LastIterationChanged = ProbingCFRIterationNum; // we don't change the iteration in the other phase, because we don't want to limit ourselves to a single pass in this phase. So really, last iteration changed means "last iteration changed in a normal phase."
                        if (TraceProbingCFR)
                        {
                            //TabbedText.WriteLine($"Optimizing {playerBeingOptimized} Iteration {ProbingCFRIterationNum} Actions to here {historyPoint.GetActionsToHereString(Navigation)}");
                            TabbedText.WriteLine(
                                $"Increasing cumulative regret for action {action} by {inverseSamplingProbabilityQ} * {(counterfactualValues[action - 1])} - {summation} = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                        }
                    }
                }
                return summation;
            }
            else
                throw new NotImplementedException();
        }


        public void AbramowiczProbingCFRIteration(int iteration)
        {
            CurrentEpsilonValue = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(
                EvolutionSettings.FirstOpponentEpsilonValue, EvolutionSettings.LastOpponentEpsilonValue, 0.75,
                (double)iteration / (double)EvolutionSettings.TotalProbingCFRIterations);
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
                AbramowiczProbe_WalkTree(historyPoint, playerBeingOptimized, 1.0, randomProducer);
                if (TraceProbingCFR)
                    TabbedText.Tabs--;
            }
        }


        public void DiscountStoredCumulativeRegrets()
        {
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                var playerRegrets = Strategies[p].InformationSetTree;
                playerRegrets.WalkTree(node =>
                {
                    InformationSetNodeTally tally = (InformationSetNodeTally)node.StoredValue;
                    if (tally != null)
                        tally.DiscountStoredCumulativeRegrets();
                });
            }
        }

        private bool IsNormalPhase = true;
        private int BeginningOfLastNormalPhase = 0;

        public unsafe void SolveAbramowiczProbingCFR()
        {
            Stopwatch s = new Stopwatch();
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Abramowicz algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            int numPhases = EvolutionSettings.EpsilonForPhases.Count;
            int iterationsPerPhase = EvolutionSettings.TotalProbingCFRIterations / numPhases;
            int extraIterationsLastPhase = EvolutionSettings.TotalProbingCFRIterations - numPhases * iterationsPerPhase;
            ProbingCFRIterationNum = 0;
            for (int phase = 0; phase < numPhases; phase++)
            {
                Debug.WriteLine($"Starting phase {phase} about to do iteration {ProbingCFRIterationNum}");
                CurrentEpsilonValue = EvolutionSettings.EpsilonForPhases[phase];
                IsNormalPhase = CurrentEpsilonValue == 0;
                if (IsNormalPhase)
                {
                    BeginningOfLastNormalPhase = ProbingCFRIterationNum;
                    if (EvolutionSettings.RemoveOldRegrets)
                    {
                        if (phase != 0)
                        {
                            Debug.WriteLine($"Removing old regrets");
                            //Debug.WriteLine($"Before");
                            //PrintInformationSets();
                            DiscountStoredCumulativeRegrets(); // discounts so that the highest absolute value of a cumulative regret is 1.0
                            //Debug.WriteLine($"After");
                            //PrintInformationSets();
                        }
                        // RecordCurrentRegrets();
                    }
                }
                int iterationsThisPhase = phase == numPhases - 1
                    ? iterationsPerPhase + extraIterationsLastPhase
                    : iterationsPerPhase;
                for (int iteration = 0;
                    iteration < iterationsThisPhase;
                    iteration++)
                {
                    s.Start();
                    AbramowiczProbingCFRIteration(ProbingCFRIterationNum);
                    s.Stop();
                    GenerateReports(ProbingCFRIterationNum,
                        () =>
                            $"Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
                    ProbingCFRIterationNum++;
                }
            }
        }
    }
}