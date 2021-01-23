using ACESim;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{
    public class EFGFileGameDefinition : GameDefinition
    {
        public EFGFileGameOptions Options => (EFGFileGameOptions)GameOptions;
        public EFGFileReader FileReader { get; set; }
        public string SourceText { get; set; }

        public override void Setup(GameOptions gameOptions)
        {
            SourceText = System.IO.File.ReadAllText(Options.EFGFileName);
            FileReader = new EFGFileReader(SourceText);
            Players = FileReader.PlayerInfo;
            _ResolutionPlayerNumber = (byte)(Players.Max(x => x.PlayerIndex) + 1);
            Players.Add(new PlayerInfo("Resolution", _ResolutionPlayerNumber, false, true));
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = FileReader.Decisions;

            IGameFactory gameFactory = new EFGFileGameFactory();
            Initialize(gameFactory);
        }

        #region Players and decisions

        byte _ResolutionPlayerNumber;
        public override byte PlayerIndex_ResolutionPlayer => _ResolutionPlayerNumber;

        #endregion

        #region Reporting

        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>
            {
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
                "EFGFileGameReport",
                null,
                rowItems,
                colItems
                );
        }

        #endregion

        #region Game situations

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory, GameProgress gameProgress)
        {
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            var actionsSoFar = gameHistory.ActionsHistory.ToArray().ToList();
            actionsSoFar.Add(actionChosen);
            var node = GetEFGFileNode(actionsSoFar);
            return node is EFGFileOutcomeNode;
        }

        public override bool SkipDecision(Decision decision, in GameHistory gameHistory)
        {
            var actionsSoFar = gameHistory.ActionsHistory.ToArray().ToList();
            var node = (EFGFileInformationSetNode) GetEFGFileNode(actionsSoFar);
            var informationSet = node.GetInformationSet();
            return decision.DecisionByteCode != informationSet.DecisionByteCode;
        }

        public override double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            EFGFileGameProgress efgProgress = (EFGFileGameProgress)gameProgress;
            List<int> actionsSoFar = efgProgress.GameActionsOnly;
            var node = (EFGFileInformationSetNode) GetEFGFileNode(actionsSoFar);
            var informationSet = (EFGFileChanceInformationSet)node.GetInformationSet();
            return informationSet.ChanceProbabilities;
        }

        private EFGFileInformationSet GetCurrentInformationSet(List<byte> actions)
        {
            EFGFileNode node = GetEFGFileNode(actions);
            var informationSet = node.GetInformationSet();
            return informationSet;
        }

        private EFGFileNode GetEFGFileNode(List<byte> actions)
        {
            return FileReader.GetEFGFileNode(actions.Select(x => (int)x));
        }
        private EFGFileNode GetEFGFileNode(List<int> actions)
        {
            return FileReader.GetEFGFileNode(actions);
        }

        #endregion

        #region Cache

        #endregion
    }
}
