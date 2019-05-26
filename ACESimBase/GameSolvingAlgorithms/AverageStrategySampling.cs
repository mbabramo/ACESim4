using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {

        double CurrentEpsilonValue; // set in algorithm.
        //double epsilon = 0.05, beta = 1000000, tau = 1000; // note that beta will keep sampling even at first, but becomes less important later on. Epsilon ensures some exploration, and larger tau weights things later toward low-probability strategies
        double avgss_epsilon = 0.05, avgss_beta = 100, avgss_tau = 1; // note that beta will keep sampling even at first, but becomes less important later on. Epsilon ensures some exploration, and larger tau weights things later toward low-probability strategies

        public unsafe double AverageStrategySampling_WalkTree(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double samplingProbabilityQ, Decision nextDecision, byte nextDecisionIndex)
        {
            if (TraceCFR)
                TabbedText.WriteLine($"WalkTree sampling probability {samplingProbabilityQ}");
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            byte sampledAction = 0;
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            HistoryPoint nextHistoryPoint;
            byte numPossibleActions;
            switch (gameStateType)
            {
                case GameStateTypeEnum.FinalUtilities:
                    FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode) gameStateForCurrentPlayer;
                    var utility = finalUtilities.Utilities[playerBeingOptimized];
                    if (TraceCFR)
                        TabbedText.WriteLine($"Utility returned {utility}");
                    return utility / samplingProbabilityQ;
                case GameStateTypeEnum.Chance:
                    ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
                    numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
                    sampledAction = chanceNode.SampleAction(numPossibleActions, RandomGenerator.NextDouble());
                    if (TraceCFR)
                        TabbedText.WriteLine(
                            $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} for chance decision {chanceNode.DecisionIndex}");
                    nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, chanceNode.Decision, chanceNode.DecisionIndex);
                    if (TraceCFR)
                        TabbedText.Tabs++;
                    double walkTreeValue =
                        AverageStrategySampling_WalkTree(ref nextHistoryPoint, playerBeingOptimized, samplingProbabilityQ, chanceNode.Decision, chanceNode.DecisionIndex);
                    if (TraceCFR)
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine($"Returning walk tree result {walkTreeValue}");
                    }
                    return walkTreeValue;
                case GameStateTypeEnum.InformationSet:
                    InformationSetNode informationSet = (InformationSetNode) gameStateForCurrentPlayer;
                    numPossibleActions = NumPossibleActionsAtDecision(informationSet.DecisionIndex);
                    double* sigma_regretMatchedActionProbabilities = stackalloc double[numPossibleActions];
                    // the following use of epsilon-on-policy for early iterations of opponent's strategy is a deviation from Gibson.
                    byte playerAtPoint = informationSet.PlayerIndex;
                    if (playerAtPoint != playerBeingOptimized && EvolutionSettings.UseEpsilonOnPolicyForOpponent &&
                        AvgStrategySamplingCFRIterationNum <= EvolutionSettings.LastOpponentEpsilonIteration)
                        informationSet.GetEpsilonAdjustedRegretMatchingProbabilities(
                            sigma_regretMatchedActionProbabilities, CurrentEpsilonValue);
                    else
                        informationSet.GetRegretMatchingProbabilities(sigma_regretMatchedActionProbabilities);
                    if (playerAtPoint != playerBeingOptimized)
                    {
                        for (byte action = 1; action <= numPossibleActions; action++)
                        {
                            double cumulativeStrategyIncrement =
                                sigma_regretMatchedActionProbabilities[action - 1] / samplingProbabilityQ;
                            if (EvolutionSettings.ParallelOptimization)
                                informationSet.IncrementCumulativeStrategy_Parallel(action,
                                    cumulativeStrategyIncrement);
                            else
                                informationSet.IncrementCumulativeStrategy(action, cumulativeStrategyIncrement);
                            if (TraceCFR)
                                TabbedText.WriteLine(
                                    $"Incrementing cumulative strategy for {action} by {cumulativeStrategyIncrement} to {informationSet.GetCumulativeStrategy(action)}");
                        }
                        sampledAction = SampleAction(sigma_regretMatchedActionProbabilities, numPossibleActions,
                            RandomGenerator.NextDouble());
                        if (TraceCFR)
                            TabbedText.WriteLine(
                                $"{sampledAction}: Sampled action {sampledAction} of {numPossibleActions} player {playerAtPoint} decision {informationSet.DecisionIndex} with regret-matched prob {sigma_regretMatchedActionProbabilities[sampledAction - 1]}");
                        nextHistoryPoint = historyPoint.GetBranch(Navigation, sampledAction, informationSet.Decision, informationSet.DecisionIndex);
                        if (TraceCFR)
                            TabbedText.Tabs++;
                        double walkTreeValue2 =
                            AverageStrategySampling_WalkTree(ref nextHistoryPoint, playerBeingOptimized,
                                samplingProbabilityQ, informationSet.Decision, informationSet.DecisionIndex);
                        if (TraceCFR)
                        {
                            TabbedText.Tabs--;
                            TabbedText.WriteLine($"Returning walk tree result {walkTreeValue2}");
                        }
                        return walkTreeValue2;
                    }
                    double counterfactualSummation = 0;
                    // player being optimized is player at this information set
                    double sumCumulativeStrategies = 0;
                    double* cumulativeStrategies = stackalloc double[numPossibleActions];
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeStrategy = informationSet.GetCumulativeStrategy(action);
                        cumulativeStrategies[action - 1] = cumulativeStrategy;
                        sumCumulativeStrategies += cumulativeStrategy;
                    }
                    double* counterfactualValues = stackalloc double[numPossibleActions];
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        // Note that we may sample multiple actions here.
                        double rho = Math.Max(avgss_epsilon,
                            (avgss_beta + avgss_tau * cumulativeStrategies[action - 1]) / (avgss_beta + sumCumulativeStrategies));
                        double rnd = RandomGenerator.NextDouble();
                        bool explore = rnd < rho;
                        if (TraceCFR)
                        {
                            TabbedText.WriteLine(
                                $"action {action}: {(explore ? "Explore" : "Do not explore")} rnd: {rnd} rho: {rho}");
                        }
                        if (explore)
                        {
                            if (TraceCFR)
                                TabbedText.Tabs++;
                            nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                            counterfactualValues[action - 1] = AverageStrategySampling_WalkTree(ref nextHistoryPoint,
                                playerBeingOptimized, samplingProbabilityQ * Math.Min(1.0, rho), informationSet.Decision, informationSet.DecisionIndex);
                            counterfactualSummation +=
                                sigma_regretMatchedActionProbabilities[action - 1] *
                                counterfactualValues[action - 1];
                            if (TraceCFR)
                                TabbedText.Tabs--;
                        }
                        else
                            counterfactualValues[action - 1] = 0;
                    }
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        double cumulativeRegretIncrement =
                            counterfactualValues[action - 1] - counterfactualSummation;
                        if (EvolutionSettings.ParallelOptimization)
                            informationSet.IncrementCumulativeRegret_Parallel(action, cumulativeRegretIncrement, false);
                        else
                            informationSet.IncrementCumulativeRegret(action, cumulativeRegretIncrement, false);
                        if (TraceCFR)
                        {
                            // v(a) is set to 0 for many of the a E A(I).The key to understanding this is that we're multiplying the utilities we get back by 1/q. So if there is a 1/3 probability of sampling, then we multiply the utility by 3. So, when we don't sample, we're adding 0 to the regrets; and when we sample, we're adding 3 * counterfactual value.Thus, v(a) is an unbiased predictor of value.Meanwhile, we're always subtracting the regret-matched probability-adjusted counterfactual values. 
                            TabbedText.WriteLine(
                                $"Increasing cumulative regret for action {action} by {(counterfactualValues[action - 1])} - {counterfactualSummation} = {cumulativeRegretIncrement} to {informationSet.GetCumulativeRegret(action)}");
                        }
                    }
                    if (TraceCFR)
                    {
                        TabbedText.WriteLine($"Returning {counterfactualSummation}");
                    }
                    return counterfactualSummation;
                default:
                    throw new NotImplementedException();
            }
        }

        private long NumberAverageStrategySamplingExplorations = 0;

        public void AvgStrategySamplingCFRIteration(int iteration)
        {
            CurrentEpsilonValue = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(
                EvolutionSettings.FirstOpponentEpsilonValue, EvolutionSettings.LastOpponentEpsilonValue, 0.75,
                (double) iteration / (double) EvolutionSettings.TotalAvgStrategySamplingCFRIterations);
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                if (ShouldEstimateImprovementOverTime)
                    PrepareForImprovementOverTimeEstimation(playerBeingOptimized);
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                if (TraceCFR)
                {
                    TabbedText.WriteLine($"Optimize player {playerBeingOptimized}");
                    TabbedText.Tabs++;
                }
                AverageStrategySampling_WalkTree(ref historyPoint, playerBeingOptimized, 1.0, GameDefinition.DecisionsExecutionOrder[0], 0);
                if (TraceCFR)
                    TabbedText.Tabs--;
                if (ShouldEstimateImprovementOverTime)
                    UpdateImprovementOverTimeEstimation(playerBeingOptimized, iteration);
            }
        }

        private int AvgStrategySamplingCFRIterationNum;

        public async Task SolveAvgStrategySamplingCFR()
        {
            if (NumNonChancePlayers > 2)
                throw new Exception(
                    "Internal error. Must implement extra code from Gibson algorithm 2 for more than 2 players.");
            ActionStrategy = ActionStrategies.RegretMatching;
            AvgStrategySamplingCFRIterationNum = -1;
            int reportingGroupSize = EvolutionSettings.ReportEveryNIterations ??
                                     EvolutionSettings.TotalAvgStrategySamplingCFRIterations;
            if (ShouldEstimateImprovementOverTime)
                InitializeImprovementOverTimeEstimation();
            Stopwatch s = new Stopwatch();
            for (int iterationGrouper = 0;
                iterationGrouper < EvolutionSettings.TotalAvgStrategySamplingCFRIterations;
                iterationGrouper += reportingGroupSize)
            {
                if (iterationGrouper == 0)
                    await GenerateReports(0, () => $"Iteration 0");
                s.Reset();
                s.Start();
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, iterationGrouper,
                    iterationGrouper + reportingGroupSize, i =>
                    {
                        Interlocked.Increment(ref AvgStrategySamplingCFRIterationNum);
                        AvgStrategySamplingCFRIteration(AvgStrategySamplingCFRIterationNum);
                    });
                s.Stop();
                await GenerateReports(iterationGrouper + reportingGroupSize,
                    () =>
                        $"Iteration {iterationGrouper + reportingGroupSize} Milliseconds per iteration {((s.ElapsedMilliseconds / ((double) reportingGroupSize)))}");
            }
            //for (AvgStrategySamplingCFRIterationNum = 0; AvgStrategySamplingCFRIterationNum < TotalAvgStrategySamplingCFRIterations; AvgStrategySamplingCFRIterationNum++)
            //{
            //    AvgStrategySamplingCFRIteration(AvgStrategySamplingCFRIterationNum);
            //}
        }
    }
}