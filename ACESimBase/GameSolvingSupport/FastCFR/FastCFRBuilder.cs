using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public enum FastCFRBuildMode { Full }

    public sealed class FastCFRBuilderOptions
    {
        public FastCFRBuildMode Mode { get; set; } = FastCFRBuildMode.Full;
        public bool UseDynamicChanceProbabilities { get; set; } = true;
    }

    public sealed partial class FastCFRBuilder
    {
        public IFastCFRNode Root => _rootAccessor();
        public IReadOnlyList<FastCFRInformationSet> FastInformationSets => _infoEntries.Select(e => e.NodeAccessor()).ToList();
        public IReadOnlyList<InformationSetNode> BackingInformationSets => _infoEntries.Select(e => e.Original).ToList();

        public FastCFRBuilder(HistoryNavigationInfo navigation, Func<HistoryPoint> rootFactory, FastCFRBuilderOptions options = null)
        {
            _nav = navigation;
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _opts = options ?? new FastCFRBuilderOptions();

            _numNonChancePlayers = (byte)_nav.GameDefinition.Players.Count(p => !p.PlayerIsChance);

            var rootHP = _rootFactory();
            _rootAccessor = Compile(rootHP);
            FinalizeNodeObjects();
            AllocatePolicyBuffers();
        }

        public FastCFRIterationContext InitializeIteration(
            byte optimizedPlayerIndex,
            int scenarioIndex,
            Func<byte, double> rand01ForDecision)
        {
            foreach (var entry in _infoEntries)
            {
                var isn = entry.Original;
                var node = entry.NodeAccessor();

                var owner = new double[isn.NumPossibleActions];
                var opp = new double[isn.NumPossibleActions];

                Span<double> ownerSpan = owner;
                isn.GetCurrentProbabilities(ownerSpan, opponentProbabilities: false);

                Span<double> oppSpan = opp;
                isn.GetCurrentProbabilities(oppSpan, opponentProbabilities: true);

                node.InitializeIteration(owner, opp);
                _frozenOwnerPolicies[entry.Id] = owner;
                _frozenOppPolicies[entry.Id] = opp;
            }

            foreach (var c in _chanceEntries)
                c.NodeAccessor().InitializeIteration(ReadOnlySpan<double>.Empty, ReadOnlySpan<double>.Empty);

            foreach (var f in _finalEntries)
                f.NodeAccessor().InitializeIteration(ReadOnlySpan<double>.Empty, ReadOnlySpan<double>.Empty);

            return new FastCFRIterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = optimizedPlayerIndex,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                ScenarioIndex = scenarioIndex,
                SuppressMath = false,
                Rand01ForDecision = rand01ForDecision ?? (_ => 0.0)
            };
        }

        public void CopyTalliesIntoBackingNodes()
        {
            foreach (var entry in _infoEntries)
            {
                var node = entry.NodeAccessor();
                var backing = entry.Original;
                var sr = node.SumRegretTimesInversePi;
                var si = node.SumInversePi;
                var inc = node.LastCumulativeStrategyIncrements;
                int n = backing.NumPossibleActions;
                for (int a = 0; a < n; a++)
                {
                    byte act = (byte)(a + 1);
                    double rTimesInvPi = sr[a];
                    double invPi = si[a];
                    double incr = inc[a];
                    if (invPi != 0 || rTimesInvPi != 0)
                        backing.IncrementLastRegret(act, rTimesInvPi, invPi);
                    if (incr != 0)
                        backing.IncrementLastCumulativeStrategyIncrements(act, incr);
                }
            }
        }

        private Func<IFastCFRNode> Compile(HistoryPoint hp)
        {
            var state = hp.GetGameStatePrerecorded(_nav);
            if (state is null)
                state = _nav.GetGameState(in hp);

            switch (state)
            {
                case InformationSetNode isn:
                    return CompileInformationSet(hp, isn);

                case ChanceNode cn:
                    return CompileChance(hp, cn);

                case FinalUtilitiesNode fu:
                    return CompileFinal(hp, fu);

                default:
                    throw new NotImplementedException($"Unhandled node type {state?.GetType().Name ?? "null"}");
            }
        }

        private Func<IFastCFRNode> CompileInformationSet(HistoryPoint hp, InformationSetNode isn)
        {
            var entry = GetOrCreateInfoEntry(isn);

            byte decisionIndex = hp.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = decision.NumPossibleActions;

            var steps = new List<FastCFRVisitStep>(numActions);
            for (byte a = 1; a <= numActions; a++)
            {
                var childHP = hp.GetBranch(_nav, a, decision, decisionIndex);
                var childAccessor = Compile(childHP);

                steps.Add(new FastCFRVisitStep(
                    FastCFRVisitStepKind.ChildForAction,
                    (byte)(a - 1),
                    childAccessor));
            }

            var program = new FastCFRVisitProgram(steps.ToArray(), _numNonChancePlayers);
            entry.VisitPrograms.Add(program);

            return entry.NodeAccessor;
        }

        private Func<IFastCFRNode> CompileChance(HistoryPoint hp, ChanceNode cn)
        {
            if (!_vectorRegionBuilt && _vectorOptions?.EnableVectorRegion == true && _vectorAnchorSelector != null && _vectorAnchorSelector(cn))
                return CompileVectorAnchorShim(hp, cn);

            var entry = GetOrCreateChanceEntry(cn);
            byte decisionIndex = hp.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numOutcomes = decision.NumPossibleActions;

            var steps = new List<FastCFRChanceStep>(numOutcomes);
            for (byte a = 1; a <= numOutcomes; a++)
            {
                var childHP = hp.GetBranch(_nav, a, decision, decisionIndex);
                var childAccessor = Compile(childHP);

                FastCFRProbProvider provider = null;
                double staticP = 0.0;

                if (_opts.UseDynamicChanceProbabilities || cn.AllProbabilitiesEqual() == false)
                {
                    int outcomeIndexOneBased = a;
                    provider = (ref FastCFRIterationContext _)
                        => cn.GetActionProbability(outcomeIndexOneBased);
                }
                else
                {
                    staticP = 1.0 / numOutcomes;
                }

                steps.Add(provider is null
                    ? new FastCFRChanceStep(childAccessor, staticP)
                    : new FastCFRChanceStep(childAccessor, provider));
            }

            var program = new FastCFRChanceVisitProgram(steps.ToArray(), _numNonChancePlayers);
            entry.VisitPrograms.Add(program);

            return entry.NodeAccessor;
        }

        private Func<IFastCFRNode> CompileFinal(HistoryPoint hp, FinalUtilitiesNode fu)
        {
            var entry = GetOrCreateFinalEntry(fu);
            return entry.NodeAccessor;
        }

        private void FinalizeNodeObjects()
        {
            foreach (var e in _infoEntries)
            {
                var visits = e.VisitPrograms.ToArray();
                var node = new FastCFRInformationSet(
                    playerIndex: e.Original.PlayerIndex,
                    decisionIndex: e.Original.DecisionIndex,
                    numActions: (byte)e.Original.NumPossibleActions,
                    visits: visits);
                e.NodeBacking = node;
            }

            foreach (var e in _chanceEntries)
            {
                var visits = e.VisitPrograms.ToArray();
                var node = new FastCFRChance(
                    decisionIndex: e.Original.DecisionIndex,
                    numOutcomes: (byte)e.Original.Decision.NumPossibleActions,
                    visits: visits);
                e.NodeBacking = node;
            }

            foreach (var e in _infoEntries)
                e.NodeBacking.BindChildrenAfterFinalize();
            foreach (var e in _chanceEntries)
                e.NodeBacking.BindChildrenAfterFinalize();
        }

        private void AllocatePolicyBuffers()
        {
            int n = _infoEntries.Count;
            _frozenOwnerPolicies = new double[n][];
            _frozenOppPolicies = new double[n][];
        }

        private InfoEntry GetOrCreateInfoEntry(InformationSetNode n)
        {
            if (_infoMap.TryGetValue(n, out var entry))
                return entry;

            entry = new InfoEntry
            {
                Id = _infoEntries.Count,
                Original = n,
                VisitPrograms = new List<FastCFRVisitProgram>(),
                NodeAccessor = () => entry.NodeBacking
            };
            _infoEntries.Add(entry);
            _infoMap[n] = entry;
            return entry;
        }

        private ChanceEntry GetOrCreateChanceEntry(ChanceNode n)
        {
            if (_chanceMap.TryGetValue(n, out var entry))
                return entry;

            entry = new ChanceEntry
            {
                Original = n,
                VisitPrograms = new List<FastCFRChanceVisitProgram>(),
                NodeAccessor = () => entry.NodeBacking
            };
            _chanceEntries.Add(entry);
            _chanceMap[n] = entry;
            return entry;
        }

        private FinalEntry GetOrCreateFinalEntry(FinalUtilitiesNode n)
        {
            if (_finalMap.TryGetValue(n, out var entry))
                return entry;

            var (utilsByScenario, customByScenario) = ExtractFinalArrays(n, _numNonChancePlayers);

            entry = new FinalEntry
            {
                Original = n,
                NodeBacking = new FastCFRFinal(utilsByScenario, customByScenario),
                NodeAccessor = () => entry.NodeBacking
            };
            _finalEntries.Add(entry);
            _finalMap[n] = entry;
            return entry;
        }

        private static (double[][] utils, FloatSet[] custom) ExtractFinalArrays(FinalUtilitiesNode node, int numPlayers)
        {
            var t = node.GetType();

            var utilsByScProp = t.GetProperty("UtilitiesByScenario", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (utilsByScProp != null)
            {
                if (utilsByScProp.GetValue(node) is System.Collections.IEnumerable seq)
                {
                    var list = new List<double[]>();
                    foreach (var item in seq)
                        list.Add((double[])item);
                    var customBySc = ExtractCustomArray(node, t, list.Count);
                    return (list.ToArray(), customBySc);
                }
            }

            var utilsProp = t.GetProperty("Utilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            double[] single = utilsProp != null ? (double[])utilsProp.GetValue(node) : new double[numPlayers];
            var one = new[] { single };
            var customOne = ExtractCustomArray(node, t, 1);
            return (one, customOne);
        }

        private static FloatSet[] ExtractCustomArray(object node, Type t, int count)
        {
            var bySc = t.GetProperty("CustomResultsByScenario", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (bySc != null && bySc.GetValue(node) is System.Collections.IEnumerable seq)
            {
                var list = new List<FloatSet>();
                foreach (var item in seq)
                    list.Add((FloatSet)item);
                return list.ToArray();
            }
            var singleProp = t.GetProperty("CustomResult", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (singleProp != null)
                return new[] { (FloatSet)singleProp.GetValue(node) };
            return Enumerable.Repeat(default(FloatSet), count).ToArray();
        }

        private sealed class InfoEntry
        {
            public int Id;
            public InformationSetNode Original;
            public List<FastCFRVisitProgram> VisitPrograms;
            public FastCFRInformationSet NodeBacking;
            public Func<FastCFRInformationSet> NodeAccessor;
        }

        private sealed class ChanceEntry
        {
            public ChanceNode Original;
            public List<FastCFRChanceVisitProgram> VisitPrograms;
            public FastCFRChance NodeBacking;
            public Func<FastCFRChance> NodeAccessor;
        }

        private sealed class FinalEntry
        {
            public FinalUtilitiesNode Original;
            public FastCFRFinal NodeBacking;
            public Func<FastCFRFinal> NodeAccessor;
        }

        private readonly HistoryNavigationInfo _nav;
        private readonly FastCFRBuilderOptions _opts;
        private readonly Func<HistoryPoint> _rootFactory;
        private Func<IFastCFRNode> _rootAccessor;

        private readonly Dictionary<InformationSetNode, InfoEntry> _infoMap = new Dictionary<InformationSetNode, InfoEntry>();
        private readonly Dictionary<ChanceNode, ChanceEntry> _chanceMap = new Dictionary<ChanceNode, ChanceEntry>();
        private readonly Dictionary<FinalUtilitiesNode, FinalEntry> _finalMap = new Dictionary<FinalUtilitiesNode, FinalEntry>();

        private readonly List<InfoEntry> _infoEntries = new List<InfoEntry>();
        private readonly List<ChanceEntry> _chanceEntries = new List<ChanceEntry>();
        private readonly List<FinalEntry> _finalEntries = new List<FinalEntry>();

        private double[][] _frozenOwnerPolicies;
        private double[][] _frozenOppPolicies;

        private readonly int _numNonChancePlayers;
    }
}
