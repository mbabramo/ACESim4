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
        public NWayTreeStorage<IGameState> TreePoint;
        public GameHistory HistoryToPoint;
        public GameProgress GameProgress;
        public IGameState GameState;

        public HistoryPoint(GameHistory historyToPoint)
        {
            HistoryToPoint = historyToPoint;
            TreePoint = null;
            GameProgress = null;
            GameState = null;
        }

        public HistoryPoint(NWayTreeStorage<IGameState> treePoint, GameHistory historyToPoint, GameProgress gameProgress)
        {
            TreePoint = treePoint;
            HistoryToPoint = historyToPoint;
            GameProgress = gameProgress;
            GameState = null;
        }

        public override string ToString()
        {
            if (GameProgress?.GameFullHistory.LastIndexAddedToHistory > 0)
                return String.Join(",", GameProgress?.GameFullHistory.GetInformationSetHistoryItems(GameProgress));
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
        public unsafe IGameState GetGameStateForCurrentPlayer(HistoryNavigationInfo navigation)
        {
            if (GameState != null)
                return GameState;
            IGameState gameStateFromGameHistory = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
            {
                if (GameProgress.GameComplete)
                {
                    GameState = new FinalUtilities(GameProgress.GetNonChancePlayerUtilities());
                    return GameState;
                }
                // Otherwise, when playing the actual game, we use the GameHistory object, so we'll set this object as the "cached" object even though it's cached.
                navigation.LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
                HistoryToPoint = GameProgress.GameHistory;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                //var informationSetHistories = HistoryToPoint.GetInformationSetHistoryItems().Select(x => x.ToString());
                navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out Decision nextDecision, out byte nextDecisionIndex); 
                byte nextPlayer = nextDecision?.PlayerNumber ?? navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                byte* informationSetsPtr = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
                // string playerInformationString = HistoryToPoint.GetPlayerInformationString(currentPlayer, nextDecision?.DecisionByteCode);
                HistoryToPoint.GetPlayerInformationCurrent(nextPlayer, informationSetsPtr);
                if (GameProgressLogger.LoggingOn)
                {
                    var informationSetList = Util.ListExtensions.GetPointerAsList_255Terminated(informationSetsPtr);
                    GameProgressLogger.Log($"Player {nextPlayer} information set: {String.Join(",", informationSetList)}");
                }
                if (nextDecision != null)
                    gameStateFromGameHistory = navigation.Strategies[nextPlayer].GetInformationSetTreeValue(nextDecisionIndex, informationSetsPtr);
                else
                    gameStateFromGameHistory = navigation.Strategies[nextPlayer].GetInformationSetTreeValue(informationSetsPtr); // resolution player -- we don't use the decision index
                if (gameStateFromGameHistory == null && navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
                    return null; // we haven't initialized, so we need to do so and then try again.
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                IGameState gameStateFromGameTree = TreePoint?.StoredValue;
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
                GameState = gameStateFromGameTree;
            }
            else
                GameState = gameStateFromGameHistory;
            return GameState;
        }

        public HistoryPoint GetBranch(HistoryNavigationInfo navigation, byte actionChosen)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
                return GetBranch_CachedGameHistory(navigation, actionChosen);

            return GetBranch_NotCacheOnly(navigation, actionChosen);
        }

        private HistoryPoint GetBranch_NotCacheOnly(HistoryNavigationInfo navigation, byte actionChosen)
        {
            HistoryPoint next = new HistoryPoint();
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                next = GetBranch_CachedGameHistory(navigation, actionChosen);
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                NWayTreeStorage<IGameState> branch = TreePoint.GetBranch(actionChosen);
                if (branch == null)
                    lock (TreePoint)
                        branch = ((NWayTreeStorageInternal<IGameState>) TreePoint).AddBranch(actionChosen, true);
                next.TreePoint = branch;
            }
            else if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
            {
                GameProgress nextProgress = GameProgress.DeepCopy();
                IGameFactory gameFactory = navigation.GameDefinition.GameFactory;
                GamePlayer player = new GamePlayer(navigation.Strategies, false, navigation.GameDefinition);
                player.ContinuePathWithAction(actionChosen, nextProgress);
                next.HistoryToPoint = nextProgress.GameHistory;
                next.GameProgress = nextProgress;
                return next;
            }
            return next;
        }

        private HistoryPoint GetBranch_CachedGameHistory(HistoryNavigationInfo navigation, byte actionChosen)
        {
            navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out Decision nextDecision, out byte nextDecisionIndex);
            HistoryPoint next = new HistoryPoint {HistoryToPoint = HistoryToPoint}; // struct is copied. We then use a ref to change the copy, since otherwise it would be copied again. This is costly, because we're copying the entire struct (and this is executed very frequently.
            Game.UpdateGameHistory(ref next.HistoryToPoint, navigation.GameDefinition, nextDecision, nextDecisionIndex, actionChosen, GameProgress);
            if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, ref next.HistoryToPoint, actionChosen))
                next.HistoryToPoint.MarkComplete();
            return next;
        }

        public void SwitchToBranch(HistoryNavigationInfo navigation, byte actionChosen)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
            {
                navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out Decision nextDecision, out byte nextDecisionIndex);
                Game.UpdateGameHistory(ref HistoryToPoint, navigation.GameDefinition, nextDecision, nextDecisionIndex, actionChosen, GameProgress);
                if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, ref HistoryToPoint, actionChosen))
                    HistoryToPoint.MarkComplete();
            }
            else if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                NWayTreeStorage<IGameState> branch = TreePoint.GetBranch(actionChosen);
                if (branch == null)
                    lock (TreePoint)
                        branch = ((NWayTreeStorageInternal<IGameState>)TreePoint).AddBranch(actionChosen, true);
                TreePoint = branch;
            }
            else if (navigation.LookupApproach == InformationSetLookupApproach.PlayUnderlyingGame)
            {
                IGameFactory gameFactory = navigation.GameDefinition.GameFactory;
                GamePlayer player = new GamePlayer(navigation.Strategies, false, navigation.GameDefinition);
                player.ContinuePathWithAction(actionChosen, GameProgress);
                HistoryToPoint = GameProgress.GameHistory;
            }
            GameState = null;
        }

        public byte GetNextPlayer(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNodeTally nt:
                        return nt.PlayerIndex;
                    case ChanceNodeSettings cn:
                        return cn.PlayerNum;
                    case FinalUtilities utils:
                        return navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out Decision nextDecision, out byte _);
                return nextDecision.PlayerNumber;
            }
        }

        public byte GetNextDecisionByteCode(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNodeTally nt:
                        return nt.DecisionByteCode;
                    case ChanceNodeSettings cn:
                        return cn.DecisionByteCode;
                    case FinalUtilities utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out Decision nextDecision, out byte _);
                return nextDecision.DecisionByteCode;
            }
        }

        public byte GetNextDecisionIndex(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNodeTally nt:
                        return nt.DecisionIndex;
                    case ChanceNodeSettings cn:
                        return cn.DecisionIndex;
                    case FinalUtilities utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(ref HistoryToPoint, out _, out byte nextDecisionIndex);
                return nextDecisionIndex;
            }
        }

        public unsafe void SetFinalUtilitiesAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress)
        {
            if (!gameProgress.GameComplete)
                throw new Exception("Game is not complete.");
            byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
            var strategy = navigation.Strategies[resolutionPlayer];
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
            gameProgress.GameHistory.GetPlayerInformationCurrent(resolutionPlayer, resolutionInformationSet);
            //var resolutionInformationSetList = Util.ListExtensions.GetPointerAsList_255Terminated(resolutionInformationSet); 
            NWayTreeStorage<IGameState> informationSetNode = strategy.SetInformationSetTreeValueIfNotSet(
                        resolutionInformationSet,
                        true,
                        () => new FinalUtilities(gameProgress.GetNonChancePlayerUtilities())
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
                utilitiesFromGameTree = ((FinalUtilities)TreePoint.StoredValue).Utilities;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                var strategy = navigation.Strategies[resolutionPlayer];
                byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
                HistoryToPoint.GetPlayerInformationCurrent(resolutionPlayer, resolutionInformationSet);
                FinalUtilities finalUtilities = (FinalUtilities) strategy.GetInformationSetTreeValue(resolutionInformationSet);
                if (finalUtilities == null)
                {
                    navigation.GetGameState(ref this); // make sure that point is initialized up to here
                    finalUtilities = (FinalUtilities)strategy.GetInformationSetTreeValue(resolutionInformationSet);
                }
                utilitiesFromCachedGameHistory = finalUtilities.Utilities;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods && !utilitiesFromGameTree.SequenceEqual(utilitiesFromCachedGameHistory))
                throw new Exception("Internal error. Caching mechanisms don't match.");
            return utilitiesFromGameTree ?? utilitiesFromCachedGameHistory;
        }



        public unsafe void SetInformationIfNotSet(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            //var informationSetString = informationSetHistory.ToString(); 
            //var informationSetList = informationSetHistory.GetInformationSetForPlayerAsList(); 
            //var actionsToHere = GetActionsToHereString(navigation);
            IGameState gameState = GetGameStateForCurrentPlayer(navigation);
            if (gameState == null)
                SetInformationAtPoint(navigation, gameProgress, informationSetHistory);
        }

        private unsafe void SetInformationAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            var decision = navigation.GameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
            var playerInfo = navigation.GameDefinition.Players[informationSetHistory.PlayerIndex];
            var playersStrategy = navigation.Strategies[informationSetHistory.PlayerIndex];
            bool isNecessarilyLast = false; // Not relevant now that we are storing final utilities decision.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
            var informationSetHistoryCopy = informationSetHistory;
            NWayTreeStorage<IGameState> informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                        informationSetHistoryCopy.DecisionIndex, // this will be a choice at the root level of the information set
                        informationSetHistoryCopy.InformationSetForPlayer,
                        isNecessarilyLast,
                        () =>
                        {
                            if (playerInfo.PlayerIsChance)
                            {
                                ChanceNodeSettings chanceNodeSettings;
                                if (decision.UnevenChanceActions)
                                    chanceNodeSettings = new ChanceNodeSettingsUnequalProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionByteCode,
                                        DecisionIndex = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        Probabilities = navigation.GameDefinition.GetChanceActionProbabilities(decision.DecisionByteCode, gameProgress), // the probabilities depend on the current state of the game
                                        CriticalNode = decision.CriticalNode,
                                        AlwaysTerminatesGame = decision.AlwaysTerminatesGame
                                    };
                                else
                                    chanceNodeSettings = new ChanceNodeSettingsEqualProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionByteCode,
                                        DecisionIndex = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        EachProbability = 1.0 / (double)decision.NumPossibleActions,
                                        CriticalNode = decision.CriticalNode,
                                        AlwaysTerminatesGame = decision.AlwaysTerminatesGame
                                    };
                                return chanceNodeSettings;
                            }
                            else
                            {
                                InformationSetNodeTally nodeInfo = new InformationSetNodeTally(informationSetHistory.DecisionByteCode, informationSetHistory.DecisionIndex, playerInfo.PlayerIndex, decision.NumPossibleActions);
                                return nodeInfo;
                            }
                        }
                        );
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                TreePoint.StoredValue = informationSetNode.StoredValue;
        }

    }
}
