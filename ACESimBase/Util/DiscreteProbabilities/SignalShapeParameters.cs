using System;

namespace ACESim.Util.DiscreteProbabilities
{
    /// <summary>
    /// Prior-weighted marginal (unconditional) signal shaping modes.
    /// Shaping is defined at the unconditional level P(signal) after mixing over the hidden prior,
    /// and is implemented by transforming the conditional tables P(signal | hidden) in a way that
    /// preserves informativeness as much as possible.
    /// </summary>
    public enum SignalShapeMode
    {
        /// <summary>
        /// No shaping. With the exogenous dispute generator, this means that five signals will be centered at 0.1, 0.3, 0.5, 0.7, and 0.9, 
        /// but there will not be an equal probability of each.
        /// </summary>
        Identity = 0,

        /// <summary>
        /// Targets an (approximately) uniform unconditional signal distribution P(signal) over labels.
        /// </summary>
        EqualMarginal = 1,

        /// <summary>
        /// Targets a symmetric tail-decay unconditional signal distribution where extreme labels are rarer.
        /// TailDecay = 0 corresponds to a uniform target (same as EqualMarginal).
        /// </summary>
        TailDecay = 2,
    }

    /// <summary>
    /// Parameters for optional prior-weighted marginal (unconditional) signal shaping.
    /// </summary>
    [Serializable]
    public struct SignalShapeParameters
    {
        public SignalShapeMode Mode;

        /// <summary>
        /// Tail-decay strength parameter used when Mode == TailDecay.
        /// Larger values imply stronger concentration toward central signal labels.
        /// TailDecay = 0 yields a uniform target marginal.
        /// </summary>
        public double TailDecay;
    }
}
