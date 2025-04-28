using ACESimBase.Util.ArrayProcessing;
using System;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Common contract for executing a single <see cref="ArrayCommandChunk"/>.
    /// Implementations may interpret, emit IL, or compile C# (Roslyn),
    /// but *all* share the same life‑cycle:
    /// <list type="number">
    /// <item><description><see cref="AddToGeneration"/> — queue the chunk so it can be batched with others for code‑generation.</description></item>
    /// <item><description><see cref="PerformGeneration"/> — finish code‑generation (if any) for all previously queued chunks.  For pure‑interpreter executors this is a no‑op.</description></item>
    /// <item><description><see cref="Execute"/> — run one prepared chunk.</description></item>
    /// </list>
    /// A single executor instance is intended to be reused for many chunks and for
    /// many iterations of an <see cref="ArrayCommandList"/>.
    /// </summary>
    public interface IChunkExecutor
    {
        Span<ArrayCommand> Commands { get; }

        /// <summary>
        /// If <c>true</c>, the executor will preserve the generated code
        /// </summary>
        bool PreserveGeneratedCode { get; set; }
        /// <summary>
        /// The generated code, if preserved
        /// </summary>
        string GeneratedCode { get; }

        bool UseCheckpoints { get; }

        ArrayCommandList ArrayCommandListForCheckpoints { get; }

        /// <summary>
        /// Queue <paramref name="chunk"/> for later batch code‑generation.  Executions
        /// should not occur until <see cref="PerformGeneration"/> has been called.
        /// </summary>
        /// <param name="chunk">The command slice that this executor will handle.</param>
        void AddToGeneration(ArrayCommandChunk chunk);

        /// <summary>
        /// Complete code‑generation for all chunks previously supplied through
        /// <see cref="AddToGeneration"/>.  Roslyn‑based executors typically perform a
        /// single in‑memory compilation here; IL‑emit executors bake their
        /// <see cref="System.Reflection.Emit.DynamicMethod"/> delegates; a pure
        /// interpreter does nothing.
        /// </summary>
        void PerformGeneration();

        /// <summary>
        /// Execute <paramref name="chunk"/>.
        /// </summary>
        /// <remarks>
        /// The shared ordered‑source and ordered‑destination pointers are passed by
        /// <c>ref</c> so that successive chunks observe updated positions.  The
        /// <paramref name="condition"/> flag propagates the state of the most recent
        /// conditional‑gate evaluation.
        /// </remarks>
        void Execute(
            ArrayCommandChunk chunk,
            double[] virtualStack,
            double[] orderedSources,
            double[] orderedDestinations,
            ref int cosi,
            ref int codi,
            ref bool condition);
    }
}
