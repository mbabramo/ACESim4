using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class GibsonProbing : CounterfactualRegretMinimization
    {

        public GibsonProbing(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GibsonProbing(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public double GibsonProbe_SinglePlayer(in HistoryPoint historyPoint, byte playerBeingOptimized,
            IRandomProducer randomProducer, Decision nextDecision, byte nextDecisionIndex)
        {
            return GibsonProbe(in historyPoint, randomProducer, nextDecision, nextDecisionIndex)[playerBeingOptimized];
        }

        public double[] GibsonProbe(in HistoryPoint historyPoint, IRandomProducer randomProducer, Decision nextDecision, byte nextDecisionIndex)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            //if (TraceCFR)
            //    TabbedText.WriteLine($"Probe optimizing player {playerBeingOptimized}");
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                var utility = finalUtilities.Utilities;
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else
            {
                byte sampledAction = 0;
                Decision decision = null;
                byte decisionIndex = 0;
                if (gameStateType == GameStateTypeEnum.Chance)
                {
                    ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
                    sampledAction = chanceNode.SampleAction(numPossibleActions,
                        randomProducer.GetDoubleAtIndex(chanceNode.DecisionIndex));
                    decisionIndex = chanceNode.DecisionIndex;
                    decision = chanceNode.Decision;
                    if (TraceCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled chance action {sampledAction} of {numPossibleActions} with probability {chanceNode.GetActionProbability(sampledAction)}");
                }
                else if (gameStateType == GameStateTypeEnum.InformationSet)
                {
                    InformationSetNode informationSet = (InformationSetNode)gameStateForCurrentPlayer;
                    byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    Span<double> actionProbabilities = stackalloc double[numPossibleActions];
                    // No epsilon exploration during the probe.
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                    sampledAction = SampleAction(actionProbabilities, numPossibleActions,
                        randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex));
                    decision = informationSet.Decision;
                    decisionIndex = informationSet.DecisionIndex;
                    if (TraceCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {informationSet.PlayerIndex}");
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, decision, decisionIndex);
                if (TraceCFR)
                    TabbedText.TabIndent();
                double[] probeResult = GibsonProbe(in nextHistoryPoint, randomProducer, decision, decisionIndex);
                if (TraceCFR)
                {
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine($"Returning probe result {probeResult}");
                }
                return probeResult;
            }
        }

        public double GibsonProbe_WalkTree(in HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, IRandomProducer randomProducer, Decision nextDecision, byte nextDecisionIndex)
        {
            if (TraceCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            byte sampledAction = 0;
            if (gameStateForCurrentPlayer is FinalUtilitiesNode finalUtilities)
            {
                var utility = finalUtilities.Utilities[playerBeingOptimized];
                if (TraceCFR)
                    TabbedText.WriteLine($"Utility returned {utility}");
                return utility;
            }
            else if (gameStateForCurrentPlayer is ChanceNode chanceNode)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
                sampledAction = chanceNode.SampleAction(numPossibleActions,
                    randomProducer.GetDoubleAtIndex(chanceNode.DecisionIndex));
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNode.DecisionIndex}");
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNode.Decision, chanceNode.DecisionIndex);
                if (TraceCFR)
                    TabbedText.TabIndent();
                double walkTreeValue = GibsonProbe_WalkTree(in nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                    randomProducer, chanceNode.Decision, chanceNode.DecisionIndex);
                if (TraceCFR)
                {
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                }
                return walkTreeValue;
            }
            else if (gameStateForCurrentPlayer is InformationSetNode informationSet)
            {
                byte numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                Span<double> sigmaRegretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
                byte playerAtPoint = informationSet.PlayerIndex;
                double randomDouble = randomProducer.GetDoubleAtIndex(informationSet.DecisionIndex);
                // OTHER PLAYER:
                if (playerAtPoint != playerBeingOptimized)
                {
                    informationSet.GetRegretMatchingProbabilities(sigmaRegretMatchedActionProbabilities);
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeStrategyIncrement =
                            sigmaRegretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                        informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                        if (TraceCFR)
                            TabbedText.WriteLine(
                                $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                    }
                    sampledAction = SampleAction(sigmaRegretMatchedActionProbabilities, numPossibleActions,
                        randomDouble);
                    if (TraceCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
                    if (TraceCFR)
                        TabbedText.TabIndent();
                    double walkTreeValue = GibsonProbe_WalkTree(in nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ,
                        randomProducer, informationSet.Decision, informationSet.DecisionIndex);
                    if (TraceCFR)
                    {
                        TabbedText.TabUnindent();
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                }
                // PLAYER BEING OPTIMIZED:
                Span<double> samplingProbabilities = stackalloc double[numPossibleActions];
                const double epsilonForProbeWalk = 0.5;
                informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(samplingProbabilities, epsilonForProbeWalk);
                sampledAction = SampleAction(samplingProbabilities, numPossibleActions, randomDouble);
                if (TraceCFR)
                    TabbedText.WriteLine(
                        $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigmaRegretMatchedActionProbabilities[sampledAction - 1]}");
                Span<double> counterfactualValues = stackalloc double[numPossibleActions];
                double summation = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    if (action == sampledAction)
                    {
                        if (TraceCFR)
                            TabbedText.WriteLine(
                                $"{action}: Sampling selected action {action} for player {informationSet.PlayerIndex} decision {informationSet.DecisionIndex}");
                        if (TraceCFR)
                            TabbedText.TabIndent();
                        double samplingProbabilityQPrime = samplingProbabilityQ * samplingProbabilities[action - 1];
                        // NOTE: This seems to me to be a problem. This is clearly what Gibson recommends on p. 61 of his thesis (algorithm 3, line 24). From the bottom of p. 60, he at least allows for the possibility that we always sample all of the player's actions. If we do so, however, the score from below will be based on a path that includes epsilon exploration. It's fine that epsilon exploration has occurred up to this point for the purpose of optimizing this information set, because the sampling probability reflects that. But our estimate of the counterfactual value should be played on the assumption that this player does not engage in further epsilon exploration. Otherwise, a player will be optimized to play now on the assumption that the player will play poorly later.
                        counterfactualValues[action - 1] = GibsonProbe_WalkTree(in nextHistoryPoint, playerBeingOptimized,
                            samplingProbabilityQPrime, randomProducer, informationSet.Decision, informationSet.DecisionIndex);
                    }
                    else
                    {
                        if (TraceCFR)
                            TabbedText.WriteLine(
                                $"{action}: GibsonProbing unselected action {action} for player {informationSet.PlayerIndex}decision {informationSet.DecisionIndex}");
                        if (TraceCFR)
                            TabbedText.TabIndent();
                        counterfactualValues[action - 1] =
                            GibsonProbe_SinglePlayer(in nextHistoryPoint, playerBeingOptimized, randomProducer, informationSet.Decision, informationSet.DecisionIndex);
                    }
                    double summationDelta = sigmaRegretMatchedActionProbabilities[action - 1] *
                                            counterfactualValues[action - 1];
                    summation += summationDelta;
                    if (TraceCFR)
                    {
                        TabbedText.TabUnindent();
                        TabbedText.WriteLine(
                            $"Counterfactual value for action {action} is {counterfactualValues[action - 1]} => increment summation by {sigmaRegretMatchedActionProbabilities[action - 1]} * {counterfactualValues[action - 1]} = {summationDelta} to {summation}");
                    }
                }
                double inverseSamplingProbabilityQ = (1.0 / samplingProbabilityQ);
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double cumulativeRegretIncrement = inverseSamplingProbabilityQ *
                                                       (counterfactualValues[action - 1] - summation);
                    informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement);
                    if (TraceCFR)
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

        public void GibsonProbingCFRIteration(int iteration)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                IRandomProducer randomProducer =
                    new ConsistentRandomSequenceProducer(iteration * 1000 + playerBeingOptimized);
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceCFR)
                {
                    TabbedText.WriteLine($"Optimize player {playerBeingOptimized}");
                    TabbedText.TabIndent();
                }
                GibsonProbe_WalkTree(in historyPoint, playerBeingOptimized, 1.0, randomProducer, GameDefinition.DecisionsExecutionOrder[0], 0);
                if (TraceCFR)
                    TabbedText.TabUnindent();
            }
        }

        private int ProbingCFRIterationNum;

        public async override Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
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
                GibsonProbingCFRIteration(ProbingCFRIterationNum);
                s.Stop();
                var result = await GenerateReports(ProbingCFRIterationNum + 1,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {ProbingCFRIterationNum} Overall milliseconds per iteration {((s.ElapsedMilliseconds / ((double)(ProbingCFRIterationNum + 1))))}");
                reportCollection.Add(result);
            }
            return reportCollection;
        }
    }
}