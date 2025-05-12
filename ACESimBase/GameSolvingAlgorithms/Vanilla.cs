using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using ACESimBase.Util.TaskManagement;
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
        public double VanillaCFRIterationForPlayer(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues,
            bool usePruning)
        {
            if (usePruning && ShouldPruneIfPruning(piValues))
                return 0;
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode) gameStateForCurrentPlayer;
                return finalUtilities.Utilities[playerBeingOptimized];
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
                return VanillaCFR_ChanceNode(in historyPoint, playerBeingOptimized, piValues, usePruning);
            }
            else
                return VanillaCFR_DecisionNode(in historyPoint, playerBeingOptimized, piValues, usePruning);
        }

        private bool ShouldPruneIfPruning(Span<double> piValues)
        {
            // If we are pruning, then we do prune when the probability of getting to this path is 0.
            // But that doesn't mean that we should prune. The results from zero reach paths can 
            // still matter.
            bool allZero = true;
            for (int i = 0; i < NumNonChancePlayers; i++)
                if (piValues[i] != 0)
                {
                    allZero = false;
                    break;
                }
            if (allZero)
                return true;
            return false;
        }

        private double VanillaCFR_DecisionNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, bool usePruning)
        {
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode) gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            Span<double> actionProbabilities = stackalloc double[numPossibleActions];
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
            Span<double> expectedValueOfAction = stackalloc double[numPossibleActions];
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
                    TabbedText.TabIndent();
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                expectedValueOfAction[action - 1] = VanillaCFRIterationForPlayer(in nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
                expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                if (TraceCFR)
                {
                    TabbedText.TabUnindent();
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
                    if (EvolutionSettings.UseStandardDiscounting && EvolutionSettings.DiscountRegrets)
                    {
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret * (regret > 0 ? PositiveRegretsAdjustment : NegativeRegretsAdjustment));
                    }
                    else
                    {
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    }
                    double contributionToAverageStrategy = EvolutionSettings.UseStandardDiscounting ? pi * actionProbabilities[action - 1] * AverageStrategyAdjustment : pi * actionProbabilities[action - 1];
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

        private double VanillaCFR_ChanceNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, bool usePruning)
        {
            Span<double> equalProbabilityNextPiValues = stackalloc double[MaxNumMainPlayers];
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            bool equalProbabilities = chanceNode.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNode.GetActionProbability(1), true,
                    equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;
            bool doParallel = EvolutionSettings.ParallelOptimization && Parallelizer.ParallelDepth < EvolutionSettings.MaxParallelDepth;
            if (doParallel)
            {
                var historyPointCopy = historyPoint.ToStorable(); // This is costly but needed given anonymous method below (because ref struct can't be accessed there), so we do this only if really parallelizing.
                var piValues2 = piValues.ToArray();
                var equalProbabilityNextPiValues2 = equalProbabilityNextPiValues.ToArray();
                Parallelizer.GoByte(doParallel, 1,
                    (byte)(numPossibleActions + 1),
                    action =>
                    {
                        var historyPointCopy2 = historyPointCopy.DeepCopyToRefStruct(); // Need to do this because we need a separate copy for each thread
                        double probabilityAdjustedExpectedValueParticularAction =
                            VanillaCFR_ChanceNode_NextAction(in historyPointCopy2, playerBeingOptimized, piValues2,
                                chanceNode, equalProbabilityNextPiValues2, expectedValue, action, usePruning);
                        Interlocking.Add(ref expectedValue, probabilityAdjustedExpectedValueParticularAction);
                    });
            }
            else
            {
                for (byte action = 1; action < (byte) numPossibleActions + 1; action++)
                {
                    double probabilityAdjustedExpectedValueParticularAction =
                        VanillaCFR_ChanceNode_NextAction(in historyPoint, playerBeingOptimized, piValues,
                            chanceNode, equalProbabilityNextPiValues, expectedValue, action, usePruning);
                    Interlocking.Add(ref expectedValue, probabilityAdjustedExpectedValueParticularAction);
                }
            }

            return expectedValue;
        }

        private double VanillaCFR_ChanceNode_NextAction(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, ChanceNode chanceNode, Span<double> equalProbabilityNextPiValues,
            double expectedValue, byte action, bool usePruning)
        {
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            if (equalProbabilityNextPiValues != null)
            {
                for (int i = 0; i < NumNonChancePlayers; i++)
                {
                    nextPiValues[i] = equalProbabilityNextPiValues[i];
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
                TabbedText.TabIndent();
            }
            double expectedValueParticularAction =
                VanillaCFRIterationForPlayer(in nextHistoryPoint, playerBeingOptimized, nextPiValues, usePruning);
            var probabilityAdjustedExpectedValueParticularAction = actionProbability * expectedValueParticularAction;
            if (TraceCFR)
            {
                TabbedText.TabUnindent();
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
            Status.IterationNumDouble = iteration;
            SetDiscountingAdjustments();

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            bool usePruning = false; // iteration >= 100;
            ActionStrategy = usePruning ? ActionStrategies.RegretMatchingWithPruning : ActionStrategies.RegretMatching;
            VanillaCFR_OptimizeEachPlayer(iteration, lastUtilities, usePruning);

#pragma warning disable CA1416
            var result = await ConsiderGeneratingReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration + 1.0)))}");
            reportCollection.Add(result);
            return reportCollection;
        }

        private void SetDiscountingAdjustments()
        {
            double positivePower = Math.Pow(Status.IterationNumDouble, EvolutionSettings.Discounting_Alpha);
            double negativePower = Math.Pow(Status.IterationNumDouble, EvolutionSettings.Discounting_Beta);
            PositiveRegretsAdjustment = positivePower / (positivePower + 1.0);
            NegativeRegretsAdjustment = negativePower / (negativePower + 1.0);
            AverageStrategyAdjustment = EvolutionSettings.Discounting_Gamma_ForIteration((int)Status.IterationNumDouble);
            AverageStrategyAdjustmentAsPctOfMax = EvolutionSettings.Discounting_Gamma_AsPctOfMax((int)Status.IterationNumDouble);
            if (AverageStrategyAdjustment < 1E-100)
                AverageStrategyAdjustment = 1E-100;
        }

        private void VanillaCFR_OptimizeEachPlayer(int iteration, double[] lastUtilities, bool usePruning)
        {
            Span<double> initialPiValues = stackalloc double[MaxNumMainPlayers];
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                GetInitialPiValues(initialPiValues);
                if (TraceCFR)
                    TabbedText.WriteLine($"{GameDefinition.OptionSetName} Iteration {iteration} Player {playerBeingOptimized}");
                StrategiesDeveloperStopwatch.Start();
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                lastUtilities[playerBeingOptimized] =
                    VanillaCFRIterationForPlayer(in historyPoint, playerBeingOptimized, initialPiValues, usePruning);
                StrategiesDeveloperStopwatch.Stop();
            }
        }
    }
}