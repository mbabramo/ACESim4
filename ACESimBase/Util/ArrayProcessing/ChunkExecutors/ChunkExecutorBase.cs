using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    public abstract class ChunkExecutorBase : IChunkExecutor
    {

        public virtual bool PreserveGeneratedCode { get; set; } = false;
        public string GeneratedCode { get; protected set; } = string.Empty;
        public bool UseCheckpoints { get; protected set; } = false;
        public ArrayCommandList ArrayCommandListForCheckpoints { get; protected set; } = null;

        private ArrayCommand[] UnderlyingCommands;
        private int StartIndex;
        private int EndIndexExclusive;

        // Place inside class ChunkExecutorBase
        protected ArrayCommand[] UnderlyingCommandsBuffer => UnderlyingCommands;
        protected int ExecutorStartIndex => StartIndex;
        protected int ExecutorEndIndexExclusive => EndIndexExclusive;


        public Span<ArrayCommand> Commands => UnderlyingCommands.AsSpan(StartIndex, EndIndexExclusive - StartIndex);

        public ChunkExecutorBase(ArrayCommand[] underlyingCommands, int startIndex, int endIndexExclusive, bool useCheckpoints, ArrayCommandList arrayCommandListForCheckpoints)
        {
            UnderlyingCommands = underlyingCommands;
            StartIndex = startIndex;
            EndIndexExclusive = endIndexExclusive;
            UseCheckpoints = useCheckpoints;
            if (UseCheckpoints)
            {
                ArrayCommandListForCheckpoints = arrayCommandListForCheckpoints;
            }
        }

        public abstract void AddToGeneration(ArrayCommandChunk chunk);
        public abstract void Execute(ArrayCommandChunk chunk, double[] virtualStack, double[] orderedSources, double[] orderedDestinations, ref int cosi, ref int codi, ref bool condition);
        public abstract void PerformGeneration();

        protected Dictionary<int, (int src, int dst)> PrecomputePointerSkips(ArrayCommandChunk chunk)
        {
            // Map: IF-command absolute index -> (sources to skip, destinations to skip)
            var map = new Dictionary<int, (int srcSkip, int dstSkip)>();

            // Track IFs that *start inside this chunk*. We intentionally ignore
            // EndIf tokens that close an IF opened before the chunk.
            var openIfs = new Stack<int>();

            var cmds = UnderlyingCommands; // use the full buffer (absolute indices)

            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                switch (cmds[i].CommandType)
                {
                    case ArrayCommandType.If:
                        openIfs.Push(i);
                        map[i] = (0, 0);
                        break;

                    case ArrayCommandType.EndIf:
                        if (openIfs.Count > 0)
                            openIfs.Pop(); // closes an IF that started in this chunk
                        break;

                    case ArrayCommandType.NextSource:
                        foreach (int ifIdx in openIfs)
                            map[ifIdx] = (map[ifIdx].srcSkip + 1, map[ifIdx].dstSkip);
                        break;

                    case ArrayCommandType.NextDestination:
                        foreach (int ifIdx in openIfs)
                            map[ifIdx] = (map[ifIdx].srcSkip, map[ifIdx].dstSkip + 1);
                        break;
                }
            }

            return map;
        }


        public string CommandListString(ArrayCommandChunk chunk)
        {
            if (UnderlyingCommands == null || UnderlyingCommands.Length == 0)
                return string.Empty;

            var stringBuilder = new StringBuilder();
            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                stringBuilder.AppendLine($"{i}: {UnderlyingCommands[i]}");
            }

            return stringBuilder.ToString();
        }
    }
}
