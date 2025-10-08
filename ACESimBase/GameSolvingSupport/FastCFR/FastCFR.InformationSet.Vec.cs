using System;
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
        private readonly InformationSetNode[] _backingPerLane;
        private readonly FastCFRVisitProgramVec[] _visits;
        private IFastCFRNodeVec[][] _childrenByVisit = Array.Empty<IFastCFRNodeVec[]>();
        private int _visitCounter;

        private double[][] _pSelfByLane = Array.Empty<double[]>();
        private double[][] _pOppByLane = Array.Empty<double[]>();

        private double[][] _sumRegretTimesInversePi = Array.Empty<double[]>();
        private double[][] _sumInversePi = Array.Empty<double[]>();
        private double[][] _lastCumulativeStrategyInc = Array.Empty<double[]>();
        private double[][] _qaByActionByLane = Array.Empty<double[]>();
        private double[][] _expectedUByPlayerByLane = Array.Empty<double[]>();
        private FloatSet[] _expectedCustomByLane = Array.Empty<FloatSet>();

        private double[] _scratchWeightsByLane = Array.Empty<double>();
        private byte[] _scratchMaskSaved = Array.Empty<byte>();
        private byte[] _scratchMaskChild = Array.Empty<byte>();
        private double[] _savedReachSelf = Array.Empty<double>();
        private double[] _savedReachOpp = Array.Empty<double>();

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

        public void InitializeIterationVec(
            double[][] ownerCurrentPolicyByLane,
            double[][] opponentTraversalPolicyByLane)
        {
            int lanes = _backingPerLane.Length;

            _pSelfByLane = _pSelfByLane.Length == lanes ? _pSelfByLane : FastCFRVecMath.AllocateJagged(lanes, NumActions);
            _pOppByLane = _pOppByLane.Length == lanes ? _pOppByLane : FastCFRVecMath.AllocateJagged(lanes, NumActions);
            _sumRegretTimesInversePi = _sumRegretTimesInversePi.Length == NumActions ? _sumRegretTimesInversePi : FastCFRVecMath.AllocateJagged(NumActions, lanes);
            _sumInversePi = _sumInversePi.Length == NumActions ? _sumInversePi : FastCFRVecMath.AllocateJagged(NumActions, lanes);
            _lastCumulativeStrategyInc = _lastCumulativeStrategyInc.Length == NumActions ? _lastCumulativeStrategyInc : FastCFRVecMath.AllocateJagged(NumActions, lanes);
            _qaByActionByLane = _qaByActionByLane.Length == NumActions ? _qaByActionByLane : FastCFRVecMath.AllocateJagged(NumActions, lanes);
            _expectedUByPlayerByLane = _expectedUByPlayerByLane.Length == _numPlayers ? _expectedUByPlayerByLane : FastCFRVecMath.AllocateJagged(_numPlayers, lanes);
            _expectedCustomByLane = _expectedCustomByLane.Length == lanes ? _expectedCustomByLane : new FloatSet[lanes];

            _scratchWeightsByLane = _scratchWeightsByLane.Length == lanes ? _scratchWeightsByLane : new double[lanes];
            _scratchMaskSaved = _scratchMaskSaved.Length == lanes ? _scratchMaskSaved : new byte[lanes];
            _scratchMaskChild = _scratchMaskChild.Length == lanes ? _scratchMaskChild : new byte[lanes];
            _savedReachSelf = _savedReachSelf.Length == lanes ? _savedReachSelf : new double[lanes];
            _savedReachOpp = _savedReachOpp.Length == lanes ? _savedReachOpp : new double[lanes];

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
                Array.Clear(_expectedUByPlayerByLane[p], 0, _expectedUByPlayerByLane[p].Length);
            Array.Clear(_expectedCustomByLane, 0, _expectedCustomByLane.Length);

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

                for (int k = 0; k < lanes; k++)
                {
                    _scratchMaskChild[k] = 0;
                    _savedReachSelf[k] = ctx.ReachSelf[k];
                    _savedReachOpp[k] = ctx.ReachOpp[k];
                }

                for (int k = 0; k < lanes; k++)
                {
                    if (ctx.ActiveMask[k] == 0) continue;
                    double w = ownerIsOptimized ? _pSelfByLane[k][ai] : _pOppByLane[k][ai];
                    if (!ownerIsOptimized && w <= Epsilon) { _scratchMaskChild[k] = 0; continue; }
                    _scratchMaskChild[k] = 1;
                    if (ownerIsOptimized)
                        ctx.ReachSelf[k] = _savedReachSelf[k] * w;
                    else
                        ctx.ReachOpp[k] = _savedReachOpp[k] * w;
                }

                for (int k = 0; k < lanes; k++)
                    _scratchMaskSaved[k] = ctx.ActiveMask[k];
                Array.Copy(_scratchMaskChild, ctx.ActiveMask, lanes);

                var childResult = child.GoVec(ref ctx);

                Array.Copy(_scratchMaskSaved, ctx.ActiveMask, lanes);
                for (int k = 0; k < lanes; k++)
                {
                    ctx.ReachSelf[k] = _savedReachSelf[k];
                    ctx.ReachOpp[k] = _savedReachOpp[k];
                }

                var cu = childResult.UtilitiesByPlayerByLane;
                for (int k = 0; k < lanes; k++)
                {
                    if (_scratchMaskChild[k] == 0) continue;
                    double w = ownerIsOptimized ? _pSelfByLane[k][ai] : _pOppByLane[k][ai];
                    for (int p = 0; p < _numPlayers; p++)
                        _expectedUByPlayerByLane[p][k] += w * cu[p][k];
                    _expectedCustomByLane[k] = _expectedCustomByLane[k].Plus(childResult.CustomByLane[k].Times((float)w));
                }

                if (ownerIsOptimized)
                {
                    for (int k = 0; k < lanes; k++)
                        _qaByActionByLane[ai][k] = childResult.UtilitiesByPlayerByLane[PlayerIndex][k];
                }
            }

            if (ownerIsOptimized)
            {
                var V = new double[lanes];
                for (int k = 0; k < lanes; k++)
                {
                    if (ctx.ActiveMask[k] == 0) { V[k] = 0.0; continue; }
                    double sum = 0.0;
                    for (int a = 0; a < NumActions; a++)
                        sum += _pSelfByLane[k][a] * _qaByActionByLane[a][k];
                    V[k] = sum;
                }

                for (int a = 0; a < NumActions; a++)
                {
                    var rti = _sumRegretTimesInversePi[a];
                    var si = _sumInversePi[a];
                    var inc = _lastCumulativeStrategyInc[a];

                    for (int k = 0; k < lanes; k++)
                    {
                        if (ctx.ActiveMask[k] == 0) continue;
                        double regret = _qaByActionByLane[a][k] - V[k];
                        rti[k] += regret * ctx.ReachOpp[k];
                        si[k] += ctx.ReachOpp[k];
                        inc[k] += ctx.ReachSelf[k] * _pSelfByLane[k][a];
                    }
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
                    double rTimesInvPi = _sumRegretTimesInversePi[a][k];
                    double invPi = _sumInversePi[a][k];
                    double incr = _lastCumulativeStrategyInc[a][k];
                    if (invPi != 0.0 || rTimesInvPi != 0.0)
                        backing.IncrementLastRegret((byte)(a + 1), rTimesInvPi, invPi);
                    if (incr != 0.0)
                        backing.IncrementLastCumulativeStrategyIncrements((byte)(a + 1), incr);
                }
            }
        }
    }
}
