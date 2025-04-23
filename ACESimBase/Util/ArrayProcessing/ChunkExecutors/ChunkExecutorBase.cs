using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    public abstract class ChunkExecutorBase : IChunkExecutor
    {

        public bool PreserveGeneratedCode { get; set; } = true;
        public string GeneratedCode { get; set; } = string.Empty;

        private ArrayCommand[] UnderlyingCommands;
        private int StartIndex;
        private int EndIndexExclusive;

        public Span<ArrayCommand> Commands => UnderlyingCommands.AsSpan(StartIndex, EndIndexExclusive - StartIndex);

        public ChunkExecutorBase(ArrayCommand[] underlyingCommands, int startIndex, int endIndexExclusive)
        {
            UnderlyingCommands = underlyingCommands;
            StartIndex = startIndex;
            EndIndexExclusive = endIndexExclusive;
        }

        public abstract void AddToGeneration(ArrayCommandChunk chunk);
        public abstract void Execute(ArrayCommandChunk chunk, double[] virtualStack, double[] orderedSources, double[] orderedDestinations, ref int cosi, ref int codi, ref bool condition);
        public abstract void PerformGeneration();
    }
}
