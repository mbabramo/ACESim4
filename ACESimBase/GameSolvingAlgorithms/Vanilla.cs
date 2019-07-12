using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class VanillaCFR : CounterfactualRegretMinimization
    {

        public VanillaCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new VanillaCFR(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public unsafe double VanillaCFRIterationForPlayer(ref HistoryPoint historyPoint, byte playerBeingOptimized, double* piValues,
            bool usePruning)
        {
            if (usePruning && ShouldPruneIfPruning(piValues))
                return 0;
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode) gameStateForCurrentPlayer;
                return finalUtilities.Utilities[playerBeingOptimized];
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
                return VanillaCFR_ChanceNode(ref historyPoint, playerBeingOptimized, piValues, usePruning);
            }
            else
                return VanillaCFR_DecisionNode(ref historyPoint, playerBeingOptimized, piValues, usePruning);
        }

        private unsafe bool ShouldPruneIfPruning(double* piValues)
        {
            // If we are pruning, then we do prune when the probability of getting to this path is 0.
            // But that doesn't mean that we should prune. The results from zero reach paths can 
            // still matter.
            bool allZero = true;
            for (int i = 0; i < NumNonChancePlayers; i++)
                if (*(piValues + i) != 0)
                {
                    allZero = false;
                    break;
                }
            if (allZero)
                return true;
            return false;
        }

        private unsafe double VanillaCFR_DecisionNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning)
        {
            double* nextPiValues = stackalloc double[MaxNumMainPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNode) gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            double* actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction != null)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                    actionProbabilities, (byte) alwaysDoAction);
            else
            {
                if (usePruning)
                    informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                else
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            }
            double* expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
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
                expectedValueOfAction[action - 1] = VanillaCFRIterationForPlayer(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceCFR)
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... action {action} expected value {expectedValueOfAction[action - 1]} cum expected value {expectedValue}");
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    if (EvolutionSettings.UseDiscounting && EvolutionSettings.DiscountRegrets)
                    {
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret * (regret > 0 ? PositiveRegretsAdjustment : NegativeRegretsAdjustment));
                    }
                    else
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    double contributionToAverageStrategy = EvolutionSettings.UseDiscounting ? pi * actionProbabilities[action - 1] * AverageStrategyAdjustment : pi * actionProbabilities[action - 1];
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

        private unsafe double VanillaCFR_ChanceNode(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, bool usePruning)
        {
            double* equalProbabilityNextPiValues = stackalloc double[MaxNumMainPlayers];
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            bool equalProbabilities = chanceNode.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNode.GetActionProbability(1), true,
                    equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use VanillaCFR much.
            Parallelizer.GoByte(EvolutionSettings.ParallelOptimization, EvolutionSettings.MaxParallelDepth, 1,
                (byte) (numPossibleActions + 1),
                action =>
                {
                    //double* piValuesToPass = stackalloc double[MaxNumPlayers];
                    //for (int i = 0; i < MaxNumPlayers; i++)
                    //    *(piValuesToPass + i) = *(piValues + i);
                    //double* equalProbabilityPiValuesToPass = stackalloc double[MaxNumPlayers];
                    //if (equalProbabilityNextPiValues == null)
                    //    equalProbabilityPiValuesToPass = null;
                    //else
                    //    for (int i = 0; i < MaxNumPlayers; i++)
                    //        *(equalProbabilityPiValuesToPass + i) = *(equalProbabilityNextPiValues + i);
                    // TODO -- we could optimize this by (a) setting to a different method; and (b) creating an alternative action to use when not running in parallel, where that action would not copy the history point.
                    var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                    double probabilityAdjustedExpectedValueParticularAction =
                        VanillaCFR_ChanceNode_NextAction(ref historyPointCopy2, playerBeingOptimized, piValues,
                            chanceNode, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    Interlocking.Add(ref expectedValue, probabilityAdjustedExpectedValueParticularAction);
                });

            return expectedValue;
        }

        private unsafe double VanillaCFR_ChanceNode_NextAction(ref HistoryPoint historyPoint, byte playerBeingOptimized,
            double* piValues, ChanceNode chanceNode, double* equalProbabilityNextPiValues,
            double expectedValue, byte action, bool usePruning)
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
                GetNextPiValues(piValues, playerBeingOptimized, chanceNode.GetActionProbability(action), true,
                    nextPiValues);
            double actionProbability = chanceNode.GetActionProbability(action);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.WriteLine(
                    $"Chance decisionNum {chanceNode.DecisionByteCode} action {action} probability {actionProbability} ...");
                TabbedText.Tabs++;
            }
            double expectedValueParticularAction =
                VanillaCFRIterationForPlayer(ref nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
            var probabilityAdjustedExpectedValueParticularAction = actionProbability * expectedValueParticularAction;
            if (TraceCFR)
            {
                TabbedText.Tabs--;
                TabbedText.WriteLine(
                    $"... action {action} expected value {expectedValueParticularAction} cum expected value {expectedValue}");
            }

            return probabilityAdjustedExpectedValueParticularAction;
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 0; iteration < EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await VanillaCFRIteration(iteration);
                reportCollection.Add(result);
            }
            return reportCollection;
        }

        double PositiveRegretsAdjustment, NegativeRegretsAdjustment, AverageStrategyAdjustment, AverageStrategyAdjustmentAsPctOfMax;
        private async Task<ReportCollection> VanillaCFRIteration(int iteration)
        {
            IterationNumDouble = iteration;
            SetDiscountingAdjustments();

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            bool usePruning = false; // iteration >= 100;
            ActionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
            VanillaCFR_OptimizeEachPlayer(iteration, lastUtilities, usePruning);

            var result = await GenerateReports(iteration,
                () =>
                    $"Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration + 1.0)))}");
            reportCollection.Add(result);
            return reportCollection;
        }

        private void SetDiscountingAdjustments()
        {
            double positivePower = Math.Pow(IterationNumDouble, EvolutionSettings.Discounting_Alpha);
            double negativePower = Math.Pow(IterationNumDouble, EvolutionSettings.Discounting_Beta);
            PositiveRegretsAdjustment = positivePower / (positivePower + 1.0);
            NegativeRegretsAdjustment = negativePower / (negativePower + 1.0);
            AverageStrategyAdjustment = EvolutionSettings.Discounting_Gamma_ForIteration((int)IterationNumDouble);
            AverageStrategyAdjustmentAsPctOfMax = EvolutionSettings.Discounting_Gamma_AsPctOfMax((int)IterationNumDouble);
            if (AverageStrategyAdjustment < 1E-100)
                AverageStrategyAdjustment = 1E-100;
        }

        private unsafe void VanillaCFR_OptimizeEachPlayer(int iteration, double[] lastUtilities, bool usePruning)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                double* initialPiValues = stackalloc double[MaxNumMainPlayers];
                GetInitialPiValues(initialPiValues);
                if (TraceCFR)
                    TabbedText.WriteLine($"Iteration {iteration} Player {playerBeingOptimized}");
                StrategiesDeveloperStopwatch.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                lastUtilities[playerBeingOptimized] =
                    VanillaCFRIterationForPlayer(ref historyPoint, playerBeingOptimized, initialPiValues, usePruning);
                StrategiesDeveloperStopwatch.Stop();
            }
        }
    }
}