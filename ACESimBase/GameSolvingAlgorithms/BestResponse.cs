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
            NumGameStates = 0;
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
                    opponentsActionStrategy, 0);
                if (TraceGEBR)
                    TabbedText.Tabs--;
            }
            return bestResponseUtility;
        }

        int NumGameStates = 0;

        public void GEBRPass1_ResetData(ref HistoryPoint historyPoint, byte playerIndex, byte depth,
            HashSet<byte> depthOfPlayerDecisions)
        {
            NumGameStates++;
            if (historyPoint.IsComplete(Navigation))
                return;
            else
            {
                byte numPossibleActionsToExplore;
                IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
                Decision decision;
                byte decisionIndex;
                if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
                {
                    numPossibleActionsToExplore = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
                    if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                        numPossibleActionsToExplore = 1;
                    decision = chanceNodeSettings.Decision;
                    decisionIndex = chanceNodeSettings.DecisionIndex;
                }
                else if (gameStateForCurrentPlayer is InformationSetNodeTally informationSet)
                {
                    byte decisionNum = informationSet.DecisionIndex;
                    numPossibleActionsToExplore = NumPossibleActionsAtDecision(decisionNum);
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
                for (byte action = 1; action <= numPossibleActionsToExplore; action++)
                {
                    var nextHistory = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                    GEBRPass1_ResetData(ref nextHistory, playerIndex, (byte) (depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar,
            double inversePi, ActionStrategies opponentsActionStrategy, int nondistributedActions)
        {
            if (historyPoint.IsComplete(Navigation))
                return GetUtilityFromTerminalHistory(ref historyPoint, playerIndex);
            else
            {
                IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
                if (gameStateForCurrentPlayer is ChanceNodeSettings)
                    return GEBRPass2_ChanceNode(ref historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                        opponentsActionStrategy, nondistributedActions);
            }
            return GEBRPass2_DecisionNode(ref historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                opponentsActionStrategy, nondistributedActions);
        }

        private unsafe double GEBRPass2_DecisionNode(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget,
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy, int nondistributedActions)
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
                              informationSet.LastBestResponseAction;
                if (action == 0)
                    return 0; // This may happen if using regret matching for opponent's strategy after evolving it with hedge. It would be a problem if using VanillaHedging.

                int nondistributedActionsNext = nondistributedActions;
                if (informationSet.Decision.NondistributedDecision)
                    nondistributedActionsNext += action * informationSet.Decision.NondistributedDecisionMultiplier;
                double expectedValue;
                if (historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                {
                    historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    expectedValue = GEBRPass2(ref historyPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy, nondistributedActionsNext);
                    GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateForCurrentPlayer);
                }
                else
                {
                    expectedValue = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, inversePi, nondistributedActionsNext);
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
                else switch (opponentsActionStrategy)
                    {
                        case ActionStrategies.RegretMatching:
                            informationSet.GetRegretMatchingProbabilities(actionProbabilities);
                            break;
                        case ActionStrategies.AverageStrategy:
                            informationSet.GetAverageStrategies(actionProbabilities);
                            break;
                        case ActionStrategies.BestResponse:
                            throw new NotSupportedException();
                        case ActionStrategies.RegretMatchingWithPruning:
                            informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                            break;
                        case ActionStrategies.NormalizedHedge:
                            informationSet.GetNormalizedHedgeProbabilities(actionProbabilities);
                            break;
                        case ActionStrategies.Hedge:
                            informationSet.GetHedgeProbabilities(actionProbabilities);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                double expectedValueSum = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    int nondistributedActionsNext = nondistributedActions;
                    if (informationSet.Decision.NondistributedDecision)
                        nondistributedActionsNext += action * informationSet.Decision.NondistributedDecisionMultiplier;
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
                            depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy, nondistributedActionsNext);
                        GameDefinition.ReverseDecision(informationSet.Decision, ref historyPoint, gameStateForCurrentPlayer);
                    }
                    else
                        expectedValue = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, nextInversePi, nondistributedActionsNext);
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
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy, int nondistributedActions)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(ref historyPoint);
            ChanceNodeSettings chanceNodeSettings = (ChanceNodeSettings) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNodeSettings.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                numPossibleActionsToExplore = 1;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
            {
                TabbedText.WriteLine(
                    $"Num chance actions {numPossibleActionsToExplore} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionIndex].Name}");
            }
            double expectedValueSum = 0;
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.WriteLine(
                        $"chance action {action} for decision {chanceNodeSettings.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNodeSettings.DecisionIndex].Name} ... ");
                double probability = chanceNodeSettings.GetActionProbability(action, nondistributedActions);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNodeSettings.DecisionIndex))
                    TabbedText.Tabs++;
                int nondistributedActionsNext = nondistributedActions;
                if (chanceNodeSettings.Decision.NondistributedDecision)
                    nondistributedActionsNext += action * chanceNodeSettings.Decision.NondistributedDecisionMultiplier;
                if (EvolutionSettings.DistributeChanceDecisions && chanceNodeSettings.Decision.DistributedDecision)
                    probability = 1.0;
                double valueBelow;
                if (historyPoint.BranchingIsReversible(Navigation, chanceNodeSettings.Decision))
                {
                    historyPoint.SwitchToBranch(Navigation, action, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex);
                    valueBelow = GEBRPass2(ref historyPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy, nondistributedActionsNext);
                    GameDefinition.ReverseDecision(chanceNodeSettings.Decision, ref historyPoint, gameStateForCurrentPlayer);
                }
                else
                    valueBelow = GEBRPass2_RecurseNotReversible(ref historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, chanceNodeSettings.Decision, chanceNodeSettings.DecisionIndex, action, inversePi * probability, nondistributedActionsNext);
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


        private unsafe double GEBRPass2_RecurseNotReversible(ref HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, ActionStrategies opponentsActionStrategy, Decision decision, byte decisionIndex, byte action, double nextInversePi, int nondistributedActions)
        {
            double expectedValue;
            {
                var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                expectedValue = GEBRPass2(ref nextHistoryPoint, playerIndex,
                    depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy, nondistributedActions);
            }

            return expectedValue;
        }
    }
}