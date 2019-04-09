using System;
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
                new Decision("P1C", "P1C", (byte) LeducGamePlayers.Player1Chance, new byte[] { (byte) LeducGamePlayers.Player1, (byte) LeducGamePlayers.Resolution, (byte) LeducGamePlayers.Player2Chance, (byte) LeducGamePlayers.FlopChance }, 6, (byte) LeducGameDecisions.P1Chance),
                new Decision("P2C", "P2C", (byte) LeducGamePlayers.Player2Chance, new byte[] { (byte) LeducGamePlayers.Player2, (byte) LeducGamePlayers.Resolution, (byte) LeducGamePlayers.FlopChance }, 6, (byte) LeducGameDecisions.P2Chance, unevenChanceActions: true)
            };
            AddRoundDecisions(true, decisions);
            AddRoundDecisions(false, decisions);
            decisions.Add(
                new Decision("FC", "FC", (byte)LeducGamePlayers.FlopChance, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution, (byte)LeducGamePlayers.FlopChance }, 6, (byte)LeducGameDecisions.FlopChance, unevenChanceActions: true));
            foreach (Decision d in decisions)
                d.IsReversible = true;
            return decisions;
        }

        private void AddRoundDecisions(bool preFlop, List<Decision> decisions)
        {
            string roundDesignation = preFlop ? "A" : "B";
            decisions.Add(new Decision($"P1D{roundDesignation}", $"P1D{roundDesignation}", (byte)LeducGamePlayers.Player1, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution }, NumActionsPerPlayer, (byte)LeducGameDecisions.P1Decision) { CanTerminateGame = !preFlop });
            decisions.Add(new Decision($"P2D{roundDesignation}", $"P2D{roundDesignation}", (byte)LeducGamePlayers.Player2, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution }, NumActionsPerPlayer, (byte)LeducGameDecisions.P2Decision) { CanTerminateGame = true });
            decisions.Add(new Decision($"P1R{roundDesignation}", $"P1R{roundDesignation}", (byte)LeducGamePlayers.Player1, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution }, NumActionsPerPlayer, (byte)LeducGameDecisions.P1Decision) { CanTerminateGame = true });
            decisions.Add(new Decision($"P2R{roundDesignation}", $"P2R{roundDesignation}", (byte)LeducGamePlayers.Player2, new byte[] { (byte)LeducGamePlayers.Player1, (byte)LeducGamePlayers.Player2, (byte)LeducGamePlayers.Resolution }, NumActionsPerPlayer, (byte)LeducGameDecisions.P2Decision) { CanTerminateGame = true });
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;
            byte decisionByteCode = currentDecision.DecisionByteCode;
            bool isPlayerDecision = (decisionByteCode == (byte)LeducGameDecisions.P1Decision || decisionByteCode == (byte)LeducGameDecisions.P2Decision || decisionByteCode == (byte)LeducGameDecisions.P1Response || decisionByteCode == (byte)LeducGameDecisions.P2Response) ;
            return isPlayerDecision && actionChosen == (byte)LeducPlayerChoice.Fold;
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

    }
}
