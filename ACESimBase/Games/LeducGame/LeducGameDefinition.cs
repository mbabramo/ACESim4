﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public class LeducGameDefinition : GameDefinition
    {
        public LeducGameOptions Options;
        public byte NumActionsPerPlayer => Options.OneBetSizeOnly ? (byte) 3 : (byte) 7;

        public LeducGameDefinition() : base()
        {

        }
        public void Setup(LeducGameOptions options)
        {
            Options = options;
            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            IGameFactory gameFactory = new LeducGameFactory();
            Initialize(gameFactory);
        }

        LeducGameProgress LeducGP(GameProgress gp) => gp as LeducGameProgress;
        LeducGameState LeducGameState(GameProgress gp) => LeducGP(gp).GameState;


        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after main players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo("P1", (int) LeducGamePlayers.Player1, false, true),
                    new PlayerInfo("P2", (int) LeducGamePlayers.Player2, false, true),
                    new PlayerInfo("P1C", (int) LeducGamePlayers.Player1Chance, true, false),
                    new PlayerInfo("P2C", (int) LeducGamePlayers.Player2Chance, true, false),
                    new PlayerInfo("FC", (int) LeducGamePlayers.FlopChance, true, false),
                    new PlayerInfo("R", (int) LeducGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) LeducGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>
            {
                new Decision("P1C", "P1C", (byte) LeducGamePlayers.Player1Chance, new byte[] { (byte) LeducGamePlayers.Player1, (byte) LeducGamePlayers.Resolution, (byte) LeducGamePlayers.Player2Chance, (byte) LeducGamePlayers.FlopChance }, 6, (byte) LeducGameDecisions.P1Chance) { StoreActionInGameCacheItem = GameHistoryCacheIndex_P1Card },
                new Decision("P2C", "P2C", (byte) LeducGamePlayers.Player2Chance, new byte[] { (byte) LeducGamePlayers.Player2, (byte) LeducGamePlayers.Resolution, (byte) LeducGamePlayers.FlopChance }, 6, (byte) LeducGameDecisions.P2Chance, unevenChanceActions: true)  { StoreActionInGameCacheItem = GameHistoryCacheIndex_P2Card }
            };
            AddRoundDecisions(true, decisions);
            AddRoundDecisions(false, decisions);
            decisions.Add(
                new Decision("FC", "FC", (byte)LeducGamePlayers.FlopChance, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution, (byte)LeducGamePlayers.FlopChance }, 6, (byte)LeducGameDecisions.FlopChance, unevenChanceActions: true) { StoreActionInGameCacheItem = GameHistoryCacheIndex_FlopCard });
            foreach (Decision d in decisions)
                d.IsReversible = true;
            return decisions;
        }

        private void AddRoundDecisions(bool beforeFlop, List<Decision> decisions)
        {
            AddRoundDecision(beforeFlop, player1: true, followup: false, decisions: decisions, choiceOptions: ChoiceOptions.FoldExcluded);
            AddRoundDecision(beforeFlop, player1: false, followup: false, decisions: decisions, choiceOptions: ChoiceOptions.AllAvailable);
            AddRoundDecision(beforeFlop, player1: false, followup: false, decisions: decisions, choiceOptions: ChoiceOptions.FoldExcluded);
            AddRoundDecision(beforeFlop, player1: true, followup: true, decisions: decisions, choiceOptions: ChoiceOptions.AllAvailable);
            AddRoundDecision(beforeFlop, player1: true, followup: true, decisions: decisions, choiceOptions: ChoiceOptions.BetExcluded);
            AddRoundDecision(beforeFlop, player1: false, followup: true, decisions: decisions, choiceOptions: ChoiceOptions.BetExcluded);
        }

        private enum ChoiceOptions
        {
            AllAvailable,
            FoldExcluded,
            BetExcluded
        }

        private void AddRoundDecision(bool beforeFlop, bool player1, bool followup, List<Decision> decisions, ChoiceOptions choiceOptions)
        {
            if (followup && choiceOptions == ChoiceOptions.FoldExcluded)
                throw new Exception();
            string roundDesignation = beforeFlop ? "Before" : "After";
            string roundDesignationAbbreviation = beforeFlop ? "B" : "A";
            string decisionOrFollowup = followup ? "R" : "F";
            string choiceDesignation = choiceOptions == ChoiceOptions.AllAvailable ? "" : (choiceOptions == ChoiceOptions.BetExcluded ? "NoBet" : "NoFold");
            byte[] playersToNotify = new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution };
            LeducGamePlayers player = player1 ? LeducGamePlayers.Player1 : LeducGamePlayers.Player2;
            string playerAbbreviation = player1 ? "P1" : "P2";
            LeducGameDecisions gameDecision = player1 ? (followup ? LeducGameDecisions.P1Response : LeducGameDecisions.P1Decision) : (followup ? LeducGameDecisions.P2Response : LeducGameDecisions.P2Decision);
            bool canTerminateGame = true;
            byte numActions = NumActionsPerPlayer;
            byte customByte = 0; // signal of whether fold is excluded
            if (choiceOptions == ChoiceOptions.FoldExcluded)
            {
                numActions--;
                canTerminateGame = false;
                customByte = 1;
                if (!player1)
                    gameDecision = LeducGameDecisions.P2DecisionFoldExcluded;
            }
            else if (choiceOptions == ChoiceOptions.BetExcluded)
            {
                numActions -= Options.OneBetSizeOnly ? (byte)1 : (byte)5;
                if (player1 && followup)
                    gameDecision = LeducGameDecisions.P1ResponseBetsExcluded;
            }
            byte? cacheIndex = null;
            switch (gameDecision)
            {
                case LeducGameDecisions.P1Decision:
                    cacheIndex = beforeFlop ? GameHistoryCacheIndex_P1Action_Initial_BeforeFlop : GameHistoryCacheIndex_P1Action_Initial_AfterFlop;
                    break;
                case LeducGameDecisions.P2Decision:
                case LeducGameDecisions.P2DecisionFoldExcluded:
                    cacheIndex = beforeFlop ? GameHistoryCacheIndex_P2Action_Initial_BeforeFlop : GameHistoryCacheIndex_P2Action_Initial_AfterFlop;
                    break;
                case LeducGameDecisions.P1Response:
                case LeducGameDecisions.P1ResponseBetsExcluded:
                    cacheIndex = beforeFlop ? GameHistoryCacheIndex_P1Action_Followup_BeforeFlop : GameHistoryCacheIndex_P1Action_Followup_AfterFlop;
                    break;
                case LeducGameDecisions.P2Response:
                    cacheIndex = beforeFlop ? GameHistoryCacheIndex_P2Action_Followup_BeforeFlop : GameHistoryCacheIndex_P2Action_Followup_AfterFlop;
                    break;
                default:
                    break;
            }

            decisions.Add(new Decision($"{playerAbbreviation}{decisionOrFollowup}{roundDesignation}{choiceDesignation}", $"{playerAbbreviation}{decisionOrFollowup}{roundDesignationAbbreviation}{choiceDesignation}", (byte)player, playersToNotify, NumActionsPerPlayer, (byte)LeducGameDecisions.P2Decision) { CanTerminateGame = canTerminateGame, CustomByte = customByte, StoreActionInGameCacheItem = cacheIndex });
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
        }

        public override bool SkipDecision(Decision decision, ref GameHistory gameHistory)
        {
            if (decision.DecisionByteCode == (byte)LeducGameDecisions.P1Chance || decision.DecisionByteCode == (byte)LeducGameDecisions.P2Chance || decision.DecisionByteCode == (byte)LeducGameDecisions.FlopChance)
                return false;
            LeducGameDecisions? d = GetNextPlayerDecision(gameHistory);
            if (d == null)
                throw new Exception();
            return ((byte)(LeducGameDecisions)d) != decision.DecisionByteCode;
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;
            byte decisionByteCode = currentDecision.DecisionByteCode;
            bool isPlayerDecisionWithPossibilityOfFold = decisionByteCode == (byte)LeducGameDecisions.P2Decision || decisionByteCode == (byte)LeducGameDecisions.P1Response || decisionByteCode == (byte) LeducGameDecisions.P1ResponseBetsExcluded || decisionByteCode == (byte)LeducGameDecisions.P2Response;
            return isPlayerDecisionWithPossibilityOfFold && actionChosen == (byte)LeducPlayerChoice.Fold;
        }


        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>
            {
                GetOverallReport()
            };
            return reports;
        }

        private SimpleReportDefinition GetOverallReport()
        {
            var colItems = new List<SimpleReportColumnItem>()
                {
                    //new SimpleReportColumnFilter("All", (GameProgress gp) => true, true),
                    //new SimpleReportColumnVariable("P1Action1", (GameProgress gp) => (MyGP(gp).P1Decision == 1 ? 1.0 : 0.0)),
                    //new SimpleReportColumnVariable("P1Action2", (GameProgress gp) => (MyGP(gp).P1Decision == 2 ? 1.0 : 0.0)),
                    //new SimpleReportColumnVariable("P2Action1", (GameProgress gp) => (MyGP(gp).P2Decision == 1 ? 1.0 : 0.0)),
                    //new SimpleReportColumnVariable("P2Action2", (GameProgress gp) => (MyGP(gp).P2Decision == 2 ? 1.0 : 0.0)),
                };

            var rowItems = new List<SimpleReportFilter>()
                {
                    //new SimpleReportFilter("All", (GameProgress gp) => true),
                    //new SimpleReportFilter("P1Action1", (GameProgress gp) => (MyGP(gp).P1Decision == 1)),
                    //new SimpleReportFilter("P1Action2", (GameProgress gp) => (MyGP(gp).P1Decision == 2)),
                    //new SimpleReportFilter("P2Action1", (GameProgress gp) => (MyGP(gp).P2Decision == 1)),
                    //new SimpleReportFilter("P2Action2", (GameProgress gp) => (MyGP(gp).P2Decision == 2)),
                };
            return new SimpleReportDefinition(
                "LeducGameReport",
                null,
                rowItems,
                colItems
                );
        }

        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            LeducGameState gameState = LeducGameState(gameProgress);
            double numCard1 = 2, numCard2 = 2, numCard3 = 2;
            if (gameState.P1Card == 1)
                numCard1--;
            else if (gameState.P1Card == 2)
                numCard2--;
            else
                numCard3--;
            if (decisionByteCode == (byte)LeducGameDecisions.P2Chance)
                return new double[] { numCard1 / 5.0, numCard2 / 5.0, numCard3 / 5.0 };
            else if (decisionByteCode == (byte)LeducGameDecisions.FlopChance)
            {
                if (gameState.P2Card == 1)
                    numCard1--;
                else if (gameState.P2Card == 2)
                    numCard2--;
                else
                    numCard3--;
                return new double[] { numCard1 / 4.0, numCard2 / 4.0, numCard3 / 4.0 };
            }
            throw new NotImplementedException();
        }

        #region Cache and game reconstruction


        // must skip 0
        public byte GameHistoryCacheIndex_P1Action_Initial_BeforeFlop = 1;
        public byte GameHistoryCacheIndex_P2Action_Initial_BeforeFlop = 2;
        public byte GameHistoryCacheIndex_P1Action_Followup_BeforeFlop = 3;
        public byte GameHistoryCacheIndex_P2Action_Followup_BeforeFlop = 4;
        public byte GameHistoryCacheIndex_P1Action_Initial_AfterFlop = 5;
        public byte GameHistoryCacheIndex_P2Action_Initial_AfterFlop = 6;
        public byte GameHistoryCacheIndex_P1Action_Followup_AfterFlop = 7;
        public byte GameHistoryCacheIndex_P2Action_Followup_AfterFlop = 8;

        public byte GameHistoryCacheIndex_P1Card = 9;
        public byte GameHistoryCacheIndex_P2Card = 10;
        public byte GameHistoryCacheIndex_FlopCard = 11;


        private static bool[] trueAndFalse = new bool[] { true, false };
        private static bool[] falseAndTrue = new bool[] { false, true };
        private LeducGameState ReconstructGameState(GameHistory history)
        {
            // NOTE: We reconstruct the game state to determine what the next decision is, so that we can skip decisions (including determining which version of a decision, e.g. with or without the fold option) to use. This is pretty inefficient, since we could answer those questions without allocating memory, but it is relatively simple and initialization speed should not matter much with this game.
            LeducGameState gs = new LeducGameState(Options.OneBetSizeOnly);
            gs.P1Card = history.GetCacheItemAtIndex(GameHistoryCacheIndex_P1Card);
            gs.P2Card = history.GetCacheItemAtIndex(GameHistoryCacheIndex_P2Card);
            gs.FlopCard = history.GetCacheItemAtIndex(GameHistoryCacheIndex_FlopCard);

            foreach (bool beforeFlop in trueAndFalse)
                foreach (bool followup in falseAndTrue)
                    foreach (bool player1 in trueAndFalse)
                    {
                        LeducPlayerChoice? c = GetCachedPlayerChoice(history, player1: player1, beforeFlop: beforeFlop, followup: followup);
                        if (c == null)
                            return gs;
                        gs.AddChoice((LeducPlayerChoice)c);
                    }
            return gs;
        }

        private LeducGameDecisions? GetNextPlayerDecision(GameHistory history)
        {
            LeducGameState gs = ReconstructGameState(history);
            if (gs.GameIsComplete())
                return null;
            if (gs.P1Card == 0)
                return null;
            bool preFlop = gs.InPreFlop;
            bool followup = gs.InFollowup;
            bool player1 = gs.GetTurn() == LeducTurn.P1;
            if (followup)
            {
                if (player1)
                    return LeducGameDecisions.P1Decision;
                else
                    return gs.FoldAvailable() ? LeducGameDecisions.P2Decision : LeducGameDecisions.P2DecisionFoldExcluded;
            }
            else
            {
                if (player1)
                    return gs.BetAvailable() ? LeducGameDecisions.P1Response : LeducGameDecisions.P1ResponseBetsExcluded;
                else
                    return LeducGameDecisions.P2Response;
            }
        }

        private LeducPlayerChoice? GetCachedPlayerChoice(GameHistory history, bool player1, bool beforeFlop, bool followup)
        {
            // We need to look up the cache index. But then we also need to adjust for the possibility that this may have been a decision in which "fold" was excluded as a possibility. (We do not need to adjust for the exclusion of particular bets, since those are at the end of the enum.)
            byte v = GetCacheValue(history, player1, beforeFlop, followup);
            if (v == 0)
                return null;
            if (player1 && !followup)
                v++; // fold was not a permissible value, so increment
            else if (!player1 && !followup)
            {
                LeducPlayerChoice p1Choice = (LeducPlayerChoice)GetCachedPlayerChoice(history, true, beforeFlop, false);
                if (p1Choice == LeducPlayerChoice.CallOrCheck)
                    v++; // here, fold was not permissible for p2 based on p1's action
            }
            return (LeducPlayerChoice)v;
        }

        private byte GetCacheValue(GameHistory history, bool player1, bool beforeFlop, bool followup)
        {
            return history.GetCacheItemAtIndex(GetCacheIndex(player1, beforeFlop, followup));
        }

        private byte GetCacheIndex(bool player1, bool beforeFlop, bool followup)
        {
            if (player1)
            {
                if (beforeFlop)
                    return followup ? GameHistoryCacheIndex_P1Action_Followup_BeforeFlop : GameHistoryCacheIndex_P1Action_Initial_BeforeFlop;
                else
                    return followup ? GameHistoryCacheIndex_P1Action_Followup_AfterFlop : GameHistoryCacheIndex_P1Action_Initial_AfterFlop;
            }
            else
            {
                if (beforeFlop)
                    return followup ? GameHistoryCacheIndex_P2Action_Followup_BeforeFlop : GameHistoryCacheIndex_P2Action_Initial_BeforeFlop;
                else
                    return followup ? GameHistoryCacheIndex_P2Action_Followup_AfterFlop : GameHistoryCacheIndex_P2Action_Initial_AfterFlop;
            }
        }


        #endregion

    }
}
