using System;
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

        private double[][] _expectedUByPlayerByLane = Array.Empty<double[]>();
        private FloatSet[] _expectedCustomByLane = Array.Empty<FloatSet>();
        private double[] _pLaneScratch = Array.Empty<double>();
        private byte[] _maskSaved = Array.Empty<byte>();
        private byte[] _maskChild = Array.Empty<byte>();
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
            _maskSaved = _maskSaved.Length == lanes ? _maskSaved : new byte[lanes];
            _maskChild = _maskChild.Length == lanes ? _maskChild : new byte[lanes];
            _savedReachOpp = _savedReachOpp.Length == lanes ? _savedReachOpp : new double[lanes];
            _savedReachChance = _savedReachChance.Length == lanes ? _savedReachChance : new double[lanes];

            for (int stepIndex = 0; stepIndex < visit.Steps.Length; stepIndex++)
            {
                ref readonly var step = ref visit.Steps[stepIndex];
                var child = children[stepIndex];

                step.FillProbabilities(ref ctx, _pLaneScratch);

                for (int k = 0; k < lanes; k++)
                {
                    _maskChild[k] = (byte)(ctx.ActiveMask[k] != 0 && _pLaneScratch[k] != 0.0 ? 1 : 0);
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                    _savedReachChance[k] = ctx.ReachChance[k];
                    if (_maskChild[k] != 0)
                    {
                        double p = _pLaneScratch[k];
                        ctx.ReachOpp[k] = _savedReachOpp[k] * p;
                        ctx.ReachChance[k] = _savedReachChance[k] * p;
                    }
                }

                Array.Copy(ctx.ActiveMask, _maskSaved, lanes);
                Array.Copy(_maskChild, ctx.ActiveMask, lanes);

                var childResult = child.GoVec(ref ctx);

                Array.Copy(_maskSaved, ctx.ActiveMask, lanes);
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachOpp[k] = _savedReachOpp[k];
                    ctx.ReachChance[k] = _savedReachChance[k];
                }

                var cu = childResult.UtilitiesByPlayerByLane;
                for (int k = 0; k < lanes; k++)
                {
                    if (_maskChild[k] == 0) continue;
                    double p = _pLaneScratch[k];
                    for (int pl = 0; pl < _numPlayers; pl++)
                        _expectedUByPlayerByLane[pl][k] += p * cu[pl][k];
                    _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)p));
                }
            }

            return new FastCFRNodeVecResult(_expectedUByPlayerByLane, _expectedCustomByLane);
        }
    }
}
