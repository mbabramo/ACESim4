using System;
using System.Runtime.CompilerServices;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRChanceVec : IFastCFRNodeVec
    {
        public readonly byte DecisionIndex;
        public readonly byte NumOutcomes;

        private readonly int _numPlayers;
        private readonly FastCFRChanceVisitProgramVec[] _visits;
        private IFastCFRNodeVec[][] _childrenByVisit = Array.Empty<IFastCFRNodeVec[]>();
        private int _visitCounter;

        // Stable wrappers returned to caller: [player][lane]
        private double[][] _expectedUByPlayerByLane = Array.Empty<double[]>();
        private FloatSet[] _expectedCustomByLane = Array.Empty<FloatSet>();

        // Contiguous accumulation buffer for utilities: [player-major][lane]
        private double[] _expectedU_PL = Array.Empty<double>();

        // Scratch
        private double[] _pLaneScratch = Array.Empty<double>();
        private double[] _savedReachOpp = Array.Empty<double>();
        private double[] _savedReachChance = Array.Empty<double>();
        private byte[]   _maskChild = Array.Empty<byte>();

        public FastCFRChanceVec(
            byte decisionIndex,
            byte numOutcomes,
            int numPlayers,
            FastCFRChanceVisitProgramVec[] visits)
        {
            DecisionIndex = decisionIndex;
            NumOutcomes = numOutcomes;
            _numPlayers = numPlayers;
            _visits = visits ?? Array.Empty<FastCFRChanceVisitProgramVec>();
        }

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

        public void InitializeIterationVec(double[][] _owner, double[][] _opp)
        {
            _visitCounter = 0;
        }

        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            int lanes = ctx.ActiveMask.Length;

            var visitIndex = _visitCounter++;
            var visit = _visits[visitIndex];
            var children = _childrenByVisit[visitIndex];

            // Allocate on first use for this node, else clear
            if (_expectedUByPlayerByLane.Length != _numPlayers)
            {
                _expectedUByPlayerByLane = FastCFRVecMath.AllocateJagged(_numPlayers, lanes);
                _expectedCustomByLane = new FloatSet[lanes];
                _expectedU_PL = new double[_numPlayers * lanes];
                _pLaneScratch = new double[lanes];
                _savedReachOpp = new double[lanes];
                _savedReachChance = new double[lanes];
                _maskChild = new byte[lanes];
            }
            else
            {
                for (int p = 0; p < _numPlayers; p++)
                    Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);
                Array.Clear(_expectedCustomByLane, 0, lanes);
                Array.Clear(_expectedU_PL, 0, _expectedU_PL.Length);
            }

            for (int stepIndex = 0; stepIndex < visit.Steps.Length; stepIndex++)
            {
                ref readonly var step = ref visit.Steps[stepIndex];
                var child = children[stepIndex];

                step.FillProbabilities(ref ctx, _pLaneScratch);

                int savedMaskBits = PackMask(ctx.ActiveMask, lanes);
                int childMaskBits = 0;

                for (int k = 0; k < lanes; k++)
                {
                    bool active = ctx.ActiveMask[k] != 0 && _pLaneScratch[k] != 0.0;
                    _maskChild[k] = active ? (byte)1 : (byte)0;
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                    _savedReachChance[k] = ctx.ReachChance[k];
                    if (active)
                    {
                        double p = _pLaneScratch[k];
                        ctx.ReachOpp[k]    = _savedReachOpp[k]    * p;
                        ctx.ReachChance[k] = _savedReachChance[k] * p;
                        childMaskBits |= (1 << k);
                    }
                }

                WriteMask(ctx.ActiveMask, lanes, childMaskBits);

                var childResult = child.GoVec(ref ctx);

                WriteMask(ctx.ActiveMask, lanes, savedMaskBits);
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachOpp[k]    = _savedReachOpp[k];
                    ctx.ReachChance[k] = _savedReachChance[k];
                }

                // E[U] += p_lane * childU  (accumulate on contiguous buffer)
                var cu = childResult.UtilitiesByPlayerByLane;
                for (int p = 0; p < _numPlayers; p++)
                {
                    var dst = new Span<double>(_expectedU_PL, p * lanes, lanes);
                    FastCFRVecMath.MulAddMasked(dst, cu[p], _pLaneScratch, _maskChild);
                }

                // Custom results (scalar per lane)
                for (int k = 0; k < lanes; k++)
                {
                    if (_maskChild[k] == 0) continue;
                    double p = _pLaneScratch[k];
                    _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)p));
                }
            }

            // Publish contiguous results into the stable wrapper rows
            for (int p = 0; p < _numPlayers; p++)
            {
                int offset = p * lanes;
                Array.Copy(_expectedU_PL, offset, _expectedUByPlayerByLane[p], 0, lanes);
            }

            return new FastCFRNodeVecResult(_expectedUByPlayerByLane, _expectedCustomByLane);
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
