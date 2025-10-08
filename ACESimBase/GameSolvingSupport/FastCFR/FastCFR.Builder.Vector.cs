#nullable enable

using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed partial class FastCFRBuilder
    {
        private FastCFRVectorRegionOptions? _vectorOptions;
        private Func<ChanceNode, bool>? _vectorAnchorSelector;

        private readonly List<FastCFRInformationSetVec> _vecInfosets = new List<FastCFRInformationSetVec>();
        private readonly List<FastCFRChanceVec> _vecChances = new List<FastCFRChanceVec>();
        private FastCFRVectorAnchorShim? _vectorAnchorShim;
        private bool _vectorRegionBuilt;
        private int _vectorWidth;

        public FastCFRBuilder(
            HistoryNavigationInfo navigation,
            Func<HistoryPoint> rootFactory,
            FastCFRBuilderOptions options,
            FastCFRVectorRegionOptions vectorOptions,
            Func<ChanceNode, bool> anchorSelector)
        {
            _nav = navigation;
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _opts = options ?? new FastCFRBuilderOptions();

            // Configure vector region BEFORE compiling the tree.
            if (vectorOptions != null && vectorOptions.EnableVectorRegion && anchorSelector != null)
                ConfigureVectorRegion(vectorOptions, anchorSelector); // defined in the vector partial

            _numNonChancePlayers = (byte)_nav.GameDefinition.Players.Count(p => !p.PlayerIsChance);

            var rootHP = _rootFactory();
            _rootAccessor = Compile(rootHP);     // may inject vector anchor shim here
            FinalizeNodeObjects();
            AllocatePolicyBuffers();
        }

        public void ConfigureVectorRegion(FastCFRVectorRegionOptions options, Func<ChanceNode, bool> anchorSelector)
        {
            _vectorOptions = options ?? throw new ArgumentNullException(nameof(options));
            _vectorAnchorSelector = anchorSelector ?? throw new ArgumentNullException(nameof(anchorSelector));
        }

        private Func<IFastCFRNode> CompileVectorAnchorShim(HistoryPoint anchorHp, ChanceNode anchorChance)
        {
            _vectorWidth = FastCFRVecCapabilities.EffectiveVectorWidth(_vectorOptions);
            if (_vectorWidth <= 1)
                throw new InvalidOperationException("Vector region requested but SIMD not available.");

            byte decisionIndex = anchorHp.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numOutcomes = (byte)decision.NumPossibleActions;

            int groups = (numOutcomes + _vectorWidth - 1) / _vectorWidth;
            var roots = new IFastCFRNodeVec[groups];

            // Compile per-group vector roots
            for (int g = 0; g < groups; g++)
            {
                int start = g * _vectorWidth;
                int lanes = Math.Min(_vectorWidth, numOutcomes - start);

                var laneHpsStored = new HistoryPointStorable[lanes];
                for (int l = 0; l < lanes; l++)
                {
                    byte aOneBased = (byte)(start + l + 1);
                    var nextHp = anchorHp.GetBranch(_nav, aOneBased, decision, decisionIndex);
                    laneHpsStored[l] = nextHp.ToStorable();
                }

                var vecAccessor = CompileVector(laneHpsStored);
                roots[g] = vecAccessor();
            }

            FinalizeVectorNodeObjects();

            _vectorAnchorShim = new FastCFRVectorAnchorShim(
                this,
                anchorChance,
                decisionIndex,
                numOutcomes,
                roots,
                _opts.UseDynamicChanceProbabilities,
                _numNonChancePlayers,
                _vectorWidth);

            _vectorRegionBuilt = true;

            FastCFRVectorAnchorShim local = _vectorAnchorShim;
            return () => local;
        }

        private void FinalizeVectorNodeObjects()
        {
            foreach (var e in _vecInfosets)
                e.BindChildrenAfterFinalize();
            foreach (var e in _vecChances)
                e.BindChildrenAfterFinalize();
        }

        private Func<IFastCFRNodeVec> CompileVector(HistoryPointStorable[] laneHps)
        {
            var hp0 = laneHps[0].ShallowCopyToRefStruct();
            var state0 = hp0.GetGameStatePrerecorded(_nav);
            if (state0 is null)
                state0 = _nav.GetGameState(in hp0);

            switch (state0)
            {
                case InformationSetNode:
                {
                    var isnByLane = new InformationSetNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                    {
                        var hp = laneHps[k].ShallowCopyToRefStruct();
                        isnByLane[k] = (InformationSetNode)_nav.GetGameState(in hp);
                    }
                    return CompileVectorInformationSet(laneHps, isnByLane);
                }
                case ChanceNode:
                {
                    var cnByLane = new ChanceNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                    {
                        var hp = laneHps[k].ShallowCopyToRefStruct();
                        cnByLane[k] = (ChanceNode)_nav.GetGameState(in hp);
                    }
                    return CompileVectorChance(laneHps, cnByLane);
                }
                case FinalUtilitiesNode:
                {
                    var fuByLane = new FinalUtilitiesNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                    {
                        var hp = laneHps[k].ShallowCopyToRefStruct();
                        fuByLane[k] = (FinalUtilitiesNode)_nav.GetGameState(in hp);
                    }
                    return CompileVectorFinal(laneHps, fuByLane);
                }
                default:
                    throw new NotImplementedException($"Unhandled vector node type {state0?.GetType().Name ?? "null"}");
            }
        }

        private Func<IFastCFRNodeVec> CompileVectorInformationSet(HistoryPointStorable[] laneHps, InformationSetNode[] isnByLane)
        {
            var hp0 = laneHps[0].ShallowCopyToRefStruct();
            byte decisionIndex = hp0.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = (byte)decision.NumPossibleActions;

            var steps = new List<FastCFRVisitStepVec>(numActions);
            for (byte a = 1; a <= numActions; a++)
            {
                var nextLaneHps = new HistoryPointStorable[laneHps.Length];
                for (int k = 0; k < laneHps.Length; k++)
                {
                    var hp = laneHps[k].ShallowCopyToRefStruct();
                    var next = hp.GetBranch(_nav, a, decision, decisionIndex).ToStorable();
                    nextLaneHps[k] = next;
                }
                var childAccessor = CompileVector(nextLaneHps);
                steps.Add(new FastCFRVisitStepVec((byte)(a - 1), childAccessor));
            }

            var program = new FastCFRVisitProgramVec(steps.ToArray(), _numNonChancePlayers);
            var ownerIndex = (byte)isnByLane[0].PlayerIndex;
            var node = new FastCFRInformationSetVec(ownerIndex, decisionIndex, numActions, _numNonChancePlayers, isnByLane, new[] { program });
            _vecInfosets.Add(node);
            return () => node;
        }

        private Func<IFastCFRNodeVec> CompileVectorChance(HistoryPointStorable[] laneHps, ChanceNode[] cnByLane)
        {
            var hp0 = laneHps[0].ShallowCopyToRefStruct();
            byte decisionIndex = hp0.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numOutcomes = (byte)decision.NumPossibleActions;

            var steps = new List<FastCFRChanceStepVec>(numOutcomes);
            for (byte a = 1; a <= numOutcomes; a++)
            {
                var nextLaneHps = new HistoryPointStorable[laneHps.Length];
                for (int k = 0; k < laneHps.Length; k++)
                {
                    var hp = laneHps[k].ShallowCopyToRefStruct();
                    var next = hp.GetBranch(_nav, a, decision, decisionIndex).ToStorable();
                    nextLaneHps[k] = next;
                }

                var childAccessor = CompileVector(nextLaneHps);

                FastCFRProbProviderVec? provider = null;
                double staticP = 0.0;

                if (_opts.UseDynamicChanceProbabilities || cnByLane[0].AllProbabilitiesEqual() == false)
                {
                    byte outcomeIndexOneBased = a;
                    provider = (ref FastCFRVecContext _ctx, byte _a, Span<double> pLane) =>
                    {
                        for (int k = 0; k < pLane.Length; k++)
                            pLane[k] = cnByLane[k].GetActionProbability(outcomeIndexOneBased);
                    };
                }
                else
                {
                    staticP = 1.0 / numOutcomes;
                }

                steps.Add(provider is null
                    ? new FastCFRChanceStepVec(a, childAccessor, staticP)
                    : new FastCFRChanceStepVec(a, childAccessor, provider));
            }

            var program = new FastCFRChanceVisitProgramVec(steps.ToArray(), _numNonChancePlayers);
            var node = new FastCFRChanceVec(decisionIndex, numOutcomes, _numNonChancePlayers, new[] { program });
            _vecChances.Add(node);
            return () => node;
        }

        private Func<IFastCFRNodeVec> CompileVectorFinal(HistoryPointStorable[] laneHps, FinalUtilitiesNode[] fuByLane)
        {
            int lanes = laneHps.Length;

            var (utils0, custom0) = ExtractFinalArrays(fuByLane[0], _numNonChancePlayers);
            int numPlayers = utils0[0].Length; // single scenario assumed
            var utilsByPlayerByLane = new double[numPlayers][];
            for (int p = 0; p < numPlayers; p++)
                utilsByPlayerByLane[p] = new double[lanes];

            var customByLane = new FloatSet[lanes];

            for (int k = 0; k < lanes; k++)
            {
                var (utilsK, customK) = ExtractFinalArrays(fuByLane[k], _numNonChancePlayers);
                var utils = utilsK.Length == 0 ? Array.Empty<double>() : utilsK[0];
                for (int p = 0; p < numPlayers; p++)
                    utilsByPlayerByLane[p][k] = p < utils.Length ? utils[p] : 0.0;

                customByLane[k] = (customK.Length == 0 ? default : customK[0]);
            }

            var node = new FastCFRFinalVec(utilsByPlayerByLane, customByLane);
            return () => node;
        }

        // New: initialize ONLY the vector nodes once per sweep (no context allocations).
        internal void InitializeVectorNodesForSweep(byte optimizedPlayerIndex)
        {
            if (!_vectorRegionBuilt)
                throw new InvalidOperationException("Vector region not built.");

            if (_vecInitDoneThisSweep)
                return;

            foreach (var node in _vecInfosets)
            {
                int ln = node.LaneCount;
                var ownerByLane = new double[ln][];
                var oppByLane = new double[ln][];

                for (int k = 0; k < ln; k++)
                {
                    var backing = node.GetBackingForLane(k);
                    var owner = new double[backing.NumPossibleActions];
                    var opp = new double[backing.NumPossibleActions];

                    backing.GetCurrentProbabilities(owner, opponentProbabilities: false);
                    backing.GetCurrentProbabilities(opp, opponentProbabilities: true);

                    ownerByLane[k] = owner;
                    oppByLane[k] = opp;
                }

                node.InitializeIterationVec(ownerByLane, oppByLane);
            }

            foreach (var node in _vecChances)
                node.InitializeIterationVec(Array.Empty<double[]>(), Array.Empty<double[]>());

            _vecInitDoneThisSweep = true;
        }

        public FastCFRVecContext InitializeIterationVec(
            byte optimizedPlayerIndex,
            int[]? scenarioIndexByLane,
            Func<byte, double>? rand01ForDecision)
        {
            if (!_vectorRegionBuilt)
                throw new InvalidOperationException("Vector region not built.");

            int lanes = scenarioIndexByLane?.Length ?? _vectorAnchorShim!.VectorWidth;

            int[] scn;
            if (scenarioIndexByLane is null)
            {
                scn = new int[lanes];
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

            // Initialize all vector nodes ONCE per sweep; subsequent calls just create a fresh context.
            if (!_vecInitDoneThisSweep)
            {
                foreach (var node in _vecInfosets)
                {
                    int ln = node.LaneCount;
                    var ownerByLane = new double[ln][];
                    var oppByLane = new double[ln][];

                    for (int k = 0; k < ln; k++)
                    {
                        var backing = node.GetBackingForLane(k);
                        var owner = new double[backing.NumPossibleActions];
                        var opp = new double[backing.NumPossibleActions];

                        backing.GetCurrentProbabilities(owner, opponentProbabilities: false);
                        backing.GetCurrentProbabilities(opp, opponentProbabilities: true);

                        ownerByLane[k] = owner;
                        oppByLane[k] = opp;
                    }

                    node.InitializeIterationVec(ownerByLane, oppByLane);
                }

                foreach (var node in _vecChances)
                    node.InitializeIterationVec(Array.Empty<double[]>(), Array.Empty<double[]>());

                _vecInitDoneThisSweep = true;
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

        public void CopyTalliesIntoBackingNodes_Vector()
        {
            foreach (var v in _vecInfosets)
                v.FlushTalliesToBacking();

            // Mark the end of the current vector sweep so the next call re-initializes nodes.
            _vecInitDoneThisSweep = false;
        }
    }

    internal sealed class FastCFRVectorAnchorShim : IFastCFRNode
    {
        private readonly FastCFRBuilder _builder;
        private readonly ChanceNode _anchorChance;
        private readonly byte _decisionIndex;
        private readonly byte _numOutcomes;
        private readonly IFastCFRNodeVec[] _rootsByGroup;
        private readonly bool _useDynamicProbs;
        private readonly int _numPlayers;
        public readonly int VectorWidth;

        // Per-group pooled buffers (eliminate per-call allocations at the anchor)
        private readonly GroupContext[] _groupContexts;

        private sealed class GroupContext
        {
            public readonly int Lanes;
            public readonly double[] ReachSelf;
            public readonly double[] ReachOpp;
            public readonly double[] ReachChance;
            public readonly byte[] ActiveMask;
            public readonly int[] ScenarioIndex;
            public readonly double[] PLaneScratch;

            public GroupContext(int lanes)
            {
                Lanes = lanes;
                ReachSelf = new double[lanes];
                ReachOpp = new double[lanes];
                ReachChance = new double[lanes];
                ActiveMask = new byte[lanes];
                ScenarioIndex = new int[lanes];
                PLaneScratch = new double[lanes];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ResetFromScalar(ref FastCFRIterationContext scalar, bool suppressMath)
            {
                for (int k = 0; k < Lanes; k++)
                {
                    ReachSelf[k] = scalar.ReachSelf;
                    ReachOpp[k] = scalar.ReachOpp;
                    ReachChance[k] = scalar.ReachChance;
                    ScenarioIndex[k] = scalar.ScenarioIndex;
                    ActiveMask[k] = suppressMath ? (byte)0 : (byte)1;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public FastCFRVecContext MakeContext(byte optimizedPlayerIndex, Func<byte, double>? rand)
            {
                return new FastCFRVecContext
                {
                    IterationNumber = 0,
                    OptimizedPlayerIndex = optimizedPlayerIndex,
                    SamplingCorrection = 1.0,
                    ReachSelf = ReachSelf,
                    ReachOpp = ReachOpp,
                    ReachChance = ReachChance,
                    ActiveMask = ActiveMask,
                    ScenarioIndex = ScenarioIndex,
                    Rand01ForDecision = rand
                };
            }
        }

        public FastCFRVectorAnchorShim(
            FastCFRBuilder builder,
            ChanceNode anchorChance,
            byte decisionIndex,
            byte numOutcomes,
            IFastCFRNodeVec[] rootsByGroup,
            bool useDynamicProbs,
            int numPlayers,
            int vectorWidth)
        {
            _builder = builder;
            _anchorChance = anchorChance;
            _decisionIndex = decisionIndex;
            _numOutcomes = numOutcomes;
            _rootsByGroup = rootsByGroup;
            _useDynamicProbs = useDynamicProbs || !anchorChance.AllProbabilitiesEqual();
            _numPlayers = numPlayers;
            VectorWidth = vectorWidth;

            _groupContexts = new GroupContext[_rootsByGroup.Length];
            for (int g = 0; g < _groupContexts.Length; g++)
            {
                int lanes = Math.Min(VectorWidth, _numOutcomes - g * VectorWidth);
                _groupContexts[g] = new GroupContext(lanes);
            }
        }

        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy, ReadOnlySpan<double> _oppTraversal) { }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            // Ensure vector nodes are (re)initialized exactly once per sweep.
            _builder.InitializeVectorNodesForSweep(ctx.OptimizedPlayerIndex);

            var expectedU = new double[_numPlayers];
            FloatSet expectedCustom = default;

            int outcomeIndex = 0;

            for (int g = 0; g < _rootsByGroup.Length; g++)
            {
                var gc = _groupContexts[g];
                int lanes = gc.Lanes;

                // Seed per-lane arrays from the scalar context; set mask.
                gc.ResetFromScalar(ref ctx, ctx.SuppressMath);

                // Fill anchor probabilities into pooled scratch and update reaches.
                for (int l = 0; l < lanes; l++)
                {
                    int outcomeOneBased = outcomeIndex + 1;
                    double p = _useDynamicProbs
                        ? _anchorChance.GetActionProbability(outcomeOneBased)
                        : (1.0 / _numOutcomes);
                    gc.PLaneScratch[l] = p;
                    outcomeIndex++;
                }

                for (int k = 0; k < lanes; k++)
                {
                    if (gc.ActiveMask[k] == 0) continue;
                    double p = gc.PLaneScratch[k];
                    gc.ReachOpp[k] *= p;
                    gc.ReachChance[k] *= p;
                }

                var vecCtx = gc.MakeContext(ctx.OptimizedPlayerIndex, ctx.Rand01ForDecision);
                var result = _rootsByGroup[g].GoVec(ref vecCtx);

                if (!ctx.SuppressMath)
                {
                    for (int k = 0; k < lanes; k++)
                    {
                        if (gc.ActiveMask[k] == 0) continue;
                        double p = gc.PLaneScratch[k];
                        for (int pl = 0; pl < _numPlayers; pl++)
                            expectedU[pl] += p * result.UtilitiesByPlayerByLane[pl][k];
                        expectedCustom = expectedCustom.Plus(result.CustomByLane[k].Times((float)p));
                    }
                }
            }

            return new FastCFRNodeResult(expectedU, expectedCustom);
        }
    }
}
#nullable restore
