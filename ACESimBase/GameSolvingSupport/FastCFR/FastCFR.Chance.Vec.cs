#nullable enable
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
        private readonly FastCFRChanceVisitProgramVec[] _visitsVec;
        private IFastCFRNodeVec[][]? _childrenByVisit;
        private int _visitCounter;

        private double[]? _probabilitiesScratch;         // [lane]
        private double[][]? _expectedUByPlayerByLane;    // [player][lane]
        private FloatSet[]? _expectedCustomByLane;       // [lane]
        private double[]? _savedReachOpp;                // [lane]
        private double[]? _savedReachChance;             // [lane]
        private byte[]? _savedMask;                      // [lane]
        private byte[]? _childMask;                      // [lane]

        public FastCFRChanceVec(byte decisionIndex, byte numOutcomes, int numPlayers, FastCFRChanceVisitProgramVec[] visits)
        {
            DecisionIndex = decisionIndex;
            NumOutcomes = numOutcomes;
            _numPlayers = numPlayers;
            _visitsVec = visits ?? Array.Empty<FastCFRChanceVisitProgramVec>();
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

        public void InitializeIterationVec(ReadOnlySpan<double>[] _owner, ReadOnlySpan<double>[] _opp)
        {
            _visitCounter = 0;
        }

        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            int lanes = ctx.ReachSelf.Length;

            _probabilitiesScratch ??= new double[lanes];
            _expectedUByPlayerByLane ??= AllocateJagged(_numPlayers, lanes);
            _expectedCustomByLane ??= new FloatSet[lanes];
            _savedReachOpp ??= new double[lanes];
            _savedReachChance ??= new double[lanes];
            _savedMask ??= new byte[lanes];
            _childMask ??= new byte[lanes];

            for (int p = 0; p < _numPlayers; p++)
                Array.Clear(_expectedUByPlayerByLane[p], 0, lanes);
            Array.Clear(_expectedCustomByLane, 0, lanes);

            int visitIndex = _visitCounter++;
            var visit = _visitsVec[visitIndex];
            var children = _childrenByVisit![visitIndex];

            for (int i = 0; i < visit.Steps.Length; i++)
            {
                var step = visit.Steps[i];
                var child = children[i];

                // Fill per-lane probabilities
                var pLane = _probabilitiesScratch.AsSpan();
                step.FillProbabilities(ref ctx, pLane);

                // Build child mask: active && p > 0
                for (int k = 0; k < lanes; k++)
                {
                    _savedMask[k] = ctx.ActiveMask[k];
                    _childMask[k] = (_savedMask[k] != 0 && pLane[k] > 0.0) ? (byte)1 : (byte)0;
                }

                // Save reaches and scale
                for (int k = 0; k < lanes; k++)
                {
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                    _savedReachChance[k] = ctx.ReachChance[k];
                }

                ctx.ActiveMask = _childMask.AsSpan();
                FastCFRVecMath.ScaleInPlaceMasked(ctx.ReachOpp, pLane, ctx.ActiveMask);
                FastCFRVecMath.ScaleInPlaceMasked(ctx.ReachChance, pLane, ctx.ActiveMask);

                // Recurse
                var childResult = child.GoVec(ref ctx);

                // Restore mask and reaches
                ctx.ActiveMask = _savedMask.AsSpan();
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachOpp[k] = _savedReachOpp[k];
                    ctx.ReachChance[k] = _savedReachChance[k];
                }

                // Accumulate expected utilities/custom
                for (int p = 0; p < _numPlayers; p++)
                    FastCFRVecMath.MulAccumulateMasked(
                        childResult.UtilitiesByPlayerByLane[p],
                        pLane,
                        _childMask,
                        _expectedUByPlayerByLane[p]);

                for (int k = 0; k < lanes; k++)
                    if (_childMask[k] != 0)
                        _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)pLane[k]));
            }

            return new FastCFRNodeVecResult(_expectedUByPlayerByLane, _expectedCustomByLane);
        }

        private static double[][] AllocateJagged(int outer, int inner)
        {
            var arr = new double[outer][];
            for (int i = 0; i < outer; i++)
                arr[i] = new double[inner];
            return arr;
        }
    }
}
