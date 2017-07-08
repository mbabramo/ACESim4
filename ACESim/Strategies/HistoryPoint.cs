using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct HistoryPoint
    {
        NWayTreeStorage<object> TreePoint;
        GameHistory HistoryToPoint;

        public HistoryPoint(NWayTreeStorage<object> treePoint, GameHistory historyToPoint)
        {
            TreePoint = treePoint;
            HistoryToPoint = historyToPoint;
        }

        /// <summary>
        /// Returns the game state, e.g. a node tally for a non-chance player, the chance node settings for a chance player, or the utilities if the game is complete.
        /// </summary>
        /// <param name="navigation">The navigation settings. If the LookupApproach is both, this method will verify that both return the same value.</param>
        /// <returns></returns>
        public unsafe object GetGameStateForCurrentPlayer(HistoryNavigationInfo navigation)
        {
            object gameStateFromGameHistory = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.GameHistory || navigation.LookupApproach == InformationSetLookupApproach.Both)
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                byte nextPlayer = nextDecision?.PlayerNumber ?? navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                byte* informationSetsPtr = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
                // string playerInformationString = HistoryToPoint.GetPlayerInformationString(currentPlayer, nextDecision?.DecisionByteCode);
                HistoryToPoint.GetPlayerInformation(nextPlayer, null, informationSetsPtr);
                var DEBUG = Util.ListExtensions.GetPointerAsList_255Terminated(informationSetsPtr);
                gameStateFromGameHistory = navigation.Strategies[nextPlayer].InformationSetTree.GetValue(informationSetsPtr);
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.GameTree || navigation.LookupApproach == InformationSetLookupApproach.Both)
            {
                object gameStateFromGameTree = null;
                if (TreePoint.StoredValue is NWayTreeStorage<object> informationSetNodeReferencedInHistoryNode)
                    gameStateFromGameTree = informationSetNodeReferencedInHistoryNode.StoredValue;
                else if (TreePoint.StoredValue is double[])
                    gameStateFromGameTree = TreePoint.StoredValue;
                if (navigation.LookupApproach == InformationSetLookupApproach.Both)
                {
                    bool equals;
                    if (gameStateFromGameTree is double[])
                        equals = ((double[])gameStateFromGameTree).SequenceEqual((double[])gameStateFromGameHistory); // The game tree may store a resolution that corresponds to a single resolution sets many places, because many sets of actions can lead to the same result.
                    else
                        equals = gameStateFromGameTree == gameStateFromGameHistory; // this should be the same object (not just equal), because the game tree points to the information set tree
                    if (!equals)
                        throw new Exception("Different value from two different approaches.");
                }
                return gameStateFromGameTree;
            }
            else
                return gameStateFromGameHistory;
        }

        public HistoryPoint GetBranch(HistoryNavigationInfo navigation, byte actionChosen)
        {
            HistoryPoint next = new HistoryPoint();

            if (navigation.LookupApproach == InformationSetLookupApproach.GameHistory || navigation.LookupApproach == InformationSetLookupApproach.Both)
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                next.HistoryToPoint = HistoryToPoint; // struct is copied
                next.HistoryToPoint.AddToHistory(nextDecision.DecisionByteCode, nextDecisionIndex, nextDecision.PlayerNumber, actionChosen, nextDecision.NumPossibleActions, nextDecision.PlayersToInform);
                navigation.GameDefinition.CustomInformationSetManipulation(nextDecision, nextDecisionIndex, actionChosen, ref next.HistoryToPoint);
                if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, next.HistoryToPoint))
                    next.HistoryToPoint.MarkComplete();
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.GameTree || navigation.LookupApproach == InformationSetLookupApproach.Both)
                next.TreePoint = TreePoint.GetBranch(actionChosen);
            return next;
        }

        public byte GetNextPlayer(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.GameHistory)
            {
                switch (TreePoint.StoredValue)
                {
                    case CRMInformationSetNodeTally nt:
                        return nt.PlayerIndex;
                    case CRMChanceNodeSettings cn:
                        return cn.PlayerNum;
                    case double[] utils:
                        return navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                return nextDecision.PlayerNumber;
            }
        }

        public unsafe void SetInformationIfNotSet(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            if (GetGameStateForCurrentPlayer(navigation) == null)
                SetInformationAtPoint(navigation, gameProgress, informationSetHistory);
        }

        public unsafe void SetInformationAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            var decision = navigation.GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
            var playerInfo = navigation.GameDefinition.Players[informationSetHistory.PlayerIndex];
            var playersStrategy = navigation.Strategies[informationSetHistory.PlayerIndex];
            bool isNecessarilyLast = decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
            var informationSetHistoryCopy = informationSetHistory;
            NWayTreeStorage<object> informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                        informationSetHistoryCopy.InformationSetForPlayer,
                        isNecessarilyLast,
                        () =>
                        {
                            if (playerInfo.PlayerIsChance)
                            {
                                CRMChanceNodeSettings chanceNodeSettings;
                                if (decision.UnevenChanceActions)
                                    chanceNodeSettings = new CRMChanceNodeSettings_UnequalProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        Probabilities = navigation.GameDefinition.GetChanceActionProbabilities(decision.DecisionByteCode, gameProgress) // the probabilities depend on the current state of the game
                                    };
                                else
                                    chanceNodeSettings = new CRMChanceNodeSettings_EqualProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        EachProbability = 1.0 / (double)decision.NumPossibleActions
                                    };
                                return chanceNodeSettings;
                            }
                            else
                            {
                                CRMInformationSetNodeTally nodeInfo = new CRMInformationSetNodeTally(informationSetHistory.DecisionByteCode, informationSetHistory.DecisionIndex, playerInfo.PlayerIndex, decision.NumPossibleActions);
                                return nodeInfo;
                            }
                        }
                        );
            if (navigation.LookupApproach == InformationSetLookupApproach.GameTree || navigation.LookupApproach == InformationSetLookupApproach.Both)
                TreePoint.StoredValue = informationSetNode;
        }



    }
}
