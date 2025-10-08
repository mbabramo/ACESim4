#nullable enable
using System;
using System.Runtime.CompilerServices;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRFinalVec : IFastCFRNodeVec
    {
        private readonly int _numPlayers;
        private readonly double[][] _utilitiesByPlayerByLane; // [player][lane]
        private readonly FloatSet[] _customByLane;            // [lane]

        public FastCFRFinalVec(double[][] utilitiesByPlayerByLane, FloatSet[] customByLane)
        {
            _utilitiesByPlayerByLane = utilitiesByPlayerByLane ?? throw new ArgumentNullException(nameof(utilitiesByPlayerByLane));
            _customByLane = customByLane ?? throw new ArgumentNullException(nameof(customByLane));
            _numPlayers = _utilitiesByPlayerByLane.Length;
        }

        public void InitializeIterationVec(ReadOnlySpan<double>[] _owner, ReadOnlySpan<double>[] _opp)
        {
            // Finals are immutable per iteration for the vector region.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx)
        {
            return new FastCFRNodeVecResult(_utilitiesByPlayerByLane, _customByLane);
        }
    }
}
