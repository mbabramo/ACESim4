using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    public class DMSReplicationGameDefinition : GameDefinition
    {

        public DMSReplicationGameOptions Options => (DMSReplicationGameOptions)GameOptions;
        public ACESimBase.Games.AdditiveEvidenceGame.DMSCalc PiecewiseLinearCalcs;
        public override byte PlayerIndex_ResolutionPlayer => (byte)DMSReplicationGamePlayers.Resolution;

        public override void Setup(GameOptions options)
        {
            base.Setup(options);

            DMSReplicationGameOptions dmsOptions = (DMSReplicationGameOptions)options;
            PiecewiseLinearCalcs = new ACESimBase.Games.AdditiveEvidenceGame.DMSCalc(dmsOptions.T, dmsOptions.C, dmsOptions.Q);

            Players = GetPlayersList();
            PlayerNames = Players.Select(x => x.PlayerName).ToArray();
            NumPlayers = (byte)Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            CalculateDistributorChanceInputDecisionMultipliers();

            IGameFactory gameFactory = new DMSReplicationGameFactory();
            Initialize(gameFactory);
        }


        private static string PlaintiffName = "Plaintiff";
        private static string DefendantName = "Defendant";
        private static string ResolutionPlayerName = "Resolution";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed after main players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) DMSReplicationGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) DMSReplicationGamePlayers.Defendant, false, true),
                    new PlayerInfo(ResolutionPlayerName, (int) DMSReplicationGamePlayers.Resolution, true, false),
                };
        }

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddLinearBidDecisions(decisions);
            return decisions;
        }

        void AddLinearBidDecisions(List<Decision> decisions)
        {
            bool useAbbreviationsForSimplifiedGame = true;
            decisions.Add(new Decision("P_Slope", useAbbreviationsForSimplifiedGame ? "PSlope" : "PSL", true, (byte)DMSReplicationGamePlayers.Plaintiff, new byte[] { (byte)DMSReplicationGamePlayers.Plaintiff, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumSlopes, (byte)DMSReplicationGameDecisions.P_Slope));
            decisions.Add(new Decision("P_MinValue", useAbbreviationsForSimplifiedGame ? "P_MinValue" : "PMIN", true, (byte)DMSReplicationGamePlayers.Plaintiff, new byte[] { (byte)DMSReplicationGamePlayers.Plaintiff, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumMinValues, (byte)DMSReplicationGameDecisions.P_MinValue));
            decisions.Add(new Decision("P_TruncationPortion", useAbbreviationsForSimplifiedGame ? "P_TruncationPortion" : "PTR", true, (byte)DMSReplicationGamePlayers.Plaintiff, new byte[] { (byte)DMSReplicationGamePlayers.Plaintiff, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumTruncationPortions, (byte)DMSReplicationGameDecisions.P_TruncationPortion));
            decisions.Add(new Decision("D_Slope", useAbbreviationsForSimplifiedGame ? "DSlope" : "DSL", true, (byte)DMSReplicationGamePlayers.Defendant, new byte[] { (byte)DMSReplicationGamePlayers.Defendant, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumSlopes, (byte)DMSReplicationGameDecisions.D_Slope));
            decisions.Add(new Decision("D_MinValue", useAbbreviationsForSimplifiedGame ? "D_MinValue" : "DMIN", true, (byte)DMSReplicationGamePlayers.Defendant, new byte[] { (byte)DMSReplicationGamePlayers.Defendant, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumMinValues, (byte)DMSReplicationGameDecisions.D_MinValue));
            decisions.Add(new Decision("D_TruncationPortion", useAbbreviationsForSimplifiedGame ? "D_TruncationPortion" : "DTR", true, (byte)DMSReplicationGamePlayers.Defendant, new byte[] { (byte)DMSReplicationGamePlayers.Defendant, (byte)DMSReplicationGamePlayers.Resolution }, (byte)DMSReplicationGameOptions.NumTruncationPortions, (byte)DMSReplicationGameDecisions.D_TruncationPortion) { CanTerminateGame = true });
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            // IMPORTANT: Any decision that can terminate the game should be listed as CanTerminateGame = true. 
            // Second, the game should set Progress.GameComplete to true when this termination occurs. 
            // Third, this function should return true when that occurs.

            if (!currentDecision.CanTerminateGame)
                return false;
            return true;

        }
    }
}
