using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new DeepCFR(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        /// <summary>
        /// Performs an iteration of Deep counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public double DeepCFRIterationForPlayer(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                return finalUtilities.Utilities[playerBeingOptimized];
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
                return DeepCFR_ChanceNode(in historyPoint, playerBeingOptimized, piValues);
            }
            else
                return DeepCFR_DecisionNode(in historyPoint, playerBeingOptimized, piValues);
        }

        private double DeepCFR_DecisionNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues)
        {
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            debug; // we don't need the game state -- we need the information set. GetGameState -> GetGameStatePrerecorded. But that adds the game to the information set tree, which is not what we want. Is there some way to always get a game state by continuously playing the underlying game? And ideally, we wouldn't replay it from the beginning at every decision point. But we still need the applicable InformationSetNode. Or do we? We really just need the information set of the current player, the Decision, etc. 
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            Span<double> actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction != null)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                    actionProbabilities, (byte)alwaysDoAction);
            else
            {
                Debug; // we don't really want the regret matching probabilities here. 
                informationSet.GetRegretMatchingProbabilities(actionProbabilities);
            }
            double expectedValue = 0;
            Debug; // here, we need to pick a SINGLE action based on the existing model, so we can continue playing through the tree. this is true whether we are dealing with the traverser or not. 
            byte actionToChoose = ChooseAction(actionProbabilities);
            Debug; // in this algorithm, the pi values don't matter -- all that matters is the action probabilities
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
                expectedValueOfAction[action - 1] = DeepCFRIterationForPlayer(in nextHistoryPoint, playerBeingOptimized, nextPiValues);
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
                    Debug; // this is where we need to do a Gibson probe
                    double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    if (EvolutionSettings.UseStandardDiscounting && EvolutionSettings.DiscountRegrets)
                    {
                        Debug; // we're not really looking to increment cumulative regret -- we're looking to record it. 
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    }
                    else
                        informationSet.IncrementCumulativeRegret(action, inversePi * regret);
                    double contributionToAverageStrategy = EvolutionSettings.UseStandardDiscounting ? pi * actionProbabilities[action - 1] : pi * actionProbabilities[action - 1];
                    Debug; // we don't want to increment cumulative strategy, either.
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

        private double DeepCFR_ChanceNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues)
        {
            Span<double> equalProbabilityNextPiValues = stackalloc double[MaxNumMainPlayers];
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            bool equalProbabilities = chanceNode.AllProbabilitiesEqual();
            if (equalProbabilities) // can set next probabilities once for all actions
                GetNextPiValues(piValues, playerBeingOptimized, chanceNode.GetActionProbability(1), true,
                    equalProbabilityNextPiValues);
            else
                equalProbabilityNextPiValues = null;
            double expectedValue = 0;

            for (byte action = 1; action < (byte)numPossibleActions + 1; action++)
            {
                double probabilityAdjustedExpectedValueParticularAction =
                    DeepCFR_ChanceNode_NextAction(in historyPoint, playerBeingOptimized, piValues,
                        chanceNode, equalProbabilityNextPiValues, expectedValue, action);
                Interlocking.Add(ref expectedValue, probabilityAdjustedExpectedValueParticularAction);
            }

            return expectedValue;
        }

        private double DeepCFR_ChanceNode_NextAction(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, ChanceNode chanceNode, Span<double> equalProbabilityNextPiValues,
            double expectedValue, byte action)
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
                DeepCFRIterationForPlayer(in nextHistoryPoint, playerBeingOptimized, nextPiValues);
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
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly)
                throw new Exception("Only play underlying game is supported.");

            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 0; iteration < EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await DeepCFRIteration(iteration);
                reportCollection.Add(result);
            }
            return reportCollection;
        }
        private async Task<ReportCollection> DeepCFRIteration(int iteration)
        {
            Status.IterationNumDouble = iteration;

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            ActionStrategy = ActionStrategies.RegretMatching;
            DeepCFR_OptimizeEachPlayer(iteration, lastUtilities);

            var result = await GenerateReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration + 1.0)))}");
            reportCollection.Add(result);
            return reportCollection;
        }

        private void DeepCFR_OptimizeEachPlayer(int iteration, double[] lastUtilities)
        {
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                Span<double> initialPiValues = stackalloc double[MaxNumMainPlayers];
                GetInitialPiValues(initialPiValues);
                if (TraceCFR)
                    TabbedText.WriteLine($"{GameDefinition.OptionSetName} Iteration {iteration} Player {playerBeingOptimized}");
                StrategiesDeveloperStopwatch.Start();

                GameProgress gameProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
                Game game = GameDefinition.GameFactory.CreateNewGame();
                game.PlaySetup(Strategies, gameProgress, GameDefinition, false, true);

                lastUtilities[playerBeingOptimized] =
                    DeepCFRIterationForPlayer(in historyPoint, playerBeingOptimized, initialPiValues);
                StrategiesDeveloperStopwatch.Stop();
            }
        }
    }
}