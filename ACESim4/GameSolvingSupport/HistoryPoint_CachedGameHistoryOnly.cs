using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct HistoryPoint_CachedGameHistoryOnly
    {
        public GameHistory HistoryToPoint;
        public IGameState GameState;

        public HistoryPoint_CachedGameHistoryOnly(GameHistory historyToPoint)
        {
            HistoryToPoint = historyToPoint;
            GameState = null;
        }

        public override string ToString()
        {
            if (HistoryToPoint.LastIndexAddedToHistory > 0)
                return String.Join(",", HistoryToPoint.GetInformationSetHistoryItems());
            return "HistoryPoint";
        }

        public bool IsComplete()
        {
            return HistoryToPoint.IsComplete();
        }

        public string GetActionsToHereString()
        {
            return String.Join(",", GetActionsToHere());
        }

        public List<byte> GetActionsToHere()
        {
            return HistoryToPoint.GetActionsAsList();
        }
        
        public unsafe IGameState GetGameStateForCurrentPlayer(GameDefinition gameDefinition, List<Strategy> strategies)
        {
            if (GameState != null)
                return GameState;
            IGameState gameStateFromGameHistory = null;
            
            //var informationSetHistories = HistoryToPoint.GetInformationSetHistoryItems().Select(x => x.ToString());
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            byte nextPlayer = nextDecision?.PlayerNumber ?? gameDefinition.PlayerIndex_ResolutionPlayer;
            byte* informationSetsPtr = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            // string playerInformationString = HistoryToPoint.GetPlayerInformationString(currentPlayer, nextDecision?.DecisionByteCode);
            HistoryToPoint.GetPlayerInformation(nextPlayer, null, informationSetsPtr);
            //var informationSetList = Util.ListExtensions.GetPointerAsList_255Terminated(informationSetsPtr);
            if (nextDecision != null)
                gameStateFromGameHistory = strategies[nextPlayer].GetInformationSetTreeValue(nextDecisionIndex, informationSetsPtr);
            else
                gameStateFromGameHistory = strategies[nextPlayer].GetInformationSetTreeValue(informationSetsPtr); // resolution player -- we don't use the decision index
            if (gameStateFromGameHistory == null)
                return null; // we haven't initialized, so we need to do so and then try again.
            
            GameState = gameStateFromGameHistory;
            return GameState;
        }

        public HistoryPoint_CachedGameHistoryOnly GetBranch(GameDefinition gameDefinition, byte actionChosen)
        {
            HistoryPoint_CachedGameHistoryOnly next = new HistoryPoint_CachedGameHistoryOnly(HistoryToPoint);
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            Game.UpdateGameHistory(ref next.HistoryToPoint, gameDefinition, nextDecision, nextDecisionIndex, actionChosen);
            if (nextDecision.CanTerminateGame && gameDefinition.ShouldMarkGameHistoryComplete(nextDecision, next.HistoryToPoint, actionChosen))
                next.HistoryToPoint.MarkComplete();
            return next;
        }

        public void SwitchToBranch(GameDefinition gameDefinition, byte actionChosen)
        {
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            Game.UpdateGameHistory(ref HistoryToPoint, gameDefinition, nextDecision, nextDecisionIndex, actionChosen);
            if (nextDecision.CanTerminateGame && gameDefinition.ShouldMarkGameHistoryComplete(nextDecision, HistoryToPoint, actionChosen))
                HistoryToPoint.MarkComplete();
        }

        public byte GetNextPlayer(GameDefinition gameDefinition)
        {
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            return nextDecision.PlayerNumber;
        }

        public byte GetNextDecisionByteCode(GameDefinition gameDefinition)
        {
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            return nextDecision.DecisionByteCode;
        }

        public byte GetNextDecisionIndex(GameDefinition gameDefinition)
        {
            (Decision nextDecision, byte nextDecisionIndex) = gameDefinition.GetNextDecision(HistoryToPoint);
            return nextDecisionIndex;
        }

        public unsafe void SetFinalUtilitiesAtPoint(GameDefinition gameDefinition, List<Strategy> strategies, GameProgress gameProgress)
        {
            if (!gameProgress.GameComplete)
                throw new Exception("Game is not complete.");
            byte resolutionPlayer = gameDefinition.PlayerIndex_ResolutionPlayer;
            var strategy = strategies[resolutionPlayer];
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            gameProgress.GameHistory.GetPlayerInformation(resolutionPlayer, null, resolutionInformationSet);
            NWayTreeStorage<IGameState> informationSetNode = strategy.SetInformationSetTreeValueIfNotSet(
                        resolutionInformationSet,
                        true,
                        () => new FinalUtilities(gameProgress.GetNonChancePlayerUtilities())
                        );
        }

        public unsafe double[] GetFinalUtilities(HistoryNavigationInfo navigation)
        {
            if (!IsComplete())
                throw new Exception("Game is not complete.");
            double[] utilitiesFromCachedGameHistory = null;
            byte resolutionPlayer = navigation.GameDefinition.PlayerIndex_ResolutionPlayer;
            var strategy = navigation.Strategies[resolutionPlayer];
            byte* resolutionInformationSet = stackalloc byte[GameHistory.MaxInformationSetLengthPerPlayer];
            HistoryToPoint.GetPlayerInformation(resolutionPlayer, null, resolutionInformationSet);
            FinalUtilities finalUtilities = (FinalUtilities)strategy.GetInformationSetTreeValue(resolutionInformationSet);
            if (finalUtilities == null)
            {
                navigation.GetGameState(new HistoryPoint(null, HistoryToPoint, null)); // make sure that point is initialized up to here
                finalUtilities = (FinalUtilities)strategy.GetInformationSetTreeValue(resolutionInformationSet);
            }
            utilitiesFromCachedGameHistory = finalUtilities.Utilities;
            return utilitiesFromCachedGameHistory;
        }



        public unsafe void SetInformationIfNotSet(GameDefinition gameDefinition, List<Strategy> strategies, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            //var informationSetString = informationSetHistory.ToString();
            //var actionsToHere = GetActionsToHereString(navigation);
            IGameState gameState = GetGameStateForCurrentPlayer(gameDefinition, strategies);
            if (gameState == null)
                SetInformationAtPoint(gameDefinition, strategies, gameProgress, informationSetHistory);
        }

        private unsafe void SetInformationAtPoint(GameDefinition gameDefinition, List<Strategy> strategies, GameProgress gameProgress, InformationSetHistory informationSetHistory)
        {
            var decision = gameDefinition.DecisionsExecutionOrder[informationSetHistory.DecisionIndex];
            var playerInfo = gameDefinition.Players[informationSetHistory.PlayerIndex];
            var playersStrategy = strategies[informationSetHistory.PlayerIndex];
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
                                        Probabilities = gameDefinition.GetChanceActionProbabilities(decision.DecisionByteCode, gameProgress), // the probabilities depend on the current state of the game
                                        CriticalNode = decision.CriticalNode
                                    };
                                else
                                    chanceNodeSettings = new ChanceNodeSettingsEqualProbabilities()
                                    {
                                        DecisionByteCode = informationSetHistory.DecisionByteCode,
                                        DecisionIndex = informationSetHistory.DecisionIndex,
                                        PlayerNum = informationSetHistory.PlayerIndex,
                                        EachProbability = 1.0 / (double)decision.NumPossibleActions,
                                        CriticalNode = decision.CriticalNode
                                    };
                                return chanceNodeSettings;
                            }
                            else
                            {
                                byte? binarySubdivisionLevels = null;
                                if (decision.Subdividable_IsSubdivision && decision.Subdividable_NumOptionsPerBranch == 2)
                                    binarySubdivisionLevels = (byte)decision.Subdividable_NumLevels;
                                InformationSetNodeTally nodeInfo = new InformationSetNodeTally(informationSetHistory.DecisionByteCode, informationSetHistory.DecisionIndex, playerInfo.PlayerIndex, decision.NumPossibleActions);
                                return nodeInfo;
                            }
                        }
                        );
        }

    }
}
