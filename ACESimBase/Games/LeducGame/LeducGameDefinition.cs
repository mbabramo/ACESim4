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

        public double ProbabilityChanceDecision1 = 2.0 / 3.0;

        public LeducGameDefinition() : base()
        {

        }
        public void Setup()
        {
            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            IGameFactory gameFactory = new MyGameFactory();
            Initialize(gameFactory);
        }

        LeducGameProgress MyGP(GameProgress gp) => gp as LeducGameProgress;
        

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo("P1", (int) LeducGamePlayers.Player1, false, true),
                    new PlayerInfo("P2", (int) LeducGamePlayers.Player2, false, true),
                    new PlayerInfo("C", (int) LeducGamePlayers.Chance, true, false),
                    new PlayerInfo("R", (int) LeducGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) LeducGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            List<byte> playersToInform = new List<byte>(){(byte)LeducGamePlayers.Resolution};
            bool P1InformsP2 = false; 
            List<byte> playersWithP1Informing = playersToInform.ToList();
            playersWithP1Informing.Add((byte)LeducGamePlayers.Player2);
            var decisions = new List<Decision>
            {
                new Decision("P1", "P1", (byte)LeducGamePlayers.Player1, P1InformsP2 ? playersWithP1Informing.ToArray() : playersToInform.ToArray(), 2, (byte)LeducGameDecisions.P1Decision),
                new Decision("P2", "P2", (byte)LeducGamePlayers.Player2, playersToInform.ToArray(), 2, (byte)LeducGameDecisions.P2Decision),
                new Decision("C", "C", (byte)LeducGamePlayers.Chance, playersToInform.ToArray(), 2, (byte)LeducGameDecisions.Chance, unevenChanceActions: true) { CanTerminateGame = true }
            };
            return decisions;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
        {
            if (!currentDecision.CanTerminateGame)
                return false;
            byte decisionByteCode = currentDecision.DecisionByteCode;
            if (decisionByteCode == (byte)LeducGameDecisions.Chance)
                return true;
            return false;
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
                    new SimpleReportColumnVariable("P2Action1", (GameProgress gp) => (MyGP(gp).P2Decision == 1 ? 1.0 : 0.0)),
                    new SimpleReportColumnVariable("P2Action2", (GameProgress gp) => (MyGP(gp).P2Decision == 2 ? 1.0 : 0.0)),
                };

            var rowItems = new List<SimpleReportFilter>()
                {
                    //new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("P1Action1", (GameProgress gp) => (MyGP(gp).P1Decision == 1)),
                    new SimpleReportFilter("P1Action2", (GameProgress gp) => (MyGP(gp).P1Decision == 2)),
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
            if (decisionByteCode == (byte)LeducGameDecisions.Chance)
                return new double[] { ProbabilityChanceDecision1, 1.0 - ProbabilityChanceDecision1 };
            throw new NotImplementedException(); // subclass should define if needed
        }

    }
}
