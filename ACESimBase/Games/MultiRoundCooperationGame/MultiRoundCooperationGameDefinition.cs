using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public class MultiRoundCooperationGameDefinition : GameDefinition
    {
        public int TotalRounds = 2; // DEBUG 5
        public static bool AllRoundCooperationBonus = true;

        public MultiRoundCooperationGameDefinition() : base()
        {
            Setup();
        }

        public void Setup()
        {
            Players = GetPlayersList();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            IGameFactory gameFactory = new MultiRoundCooperationGameFactory();
            Initialize(gameFactory);
        }

        MultiRoundCooperationGameProgress MRCGP(GameProgress gp) => gp as MultiRoundCooperationGameProgress;
        

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo("P1", (int) MultiRoundCooperationGamePlayers.Player1, false, true),
                    new PlayerInfo("P2", (int) MultiRoundCooperationGamePlayers.Player2, false, true),
                    new PlayerInfo("C", (int) MultiRoundCooperationGamePlayers.Chance, true, false),
                    new PlayerInfo("R", (int) MultiRoundCooperationGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MultiRoundCooperationGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            List<byte> playersToInform = new List<byte>(){(byte)MultiRoundCooperationGamePlayers.Resolution};
            List<byte> playersWithP1Informing = playersToInform.ToList();
            playersWithP1Informing.Add((byte)MultiRoundCooperationGamePlayers.Player2);
            var decisions = new List<Decision>();
            for (byte i = 1; i <= TotalRounds; i++)
            {
                decisions.Add(
                    new Decision("P1-" + i, "P1-" + i, (byte) MultiRoundCooperationGamePlayers.Player1, new byte[] { (byte) MultiRoundCooperationGamePlayers.Player1, (byte) MultiRoundCooperationGamePlayers.Player2, (byte) MultiRoundCooperationGamePlayers.Resolution}, 2, (byte) MultiRoundCooperationGameDecisions.P1Decision));
                var p2Decision = new Decision("P2-" + i, "P2-" + i, (byte)MultiRoundCooperationGamePlayers.Player2, new byte[] { (byte)MultiRoundCooperationGamePlayers.Player1, (byte)MultiRoundCooperationGamePlayers.Player2, (byte)MultiRoundCooperationGamePlayers.Resolution }, 2, (byte)MultiRoundCooperationGameDecisions.P2Decision);
                if (i == TotalRounds)
                    p2Decision.AlwaysTerminatesGame = p2Decision.CanTerminateGame = true;
                decisions.Add(
                    p2Decision);
            };
            return decisions;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
        {
            if (currentDecision.AlwaysTerminatesGame)
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
                    new SimpleReportColumnFilter("All", (GameProgress gp) => true, true),
                    //new SimpleReportColumnVariable("P1Action1", (GameProgress gp) => (MyGP(gp).P1Decision == 1 ? 1.0 : 0.0)),
                    //new SimpleReportColumnVariable("P1Action2", (GameProgress gp) => (MyGP(gp).P1Decision == 2 ? 1.0 : 0.0)),
                    new SimpleReportColumnVariable("P1Welfare", (GameProgress gp) => (MRCGP(gp).GetNonChancePlayerUtilities()[0])),
                    new SimpleReportColumnVariable("P2Welfare", (GameProgress gp) => (MRCGP(gp).GetNonChancePlayerUtilities()[1])),
                };
            for (int i = 1; i <= TotalRounds; i++)
            {
                byte round = (byte) i; // to avoid closure
                colItems.Add(new SimpleReportColumnFilter("P1Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 1), false));
                colItems.Add(new SimpleReportColumnFilter("P2Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P2Decisions[round - 1] == 1), false));
            }
            colItems.Add(new SimpleReportColumnFilter("FullCooperation", (GameProgress gp) => (MRCGP(gp).P1Decisions.All(x => x == 1) && MRCGP(gp).P2Decisions.All(x => x == 1)), false));

            var rowItems = new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    //new SimpleReportFilter("P1Action1", (GameProgress gp) => (MRCGP(gp).P1Decision == 1)),
                    //new SimpleReportFilter("P1Action2", (GameProgress gp) => (MRCGP(gp).P1Decision == 2)),
                    //new SimpleReportFilter("P2Action1", (GameProgress gp) => (MyGP(gp).P2Decision == 1)),
                    //new SimpleReportFilter("P2Action2", (GameProgress gp) => (MyGP(gp).P2Decision == 2)),
                };
            for (int i = 1; i <= TotalRounds; i++)
            {
                byte round = (byte)i; // to avoid closure
                rowItems.Add(new SimpleReportFilter("P1Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 1)));
                rowItems.Add(new SimpleReportFilter("P1Defects-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 2)));
                rowItems.Add(new SimpleReportFilter("P1CooperatesP2Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 1) && (MRCGP(gp).P2Decisions[round - 1] == 1)));
                rowItems.Add(new SimpleReportFilter("P1CooperatesP2Defects-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 1) && (MRCGP(gp).P2Decisions[round - 1] == 2)));
                rowItems.Add(new SimpleReportFilter("P1DefectsP2Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 2) && (MRCGP(gp).P2Decisions[round - 1] == 1)));
                rowItems.Add(new SimpleReportFilter("P1DefectsP2Defects-" + i, (GameProgress gp) => (MRCGP(gp).P1Decisions[round - 1] == 2) && (MRCGP(gp).P2Decisions[round - 1] == 2)));
                //rowItems.Add(new SimpleReportFilter("P2Cooperates-" + i, (GameProgress gp) => (MRCGP(gp).P2Decisions[round - 1] == 1)));
            }
            return new SimpleReportDefinition(
                "MultiRoundCooperationGameReport",
                null,
                rowItems,
                colItems
                );
        }

        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            throw new NotImplementedException(); // subclass should define if needed
        }

    }
}
