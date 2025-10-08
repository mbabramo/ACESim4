using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public enum FastCFRBuildMode { Full /* sampling can be added later via ctx.Rand01ForDecision */ }

    public sealed class FastCFRBuilderOptions
    {
        public FastCFRBuildMode Mode { get; set; } = FastCFRBuildMode.Full;
        public bool UseDynamicChanceProbabilities { get; set; } = true;
    }

    /// <summary>
    /// One-time compiler from HistoryPoint/Navigation to FastCFR runtime nodes with pre-recorded visit programs.
    /// </summary>
    public sealed class FastCFRBuilder
    {
        public IFastCFRNode Root => _rootAccessor();
        public IReadOnlyList<FastCFRInformationSet> FastInformationSets => _infoEntries.Select(e => e.NodeAccessor()).ToList();
        public IReadOnlyList<InformationSetNode> BackingInformationSets => _infoEntries.Select(e => e.Original).ToList();

        public FastCFRBuilder(HistoryNavigationInfo navigation, Func<HistoryPoint> rootFactory, FastCFRBuilderOptions options = null)
        {
            _nav = navigation ;
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _opts = options ?? new FastCFRBuilderOptions();

            _numNonChancePlayers = (byte)_nav.GameDefinition.Players.Count(p => !p.PlayerIsChance);

            var rootHP = _rootFactory();
            _rootAccessor = Compile(rootHP);
            FinalizeNodeObjects();
            AllocatePolicyBuffers();
        }

        // ---------------------------------------------------------------------
        // Public iteration lifecycle
        // ---------------------------------------------------------------------

        public FastCFRIterationContext InitializeIteration(
            byte optimizedPlayerIndex,
            int scenarioIndex,
            Func<byte, double> rand01ForDecision)
        {
            // Freeze policies into fast nodes and into local arrays used by reach-updating delegates.
            foreach (var entry in _infoEntries)
            {
                var isn = entry.Original;
                var node = entry.NodeAccessor();

                var owner = new double[isn.NumPossibleActions];
                var opp   = new double[isn.NumPossibleActions];

                // Owner's current policy (currentProbabilityDimension)
                {
                    Span<double> buf = owner;
                    isn.GetCurrentProbabilities(buf, opponentProbabilities: false);
                }
                // Opponent traversal policy (currentProbabilityForOpponentDimension)
                {
                    Span<double> buf = opp;
                    isn.GetCurrentProbabilities(buf, opponentProbabilities: true);
                }

                node.InitializeIteration(owner, opp);
                _frozenOwnerPolicies[entry.Id] = owner;
                _frozenOppPolicies[entry.Id]   = opp;
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

        /// <summary>
        /// Bulk-copy per-iteration tallies from FastCFRInformationSet nodes into the backing InformationSetNode instances.
        /// </summary>
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

        // ---------------------------------------------------------------------
        // Build (one pass)
        // ---------------------------------------------------------------------

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

            // Determine the decision index and decision instance at this point.
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

        public FastCFRVecContext InitializeIterationVec(
            byte optimizedPlayerIndex,
            int[]? scenarioIndexByLane,
            Func<byte, double>? rand01ForDecision)
        {
            if (!_vectorRegionBuilt)
                throw new InvalidOperationException("Vector region not built.");

            int lanes = _vectorAnchorShim!.VectorWidth;

            // Enforce single scenario across lanes if supplied
            int[] scn;
            if (scenarioIndexByLane is null)
            {
                scn = new int[lanes]; // zeros
            }
            else
            {
                if (scenarioIndexByLane.Length != lanes)
                    throw new ArgumentException("scenarioIndexByLane length mismatch.");
                int s0 = scenarioIndexByLane[0];
                for (int k = 1; k < lanes; k++)
                    if (scenarioIndexByLane[k] != s0)
                        throw new NotSupportedException("Vector region currently supports only a single scenario index across lanes.");
                scn = scenarioIndexByLane;
            }

            var reachSelf = new double[lanes];
            var reachOpp = new double[lanes];
            var reachChance = new double[lanes];
            var mask = new byte[lanes];
            for (int k = 0; k < lanes; k++)
            {
                reachSelf[k] = 1.0;
                reachOpp[k] = 1.0;
                reachChance[k] = 1.0;
                mask[k] = 1;
            }

            // Freeze per-lane policies for each vector infoset
            foreach (var node in _vecInfosets)
            {
                var ownerByLane = new double[lanes][];
                var oppByLane = new double[lanes][];

                for (int k = 0; k < lanes; k++)
                {
                    var backing = node.GetBackingForLane(k);
                    var owner = new double[backing.NumPossibleActions];
                    var opp   = new double[backing.NumPossibleActions];

                    backing.GetCurrentProbabilities(owner, opponentProbabilities: false);
                    backing.GetCurrentProbabilities(opp,   opponentProbabilities: true);

                    ownerByLane[k] = owner;
                    oppByLane[k]   = opp;
                }

                node.InitializeIterationVec(ownerByLane, oppByLane);
            }

            foreach (var node in _vecChances)
                node.InitializeIterationVec(Array.Empty<double[]>(), Array.Empty<double[]>()); // no-op

            return new FastCFRVecContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = optimizedPlayerIndex,
                SamplingCorrection = 1.0,
                ReachSelf = reachSelf,
                ReachOpp = reachOpp,
                ReachChance = reachChance,
                ActiveMask = mask,
                ScenarioIndex = scn,
                Rand01ForDecision = rand01ForDecision
            };
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
                    provider = (ref FastCFRIterationContext _) => cn.GetActionProbability(outcomeIndexOneBased);
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

        // ---------------------------------------------------------------------
        // Materialization
        // ---------------------------------------------------------------------

        private void FinalizeNodeObjects()
        {
            // Create FastCFRInformationSet objects now that all visit programs are known
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

            // Create FastCFRChance objects
            foreach (var e in _chanceEntries)
            {
                var visits = e.VisitPrograms.ToArray();
                var node = new FastCFRChance(
                    decisionIndex: e.Original.DecisionIndex,
                    numOutcomes: (byte)e.Original.Decision.NumPossibleActions,
                    visits: visits);
                e.NodeBacking = node;
            }

            // Bind children (must happen after all NodeBacking references are populated)
            foreach (var e in _infoEntries)
                e.NodeBacking.BindChildrenAfterFinalize();
            foreach (var e in _chanceEntries)
                e.NodeBacking.BindChildrenAfterFinalize();

            // Finals already created in GetOrCreateFinalEntry
        }

        private void AllocatePolicyBuffers()
        {
            int n = _infoEntries.Count;
            _frozenOwnerPolicies = new double[n][];
            _frozenOppPolicies = new double[n][];
        }

        // ---------------------------------------------------------------------
        // Entry bookkeeping
        // ---------------------------------------------------------------------

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
            // Try common shapes via reflection. Fallback to a single scenario if multi-scenario arrays are not present.
            var t = node.GetType();

            // Attempt UtilitiesByScenario : IEnumerable<double[]>
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

            // Fallback: Utilities : double[] for current scenario
            var utilsProp = t.GetProperty("Utilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            double[] single = utilsProp != null ? (double[])utilsProp.GetValue(node) : new double[numPlayers];
            var one = new[] { single };
            var customOne = ExtractCustomArray(node, t, 1);
            return (one, customOne);
        }

        private static FloatSet[] ExtractCustomArray(object node, Type t, int count)
        {
            // Try CustomResultsByScenario : IEnumerable<FloatSet>, else CustomResult : FloatSet, else default
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

        // ---------------------------------------------------------------------
        // Storage types
        // ---------------------------------------------------------------------

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

        // ---------------------------------------------------------------------
        // Fields
        // ---------------------------------------------------------------------

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

        private double[][] _frozenOwnerPolicies; // [infoId][action]
        private double[][] _frozenOppPolicies;   // [infoId][action]

        private readonly int _numNonChancePlayers;
    }
}
