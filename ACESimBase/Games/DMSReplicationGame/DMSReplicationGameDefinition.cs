using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Statistical;
using ACESimBase.Util.Tikz;
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
                GameStructureSameForEachAction = true,
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

        public override IEnumerable<(string suffix, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            var series = new List<(string name, List<double> values)>();
            StringBuilder csvBuilder = new StringBuilder();
            
            var dataSeriesForRepeatedGraph = new List<List<List<(double weight, List<double?> values)>>>();
            dataSeriesForRepeatedGraph.Add(new List<List<(double weight, List<double?> values)>>()); // first row -- computational
            dataSeriesForRepeatedGraph.Add(new List<List<(double weight, List<double?> values)>>()); // second row -- analytical
            dataSeriesForRepeatedGraph[0].Add(new List<(double weight, List<double?> values)>()); // first row, first column -- computational, plaintiff
            dataSeriesForRepeatedGraph[0].Add(new List<(double weight, List<double?> values)>()); // first row, second column -- computational, defendant
            dataSeriesForRepeatedGraph[1].Add(new List<(double weight, List<double?> values)>()); // second row, first column -- analytical, plaintiff
            dataSeriesForRepeatedGraph[1].Add(new List<(double weight, List<double?> values)>()); // second row, second column -- analytical, defendant

            void AddSeries(string name, double weight, List<double> values, bool addToLatexGraph, bool computational, bool plaintiff)
            {
                csvBuilder.Append($"{name},{weight:0.#####}");
                foreach(double v in values)
                    csvBuilder.Append($",{v:0.#####}");
                csvBuilder.AppendLine();
                if (addToLatexGraph)
                {
                    var miniGraphDataSeriesCollection = dataSeriesForRepeatedGraph[computational ? 0 : 1][plaintiff ? 0 : 1];
                    miniGraphDataSeriesCollection.Add((weight, values.Select(x => (double?)x).ToList()));
                }
            }

            var zs = Enumerable.Range(0, 101).Select(x => EquallySpaced.GetLocationOfEquallySpacedPoint(x, 101, true)).ToList();
            AddSeries("z", 1.0, zs, false, false, false);

            int progressNum = 0;
            foreach (var progressAndWeight in gameProgresses)
            {
                progressNum++;
                var prog = progressAndWeight.theProgress as DMSReplicationGameProgress;
                var ps = zs.Select(z => prog.P(z)).ToList();
                AddSeries($"p{progressNum}", progressAndWeight.weight, ps, true, true, true);
                var ds = zs.Select(z => prog.D(z)).ToList();
                AddSeries($"d{progressNum}", progressAndWeight.weight, ds, true, true, false);
            }
            AdditiveEvidenceGame.DMSCalc calc = new AdditiveEvidenceGame.DMSCalc(Options.T, Options.C, Options.Q);
            var correct = calc.GetCorrectStrategiesPair(false);
            var correctBids  = zs.Select(z => calc.GetBids(z, z)).ToList();
            var p_corrects = correctBids.Select(x => x.pBid).ToList();
            var d_corrects = correctBids.Select(x => x.dBid).ToList();
            AddSeries("p*", 1.0, p_corrects, true, false, true);
            AddSeries("d*", 1.0, d_corrects, true, false, false);
            var p_correct_deltas = Enumerable.Range(1, p_corrects.Count - 1).Select(x => Math.Abs(p_corrects[x] - p_corrects[x - 1])).ToList();
            var d_correct_deltas = Enumerable.Range(1, d_corrects.Count - 1).Select(x => Math.Abs(d_corrects[x] - d_corrects[x - 1])).ToList();

            yield return ($" dms{supplementalString}.txt", csvBuilder.ToString());

            var lineGraphData = dataSeriesForRepeatedGraph.Select(majorRow => majorRow.Select(majorColumn => new TikzLineGraphData(majorColumn.Select(x => x.values).ToList(), majorColumn.Select(x => x.weight).Select(x => $"black, opacity={x}, line width=1mm, solid").ToList(), majorColumn.Select(x => "").ToList())).ToList()).ToList();

            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = new List<string>() { "P", "D" },
                majorXAxisLabel = "Party",
                majorYValueNames = new List<string>() { "Computational", "Analytical" },
                majorYAxisLabel = "Model",
                minorXValueNames = zs.Select(z => $"{(Math.Abs(z % 0.10) < 1E-5 || Math.Abs(z % 0.10 - 0.10) < 1E-5 ? z.ToString() : "")}").ToList(),
                minorXAxisLabel = "z",
                minorYValueNames = Enumerable.Range(0, 6).Select(y => $"{y * 0.2M}").ToList(),
                minorYAxisLabel = "Bid",
                graphType = TikzAxisSet.GraphType.Line,
                lineGraphData = lineGraphData,
            };
            string latexDoc = r.GetStandaloneDocument();

            if (p_correct_deltas.Max() < 0.01 && d_correct_deltas.Max() < 0.01) // for now, only include where there is no discontinuity
                yield return ($" dms{supplementalString}.tex", latexDoc);
        }

        public override string ToString()
        {
            return $"T: {Options.T}, C: {Options.C}, Q: {Options.Q}";
        }
    }
}
