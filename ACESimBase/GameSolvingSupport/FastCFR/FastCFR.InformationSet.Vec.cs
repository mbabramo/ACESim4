using System;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;
using System.Runtime.CompilerServices;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRInformationSetVec : IFastCFRNodeVec
    {
        public readonly byte PlayerIndex;
        public readonly byte DecisionIndex;
        public readonly byte NumActions;

        private readonly int _numPlayers;
        private readonly InformationSetNode[] _backingPerLane;
        private readonly FastCFRVisitProgramVec[] _visits;
        private IFastCFRNodeVec[][] _childrenByVisit = Array.Empty<IFastCFRNodeVec[]>();
        private int _visitCounter;

        // ---- SoA contiguous buffers [action-major][lane] ----
        private double[] _pSelf_AL = Array.Empty<double>();
        private double[] _pOpp_AL = Array.Empty<double>();
        private double[] _qa_AL = Array.Empty<double>();
        private double[] _sumRegretTimesInversePi_AL = Array.Empty<double>();
        private double[] _sumInversePi_AL = Array.Empty<double>();
        private double[] _lastCumulativeStrategyInc_AL = Array.Empty<double>();

        // Small results: keep rows [player][lane], one-time allocated
        private double[][] _expectedUByPlayerByLane = Array.Empty<double[]>();
        private FloatSet[] _expectedCustomByLane = Array.Empty<FloatSet>();

        // Scratch (per node)
        private double[] _savedReachSelf = Array.Empty<double>();
        private double[] _savedReachOpp = Array.Empty<double>();
        private byte[] _maskChild = Array.Empty<byte>();
        private double[] _scratchVByLane = Array.Empty<double>();
        private double[] _scratchTempByLane = Array.Empty<double>();

        public int LaneCount => _backingPerLane.Length;

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
            _backingPerLane = backingPerLane ?? Array.Empty<InformationSetNode>();
            _visits = visits ?? Array.Empty<FastCFRVisitProgramVec>();
        }

        public InformationSetNode GetBackingForLane(int lane) => _backingPerLane[lane];

        internal void BindChildrenAfterFinalize()
        {
            _childrenByVisit = new IFastCFRNodeVec[_visits.Length][];
            for (int v = 0; v < _visits.Length; v++)
            {
                var steps = _visits[v].Steps;
                var arr = new IFastCFRNodeVec[steps.Length];
                for (int i = 0; i < steps.Length; i++)
                    arr[i] = steps[i].ChildAccessor();
                _childrenByVisit[v] = arr;
            }
        }

        public void InitializeIterationVec(double[][] ownerCurrentPolicyByLane, double[][] opponentTraversalPolicyByLane)
        {
            int lanes = _backingPerLane.Length;
            int AL = lanes * NumActions;

            if (_pSelf_AL.Length != AL)
            {
                _pSelf_AL = new double[AL];
                _pOpp_AL = new double[AL];
                _qa_AL = new double[AL];
                _sumRegretTimesInversePi_AL = new double[AL];
                _sumInversePi_AL = new double[AL];
                _lastCumulativeStrategyInc_AL = new double[AL];
                _savedReachSelf = new double[lanes];
                _savedReachOpp = new double[lanes];
                _maskChild = new byte[lanes];
                _scratchVByLane = new double[lanes];
                _scratchTempByLane = new double[lanes];

                // allocate small result rows once
                _expectedUByPlayerByLane = FastCFRVecMath.AllocateJagged(_numPlayers, lanes);
                _expectedCustomByLane = new FloatSet[lanes];
            }
            else
            {
                Array.Clear(_qa_AL, 0, _qa_AL.Length);
                Array.Clear(_sumRegretTimesInversePi_AL, 0, _sumRegretTimesInversePi_AL.Length);
                Array.Clear(_sumInversePi_AL, 0, _sumInversePi_AL.Length);
                Array.Clear(_lastCumulativeStrategyInc_AL, 0, _lastCumulativeStrategyInc_AL.Length);
                Array.Clear(_expectedCustomByLane, 0, _expectedCustomByLane.Length);
            }

            // Fill SoA: for each action, lay out probabilities across lanes
            for (int a = 0; a < NumActions; a++)
            {
                int baseIx = a * lanes;
                for (int k = 0; k < lanes; k++)
                {
                    _pSelf_AL[baseIx + k] = ownerCurrentPolicyByLane[k][a];
                    _pOpp_AL [baseIx + k] = opponentTraversalPolicyByLane[k][a];
                }
            }

            // Clear expected utilities rows
            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);

            _visitCounter = 0;
        }

        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            int lanes = _backingPerLane.Length;
            var visitIndex = _visitCounter++;
            var visit = _visits[visitIndex];
            var children = _childrenByVisit[visitIndex];
            bool ownerIsOptimized = PlayerIndex == ctx.OptimizedPlayerIndex;

            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);
            Array.Clear(_expectedCustomByLane, 0, lanes);

            for (int i = 0; i < children.Length; i++)
            {
                int ai = visit.Steps[i].ActionIndex;
                var child = children[i];

                // Save reaches and assemble child mask using contiguous weights row
                for (int k = 0; k < lanes; k++)
                {
                    _savedReachSelf[k] = ctx.ReachSelf[k];
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                }

                int savedMaskBits = PackMask(ctx.ActiveMask, lanes);
                int childMaskBits = 0;
                ReadOnlySpan<double> wRow = ownerIsOptimized
                    ? new ReadOnlySpan<double>(_pSelf_AL, ai * lanes, lanes)
                    : new ReadOnlySpan<double>(_pOpp_AL, ai * lanes, lanes);

                for (int k = 0; k < lanes; k++)
                {
                    if (ctx.ActiveMask[k] == 0) { _maskChild[k] = 0; continue; }

                    double w = wRow[k];
                    if (!ownerIsOptimized && w <= Epsilon) { _maskChild[k] = 0; continue; }

                    _maskChild[k] = 1;
                    childMaskBits |= (1 << k);

                    if (ownerIsOptimized)
                        ctx.ReachSelf[k] = _savedReachSelf[k] * w;
                    else
                        ctx.ReachOpp[k] = _savedReachOpp[k] * w;
                }

                WriteMask(ctx.ActiveMask, lanes, childMaskBits);

                var childResult = child.GoVec(ref ctx);

                WriteMask(ctx.ActiveMask, lanes, savedMaskBits);
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachSelf[k] = _savedReachSelf[k];
                    ctx.ReachOpp[k] = _savedReachOpp[k];
                }

                var cu = childResult.UtilitiesByPlayerByLane;

                // E[U] += wRow * childU (masked)
                for (int p = 0; p < _numPlayers; p++)
                    FastCFRVecMath.MulAddMasked(_expectedUByPlayerByLane[p], cu[p], wRow, _maskChild);

                // custom (scalar)
                for (int k = 0; k < lanes; k++)
                {
                    if (_maskChild[k] == 0) continue;
                    double w = wRow[k];
                    _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)w));
                }

                if (ownerIsOptimized)
                {
                    // Qa[a, k] from child owner utility
                    ReadOnlySpan<double> childOwner = cu[PlayerIndex];
                    Span<double> qaRow = new Span<double>(_qa_AL, ai * lanes, lanes);
                    for (int k = 0; k < lanes; k++)
                        if (_maskChild[k] != 0)
                            qaRow[k] = childOwner[k];
                }
            }

            if (ownerIsOptimized)
            {
                // V[k] = Σ_a pSelf[a,k] * Qa[a,k]
                Array.Clear(_scratchVByLane, 0, lanes);
                for (int a = 0; a < NumActions; a++)
                {
                    ReadOnlySpan<double> wRow = new ReadOnlySpan<double>(_pSelf_AL, a * lanes, lanes);
                    ReadOnlySpan<double> qRow = new ReadOnlySpan<double>(_qa_AL, a * lanes, lanes);
                    FastCFRVecMath.MulAddMasked(_scratchVByLane, qRow, wRow, ctx.ActiveMask);
                }

                for (int a = 0; a < NumActions; a++)
                {
                    ReadOnlySpan<double> qRow = new ReadOnlySpan<double>(_qa_AL, a * lanes, lanes);
                    Span<double> rtiRow = new Span<double>(_sumRegretTimesInversePi_AL, a * lanes, lanes);
                    Span<double> siRow  = new Span<double>(_sumInversePi_AL, a * lanes, lanes);
                    Span<double> incRow = new Span<double>(_lastCumulativeStrategyInc_AL, a * lanes, lanes);
                    ReadOnlySpan<double> wRow = new ReadOnlySpan<double>(_pSelf_AL, a * lanes, lanes);

                    FastCFRVecMath.SubMaskedInto(_scratchTempByLane, qRow, _scratchVByLane, ctx.ActiveMask);
                    FastCFRVecMath.MulAddMasked(rtiRow, _scratchTempByLane, ctx.ReachOpp, ctx.ActiveMask);
                    FastCFRVecMath.AddMasked(siRow, ctx.ReachOpp, ctx.ActiveMask);
                    FastCFRVecMath.MulAddMasked(incRow, ctx.ReachSelf, wRow, ctx.ActiveMask);
                }
            }

            return new FastCFRNodeVecResult(_expectedUByPlayerByLane, _expectedCustomByLane);
        }

        public void FlushTalliesToBacking()
        {
            int lanes = _backingPerLane.Length;
            for (int k = 0; k < lanes; k++)
            {
                var backing = _backingPerLane[k];
                for (int a = 0; a < NumActions; a++)
                {
                    int ix = a * lanes + k;
                    double rTimesInvPi = _sumRegretTimesInversePi_AL[ix];
                    double invPi = _sumInversePi_AL[ix];
                    double incr = _lastCumulativeStrategyInc_AL[ix];
                    if (invPi != 0.0 || rTimesInvPi != 0.0)
                        backing.IncrementLastRegret((byte)(a + 1), rTimesInvPi, invPi);
                    if (incr != 0.0)
                        backing.IncrementLastCumulativeStrategyIncrements((byte)(a + 1), incr);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PackMask(ReadOnlySpan<byte> mask, int count)
        {
            int bits = 0;
            for (int k = 0; k < count; k++)
                if (mask[k] != 0) bits |= (1 << k);
            return bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteMask(Span<byte> dest, int count, int bits)
        {
            for (int k = 0; k < count; k++)
                dest[k] = ((bits >> k) & 1) != 0 ? (byte)1 : (byte)0;
        }
    }
}
