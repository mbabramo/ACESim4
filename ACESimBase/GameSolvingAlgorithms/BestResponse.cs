using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {
        private bool TraceGEBR = false;
        private List<byte> TraceGEBR_SkipDecisions = new List<byte>() { };

        

        /// <summary>
        /// This calculates the best response for a player, and it also sets up the information set so that we can then play that best response strategy.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        public double CalculateBestResponse(byte playerIndex, ActionStrategies opponentsActionStrategy)
        {
            HashSet<byte> depthsOfPlayerDecisions = new HashSet<byte>();
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            GEBRPass1_ResetData(ref startHistoryPoint, playerIndex, 1,
                depthsOfPlayerDecisions); // setup counting first decision as depth 1
            List<byte> depthsOrdered = depthsOfPlayerDecisions.OrderByDescending(x => x).ToList();
            depthsOrdered.Add(0); // last depth to play should return outcome
            double bestResponseUtility = 0;
            foreach (byte depthToTarget in depthsOrdered)
            {
                if (TraceGEBR)
                {
                    TabbedText.WriteLine($"Optimizing {playerIndex} depthToTarget {depthToTarget}: ");
                    TabbedText.Tabs++;
                }
                var startHistoryPoint2 = GetStartOfGameHistoryPoint();
                bestResponseUtility = GEBRPass2(ref startHistoryPoint2, playerIndex, depthToTarget, 1, 1.0,
                    opponentsActionStrategy);
                if (TraceGEBR)
                    TabbedText.Tabs--;
            }
            return bestResponseUtility;
        }

        public void GEBRPass1_ResetData(ref HistoryPoint historyPoint, byte playerIndex, byte depth,
            HashSet<byte> depthOfPlayerDecisions)
        {
            if (historyPoint.IsComplete(Navigation))
                return;
            else
            {
                byte numPossibleActions;
                IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
                Decision decision;
                byte decisionIndex;
                if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
                {
                    numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    decision = chanceNodeSettings.Decision;
                    decisionIndex = chanceNodeSettings.DecisionIndex;
                }
                else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
                {
                    byte decisionNum = informationSet.DecisionIndex;
                    numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
                    byte playerMakingDecision = informationSet.PlayerIndex;
                    if (playerMakingDecision == playerIndex)
                    {
                        depthOfPlayerDecisions.Add(depth);
                        informationSet.ResetBestResponseData();
                    }
                    decision = informationSet.Decision;
                    decisionIndex = informationSet.DecisionIndex;
                }
                else
                    throw new Exception("Unexpected game state type.");
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    var nextHistory = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                    GEBRPass1_ResetData(ref nextHistory, playerIndex, (byte) (depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar,
            double inversePi, ActionStrategies opponentsActionStrategy)
        {
            if (historyPoint.IsComplete(Navigation))
                return GetUtilityFromTerminalHistory(ref historyPoint, playerIndex);
            else
            {
                IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
                if (gameStateForCurrentPlayer is ChanceNodeSettings)
                    return GEBRPass2_ChanceNode(ref historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                        opponentsActionStrategy);
            }
            return GEBRPass2_DecisionNode(ref historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                opponentsActionStrategy);
        }

        private unsafe double GEBRPass2_DecisionNode(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget,
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            var informationSet = (InformationSetNodeTally) gameStateForCurrentPlayer;
            byte decisionIndex = informationSet.DecisionIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            byte playerMakingDecision = informationSet.PlayerIndex;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
            {
                TabbedText.WriteLine(
                    $"Decision {decisionIndex} {GameDefinition.DecisionsExecutionOrder[decisionIndex].Name} playerMakingDecision {playerMakingDecision} information set {informationSet.InformationSetNumber} inversePi {inversePi} depthSoFar {depthSoFar} ");
            }
            if (playerMakingDecision == playerIndex && depthSoFar > depthToTarget)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    TabbedText.Tabs++;
                byte action = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ??
                              informationSet.GetBestResponseAction();
                if (action == 0)
                    throw new Exception("Invalid action."); // This may happen if using regret matching for opponent's strategy after evolving it with hedge.
                double expectedValue;
                if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                {
                    historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    expectedValue = GEBRPass2(ref historyPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy);
                    GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateForCurrentPlayer);
                }
                else
                {
                    expectedValue = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, inversePi);
                }
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine($"Best response action {action} producing expected value {expectedValue}");
                }
                return expectedValue;
            }
            else
            {
                double* actionProbabilities = stackalloc double[numPossibleActions];
                byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction;
                if (alwaysDoAction != null)
                    ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                        actionProbabilities, (byte) alwaysDoAction);
                else if (opponentsActionStrategy == ActionStrategies.RegretMatching)
                    informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                else if (opponentsActionStrategy == ActionStrategies.RegretMatchingWithPruning)
                    informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                else if (opponentsActionStrategy == ActionStrategies.AverageStrategy)
                    informationSet.GetAverageStrategies(actionProbabilities);
                else
                    throw new NotImplementedException();
                double expectedValueSum = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double nextInversePi = inversePi;
                    if (playerMakingDecision != playerIndex)
                        nextInversePi *= actionProbabilities[action - 1];
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.WriteLine($"action {action} for playerMakingDecision {playerMakingDecision}...");
                        TabbedText.Tabs++;
                    }
                    double expectedValue;
                    if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                    {
                        historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                        expectedValue = GEBRPass2(ref historyPoint, playerIndex,
                            depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
                        GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateForCurrentPlayer);
                    }
                    else
                        expectedValue = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, nextInversePi);
                    double product = actionProbabilities[action - 1] * expectedValue;
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.Tabs--;
                        TabbedText.WriteLine(
                            $"... action {action} producing expected value {expectedValue} * probability {actionProbabilities[action - 1]} = product {product}");
                    }
                    if (playerMakingDecision != playerIndex)
                        expectedValueSum += product;
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                    {
                        // This is the key part -- incrementing best responses. Note that the expected value is NOT here multiplied by this player's probability of playing to the next stage. The overall best response will depend on how often other players play to this point. We'll be calling this for EACH history that leads to this information set.
                        informationSet.IncrementBestResponse(action, inversePi, expectedValue);
                        if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                        {
                            TabbedText.WriteLine(
                                $"Incrementing best response for information set {informationSet.InformationSetNumber} for action {action} inversePi {inversePi} expectedValue {expectedValue}");
                        }
                    }
                }
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                {
                    if (playerMakingDecision != playerIndex)
                        TabbedText.WriteLine($"Returning from other player node expectedvaluesum {expectedValueSum}");
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                        TabbedText.WriteLine($"Returning 0 (from own decision not yet fully developed)");
                }
                return expectedValueSum;
            }
        }


        private double GEBRPass2_ChanceNode(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget,
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
            {
                TabbedText.WriteLine(
                    $"Num chance actions {numPossibleActions} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionByteCode].Name}");
            }
            double expectedValueSum = 0;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.WriteLine(
                        $"chance action {action} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionIndex].Name} ... ");
                double probability = chanceNodeSettings.GetActionProbability(action);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.Tabs++;
                double valueBelow;
                if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
                {
                    historyPoint.SwitchToBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                    valueBelow = GEBRPass2(ref historyPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
                    GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateForCurrentPlayer);
                }
                else
                    valueBelow = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex, action, inversePi * probability);
                double expectedValue = probability * valueBelow;
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                {
                    TabbedText.Tabs--;
                    TabbedText.WriteLine(
                        $"... chance action {action} probability {probability} * valueBelow {valueBelow} = expectedValue {expectedValue}");
                }
                expectedValueSum += expectedValue;
            }
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
            {
                TabbedText.WriteLine($"Chance expected value sum {expectedValueSum}");
            }
            return expectedValueSum;
        }


        private unsafe double GEBRPass2_RecurseNotReversible(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, ActionStrategies opponentsActionStrategy, Decision decision, byte decisionIndex, byte action, double nextInversePi)
        {
            double expectedValue;
            {
                var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                expectedValue = GEBRPass2(ref nextHistoryPoint, playerIndex,
                    depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
            }

            return expectedValue;
        }
    }
}