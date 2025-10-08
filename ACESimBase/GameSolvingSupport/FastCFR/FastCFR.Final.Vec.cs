using System;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRFinalVec : IFastCFRNodeVec
    {
        private readonly double[][] _utilitiesByPlayerByLane; // [player][lane]
        private readonly FloatSet[] _customByLane;            // [lane]
        private readonly int _numPlayers;

        public FastCFRFinalVec(double[][] utilitiesByPlayerByLane, FloatSet[] customByLane)
        {
            _utilitiesByPlayerByLane = utilitiesByPlayerByLane ?? throw new ArgumentNullException(nameof(utilitiesByPlayerByLane));
            _customByLane = customByLane ?? throw new ArgumentNullException(nameof(customByLane));
            _numPlayers = _utilitiesByPlayerByLane.Length == 0 ? 0 : _utilitiesByPlayerByLane.Length;
        }

        public void InitializeIterationVec(double[][] _owner, double[][] _opp) { }

        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            return new FastCFRNodeVecResult(_utilitiesByPlayerByLane, _customByLane);
        }
    }
}
