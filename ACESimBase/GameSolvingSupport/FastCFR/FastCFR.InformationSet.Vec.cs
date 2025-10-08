#nullable enable
using System;
using System.Runtime.CompilerServices;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRInformationSetVec : IFastCFRNodeVec
    {
        public readonly byte PlayerIndex;
        public readonly byte DecisionIndex;
        public readonly byte NumActions;

        private readonly int _numPlayers;
        private readonly FastCFRVisitProgramVec[] _visitsVec;
        private IFastCFRNodeVec[][]? _childrenByVisit;
        private int _visitCounter;

        private readonly InformationSetNode[] _backingPerLane;

        private double[][]? _pSelfByLane; // [lane][action]
        private double[][]? _pOppByLane;  // [lane][action]

        private double[][]? _sumRegretTimesInversePi;      // [action][lane]
        private double[][]? _sumInversePi;                 // [action][lane]
        private double[][]? _lastCumulativeStrategyInc;    // [action][lane]

        private double[][]? _qaByActionByLane;             // [action][lane]
        private double[][]? _expectedUByPlayerByLane;      // [player][lane]
        private FloatSet[]? _expectedCustomByLane;         // [lane]

        private double[]? _scratchWeightsByLane;           // [lane]
        private byte[]? _scratchMaskSaved;                 // [lane]
        private byte[]? _scratchMaskChild;                 // [lane]
        private double[]? _savedReachSelf;                 // [lane]
        private double[]? _savedReachOpp;                  // [lane]

        private const double Epsilon = double.Epsilon;

        public FastCFRInformationSetVec(
            byte playerIndex,
            byte decisionIndex,
            byte numActions,
            int numPlayers,
            InformationSetNode[] backingPerLane,
            FastCFRVisitProgramVec[] visits)
        {
            PlayerIndex = playerIndex;
            DecisionIndex = decisionIndex;
            NumActions = numActions;
            _numPlayers = numPlayers;
            _backingPerLane = backingPerLane ?? throw new ArgumentNullException(nameof(backingPerLane));
            _visitsVec = visits ?? Array.Empty<FastCFRVisitProgramVec>();
        }

        public void BindChildrenAfterFinalizeVec()
        {
            _childrenByVisit = new IFastCFRNodeVec[_visitsVec.Length][];
            for (int v = 0; v < _visitsVec.Length; v++)
            {
                var steps = _visitsVec[v].Steps;
                var arr = new IFastCFRNodeVec[steps.Length];
                for (int i = 0; i < steps.Length; i++)
                    arr[i] = steps[i].ChildAccessor();
                _childrenByVisit[v] = arr;
            }
        }

        public void InitializeIterationVec(
            double[][] ownerCurrentPolicyByLane,
            double[][] opponentTraversalPolicyByLane)
        {
            int lanes = _backingPerLane.Length;

            _pSelfByLane ??= AllocateJagged(lanes, NumActions);
            _pOppByLane ??= AllocateJagged(lanes, NumActions);
            _sumRegretTimesInversePi ??= AllocateJagged(NumActions, lanes);
            _sumInversePi ??= AllocateJagged(NumActions, lanes);
            _lastCumulativeStrategyInc ??= AllocateJagged(NumActions, lanes);
            _qaByActionByLane ??= AllocateJagged(NumActions, lanes);
            _expectedUByPlayerByLane ??= AllocateJagged(_numPlayers, lanes);
            _expectedCustomByLane ??= new FloatSet[lanes];

            _scratchWeightsByLane ??= new double[lanes];
            _scratchMaskSaved ??= new byte[lanes];
            _scratchMaskChild ??= new byte[lanes];
            _savedReachSelf ??= new double[lanes];
            _savedReachOpp ??= new double[lanes];

            for (int k = 0; k < lanes; k++)
            {
                var selfSrc = ownerCurrentPolicyByLane[k];
                var oppSrc = opponentTraversalPolicyByLane[k];
                for (int a = 0; a < NumActions; a++)
                {
                    _pSelfByLane[k][a] = selfSrc[a];
                    _pOppByLane[k][a] = oppSrc[a];
                }
            }

            for (int a = 0; a < NumActions; a++)
            {
                Array.Clear(_sumRegretTimesInversePi[a], 0, lanes);
                Array.Clear(_sumInversePi[a], 0, lanes);
                Array.Clear(_lastCumulativeStrategyInc[a], 0, lanes);
                Array.Clear(_qaByActionByLane[a], 0, lanes);
            }

            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);
            Array.Clear(_expectedCustomByLane, 0, lanes);

            _visitCounter = 0;
        }


        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            int lanes = _backingPerLane.Length;
            if (!ctx.AnyActive())
            {
                EnsureExpectedBuffersCleared(lanes);
                return new FastCFRNodeVecResult(_expectedUByPlayerByLane!, _expectedCustomByLane!);
            }

            int visitIndex = _visitCounter++;
            var visit = _visitsVec[visitIndex];
            var children = _childrenByVisit![visitIndex];

            bool ownerIsOptimized = PlayerIndex == ctx.OptimizedPlayerIndex;

            // Clear expected utilities/custom for this visit
            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane![p], 0, lanes);
            Array.Clear(_expectedCustomByLane!, 0, lanes);

            var weightsByLane = _scratchWeightsByLane!;
            var savedMask = _scratchMaskSaved!;
            var childMask = _scratchMaskChild!;
            var savedSelf = _savedReachSelf!;
            var savedOpp = _savedReachOpp!;

            for (int stepIdx = 0; stepIdx < children.Length; stepIdx++)
            {
                var step = visit.Steps[stepIdx];
                var child = children[stepIdx];
                int a = step.ActionIndex;

                // Build per-lane weights for this action
                if (ownerIsOptimized)
                {
                    for (int k = 0; k < lanes; k++)
                        weightsByLane[k] = _pSelfByLane![k][a];
                }
                else
                {
                    for (int k = 0; k < lanes; k++)
                        weightsByLane[k] = _pOppByLane![k][a];
                }

                // Save reaches
                if (ownerIsOptimized)
                {
                    for (int k = 0; k < lanes; k++)
                        savedSelf[k] = ctx.ReachSelf[k];
                    // Scale self reach on active lanes (always recurse for owner-optimized)
                    FastCFRVecMath.ScaleInPlaceMasked(ctx.ReachSelf, weightsByLane, ctx.ActiveMask);
                }
                else
                {
                    for (int k = 0; k < lanes; k++)
                        savedOpp[k] = ctx.ReachOpp[k];
                    // Build child mask = active && w > epsilon
                    for (int k = 0; k < lanes; k++)
                    {
                        savedMask[k] = ctx.ActiveMask[k];
                        childMask[k] = (savedMask[k] != 0 && weightsByLane[k] > Epsilon) ? (byte)1 : (byte)0;
                    }
                    ctx.ActiveMask = childMask.AsSpan();
                    FastCFRVecMath.ScaleInPlaceMasked(ctx.ReachOpp, weightsByLane, ctx.ActiveMask);
                }

                // Recurse
                var childResult = child.GoVec(ref ctx);

                // Restore reaches and mask
                if (ownerIsOptimized)
                {
                    for (int k = 0; k < lanes; k++)
                        ctx.ReachSelf[k] = savedSelf[k];
                }
                else
                {
                    for (int k = 0; k < lanes; k++)
                        ctx.ReachOpp[k] = savedOpp[k];
                    ctx.ActiveMask = savedMask.AsSpan();
                }

                // Accumulate expected utilities and custom
                if (ownerIsOptimized)
                {
                    // expected += w * childU on active lanes
                    for (int p = 0; p < _numPlayers; p++)
                        FastCFRVecMath.MulAccumulateMasked(
                            weightsByLane,
                            childResult.UtilitiesByPlayerByLane[p],
                            ctx.ActiveMask,
                            _expectedUByPlayerByLane![p]);

                    // Store Qa for regret calc (owner payoff per lane)
                    var ownerU = childResult.UtilitiesByPlayerByLane[PlayerIndex];
                    var qaLane = _qaByActionByLane![a];
                    Array.Copy(ownerU, qaLane, lanes);

                    // Custom per lane
                    for (int k = 0; k < lanes; k++)
                        if (ctx.ActiveMask[k] != 0)
                            _expectedCustomByLane![k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)weightsByLane[k]));
                }
                else
                {
                    // expected += w * childU on childMask lanes
                    for (int p = 0; p < _numPlayers; p++)
                        FastCFRVecMath.MulAccumulateMasked(
                            childResult.UtilitiesByPlayerByLane[p],
                            weightsByLane,
                            childMask,
                            _expectedUByPlayerByLane![p]);

                    for (int k = 0; k < lanes; k++)
                        if (childMask[k] != 0)
                            _expectedCustomByLane![k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)weightsByLane[k]));
                }
            }

            // Regret updates if owner is optimized
            if (ownerIsOptimized)
            {
                // Compute V[k] = sum_a pSelf[k][a] * Qa[a][k]
                var vLane = savedSelf; // reuse scratch
                Array.Clear(vLane, 0, lanes);
                for (int a = 0; a < NumActions; a++)
                {
                    var qa = _qaByActionByLane![a];
                    for (int k = 0; k < lanes; k++)
                        if (ctx.ActiveMask[k] != 0)
                            vLane[k] += _pSelfByLane![k][a] * qa[k];
                }

                // Update tallies per action × lane
                for (int a = 0; a < NumActions; a++)
                {
                    var qa = _qaByActionByLane![a];
                    var sumR = _sumRegretTimesInversePi![a];
                    var sumI = _sumInversePi![a];
                    var inc = _lastCumulativeStrategyInc![a];

                    for (int k = 0; k < lanes; k++)
                    {
                        if (ctx.ActiveMask[k] == 0) continue;

                        double regret = qa[k] - vLane[k];
                        double invPi = ctx.ReachOpp[k];
                        sumR[k] += regret * invPi;
                        sumI[k] += invPi;
                        inc[k] += ctx.ReachSelf[k] * _pSelfByLane![k][a];
                    }
                }
            }

            return new FastCFRNodeVecResult(_expectedUByPlayerByLane!, _expectedCustomByLane!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<double> GetSumRegretTimesInversePi(int actionIndexZeroBased) => _sumRegretTimesInversePi![actionIndexZeroBased];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<double> GetSumInversePi(int actionIndexZeroBased) => _sumInversePi![actionIndexZeroBased];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<double> GetLastCumulativeStrategyIncrements(int actionIndexZeroBased) => _lastCumulativeStrategyInc![actionIndexZeroBased];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InformationSetNode GetBackingForLane(int laneIndex) => _backingPerLane[laneIndex];

        private static double[][] AllocateJagged(int outer, int inner)
        {
            var arr = new double[outer][];
            for (int i = 0; i < outer; i++)
                arr[i] = new double[inner];
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureExpectedBuffersCleared(int lanes)
        {
            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane![p], 0, lanes);
            Array.Clear(_expectedCustomByLane!, 0, lanes);
        }
    }
}
