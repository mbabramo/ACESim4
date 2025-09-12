using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;
using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public abstract partial class StrategiesDeveloperBase : IStrategiesDeveloper
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
            GEBRPass1_ResetData(in startHistoryPoint, playerIndex, 1,
                depthsOfPlayerDecisions); // setup counting first decision as depth 1
            List<byte> depthsOrdered = depthsOfPlayerDecisions.OrderByDescending(x => x).ToList();
            depthsOrdered.Add(0); // last depth to play should return outcome
            double bestResponseUtility = 0;
            foreach (byte depthToTarget in depthsOrdered)
            {
                if (TraceGEBR)
                {
                    TabbedText.WriteLine($"Optimizing {playerIndex} depthToTarget {depthToTarget}: ");
                    TabbedText.TabIndent();
                }
                var startHistoryPoint2 = GetStartOfGameHistoryPoint();
                bestResponseUtility = GEBRPass2(in startHistoryPoint2, playerIndex, depthToTarget, 1, 1.0,
                    opponentsActionStrategy);
                if (TraceGEBR)
                    TabbedText.TabUnindent();
            }
            return bestResponseUtility;
        }

        int NumGameStates = 0;

        public void GEBRPass1_ResetData(in HistoryPoint historyPoint, byte playerIndex, byte depth,
            HashSet<byte> depthOfPlayerDecisions)
        {
            NumGameStates++;
            if (historyPoint.IsComplete(Navigation))
                return;
            else
            {
                byte numPossibleActionsToExplore;
                IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
                Decision decision;
                byte decisionIndex;
                if (gameStateForCurrentPlayer is ChanceNode chanceNode)
                {
                    numPossibleActionsToExplore = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
                    decision = chanceNode.Decision;
                    decisionIndex = chanceNode.DecisionIndex;
                }
                else if (gameStateForCurrentPlayer is InformationSetNode informationSet)
                {
                    byte decisionNum = informationSet.DecisionIndex;
                    numPossibleActionsToExplore = NumPossibleActionsAtDecision(decisionNum);
                    byte playerMakingDecision = informationSet.PlayerIndex;
                    if (playerMakingDecision == playerIndex)
                    {
                        depthOfPlayerDecisions.Add(depth);
                        informationSet.ClearBestResponse();
                    }
                    decision = informationSet.Decision;
                    decisionIndex = informationSet.DecisionIndex;
                }
                else
                    throw new Exception("Unexpected game state type.");
                for (byte action = 1; action <= numPossibleActionsToExplore; action++)
                {
                    var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                    GEBRPass1_ResetData(in nextHistoryPoint, playerIndex, (byte) (depth + 1), depthOfPlayerDecisions);
                }
            }
        }

        public double GEBRPass2(in HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar,
            double inversePi, ActionStrategies opponentsActionStrategy)
        {
            if (historyPoint.IsComplete(Navigation))
                return GetUtilityFromTerminalHistory(in historyPoint, playerIndex);
            else
            {
                IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
                if (gameStateForCurrentPlayer is ChanceNode)
                    return GEBRPass2_ChanceNode(in historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                        opponentsActionStrategy);
            }
            return GEBRPass2_DecisionNode(in historyPoint, playerIndex, depthToTarget, depthSoFar, inversePi,
                opponentsActionStrategy);
        }

        private double GEBRPass2_DecisionNode(in HistoryPoint historyPoint, byte playerIndex, byte depthToTarget,
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode) gameStateForCurrentPlayer;
            byte decisionIndex = informationSet.DecisionIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            byte playerMakingDecision = informationSet.PlayerIndex;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
            {
                TabbedText.WriteLine(
                    $"Decision {decisionIndex} {GameDefinition.DecisionsExecutionOrder[decisionIndex].Name} playerMakingDecision {playerMakingDecision} information set {informationSet.InformationSetNodeNumber} inversePi {inversePi} depthSoFar {depthSoFar} depthToTarget {depthToTarget}"); 
            }
            if (playerMakingDecision == playerIndex && depthSoFar > depthToTarget) // case in which depthSoFar == depthToTarget is addressed below
            {
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    TabbedText.TabIndent();
                byte? alwaysAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction;
                byte action;
                if (alwaysAction != null)
                    action = (byte)alwaysAction;
                else
                {
                    if (!informationSet.BestResponseDeterminedFromIncrements)
                        informationSet.DetermineBestResponseAction();
                    action = informationSet.BestResponseAction;
                }
                if (action == 0)
                    return 0; // This may happen if using regret matching for opponent's strategy after evolving it with hedge. It would be a problem if using VanillaHedging.

                double expectedValue;
                if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                {
                    var nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    expectedValue = GEBRPass2(in nextHistoryPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi, opponentsActionStrategy);
                    GameDefinition.ReverseSwitchToBranchEffects(informationSet.Decision, in nextHistoryPoint);
                }
                else
                {
                    expectedValue = GEBRPass2_RecurseNotReversible(in historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, inversePi);
                }
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                {
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine($"Best response action {action} producing expected value {expectedValue}");
                }
                return expectedValue;
            }
            else
            {
                Span<double> actionProbabilities = stackalloc double[numPossibleActions];
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
                            informationSet.GetAverageStrategyProbabilities(actionProbabilities);
                            break;
                        case ActionStrategies.BestResponse:
                            throw new NotSupportedException();
                        case ActionStrategies.RegretMatchingWithPruning:
                            informationSet.GetRegretMatchingProbabilities_WithPruning(actionProbabilities);
                            break;
                        case ActionStrategies.CurrentProbability:
                            informationSet.GetCurrentProbabilities(actionProbabilities, true);
                            break;
                        case ActionStrategies.CorrelatedEquilibrium:
                            informationSet.CalculateAverageStrategyFromCumulative(actionProbabilities); // playing best response against correlated equilibrium is same as playing against average strategies
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                double expectedValueSum = 0;
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double nextInversePi = inversePi;
                    if (playerMakingDecision != playerIndex)
                        nextInversePi *= actionProbabilities[action - 1];
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.WriteLine($"action {action} for playerMakingDecision {playerMakingDecision} probability {actionProbabilities[action - 1]}...");
                        TabbedText.TabIndent();
                    }
                    double expectedValue;
                    if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && historyPoint.BranchingIsReversible(Navigation, informationSet.Decision))
                    {
                        var nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                        expectedValue = GEBRPass2(in nextHistoryPoint, playerIndex,
                            depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
                        GameDefinition.ReverseSwitchToBranchEffects(informationSet.Decision, in nextHistoryPoint);
                    }
                    else
                        expectedValue = GEBRPass2_RecurseNotReversible(in historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, informationSet.Decision, informationSet.DecisionIndex, action, nextInversePi);
                    double product = actionProbabilities[action - 1] * expectedValue;
                    if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                    {
                        TabbedText.TabUnindent();
                        TabbedText.WriteLine(
                            $"... action {action} producing expected value {expectedValue} * probability {actionProbabilities[action - 1]} = product {product}");
                    }
                    if (playerMakingDecision != playerIndex)
                        expectedValueSum += product;
                    else if (playerMakingDecision == playerIndex && depthToTarget == depthSoFar)
                    {
                        // This is the key part -- incrementing best responses. Note that the expected value is NOT here multiplied by this player's probability of playing to the next stage. The overall best response will depend on how often other players play to this point. We'll be calling this for EACH history that leads to this information set.
                        if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(decisionIndex))
                        {
                            TabbedText.WriteLine(
                                $"Incrementing best response for information set {informationSet.InformationSetNodeNumber} for action {action} inversePi {inversePi} expectedValue {expectedValue}");
                        }
                        informationSet.IncrementBestResponse(action, inversePi, expectedValue);
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


        private double GEBRPass2_ChanceNode(in HistoryPoint historyPoint, byte playerIndex, byte depthToTarget,
            byte depthSoFar, double inversePi, ActionStrategies opponentsActionStrategy)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode) gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNode.DecisionIndex))
            {
                TabbedText.WriteLine(
                    $"Num chance actions {numPossibleActionsToExplore} for decision {chanceNode.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNode.DecisionIndex].Name} (inversePi {inversePi})");
            }
            double expectedValueSum = 0;
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                double probability = chanceNode.GetActionProbability(action);
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNode.DecisionIndex))
                    TabbedText.WriteLine(
                        $"chance action {action} for decision {chanceNode.DecisionByteCode} {GameDefinition.DecisionsExecutionOrder[chanceNode.DecisionIndex].Name} (probability {probability}) ... ");
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNode.DecisionIndex))
                    TabbedText.TabIndent();
                double valueBelow;
                if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && historyPoint.BranchingIsReversible(Navigation, chanceNode.Decision))
                {
                    var nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
                    valueBelow = GEBRPass2(in nextHistoryPoint, playerIndex, depthToTarget,
                        (byte)(depthSoFar + 1), inversePi * probability, opponentsActionStrategy);
                    GameDefinition.ReverseSwitchToBranchEffects(chanceNode.Decision, in nextHistoryPoint);
                }
                else
                    valueBelow = GEBRPass2_RecurseNotReversible(in historyPoint, playerIndex, depthToTarget, depthSoFar, opponentsActionStrategy, chanceNode.Decision, chanceNode.DecisionIndex, action, inversePi * probability);
                double expectedValue = probability * valueBelow;
                if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNode.DecisionIndex))
                {
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine(
                        $"... chance action {action} probability {probability} * valueBelow {valueBelow} = expectedValue {expectedValue}");
                }
                expectedValueSum += expectedValue;
            }
            if (TraceGEBR && !TraceGEBR_SkipDecisions.Contains(chanceNode.DecisionIndex))
            {
                TabbedText.WriteLine($"Chance expected value sum {expectedValueSum}");
            }
            return expectedValueSum;
        }


        private double GEBRPass2_RecurseNotReversible(in HistoryPoint historyPoint, byte playerIndex, byte depthToTarget, byte depthSoFar, ActionStrategies opponentsActionStrategy, Decision decision, byte decisionIndex, byte action, double nextInversePi)
        {
            double expectedValue;
            {
                var nextHistoryPoint = historyPoint.GetBranch(Navigation, action, decision, decisionIndex);
                expectedValue = GEBRPass2(in nextHistoryPoint, playerIndex,
                    depthToTarget, (byte)(depthSoFar + 1), nextInversePi, opponentsActionStrategy);
            }

            return expectedValue;
        }
    }
}