﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct HistoryPoint
    {
        NWayTreeStorage<ICRMGameState> TreePoint;
        GameHistory HistoryToPoint;
        GameProgress GameProgress;

        public HistoryPoint(NWayTreeStorage<ICRMGameState> treePoint, GameHistory historyToPoint, GameProgress gameProgress)
        {
            TreePoint = treePoint;
            HistoryToPoint = historyToPoint;
            GameProgress = gameProgress;
        }

        public override string ToString()
        {
            if (HistoryToPoint.LastIndexAddedToHistory > 0)
                return String.Join(",", HistoryToPoint.GetInformationSetHistoryItems());
            return "HistoryPoint";
        }

        public bool IsComplete(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                return GameProgress.GameComplete;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                return HistoryToPoint.IsComplete();
            return TreePoint.IsLeaf();
        }

        public string GetActionsToHereString(HistoryNavigationInfo navigation)
        {
            return String.Join(",", GetActionsToHere(navigation));
        }

        public List<byte> GetActionsToHere(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                return GameProgress.ActionsPlayed();
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                return HistoryToPoint.GetActionsAsList();
            return TreePoint.GetSequenceToHere();
        }

        /// <summary>
        /// Returns the game state, e.g. a node tally for a non-chance player, the chance node settings for a chance player, or the utilities if the game is complete.
        /// </summary>
        /// <param name="navigation">The navigation settings. If the LookupApproach is both, this method will verify that both return the same value.</param>
        /// <returns></returns>
        public unsafe ICRMGameState GetGameStateForCurrentPlayer(HistoryNavigationInfo navigation)
        {
            ICRMGameState gameStateFromGameHistory = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
            {
                if (GameProgress.GameComplete)
                    return new CRMFinalUtilities(GameProgress.GetNonChancePlayerUtilities());
                // Otherwise, when playing the actual game, we use the GameHistory object, so we'll set this object as the "cached" object even though it's cached.
                navigation.LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
                HistoryToPoint = GameProgress.GameHistory;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                var DEBUG = HistoryToPoint.GetInformationSetHistoryItems().Select(x => x.ToString());
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                byte nextPlayer = nextDecision?.PlayerNumber ?? navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                byte* informationSetsPtr = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
                // string playerInformationString = HistoryToPoint.GetPlayerInformationString(currentPlayer, nextDecision?.DecisionByteCode);
                HistoryToPoint.GetPlayerInformation(nextPlayer, null, informationSetsPtr);
                var DEBUG2 = Util.ListExtensions.GetPointerAsList_255Terminated(informationSetsPtr);
                gameStateFromGameHistory = navigation.Strategies[nextPlayer].InformationSetTree?.GetValue(informationSetsPtr);
                if (gameStateFromGameHistory == null && navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
                    return null; // we haven't initialized, so we need to do so and then try again.
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                ICRMGameState gameStateFromGameTree = TreePoint?.StoredValue;
                if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                {
                    bool equals = gameStateFromGameTree == gameStateFromGameHistory; // this should be the same object (not just equal), because the game tree points to the information set tree
                    if (!equals)
                    {
                        if (gameStateFromGameTree == null)
                            return null; // the game tree hasn't been set yet, so no need to throw; just indicate this
                        throw new Exception("Different value from two different approaches.");
                    }
                }
                return gameStateFromGameTree;
            }
            else
                return gameStateFromGameHistory;
        }

        public bool NodeIsChanceNode(ICRMGameState gameStateForCurrentPlayer)
        {
            return gameStateForCurrentPlayer is CRMChanceNodeSettings;
        }

        public CRMInformationSetNodeTally GetInformationSetNodeTally(ICRMGameState gameStateForCurrentPlayer)
        {
            return gameStateForCurrentPlayer as CRMInformationSetNodeTally;
        }

        public CRMChanceNodeSettings GetInformationSetChanceSettings(ICRMGameState gameStateForCurrentPlayer)
        {
            return gameStateForCurrentPlayer as CRMChanceNodeSettings;
        }

        public CRMFinalUtilities GetFinalUtilities(ICRMGameState gameStateForCurrentPlayer)
        {
            return gameStateForCurrentPlayer as CRMFinalUtilities;
        }

        public HistoryPoint GetBranch(HistoryNavigationInfo navigation, byte actionChosen)
        {
            HistoryPoint next = new HistoryPoint();
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
            {
                var DEBUG = GameProgress.GameHistory.GetActionsAsListString();
                GameProgress nextProgress = GameProgress.DeepCopy();
                var DEBUG2 = GameProgress.GameHistory.GetActionsAsListString();
                IGameFactory gameFactory = navigation.Strategies[0].SimulationInteraction.CurrentExecutionInformation.GameFactory;
                GamePlayer player = new GamePlayer(navigation.Strategies, gameFactory, false, navigation.GameDefinition);
                player.ContinuePathWithAction(actionChosen, nextProgress);
                next.HistoryToPoint = nextProgress.GameHistory;
                next.GameProgress = nextProgress;
                return next;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                next.HistoryToPoint = HistoryToPoint; // struct is copied
                next.HistoryToPoint.AddToHistory(nextDecision.DecisionByteCode, nextDecisionIndex, nextDecision.PlayerNumber, actionChosen, nextDecision.NumPossibleActions, nextDecision.PlayersToInform);
                navigation.GameDefinition.CustomInformationSetManipulation(nextDecision, nextDecisionIndex, actionChosen, ref next.HistoryToPoint);
                if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, next.HistoryToPoint))
                    next.HistoryToPoint.MarkComplete();
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                NWayTreeStorage<ICRMGameState> branch = TreePoint.GetBranch(actionChosen);
                if (branch == null)
                    lock (TreePoint)
                        branch = ((NWayTreeStorageInternal<ICRMGameState>)TreePoint).AddBranch(actionChosen, true);
                next.TreePoint = branch;
            }
            return next;
        }

        public byte GetNextPlayer(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case CRMInformationSetNodeTally nt:
                        return nt.PlayerIndex;
                    case CRMChanceNodeSettings cn:
                        return cn.PlayerNum;
                    case CRMFinalUtilities utils:
                        return navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                return nextDecision.PlayerNumber;
            }
        }

        public byte GetNextDecisionByteCode(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case CRMInformationSetNodeTally nt:
                        return nt.DecisionByteCode;
                    case CRMChanceNodeSettings cn:
                        return cn.DecisionByteCode;
                    case CRMFinalUtilities utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                return nextDecision.DecisionByteCode;
            }
        }

        public byte GetNextDecisionIndex(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case CRMInformationSetNodeTally nt:
                        return nt.DecisionIndex;
                    case CRMChanceNodeSettings cn:
                        return cn.DecisionIndex;
                    case CRMFinalUtilities utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                (Decision nextDecision, byte nextDecisionIndex) = navigation.GameDefinition.GetNextDecision(HistoryToPoint);
                return nextDecisionIndex;
            }
        }

        public unsafe void SetFinalUtilitiesAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress)
        {
            if (!gameProgress.GameComplete)
                throw new Exception("Game is not complete.");
            byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
            var strategy = navigation.Strategies[resolutionPlayer];
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            gameProgress.GameHistory.GetPlayerInformation(resolutionPlayer, null, resolutionInformationSet);
            NWayTreeStorage<ICRMGameState> informationSetNode = strategy.SetInformationSetTreeValueIfNotSet(
                        resolutionInformationSet,
                        true,
                        () => new CRMFinalUtilities(gameProgress.GetNonChancePlayerUtilities())
                        );
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                TreePoint.StoredValue = informationSetNode.StoredValue;
        }

        public unsafe double[] GetFinalUtilities(HistoryNavigationInfo navigation)
        {
            if (!IsComplete(navigation))
                throw new Exception("Game is not complete.");
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
                return GameProgress.GetNonChancePlayerUtilities();
            double[] utilitiesFromGameTree = null;
            double[] utilitiesFromCachedGameHistory = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                utilitiesFromGameTree = ((CRMFinalUtilities)TreePoint.StoredValue).Utilities;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                var strategy = navigation.Strategies[resolutionPlayer];
                byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
                HistoryToPoint.GetPlayerInformation(resolutionPlayer, null, resolutionInformationSet);
                CRMFinalUtilities finalUtilities = (CRMFinalUtilities) strategy.GetInformationSetTreeValue(resolutionInformationSet);
                utilitiesFromCachedGameHistory = finalUtilities.Utilities;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods && !utilitiesFromGameTree.SequenceEqual(utilitiesFromCachedGameHistory))
                throw new Exception("Internal error. Caching mechanisms don't match.");
            return utilitiesFromGameTree ?? utilitiesFromCachedGameHistory;
        }



        public unsafe void SetInformationIfNotSet(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            if (GetGameStateForCurrentPlayer(navigation) == null)
                SetInformationAtPoint(navigation, gameProgress, informationSetHistory);
        }

        private unsafe void SetInformationAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            var decision = navigation.GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
            var playerInfo = navigation.GameDefinition.Players[informationSetHistory.PlayerIndex];
            var playersStrategy = navigation.Strategies[informationSetHistory.PlayerIndex];
            bool isNecessarilyLast = false; // Not relevant now that we are storing final utilities decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
            var informationSetHistoryCopy = informationSetHistory;
            NWayTreeStorage<ICRMGameState> informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
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
                                        DecisionByteCode = informationSetHistory.DecisionByteCode,
                                        DecisionIndex = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        Probabilities = navigation.GameDefinition.GetChanceActionProbabilities(decision.DecisionByteCode, gameProgress) // the probabilities depend on the current state of the game
                                    };
                                else
                                    chanceNodeSettings = new CRMChanceNodeSettings_EqualProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionByteCode,
                                        DecisionIndex = informationSetHistory.DecisionIndex,
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
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                TreePoint.StoredValue = informationSetNode.StoredValue;
        }

    }
}
