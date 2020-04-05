using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public readonly ref struct HistoryPoint
    {

        public readonly NWayTreeStorage<IGameState> TreePoint;
        public readonly GameHistory HistoryToPoint;
        public readonly GameProgress GameProgress;
        public readonly IGameState GameState;

        public bool ContainsGameHistoryOnly => TreePoint == null && GameProgress == null && GameState == null;

        public HistoryPoint(GameHistory historyToPoint)
        {
            HistoryToPoint = historyToPoint;
            TreePoint = null;
            GameProgress = null;
            GameState = null;
        }

        public HistoryPoint(NWayTreeStorage<IGameState> treePoint, GameHistory historyToPoint, GameProgress gameProgress, IGameState gameState = null)
        {
            TreePoint = treePoint;
            HistoryToPoint = historyToPoint;
            GameProgress = gameProgress;
            GameState = gameState;
        }

        public HistoryPoint DeepCopy()
        {
            return new HistoryPoint(TreePoint, HistoryToPoint.DeepCopy(), GameProgress?.DeepCopy(), GameState);
        }

        public HistoryPoint WithTreePoint(NWayTreeStorage<IGameState> treePoint)
        {
            return new HistoryPoint(treePoint, HistoryToPoint, GameProgress, GameState);
        }

        public HistoryPoint WithGameState(IGameState gameState)
        {
            return new HistoryPoint(TreePoint, HistoryToPoint, GameProgress, gameState);
        }

        public HistoryPoint WithGameProgress(GameProgress gameProgress)
        {
            return new HistoryPoint(TreePoint, HistoryToPoint, gameProgress, GameState);
        }

        public HistoryPoint WithHistoryToPoint(GameHistory historyToPoint)
        {
            return new HistoryPoint(TreePoint, historyToPoint, GameProgress, GameState);
        }

        public HistoryPointStorable ToStorable()
        {
            return new HistoryPointStorable()
            {
                TreePoint = TreePoint,
                HistoryToPointStorable = HistoryToPoint.DeepCopyToStorable(),
                GameProgress = GameProgress,
                GameState = GameState
            };
        }

        public override string ToString()
        {
            if (GameProgress?.GameFullHistory.NextIndexToAddToHistory > 0)
                return GameProgress?.GameFullHistoryStorable.GetInformationSetHistoryItemsString(GameProgress);
            return "HistoryPoint";
        }

        public bool IsComplete(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
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
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
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
        public IGameState GetGameStatePrerecorded(HistoryNavigationInfo navigation)
        {
            if (GameState != null)
            {
                return GameState;
            }
            IGameState gameStateFromGameHistory = null;
            var historyToPoint = HistoryToPoint;
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
            {
                if (GameProgress.GameComplete)
                {
                    var finalUtilitiesResult = new FinalUtilitiesNode(GameProgress.GetNonChancePlayerUtilities(), GameProgress.GetCustomResult(), -1); // If we want to, we could add the FInalUtilitiesNode to the list, but this means that we will have a new node everytime we play. If we want to be able to use unrolling with final utilities nodes, then we would need to check the list (e.g., by using a dictionary) for a matching FinalUtilitiesNode, rather than continually adding new ones.
                    //navigation.FinalUtilitiesNodes.Add(finalUtilitiesResult);
                    return finalUtilitiesResult;
                }
                // use the cached game history to look up the game state
                navigation = navigation.WithLookupApproach(InformationSetLookupApproach.CachedGameHistoryOnly);
                historyToPoint = GameProgress.GameHistory;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                //var informationSetHistories = HistoryToPoint.GetInformationSetHistoryItems().Select(x => x.ToString());
                navigation.GameDefinition.GetNextDecision(in historyToPoint, out Decision nextDecision, out byte nextDecisionIndex);
                // If nextDecision is null, then there are no more player decisions. (If this seems wrong, it could be a result of an error in whether to mark a game complete.) When there are no more player decisions, the resolution "player" is used.
                byte nextPlayer = nextDecision?.PlayerNumber ?? navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                Span<byte> informationSetsPtr = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
                // string playerInformationString = HistoryToPoint.GetPlayerInformationString(currentPlayer, nextDecision?.DecisionByteCode);
                GameHistory.GetPlayerInformationCurrent(nextPlayer, historyToPoint.InformationSets, informationSetsPtr);
                if (GameProgressLogger.LoggingOn)
                {
                    var informationSetList = Util.ListExtensions.GetSpan255TerminatedAsList(informationSetsPtr);
                    var actionsToHere = String.Join(",", historyToPoint.GetActionsAsList());
                    GameProgressLogger.Log($"Player {nextPlayer} decision: {nextDecision?.Name ?? "Resolution"} information set: {String.Join(",", informationSetList)} actions to here: {actionsToHere}");
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
                        throw new Exception("Different value from two different approaches."); // NOTE: One possible cause of this error is that a player faces the same information set at two different points of the game. Each information set must lead to a unique decision. Thus, if a player makes two consecutive decisions with the same information set, you should add a dummy piece of information to the player's information set after the first decision to allow this to be distinguished. NOTE2: Another possible cause is that you may add to information set and log passing the decision byte code rather than the decision index. NOTE3: IT also could be that you define a decision as reversible when it is not.
                    }
                }
                return gameStateFromGameTree;
            }
            else
                return gameStateFromGameHistory;
        }

        public bool BranchingIsReversible(HistoryNavigationInfo navigation, Decision nextDecision)
        {
            return navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly && nextDecision.IsReversible && GameProgress == null;
        }

        public HistoryPoint GetBranch(HistoryNavigationInfo navigation, byte actionChosen, Decision nextDecision, byte nextDecisionIndex)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly)
                return GetBranch_CachedGameHistory(navigation, actionChosen, nextDecision, nextDecisionIndex);

            return GetBranch_NotCacheOnly(navigation, actionChosen, nextDecision, nextDecisionIndex);
        }

        private HistoryPoint GetBranch_NotCacheOnly(HistoryNavigationInfo navigation, byte actionChosen, Decision nextDecision, byte nextDecisionIndex)
        {
            NWayTreeStorage<IGameState> nextTreePoint = null;
            GameHistory nextHistoryToPoint = default;
            GameProgress nextGameProgress = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                nextHistoryToPoint = GetBranch_CachedGameHistory(navigation, actionChosen, nextDecision, nextDecisionIndex).HistoryToPoint;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                NWayTreeStorage<IGameState> branch = TreePoint.GetBranch(actionChosen);
                if (branch == null)
                    lock (TreePoint)
                        branch = ((NWayTreeStorageInternal<IGameState>)TreePoint).AddBranch(actionChosen, true);
                nextTreePoint = branch;
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log($"Getting game tree branch for {actionChosen} (decision {nextDecision.Name}) => {branch.SequenceToHereString}");
            }
            else if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
            {
                GameProgress nextProgress = GameProgress.DeepCopy();
                IGameFactory gameFactory = navigation.GameDefinition.GameFactory;
                GamePlayer player = new GamePlayer(navigation.Strategies, false, navigation.GameDefinition);
                player.ContinuePathWithAction(actionChosen, nextProgress);
                nextHistoryToPoint = nextProgress.GameHistory;
                nextGameProgress = nextProgress;
            }
            return new HistoryPoint(nextTreePoint, nextHistoryToPoint, nextGameProgress);
        }

        private HistoryPoint GetBranch_CachedGameHistory(HistoryNavigationInfo navigation, byte actionChosen, Decision nextDecision, byte nextDecisionIndex)
        {
            GameHistory historyToPointCopy = HistoryToPoint.DeepCopy(); // struct is copied, along with enclosed arrays. 
            Game.UpdateGameHistory(ref historyToPointCopy, navigation.GameDefinition, nextDecision, nextDecisionIndex, actionChosen, GameProgress);
            HistoryPoint next = new HistoryPoint(historyToPointCopy);
            if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, in next.HistoryToPoint, actionChosen))
            {
                GameHistory historyToPoint = next.HistoryToPoint;
                historyToPoint.MarkComplete();
                next = next.WithHistoryToPoint(historyToPoint);
            }
            return next;
        }

        public HistoryPoint SwitchToBranch(HistoryNavigationInfo navigation, byte actionChosen, Decision nextDecision, byte nextDecisionIndex)
        {
            HistoryPoint toReturn = this;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                GameHistory historyToPointCopy = HistoryToPoint;
                Game.UpdateGameHistory(ref historyToPointCopy, navigation.GameDefinition, nextDecision, nextDecisionIndex, actionChosen, GameProgress);
                if (nextDecision.CanTerminateGame && navigation.GameDefinition.ShouldMarkGameHistoryComplete(nextDecision, in historyToPointCopy, actionChosen))
                {
                    historyToPointCopy.MarkComplete();
                }
                toReturn = toReturn.WithHistoryToPoint(historyToPointCopy);
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                NWayTreeStorage<IGameState> branch = TreePoint.GetBranch(actionChosen);
                if (branch == null)
                    lock (TreePoint)
                        branch = ((NWayTreeStorageInternal<IGameState>)TreePoint).AddBranch(actionChosen, true);
                toReturn = toReturn.WithTreePoint(branch);
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
            {
                IGameFactory gameFactory = navigation.GameDefinition.GameFactory;
                GamePlayer player = new GamePlayer(navigation.Strategies, false, navigation.GameDefinition);
                player.ContinuePathWithAction(actionChosen, GameProgress);
                toReturn = toReturn.WithGameProgress(GameProgress);
            }
            toReturn = toReturn.WithGameState(null);
            return toReturn;
        }

        public byte GetNextPlayer(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNode nt:
                        return nt.PlayerIndex;
                    case ChanceNode cn:
                        return cn.PlayerNum;
                    case FinalUtilitiesNode utils:
                        return navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(in HistoryToPoint, out Decision nextDecision, out byte _);
                return nextDecision.PlayerNumber;
            }
        }

        public byte GetNextDecisionByteCode(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNode nt:
                        return nt.DecisionByteCode;
                    case ChanceNode cn:
                        return cn.DecisionByteCode;
                    case FinalUtilitiesNode utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(in HistoryToPoint, out Decision nextDecision, out byte _);
                return nextDecision.DecisionByteCode;
            }
        }

        public byte GetNextDecisionIndex(HistoryNavigationInfo navigation)
        {
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly)
            {
                switch (TreePoint.StoredValue)
                {
                    case InformationSetNode nt:
                        return nt.DecisionIndex;
                    case ChanceNode cn:
                        return cn.DecisionIndex;
                    case FinalUtilitiesNode utils:
                        throw new NotImplementedException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else // may be actual game or cached game history -- either way, we'll use the game history
            {
                navigation.GameDefinition.GetNextDecision(in HistoryToPoint, out _, out byte nextDecisionIndex);
                return nextDecisionIndex;
            }
        }

        public void SetFinalUtilitiesAtPoint(HistoryNavigationInfo navigation, GameProgress gameProgress)
        {
            if (!gameProgress.GameComplete)
                throw new Exception("Game is not complete.");
            byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
            var strategy = navigation.Strategies[resolutionPlayer];
            Span<byte> resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
            GameHistory.GetPlayerInformationCurrent(resolutionPlayer, gameProgress.GameHistory.InformationSets, resolutionInformationSet);
            //var resolutionInformationSetList = Util.ListExtensions.GetPointerAsList_255Terminated(resolutionInformationSet); 
            NWayTreeStorage<IGameState> informationSetNode = strategy.SetInformationSetTreeValueIfNotSet(
                        resolutionInformationSet,
                        true,
                        () =>
                        {
                            FinalUtilitiesNode finalUtilitiesResult = new FinalUtilitiesNode(gameProgress.GetNonChancePlayerUtilities_IncludingAlternateScenarios(navigation.GameDefinition), gameProgress.GetCustomResult_IncludingAlternateScenarios(navigation.GameDefinition), navigation.FinalUtilitiesNodes.Count());
                            navigation.FinalUtilitiesNodes.Add(finalUtilitiesResult);
                            return finalUtilitiesResult;
                        }
                        );
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                TreePoint.StoredValue = informationSetNode.StoredValue;
        }

        public double[] GetFinalUtilities(HistoryNavigationInfo navigation)
        {
            if (!IsComplete(navigation))
                throw new Exception("Game is not complete.");
            if (navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
                return GameProgress.GetNonChancePlayerUtilities();
            double[] utilitiesFromGameTree = null;
            double[] utilitiesFromCachedGameHistory = null;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                utilitiesFromGameTree = ((FinalUtilitiesNode)TreePoint.StoredValue).Utilities;
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameHistoryOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
            {
                byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
                var strategy = navigation.Strategies[resolutionPlayer];
                Span<byte> resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerFullPlayer];
                GameHistory.GetPlayerInformationCurrent(resolutionPlayer, HistoryToPoint.InformationSets, resolutionInformationSet);
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)strategy.GetInformationSetTreeValue(resolutionInformationSet);
                if (finalUtilities == null)
                {
                    navigation.GetGameState(in this); // make sure that point is initialized up to here
                    finalUtilities = (FinalUtilitiesNode)strategy.GetInformationSetTreeValue(resolutionInformationSet);
                }
                utilitiesFromCachedGameHistory = finalUtilities.Utilities;
            }
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods && !utilitiesFromGameTree.SequenceEqual(utilitiesFromCachedGameHistory))
                throw new Exception("Internal error. Caching mechanisms don't match.");
            return utilitiesFromGameTree ?? utilitiesFromCachedGameHistory;
        }



        public IGameState GetGameStateFromGameProgress(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            //var informationSetString = informationSetHistory.ToString(); 
            //var informationSetList = informationSetHistory.GetInformationSetForPlayerAsList(); 
            //var actionsToHere = GetActionsToHereString(navigation);
            IGameState gameState = null;
            gameState = GetGameStatePrerecorded(navigation);
            if (gameState == null)
            {
                return SetGameStateForInformationSet(navigation, gameProgress, informationSetHistory);
            }
            return gameState;
        }

        private IGameState SetGameStateForInformationSet(HistoryNavigationInfo navigation, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            byte decisionIndex = informationSetHistory.DecisionIndex;
            var decision = navigation.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            var playerInfo = navigation.GameDefinition.Players[informationSetHistory.PlayerIndex];
            var playersStrategy = navigation.Strategies[informationSetHistory.PlayerIndex];
            bool isNecessarilyLast = false; // Not relevant now that we are storing final utilities decisio n.IsAlwaysPlayersLastDecision || informationSetHistory.IsTerminalAction;
            bool creatingInformationSet = false; // verify inner lock working correctly
            InformationSetHistory informationSetHistoryCopy = informationSetHistory;
            Span<byte> informationSetForPlayer = informationSetHistoryCopy.InformationSetForPlayer;
            NWayTreeStorage<IGameState> informationSetNode = playersStrategy.SetInformationSetTreeValueIfNotSet(
                        informationSetHistoryCopy.DecisionIndex, // this will be a choice at the root level of the information set
                        informationSetHistoryCopy.InformationSetForPlayer,
                        isNecessarilyLast,
                        () =>
                        {
                            if (playerInfo.PlayerIsChance)
                            {
                                ChanceNode chanceNode;
                                int chanceNodeNumber = navigation.ChanceNodes.Count();
                                if (decision.UnevenChanceActions)
                                    chanceNode = new ChanceNodeUnequalProbabilities(chanceNodeNumber)
                                    {
                                        Decision = decision,
                                        DecisionIndex = decisionIndex,
                                        Probabilities = navigation.GameDefinition.GetUnevenChanceActionProbabilities(decision.DecisionByteCode, gameProgress), // the probabilities depend on the current state of the game
                                    };
                                else
                                    chanceNode = new ChanceNodeEqualProbabilities(chanceNodeNumber)
                                    {
                                        Decision = decision,
                                        DecisionIndex = decisionIndex,
                                        EachProbability = 1.0 / (double)decision.NumPossibleActions,
                                    };
                                navigation.ChanceNodes.Add(chanceNode);
                                return chanceNode;
                            }
                            else
                            {
                                if (creatingInformationSet)
                                    throw new Exception("Internal exception. Lock failing.");
                                creatingInformationSet = true;
                                InformationSetNode nodeInfo = new InformationSetNode(decision, decisionIndex, navigation.EvolutionSettings, navigation.InformationSets.Count());
                                navigation.InformationSets.Add(nodeInfo);
                                creatingInformationSet = false;
                                return nodeInfo;
                            }
                        }
                        );
            if (informationSetNode.StoredValue is InformationSetNode isn && isn.InformationSetContents == null)
                isn.InformationSetContents = ListExtensions.GetSpan255TerminatedAsArray(informationSetForPlayer);
            if (navigation.LookupApproach == InformationSetLookupApproach.CachedGameTreeOnly || navigation.LookupApproach == InformationSetLookupApproach.CachedBothMethods)
                TreePoint.StoredValue = informationSetNode.StoredValue;
            return informationSetNode.StoredValue;
        }

    }
}
