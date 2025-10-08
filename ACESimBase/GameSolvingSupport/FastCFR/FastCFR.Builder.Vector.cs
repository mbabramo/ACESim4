#nullable enable
using System;
using System.Collections.Generic;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public partial class FastCFRBuilder
    {
        // ----------------------------
        // Static configuration
        // ----------------------------

        private static FastCFRVectorRegionOptions? s_VectorOptions;
        private static Func<ChanceNode, bool>? s_VectorAnchorSelector;

        public static void ConfigureVectorRegion(FastCFRVectorRegionOptions options, Func<ChanceNode, bool> anchorSelector)
        {
            s_VectorOptions = options ?? throw new ArgumentNullException(nameof(options));
            s_VectorAnchorSelector = anchorSelector ?? throw new ArgumentNullException(nameof(anchorSelector));
        }

        // ----------------------------
        // Vector region state
        // ----------------------------

        private bool _vectorRegionBuilt;
        private int _vectorWidth;
        private readonly List<FastCFRInformationSetVec> _vecInfosets = new();
        private readonly List<FastCFRChanceVec> _vecChances = new();

        // The vector region is compiled beneath the anchor chance. We partition anchor outcomes into groups.
        private FastCFRVectorAnchorShim? _vectorAnchorShim;

        // ----------------------------
        // Public iteration lifecycle (vector)
        // ----------------------------

        /// <summary>
        /// Freeze per-lane policies across all vector infosets for this iteration.
        /// Must be called once per iteration if a vector region was built.
        /// </summary>
        public FastCFRVecContext InitializeIterationVec(byte optimizedPlayerIndex, int[]? scenarioIndexByLane, Func<byte, double>? rand01ForDecision)
        {
            if (!_vectorRegionBuilt)
                throw new InvalidOperationException("Vector region not built.");

            // Determine the lane count from the anchor shim.
            int lanes = _vectorAnchorShim!.VectorWidth;

            // Prepare context storage.
            var reachSelf = new double[lanes];
            var reachOpp = new double[lanes];
            var reachChance = new double[lanes];
            var mask = new byte[lanes];
            var scn = scenarioIndexByLane ?? new int[lanes];

            for (int k = 0; k < lanes; k++)
            {
                reachSelf[k] = 1.0;
                reachOpp[k] = 1.0;
                reachChance[k] = 1.0;
                mask[k] = 1;
            }

            // Initialize nodes (freeze policies per lane) using each node's own backing-per-lane.
            foreach (var node in _vecInfosets)
            {
                int L = node.GetBackingForLane(0).NumPossibleActions; // used only to shape spans below

                var ownerByLane = new ReadOnlySpan<double>[lanes];
                var oppByLane = new ReadOnlySpan<double>[lanes];

                for (int k = 0; k < lanes; k++)
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
                node.InitializeIterationVec(Array.Empty<ReadOnlySpan<double>>(), Array.Empty<ReadOnlySpan<double>>());

            // Build and return the context
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

        /// <summary>
        /// Copy per-lane tallies from vector infosets into their lane-specific backing InformationSetNodes.
        /// Call after each iteration, in addition to CopyTalliesIntoBackingNodes() for the scalar path.
        /// </summary>
        public void CopyTalliesIntoBackingNodes_Vector()
        {
            if (!_vectorRegionBuilt)
                return;

            foreach (var node in _vecInfosets)
            {
                var backingLane0 = node.GetBackingForLane(0);
                int numActions = backingLane0.NumPossibleActions;

                for (int a = 0; a < numActions; a++)
                {
                    var sr = node.GetSumRegretTimesInversePi(a);
                    var si = node.GetSumInversePi(a);
                    var inc = node.GetLastCumulativeStrategyIncrements(a);

                    for (int k = 0; k < sr.Length; k++)
                    {
                        var backing = node.GetBackingForLane(k);
                        double rTimesInvPi = sr[k];
                        double invPi = si[k];
                        double incr = inc[k];

                        if (invPi != 0.0 || rTimesInvPi != 0.0)
                            backing.IncrementLastRegret((byte)(a + 1), rTimesInvPi, invPi);
                        if (incr != 0.0)
                            backing.IncrementLastCumulativeStrategyIncrements((byte)(a + 1), incr);
                    }
                }
            }
        }

        // ----------------------------
        // Anchor compilation entry
        // ----------------------------

        private Func<IFastCFRNode> CompileVectorAnchorShim(HistoryPoint anchorHp, ChanceNode anchorChance)
        {
            // Effective width from options/hardware
            _vectorWidth = FastCFRVecCapabilities.EffectiveVectorWidth(s_VectorOptions);
            if (_vectorWidth <= 1)
                throw new InvalidOperationException("Vector region requested but SIMD not available.");

            // Decision metadata
            byte decisionIndex = anchorHp.GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numOutcomes = (byte)decision.NumPossibleActions;

            // Partition outcomes into groups of lanes and compile a vector subtree for each group
            int groups = (numOutcomes + _vectorWidth - 1) / _vectorWidth;
            var roots = new IFastCFRNodeVec[groups];

            for (int g = 0; g < groups; g++)
            {
                int start = g * _vectorWidth;
                int lanes = Math.Min(_vectorWidth, numOutcomes - start);

                var laneHps = new HistoryPoint[lanes];
                for (int l = 0; l < lanes; l++)
                {
                    byte aOneBased = (byte)(start + l + 1);
                    laneHps[l] = anchorHp.GetBranch(_nav, aOneBased, decision, decisionIndex);
                }

                var vecAccessor = CompileVector(laneHps);
                roots[g] = vecAccessor();
            }

            // Finalize/bind vector children now that all node references exist
            FinalizeVectorNodeObjects();

            // Create a scalar node that will drive the vector subtree(s)
            _vectorAnchorShim = new FastCFRVectorAnchorShim(this, anchorChance, decisionIndex, numOutcomes, roots, _opts.UseDynamicChanceProbabilities, _numNonChancePlayers, _vectorWidth);
            _vectorRegionBuilt = true;

            // Return an accessor just like the scalar build methods do
            FastCFRVectorAnchorShim local = _vectorAnchorShim;
            return () => local;
        }

        // ----------------------------
        // Vector subtree compilation
        // ----------------------------

        private Func<IFastCFRNodeVec> CompileVector(HistoryPoint[] laneHps)
        {
            // We assume identical shape beneath the anchor (as per configuration).
            var state0 = laneHps[0].GetGameStatePrerecorded(_nav);
            if (state0 is null)
                state0 = _nav.GetGameState(in laneHps[0]);

            switch (state0)
            {
                case InformationSetNode isn:
                {
                    var isnByLane = new InformationSetNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                        isnByLane[k] = (InformationSetNode)(_nav.GetGameState(in laneHps[k]));
                    return CompileVectorInformationSet(laneHps, isnByLane);
                }
                case ChanceNode cn:
                {
                    var cnByLane = new ChanceNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                        cnByLane[k] = (ChanceNode)(_nav.GetGameState(in laneHps[k]));
                    return CompileVectorChance(laneHps, cnByLane);
                }
                case FinalUtilitiesNode fu:
                {
                    var fuByLane = new FinalUtilitiesNode[laneHps.Length];
                    for (int k = 0; k < laneHps.Length; k++)
                        fuByLane[k] = (FinalUtilitiesNode)(_nav.GetGameState(in laneHps[k]));
                    return CompileVectorFinal(laneHps, fuByLane);
                }
                default:
                    throw new NotImplementedException($"Unhandled vector node type {state0?.GetType().Name ?? "null"}");
            }
        }

        private Func<IFastCFRNodeVec> CompileVectorInformationSet(HistoryPoint[] laneHps, InformationSetNode[] isnByLane)
        {
            byte decisionIndex = laneHps[0].GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = (byte)decision.NumPossibleActions;

            var steps = new List<FastCFRVisitStepVec>(numActions);
            for (byte a = 1; a <= numActions; a++)
            {
                var nextLaneHps = new HistoryPoint[laneHps.Length];
                for (int k = 0; k < laneHps.Length; k++)
                    nextLaneHps[k] = laneHps[k].GetBranch(_nav, a, decision, decisionIndex);

                var childAccessor = CompileVector(nextLaneHps);
                steps.Add(new FastCFRVisitStepVec((byte)(a - 1), childAccessor));
            }

            var program = new FastCFRVisitProgramVec(steps.ToArray(), _numNonChancePlayers);

            // Node creation (use lane 0 for identity metadata)
            var ownerIndex = (byte)isnByLane[0].PlayerIndex;
            var node = new FastCFRInformationSetVec(ownerIndex, decisionIndex, numActions, _numNonChancePlayers, isnByLane, new[] { program });
            _vecInfosets.Add(node);

            return () => node;
        }

        private Func<IFastCFRNodeVec> CompileVectorChance(HistoryPoint[] laneHps, ChanceNode[] cnByLane)
        {
            byte decisionIndex = laneHps[0].GetNextDecisionIndex(_nav);
            var decision = _nav.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numOutcomes = (byte)decision.NumPossibleActions;

            var steps = new List<FastCFRChanceStepVec>(numOutcomes);
            for (byte a = 1; a <= numOutcomes; a++)
            {
                var nextLaneHps = new HistoryPoint[laneHps.Length];
                for (int k = 0; k < laneHps.Length; k++)
                    nextLaneHps[k] = laneHps[k].GetBranch(_nav, a, decision, decisionIndex);

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

        private Func<IFastCFRNodeVec> CompileVectorFinal(HistoryPoint[] laneHps, FinalUtilitiesNode[] fuByLane)
        {
            // Build [player][lane] utilities and [lane] custom arrays
            int lanes = laneHps.Length;

            // Use lane 0 to discover player count
            var (utils0, custom0) = ExtractFinalArrays(fuByLane[0], _numNonChancePlayers);
            int numPlayers = utils0[0].Length; // one scenario assumed
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

        private void FinalizeVectorNodeObjects()
        {
            foreach (var e in _vecInfosets)
                e.BindChildrenAfterFinalizeVec();
            foreach (var e in _vecChances)
                e.BindChildrenAfterFinalizeVec();
        }

        // ----------------------------
        // Scalar anchor shim
        // ----------------------------

        private sealed class FastCFRVectorAnchorShim : IFastCFRNode
        {
            private readonly FastCFRBuilder _owner;
            private readonly ChanceNode _anchorChance;
            private readonly byte _decisionIndex;
            private readonly byte _numOutcomes;
            private readonly IFastCFRNodeVec[] _rootsByGroup;
            private readonly bool _useDynamicChanceProbabilities;
            private readonly int _numPlayers;

            public int VectorWidth { get; }

            // Scratch reused across calls
            private readonly double[] _pLane;
            private readonly double[] _laneSelf;
            private readonly double[] _laneOpp;
            private readonly double[] _laneChance;
            private readonly byte[] _laneMask;
            private readonly int[] _laneScenarioIdx;
            private readonly double[] _expectedUWork; // [player]
            private readonly FloatSet _expectedCustomZero = default;

            public FastCFRVectorAnchorShim(
                FastCFRBuilder owner,
                ChanceNode anchorChance,
                byte decisionIndex,
                byte numOutcomes,
                IFastCFRNodeVec[] rootsByGroup,
                bool useDynamicChanceProbabilities,
                int numPlayers,
                int vectorWidth)
            {
                _owner = owner;
                _anchorChance = anchorChance;
                _decisionIndex = decisionIndex;
                _numOutcomes = numOutcomes;
                _rootsByGroup = rootsByGroup;
                _useDynamicChanceProbabilities = useDynamicChanceProbabilities;
                _numPlayers = numPlayers;
                VectorWidth = vectorWidth;

                _pLane = new double[vectorWidth];
                _laneSelf = new double[vectorWidth];
                _laneOpp = new double[vectorWidth];
                _laneChance = new double[vectorWidth];
                _laneMask = new byte[vectorWidth];
                _laneScenarioIdx = new int[vectorWidth];
                _expectedUWork = new double[numPlayers];
            }

            public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy, ReadOnlySpan<double> _opponentTraversal)
            {
                // No-op. Vector nodes are initialized via FastCFRBuilder.InitializeIterationVec()
            }

            public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
            {
                Array.Clear(_expectedUWork, 0, _expectedUWork.Length);
                var expectedCustom = default(FloatSet);

                int groups = _rootsByGroup.Length;
                int outcomeIndex = 1;

                for (int g = 0; g < groups; g++)
                {
                    var root = _rootsByGroup[g];
                    int lanes = Math.Min(VectorWidth, _numOutcomes - (g * VectorWidth));

                    // Prepare lane context
                    for (int k = 0; k < lanes; k++)
                    {
                        _laneSelf[k] = ctx.ReachSelf;
                        _laneOpp[k] = ctx.ReachOpp;
                        _laneChance[k] = ctx.ReachChance;
                        _laneMask[k] = 1;
                        _laneScenarioIdx[k] = 0; // scenario unused in this path
                    }
                    for (int k = lanes; k < VectorWidth; k++)
                    {
                        _laneMask[k] = 0;
                        _pLane[k] = 0.0;
                        _laneSelf[k] = _laneOpp[k] = _laneChance[k] = 0.0;
                        _laneScenarioIdx[k] = 0;
                    }

                    // Fill anchor probabilities per lane for this group
                    if (_useDynamicChanceProbabilities || !_anchorChance.AllProbabilitiesEqual())
                    {
                        for (int k = 0; k < lanes; k++)
                            _pLane[k] = _anchorChance.GetActionProbability((byte)(outcomeIndex + k));
                    }
                    else
                    {
                        double p = 1.0 / _numOutcomes;
                        for (int k = 0; k < lanes; k++)
                            _pLane[k] = p;
                    }

                    // Scale reaches for chance at anchor
                    FastCFRVecContext vctx = new FastCFRVecContext
                    {
                        IterationNumber = ctx.IterationNumber,
                        OptimizedPlayerIndex = ctx.OptimizedPlayerIndex,
                        SamplingCorrection = ctx.SamplingCorrection,
                        ReachSelf = _laneSelf.AsSpan(),
                        ReachOpp = _laneOpp.AsSpan(),
                        ReachChance = _laneChance.AsSpan(),
                        ActiveMask = _laneMask.AsSpan(),
                        ScenarioIndex = _laneScenarioIdx.AsSpan(),
                        Rand01ForDecision = ctx.Rand01ForDecision
                    };

                    FastCFRVecMath.ScaleInPlaceMasked(vctx.ReachOpp, _pLane, vctx.ActiveMask);
                    FastCFRVecMath.ScaleInPlaceMasked(vctx.ReachChance, _pLane, vctx.ActiveMask);

                    // Run vector subtree
                    var vecResult = root.GoVec(ref vctx);

                    // Accumulate expected value across lanes
                    for (int p = 0; p < _numPlayers; p++)
                        FastCFRVecMath.MulAccumulateMasked(vecResult.UtilitiesByPlayerByLane[p], _pLane, _laneMask, _expectedUWork);

                    for (int k = 0; k < lanes; k++)
                        if (_laneMask[k] != 0)
                            expectedCustom = expectedCustom.Plus(vecResult.CustomByLane[k].Times((float)_pLane[k]));

                    outcomeIndex += lanes;
                }

                return new FastCFRNodeResult(_expectedUWork, expectedCustom);
            }
        }
    }
}
