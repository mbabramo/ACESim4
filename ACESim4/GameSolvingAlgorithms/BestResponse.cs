using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public partial class CounterfactualRegretMaximization
    {
        private bool TraceGEBR = false;
        private List<byte> TraceGEBR_SkipDecisions = new List<byte>() { };

        // DEBUG TODO: Accelerated best response
        // Preparation. Each information set node tally should contain the information set of the player. In addition, for each decision, we should have a list of all information sets for that player.
        // 1. If we are optimizing player X, identify all decisions by player X. 
        // 2. Play from start of game, to X's first decision. Whenever we get to an X information set (at that first decision), create a string with information on the information set of all players besides X, including the resolution player. Use this as a dictionary to the HistoryPoint and the accumulated probability that others (including chance) will play to here. If we get to an information set where there is already something in the dictionary, then we increase this probability. Note that we are using a single HistoryPoint to represent a number of different histories, but all of these histories reflect the same information sets for other players, so all of these information sets will, given the same subsequent actions, lead to the same outcome.
        // 3. Play from X's first decision to X's second decision. That is, for each information set in X's first decision, play beginning with each HistoryPoint in the dictionary, starting with the accumulated probability specified. Do the same thing for X's second decision that we did for X's first decision. The time savings here is that we don't need need to go back to the beginning of the game.
        // 4. Continue until we get to X's last decision. Then, we play from X's last decision to the end of the game. We can determine X's best response at this point by probability weighting X's utilities. Record the best response and the corresponding utility.
        // 5. Play from X's second-to-last to last decision. Again, we can determine X's best response and expected utility at this point by probability weighting X's utilities.
        // 6. Continue backwards until we get to X's first decision and have determined the best action for each information set in this first decision. We now know the expected utility for each information set, plus we can aggregate all of the probabilities leading to this decision to determine the expected utility for X. The best response calculation is thus complete.
        // 7. Clear all information. Note that there may be some additional or fewer HistoryPoints at an information set next time, because the opponent might play actions not previously played. (This is less likely, however, if we are using average strategies.)

        /// <summary>
        /// This calculates the best response for a player, and it also sets up the information set so that we can then play that best response strategy.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        public double CalculateBestResponse(byte playerIndex, ActionStrategies opponentsActionStrategy)
        {
            HashSet<byte> depthsOfPlayerDecisions = new HashSet<byte>();
            var startHistoryPoint = GetStartOfGameHistoryPoint();
            GEBRPass1(ref startHistoryPoint, playerIndex, 1,
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

        public void GEBRPass1(ref HistoryPoint historyPoint, byte playerIndex, byte depth,
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
                        if (!depthOfPlayerDecisions.Contains(depth))
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
                    GEBRPass1(ref nextHistory, playerIndex, (byte) (depth + 1), depthOfPlayerDecisions);
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

                var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                double expectedValue = GEBRPass2(ref nextHistoryPoint, playerIndex, depthToTarget,
                    (byte) (depthSoFar + 1), inversePi, opponentsActionStrategy);
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
                    var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    double expectedValue = GEBRPass2(ref nextHistoryPoint, playerIndex,
                        depthToTarget, (byte) (depthSoFar + 1), nextInversePi, opponentsActionStrategy);
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
                var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                var valueBelow = GEBRPass2(ref nextHistoryPoint, playerIndex, depthToTarget,
                    (byte) (depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
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
    }
}