using System;
using ACESimBase.Util.Collections;
using System.Runtime.CompilerServices;

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

        private double[][] _expectedUByPlayerByLane = Array.Empty<double[]>();
        private FloatSet[] _expectedCustomByLane = Array.Empty<FloatSet>();
        private double[] _pLaneScratch = Array.Empty<double>();
        private double[] _savedReachOpp = Array.Empty<double>();
        private double[] _savedReachChance = Array.Empty<double>();

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

            _expectedUByPlayerByLane = _expectedUByPlayerByLane.Length == _numPlayers ? _expectedUByPlayerByLane : FastCFRVecMath.AllocateJagged(_numPlayers, lanes);
            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);
            _expectedCustomByLane = _expectedCustomByLane.Length == lanes ? _expectedCustomByLane : new FloatSet[lanes];
            Array.Clear(_expectedCustomByLane, 0, lanes);

            _pLaneScratch = _pLaneScratch.Length == lanes ? _pLaneScratch : new double[lanes];
            _savedReachOpp = _savedReachOpp.Length == lanes ? _savedReachOpp : new double[lanes];
            _savedReachChance = _savedReachChance.Length == lanes ? _savedReachChance : new double[lanes];

            for (int stepIndex = 0; stepIndex < visit.Steps.Length; stepIndex++)
            {
                ref readonly var step = ref visit.Steps[stepIndex];
                var child = children[stepIndex];

                step.FillProbabilities(ref ctx, _pLaneScratch);

                for (int k = 0; k < lanes; k++)
                {
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                    _savedReachChance[k] = ctx.ReachChance[k];
                }

                int savedMaskBits = PackMask(ctx.ActiveMask, lanes);
                int childMaskBits = 0;

                for (int k = 0; k < lanes; k++)
                {
                    bool active = ctx.ActiveMask[k] != 0 && _pLaneScratch[k] != 0.0;
                    if (active)
                    {
                        childMaskBits |= (1 << k);
                        double p = _pLaneScratch[k];
                        ctx.ReachOpp[k] = _savedReachOpp[k] * p;
                        ctx.ReachChance[k] = _savedReachChance[k] * p;
                    }
                }

                WriteMask(ctx.ActiveMask, lanes, childMaskBits);

                var childResult = child.GoVec(ref ctx);

                WriteMask(ctx.ActiveMask, lanes, savedMaskBits);
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachOpp[k] = _savedReachOpp[k];
                    ctx.ReachChance[k] = _savedReachChance[k];
                }

                var cu = childResult.UtilitiesByPlayerByLane;
                for (int k = 0; k < lanes; k++)
                {
                    if ((childMaskBits & (1 << k)) == 0) continue;
                    double p = _pLaneScratch[k];
                    for (int pl = 0; pl < _numPlayers; pl++)
                        _expectedUByPlayerByLane[pl][k] += p * cu[pl][k];
                    _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)p));
                }
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
