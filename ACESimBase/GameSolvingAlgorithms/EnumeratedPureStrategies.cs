using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{
    /// <summary>A finite, deterministic strategy domain. Actions must be specified at EVERY information set.</summary>
    public interface IEnumeratedPureStrategySpace
    {
        int Count(int player);
        string Id(int player, int strategy);
        byte Action(int player, int strategy, InformationSetNode informationSet);
        string DescriptionJson { get; }
    }

    [Serializable]
    public sealed class EnumeratedPureSettings
    {
        [NonSerialized] public Func<StrategiesDeveloperBase, IEnumeratedPureStrategySpace> SpaceFactory;
        public string OutputDirectory;
        public string ConfigurationJson;
        public string SourceStateJson;
        public int Workers = 1;
        public bool Resume;
        public double NumericalTolerance = 1E-9;
    }

    public sealed record PurePathStep(byte DecisionIndex, byte Action);
    public sealed record PureTerminal(int[] Rows, int[] Columns, double ChanceProbability,
        double PlaintiffUtility, double DefendantUtility, PurePathStep[] Path);
    public sealed record PureBestResponse(byte Player, int OpponentStrategy, string OpponentId,
        double Utility, byte[] Actions, double Seconds, string Fingerprint);

    /// <summary>
    /// Exhaustive two-player maximization over a supplied pure strategy domain. One serial game-tree
    /// traversal compiles chance-weighted terminal contributions. Matrix workers touch only immutable
    /// contributions and disjoint rows, never StrategiesDeveloperBase or a mutable game evaluator.
    /// </summary>
    [Serializable]
    public sealed class EnumeratedPureStrategies : StrategiesDeveloperBase
    {
        [NonSerialized] public IEnumeratedPureStrategySpace Space;
        [NonSerialized] public List<PureTerminal> Terminals;
        [NonSerialized] public PurePayoffMatrix Matrix;
        [NonSerialized] public PureMatrixScores Scores;
        [NonSerialized] public PureRunStore Store;
        public double InitializationSeconds;
        private InformationSetNode[] orderedNodes;
        private Dictionary<int, byte[]> actions;

        public EnumeratedPureStrategies(List<Strategy> strategies, EvolutionSettings settings, GameDefinition definition)
            : base(strategies, settings, definition) { }

        public override IStrategiesDeveloper DeepCopy() => throw new NotSupportedException(
            "Initialize a separate enumerated solver for each evaluator; only immutable matrix accumulation is parallel.");

        public override async Task Initialize()
        {
            var timer = Stopwatch.StartNew();
            if (NumNonChancePlayers != 2 || GameDefinition.Players.Where(p => !p.PlayerIsChance).Any(p => !p.HighestIsBest))
                throw new NotSupportedException("Enumerated pure strategies currently require two utility-maximizing players.");
            var settings = EvolutionSettings.EnumeratedPure ?? throw new ArgumentException("Supply EnumeratedPure settings and a strategy-space factory.");
            if (settings.SpaceFactory == null || settings.Workers < 1 || !double.IsFinite(settings.NumericalTolerance) || settings.NumericalTolerance < 0)
                throw new ArgumentException("Invalid enumerated solver settings.");
            // Match SequenceForm's initialized extensive game and its numerical representation.
            EvolutionSettings.ParallelOptimization = false;
            EvolutionSettings.UseAcceleratedBestResponse = false;
            GameDefinition.MakeAllChanceDecisionsKnowAllChanceActions();
            AllowSkipEveryPermutationInitialization = false;
            StoreGameStateNodesInLists = true;
            await base.Initialize();
            InitializeInformationSets(1);
            SetFinalUtilitiesToRoundedOffValues();
            foreach (var chance in ChanceNodes)
            {
                chance.GetProbabilitiesAsRationals(!EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes,
                    EvolutionSettings.MaxIntegralUtility);
                var probabilities = chance.GetActionProbabilities();
                if (probabilities.Any(p => !double.IsFinite(p) || p < 0) || Math.Abs(probabilities.Sum() - 1) > 1E-12)
                    throw new InvalidDataException("Invalid rounded chance distribution.");
            }
            Space = settings.SpaceFactory(this);
            orderedNodes = InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).ToArray();
            actions = orderedNodes.ToDictionary(n => n.InformationSetNodeNumber, n =>
                Enumerable.Range(0, Space.Count(n.PlayerIndex)).Select(s =>
                {
                    byte a = Space.Action(n.PlayerIndex, s, n);
                    if (a < 1 || a > n.NumPossibleActions || (n.Decision.AlwaysDoAction.HasValue && a != n.Decision.AlwaysDoAction.Value))
                        throw new InvalidDataException("Catalog supplied an invalid or omitted action.");
                    return a;
                }).ToArray());
            InitializationSeconds = timer.Elapsed.TotalSeconds;
        }

        public double[] Profile(int row, int column)
        {
            var profile = new List<double>();
            foreach (var node in orderedNodes)
            {
                byte selected = actions[node.InformationSetNodeNumber][node.PlayerIndex == 0 ? row : column];
                for (int a = 1; a <= node.NumPossibleActions; a++) profile.Add(a == selected ? 1.0 : 0.0);
            }
            return profile.ToArray();
        }

        public void CompileTerminals()
        {
            var compiler = new TerminalCompiler(this);
            TreeWalk_Tree(compiler);
            Terminals = compiler.Terminals;
        }

        public override Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            var timer = Stopwatch.StartNew();
            CompileTerminals();
            var settings = EvolutionSettings.EnumeratedPure;
            Store = PureRunStore.Open(settings, Space, Terminals);
            Matrix = Store.BuildOrLoadMatrix(Terminals, settings.Workers);
            Scores = Matrix.Score(settings.NumericalTolerance);
            Store.SaveScores(Scores);
            Store.Complete(InitializationSeconds, timer.Elapsed.TotalSeconds, Terminals.Count, settings.Workers);
            var best = Scores.OrderedProfiles().First();
            SetInformationSetsToEquilibrium(Profile(best.Row, best.Column));
            var report = new ReportCollection();
            string summary = $"Enumerated {Matrix.Rows} x {Matrix.Columns} pure profiles; minimum restricted maximum deviation gain {best.Epsilon:G17}. Numerical tolerance {settings.NumericalTolerance:G17}.";
            report.Add(summary, FormattableString.Invariant($"OptionSetName,Profiles,MinimumRestrictedGain\n{optionSetName},{Matrix.Rows * Matrix.Columns},{best.Epsilon:G17}\n"),
                integrateCSVReportsIfPossible: false, ifNotIntegratingAlwaysMakeSeparateReport: true);
            report.AddReportSuffix("EnumeratedPureSummary");
            return Task.FromResult(report);
        }

        public PureBestResponse VerifyOpponent(byte player, int opponent)
        {
            if (player > 1) throw new ArgumentOutOfRangeException(nameof(player));
            if (opponent < 0 || opponent >= Space.Count(1 - player)) throw new ArgumentOutOfRangeException(nameof(opponent));
            var cached = Store?.LoadBestResponse(player, opponent);
            if (cached != null) return cached;
            SetInformationSetsToEquilibrium(Profile(player == 0 ? 0 : opponent, player == 0 ? opponent : 0));
            var timer = Stopwatch.StartNew();
            // GEBR in BestResponse.cs optimizes deepest own decisions first at information sets,
            // ignoring own reach. Thus it can jointly change participation, exit, AND offers.
            double utility = CalculateBestResponse(player, ActionStrategies.CurrentProbability);
            var responseActions = orderedNodes.Where(n => n.PlayerIndex == player)
                .Select(n => n.BestResponseAction == 0 ? (byte)1 : n.BestResponseAction).ToArray();
            // A saved response is a complete pure strategy. Verify that replaying it achieves
            // the returned BR value in precisely the matrix's rounded extensive game.
            int responseOffset = 0;
            var responseProfile = new List<double>();
            foreach (var node in orderedNodes)
            {
                byte selected = node.PlayerIndex == player ? responseActions[responseOffset++] : actions[node.InformationSetNodeNumber][opponent];
                for (int a = 1; a <= node.NumPossibleActions; a++) responseProfile.Add(a == selected ? 1.0 : 0.0);
            }
            SetInformationSetsToEquilibrium(responseProfile.ToArray());
            double replay = GetAverageUtilities(false)[player];
            if (Math.Abs(replay - utility) > EvolutionSettings.EnumeratedPure.NumericalTolerance)
                throw new InvalidDataException("Best-response action profile does not replay to the reported utility.");
            var result = new PureBestResponse(player, opponent, Space.Id(1 - player, opponent), utility,
                responseActions, timer.Elapsed.TotalSeconds, Store?.Fingerprint);
            if (Matrix != null)
            {
                double restricted = player == 0 ? Scores.PlaintiffBestUtilities[opponent] : Scores.DefendantBestUtilities[opponent];
                if (utility + EvolutionSettings.EnumeratedPure.NumericalTolerance < restricted)
                    throw new InvalidDataException($"Unrestricted response {utility:G17} is below restricted {restricted:G17}.");
            }
            Store?.SaveBestResponse(result);
            return result;
        }

        private sealed record Frame(int[] Rows, int[] Columns, double Chance, PurePathStep[] Path);
        private sealed class TerminalCompiler : ITreeNodeProcessor<Frame, int>
        {
            private readonly EnumeratedPureStrategies solver;
            public readonly List<PureTerminal> Terminals = new();
            public TerminalCompiler(EnumeratedPureStrategies solver) { this.solver = solver; }
            private Frame Advance(IGameState predecessor, byte action, Frame frame)
            {
                if (predecessor == null) return new Frame(Enumerable.Range(0, solver.Space.Count(0)).ToArray(),
                    Enumerable.Range(0, solver.Space.Count(1)).ToArray(), 1, Array.Empty<PurePathStep>());
                byte index = predecessor is ChanceNode c ? c.DecisionIndex : ((InformationSetNode)predecessor).DecisionIndex;
                var path = frame.Path.Append(new PurePathStep(index, action)).ToArray();
                if (predecessor is ChanceNode chance)
                    return frame with { Chance = frame.Chance * chance.GetActionProbability(action), Path = path };
                var info = (InformationSetNode)predecessor;
                var choices = solver.actions[info.InformationSetNodeNumber];
                return info.PlayerIndex == 0
                    ? frame with { Rows = frame.Rows.Where(i => choices[i] == action).ToArray(), Path = path }
                    : frame with { Columns = frame.Columns.Where(i => choices[i] == action).ToArray(), Path = path };
            }
            public Frame ChanceNode_Forward(ChanceNode n, IGameState p, byte a, Frame f) => Advance(p, a, f);
            public Frame InformationSet_Forward(InformationSetNode n, IGameState p, byte a, Frame f) => Advance(p, a, f);
            public int ChanceNode_Backward(ChanceNode n, IEnumerable<int> s) { foreach (var _ in s) { } return 0; }
            public int InformationSet_Backward(InformationSetNode n, IEnumerable<int> s) { foreach (var _ in s) { } return 0; }
            public int FinalUtilities_TurnAround(FinalUtilitiesNode n, IGameState p, byte a, Frame f)
            {
                var frame = Advance(p, a, f);
                // Keep even incompatible terminals in the fingerprint: unrestricted verification uses them.
                Terminals.Add(new PureTerminal(frame.Rows, frame.Columns, frame.Chance,
                    n.Utilities[0], n.Utilities[1], frame.Path));
                return 0;
            }
        }
    }

    public sealed record PureProfileScore(int Row, int Column, double PlaintiffGain, double DefendantGain)
    {
        public double Epsilon => Math.Max(PlaintiffGain, DefendantGain);
    }

    public sealed class PurePayoffMatrix
    {
        public int Rows { get; }
        public int Columns { get; }
        public double[] Plaintiff { get; }
        public double[] Defendant { get; }
        public PurePayoffMatrix(int rows, int columns, double[] plaintiff = null, double[] defendant = null)
        {
            if (rows < 1 || columns < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            Rows = rows; Columns = columns;
            Plaintiff = plaintiff ?? new double[checked(rows * columns)];
            Defendant = defendant ?? new double[checked(rows * columns)];
            if (Plaintiff.Length != checked(rows * columns) || Defendant.Length != Plaintiff.Length)
                throw new ArgumentException("Invalid matrix dimensions.");
        }
        public PureMatrixScores Score(double tieTolerance = 1E-9) => new(this, tieTolerance);
    }

    public sealed class PureMatrixScores
    {
        public double[] PlaintiffBestUtilities { get; }
        public double[] DefendantBestUtilities { get; }
        public int[][] PlaintiffBestResponses { get; }
        public int[][] DefendantBestResponses { get; }
        public PurePayoffMatrix Matrix { get; }
        public double TieTolerance { get; }
        public PureMatrixScores(PurePayoffMatrix matrix, double tieTolerance)
        {
            if (!double.IsFinite(tieTolerance) || tieTolerance < 0 || matrix.Plaintiff.Concat(matrix.Defendant).Any(x => !double.IsFinite(x)))
                throw new ArgumentException("Nonfinite payoffs or invalid tie tolerance.");
            Matrix = matrix; TieTolerance = tieTolerance;
            PlaintiffBestUtilities = Enumerable.Range(0, matrix.Columns).Select(c => Enumerable.Range(0, matrix.Rows).Max(r => matrix.Plaintiff[r * matrix.Columns + c])).ToArray();
            DefendantBestUtilities = Enumerable.Range(0, matrix.Rows).Select(r => Enumerable.Range(0, matrix.Columns).Max(c => matrix.Defendant[r * matrix.Columns + c])).ToArray();
            PlaintiffBestResponses = Enumerable.Range(0, matrix.Columns).Select(c => Enumerable.Range(0, matrix.Rows).Where(r => PlaintiffBestUtilities[c] - matrix.Plaintiff[r * matrix.Columns + c] <= tieTolerance).ToArray()).ToArray();
            DefendantBestResponses = Enumerable.Range(0, matrix.Rows).Select(r => Enumerable.Range(0, matrix.Columns).Where(c => DefendantBestUtilities[r] - matrix.Defendant[r * matrix.Columns + c] <= tieTolerance).ToArray()).ToArray();
        }
        public PureProfileScore At(int row, int column)
        {
            int index = row * Matrix.Columns + column;
            return new(row, column, PlaintiffBestUtilities[column] - Matrix.Plaintiff[index], DefendantBestUtilities[row] - Matrix.Defendant[index]);
        }
        public IEnumerable<PureProfileScore> Profiles()
        {
            for (int r = 0; r < Matrix.Rows; r++) for (int c = 0; c < Matrix.Columns; c++) yield return At(r, c);
        }
        public IOrderedEnumerable<PureProfileScore> OrderedProfiles() => Profiles().OrderBy(p => p.Epsilon).ThenBy(p => p.Row).ThenBy(p => p.Column);
    }
}
