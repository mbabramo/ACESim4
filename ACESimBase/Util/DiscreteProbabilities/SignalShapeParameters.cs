using System;

namespace ACESim.Util.DiscreteProbabilities
{
    /// <summary>
    /// Placeholder enumeration for prior-weighted marginal (unconditional) signal shaping.
    /// Only Identity is currently implemented; other values are reserved for future use.
    /// </summary>
    public enum SignalShapeMode
    {
        Identity = 0,
        EqualMarginal = 1,
        TailDecay = 2,
    }

    /// <summary>
    /// Placeholder parameters for optional prior-weighted marginal (unconditional) signal shaping.
    /// Current behavior is a no-op in all modes (non-identity behavior is implemented later).
    /// </summary>
    [Serializable]
    public struct SignalShapeParameters
    {
        public SignalShapeMode Mode;

        /// <summary>
        /// Tail-decay strength parameter (reserved for future use when Mode == TailDecay).
        /// </summary>
        public double TailDecay;
    }
}
