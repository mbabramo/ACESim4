using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SimpleGameDefinition")]
    public class SimpleGameDefinition : GameDefinition, ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName
    {

        public double ProbabilityChanceDecision1 = 0.99;

        public SimpleGameDefinition() : base()
        {

        }

        public string CodeGeneratorName => "SimpleGameDefinition";

        public object GenerateSetting(string options)
        {
            ParseOptions(options);

            Players = GetPlayersList();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            return this;
        }

        SimpleGameProgress MyGP(GameProgress gp) => gp as SimpleGameProgress;
        

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo("P1", (int) SimpleGamePlayers.Player1, false, true),
                    new PlayerInfo("P2", (int) SimpleGamePlayers.Player2, false, true),
                    new PlayerInfo("C", (int) SimpleGamePlayers.Chance, true, false),
                    new PlayerInfo("R", (int) SimpleGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) SimpleGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            List<byte> playersToInform = new List<byte>(){(byte)SimpleGamePlayers.Resolution};
            var decisions = new List<Decision>();
            decisions.Add(new Decision("P1", "P1", (byte)SimpleGamePlayers.Player1, playersToInform, 2, (byte)SimpleGameDecisions.P1Decision));
            decisions.Add(new Decision("P2", "P2", (byte)SimpleGamePlayers.Player2, playersToInform, 2, (byte)SimpleGameDecisions.P2Decision));
            decisions.Add(new Decision("C", "C", (byte)SimpleGamePlayers.Chance, playersToInform, 2, (byte)SimpleGameDecisions.Chance, unevenChanceActions: true) { CanTerminateGame = true });
            return decisions;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory)
        {
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, GameHistory gameHistory)
        {
            if (!currentDecision.CanTerminateGame)
                return false;
            byte decisionByteCode = currentDecision.DecisionByteCode;
            if (decisionByteCode == (byte)SimpleGameDecisions.Chance)
                return true;
            return false;
        }


        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>();
            reports.Add(GetOverallReport());
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
                "SimpleGameReport",
                null,
                rowItems,
                colItems
                );
        }
        

        private void ParseOptions(string options)
        {
        }

        public override double[] GetChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)SimpleGameDecisions.Chance)
                return new double[] { ProbabilityChanceDecision1, 1.0 - ProbabilityChanceDecision1 };
            throw new NotImplementedException(); // subclass should define if needed
        }

    }
}
