using System;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;

namespace ACESimBase.GameSolvingSupport.GpuCFR
{
    /// <summary>
    /// Minimal GPU builder scaffold. It mirrors the shape of the FastCFR builder so that
    /// GeneralizedVanilla can drive sweeps identically. You can later map these calls to
    /// actual GPU buffers/kernels (e.g., gpugt) without touching higher-level CFR code.
    /// </summary>
    public sealed class GpuCFRBuilder
    {
        public sealed class RootNode
        {
            private readonly GpuCFRBuilder _owner;

            internal RootNode(GpuCFRBuilder owner) => _owner = owner;

            public NodeResult Go(ref IterationContext ctx)
            {
#if GPUGT
                // TODO: Implement GPU kernel orchestration here:
                // - gather per-node data into device buffers
                // - launch traversal + reduction kernels
                // - write tallies/regret numerators/denominators
                // - compute utilities by player
                throw new NotImplementedException("GPU kernel path not yet implemented.");
#else
                // With no GPU support compiled in, this path should never be called,
                // because GeneralizedVanilla falls back to Fast flavor when IsAvailable == false.
                // We return zeros defensively.
                return new NodeResult(new double[_owner.NumNonChancePlayers]);
#endif
            }
        }

        public readonly struct NodeResult
        {
            public NodeResult(double[] utilities) => Utilities = utilities;
            public double[] Utilities { get; }
        }

        public struct IterationContext
        {
            public int IterationNumber;
            public double ReachSelf;
            public double ReachOpp;
            public double ReachChance;
            public double SamplingCorrection;
            public bool SuppressMath;
        }

        public sealed class GpuCFRBuilderOptions
        {
            public bool UseDynamicChanceProbabilities { get; set; } = true;
            public byte NumNonChancePlayers { get; set; } = 2;
        }

        private readonly HistoryNavigationInfo _navigation;
        private readonly Func<HistoryPoint> _rootFactory;
        private readonly bool _useFloat;
        private readonly GpuCFRBuilderOptions _options;

        public RootNode Root { get; }
        public bool IsAvailable { get; }
        public byte NumNonChancePlayers { get; }

        public GpuCFRBuilder(
            HistoryNavigationInfo navigation,
            Func<HistoryPoint> rootFactory,
            bool useFloat,
            GpuCFRBuilderOptions options)
        {
            _navigation = navigation;
            _rootFactory = rootFactory;
            _useFloat = useFloat;
            _options = options ?? new GpuCFRBuilderOptions();
            Root = new RootNode(this);

            // Simple runtime availability probe hook. Replace with actual device/context initialization.
#if GPUGT
            IsAvailable = ProbeGpuAvailability();
#else
            IsAvailable = false;
#endif
            NumNonChancePlayers = (byte)options.NumNonChancePlayers;
        }

        public IterationContext InitializeIteration(byte optimizedPlayerIndex, int scenarioIndex, Func<int, double> rand01ForDecision)
        {
            // Note: when you wire in GPU, pin/prepare per-iteration buffers here.
            return new IterationContext
            {
                IterationNumber = 0,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                SuppressMath = false
            };
        }

        public void CopyTalliesIntoBackingNodes()
        {
            // When GPU is wired, read back (or map) cumulative tallies/regret numerators/denominators
            // and add into the backing InformationSetNode structures.
            // For now, this is intentionally a no-op.
        }

#if GPUGT
        private static bool ProbeGpuAvailability()
        {
            // TODO: Initialize gpugt device/context here and return true on success
            return true;
        }
#endif
    }
}
