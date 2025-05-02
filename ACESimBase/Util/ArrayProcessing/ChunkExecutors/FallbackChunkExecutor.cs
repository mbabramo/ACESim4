// -----------------------------------------------------------------------------
//  FallbackChunkExecutor.cs  (revised)
//  ---------------------------------------------------------------------------
//  Delegates every ArrayCommandChunk to either a “small” or a “large” backend
//  executor chosen by chunk length.  The two executors are supplied directly by
//  the caller – **no internal factory logic and no defaults**.
// -----------------------------------------------------------------------------

using System;
using System.Text;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Facade that forwards each chunk to one of two already‑constructed
    /// <see cref="ChunkExecutorBase"/> instances:
    /// <list type="bullet">
    ///   <item><description><b>Small‑chunk executor</b> – used when the chunk contains
    ///   ≤ <see cref="_threshold"/> commands.</description></item>
    ///   <item><description><b>Large‑chunk executor</b> – used otherwise.</description></item>
    /// </list>
    /// The choice of concrete executor types (e.g. interpreter, IL emit, Roslyn)
    /// is made entirely by the caller.  This class merely applies a size‑based
    /// routing policy.
    /// </summary>
    public sealed class FallbackChunkExecutor : ChunkExecutorBase
    {
        // ────────────────────────────────── fields ──────────────────────────────────
        private readonly int _threshold;
        private readonly ChunkExecutorBase _smallExecutor;
        private readonly ChunkExecutorBase _largeExecutor;

        // ───────────────────────────────── constructor ─────────────────────────────
        /// <param name="commands">Shared <see cref="ArrayCommand"/> buffer.</param>
        /// <param name="start">First command index handled by <c>this</c> facade.</param>
        /// <param name="end">End index (exclusive).</param>
        /// <param name="chunkSizeThreshold">Max command count still considered “small”.</param>
        /// <param name="smallExecutor">Executor used for chunks ≤ <paramref name="chunkSizeThreshold"/>. Required.</param>
        /// <param name="largeExecutor">Executor used for larger chunks. Required.</param>
        public FallbackChunkExecutor(
            ArrayCommand[] commands,
            int start,
            int end,
            int chunkSizeThreshold,
            ChunkExecutorBase smallExecutor,
            ChunkExecutorBase largeExecutor)
            : base(commands, start, end, useCheckpoints: false, arrayCommandListForCheckpoints: null)
        {
            _threshold = chunkSizeThreshold > 0
                ? chunkSizeThreshold
                : throw new ArgumentOutOfRangeException(nameof(chunkSizeThreshold), "Threshold must be positive.");

            _smallExecutor = smallExecutor ?? throw new ArgumentNullException(nameof(smallExecutor));
            _largeExecutor = largeExecutor ?? throw new ArgumentNullException(nameof(largeExecutor));
        }

        // ───────────────────────────── helper ─────────────────────────────
        private ChunkExecutorBase SelectBackend(ArrayCommandChunk chunk)
            => (chunk.EndCommandRangeExclusive - chunk.StartCommandRange) <= _threshold
               ? _smallExecutor
               : _largeExecutor;

        // ─────────────────────────── overrides ────────────────────────────
        public override bool PreserveGeneratedCode
        {
            get => base.PreserveGeneratedCode;
            set
            {
                base.PreserveGeneratedCode = value;
                _smallExecutor.PreserveGeneratedCode = value;
                _largeExecutor.PreserveGeneratedCode = value;
            }
        }

        public override void AddToGeneration(ArrayCommandChunk chunk)
            => SelectBackend(chunk).AddToGeneration(chunk);

        public override void PerformGeneration()
        {
            _smallExecutor.PerformGeneration();
            _largeExecutor.PerformGeneration();

            if (PreserveGeneratedCode)
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(_smallExecutor.GeneratedCode))
                    sb.AppendLine(_smallExecutor.GeneratedCode);
                if (!string.IsNullOrEmpty(_largeExecutor.GeneratedCode))
                    sb.AppendLine(_largeExecutor.GeneratedCode);
                GeneratedCode = sb.ToString(); // this won't compile, but it's used only for debugging
            }
        }

        public override void Execute(
            ArrayCommandChunk chunk,
            double[] virtualStack,
            double[] orderedSources,
            ref int cosi,
            ref bool condition)
            => SelectBackend(chunk).Execute(chunk, virtualStack, orderedSources, ref cosi, ref condition);
    }
}
