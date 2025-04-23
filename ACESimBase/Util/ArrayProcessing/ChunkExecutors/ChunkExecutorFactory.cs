// -----------------------------------------------------------------------------
//  ChunkExecutorFactory.cs
//  ---------------------------------------------------------------------------
//  Creates the appropriate ChunkExecutor implementation (Interpreter, Roslyn,
//  IL, etc.) with an optional FallbackChunkExecutor wrapper.
// -----------------------------------------------------------------------------

using System;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Enumerates the concrete <see cref="ChunkExecutorBase"/> variants supported by
    /// <see cref="ChunkExecutorFactory"/>.
    /// </summary>
    public enum ChunkExecutorKind
    {
        /// <summary>Pure interpreter – no code‑generation.</summary>
        Interpreted,
        /// <summary>Roslyn backend <b>without</b> local‑variable recycling.</summary>
        Roslyn,
        /// <summary>Roslyn backend <b>with</b> depth‑aware local‑variable recycling.</summary>
        RoslynWithLocalVariableRecycling,
        /// <summary>IL‑emit backend <b>without</b> local‑variable recycling.</summary>
        IL,
        /// <summary>IL‑emit backend <b>with</b> local‑variable recycling.</summary>
        ILWithLocalVariableRecycling,
    }

    /// <summary>
    /// Static helper that instantiates the requested <see cref="ChunkExecutorBase"/>.
    /// If <paramref name="fallbackThreshold"/> is supplied, the returned instance is a
    /// <see cref="FallbackChunkExecutor"/> which routes “small” chunks to an
    /// <see cref="InterpreterChunkExecutor"/> and all other chunks to the primary
    /// executor chosen via <paramref name="kind"/>.
    /// </summary>
    public static class ChunkExecutorFactory
    {
        /// <summary>
        /// Creates a <see cref="ChunkExecutorBase"/> matching the requested <paramref name="kind"/>.
        /// </summary>
        /// <param name="kind">Requested executor type.</param>
        /// <param name="commands">Shared <see cref="ArrayCommand"/> buffer.</param>
        /// <param name="start">First command index handled by the executor.</param>
        /// <param name="end">End index (exclusive).</param>
        /// <param name="useCheckpoints">Whether the executor should use checkpoints (only respected by implementations that support it).</param>
        /// <param name="fallbackThreshold">
        ///     When non‑null, wrap the primary executor in a <see cref="FallbackChunkExecutor"/>
        ///     that uses an <see cref="InterpreterChunkExecutor"/> for chunks containing
        ///     ≤ <paramref name="fallbackThreshold"/> commands.  The primary executor is
        ///     routed all larger chunks.
        /// </param>
        /// <returns>A ready‑to‑use <see cref="ChunkExecutorBase"/> instance.</returns>
        public static ChunkExecutorBase Create(
            ChunkExecutorKind kind,
            ArrayCommand[] commands,
            int start,
            int end,
            bool useCheckpoints = false,
            int? fallbackThreshold = null)
        {
            // ────────────────────────── primary backend ──────────────────────────
            ChunkExecutorBase primary = kind switch
            {
                ChunkExecutorKind.Interpreted => new InterpreterChunkExecutor(commands, start, end, useCheckpoints),

                ChunkExecutorKind.Roslyn => new RoslynChunkExecutor(commands, start, end, useCheckpoints, localVariableReuse: false),

                ChunkExecutorKind.RoslynWithLocalVariableRecycling => new RoslynChunkExecutor(commands, start, end, useCheckpoints, localVariableReuse: true),

                ChunkExecutorKind.IL => new ILChunkExecutor(commands, start, end, false),

                ChunkExecutorKind.ILWithLocalVariableRecycling => new ILChunkExecutor(commands, start, end, true),

                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            // ────────────────────────── optional fallback ─────────────────────────
            if (fallbackThreshold.HasValue && kind != ChunkExecutorKind.Interpreted)
            {
                // Small‑chunk interpreter (never code‑generates – avoids Roslyn/IL overhead).
                var small = new InterpreterChunkExecutor(commands, start, end, useCheckpoints);
                return new FallbackChunkExecutor(commands, start, end, useCheckpoints, fallbackThreshold.Value, small, primary);
            }

            return primary;
        }
    }
}
