using System;
using System.Collections.Generic;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Executes ArrayCommandChunks by a (placeholder) IL-based approach.
    /// Currently supports only Zero commands by delegating to interpreter logic.
    /// </summary>
    public class ILChunkExecutor : ChunkExecutorBase
    {
        public ILChunkExecutor(ArrayCommand[] underlyingCommands, int startIndex, int endIndexExclusive) : base(underlyingCommands, startIndex, endIndexExclusive)
        {
        }

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            // Placeholder: would queue for IL emit
        }

        public override void PerformGeneration()
        {
            // Placeholder: would emit DynamicMethods here
        }

        public override void Execute(
            ArrayCommandChunk chunk,
            double[] virtualStack,
            double[] orderedSources,
            double[] orderedDestinations,
            ref int cosi,
            ref int codi,
            ref bool condition)
        {
            throw new NotImplementedException();
        }
    }
}
