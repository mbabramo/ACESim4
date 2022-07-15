using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    public partial class DMSReplicationGameDefinition : GameDefinition
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
                    new PlayerInfo("Chance", (int) DMSReplicationGamePlayers.Chance, true, false),
                };
        }

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            decisions.Add(new Decision("Chance", "C", true, (byte)DMSReplicationGamePlayers.Chance, new byte[] { (byte)DMSReplicationGamePlayers.Resolution }, 1 /* dummy decision -- only one possibility */, (byte)DMSReplicationGameDecisions.C_Dummy)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = true,
                CanTerminateGame = false
            });
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

        public override IEnumerable<(string filename, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            var prog = gameProgresses.First().theProgress as DMSReplicationGameProgress;
            var zs = Enumerable.Range(0, 100).Select(x => EquallySpaced.GetLocationOfEquallySpacedPoint(x, 101, true)).ToList();
            var ps = zs.Select(z => prog.P(z)).ToList();
            var ds = zs.Select(z => prog.D(z)).ToList();
            AdditiveEvidenceGame.DMSCalc calc = new AdditiveEvidenceGame.DMSCalc(Options.T, Options.C, Options.Q);
            var correct = calc.GetCorrectStrategiesPair(false);
            var correctBids  = zs.Select(z => calc.GetBids(z, z)).ToList();
            var p_corrects = correctBids.Select(x => x.pBid).ToList();
            var d_corrects = correctBids.Select(x => x.dBid).ToList();
            StringBuilder sb = new StringBuilder();
            foreach ((string name, List<double> values) row in new List<(string name, List<double> values)> { ("z", zs), ("p", ps), ("d", ds), ("p*", p_corrects), ("d*", d_corrects) })
            {
                sb.Append(row.name);
                foreach (double v in row.values)
                    sb.Append($",{v:0.#####}");
                sb.AppendLine("");
            }

            yield return (OptionSetName + $"-dms{supplementalString}.txt", sb.ToString());
        }

        public override string ToString()
        {
            return $"T: {Options.T}, C: {Options.C}, Q: {Options.Q}";
        }
    }
}
