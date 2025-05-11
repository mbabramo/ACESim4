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
        public abstract void Execute(ArrayCommandChunk chunk, double[] virtualStack, double[] orderedSources, ref int cosi, ref bool condition);
        public abstract void PerformGeneration();

        protected Dictionary<int, (int src, int dst)> PrecomputePointerSkips(ArrayCommandChunk chunk)
        {
            var map = new Dictionary<int, (int srcSkip, int dstSkip)>();
            var stack = new Stack<int>();          // holds command indices of open Ifs
            int depth = 0;                         // current nesting level *inside*
                                                   // the chunk (may start > 0)

            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                switch (Commands[i].CommandType)
                {
                    /* ── open a new outer-level If that starts *inside* this chunk ── */
                    case ArrayCommandType.If:
                        depth++;
                        stack.Push(i);             // remember the If’s position
                        map[i] = (0, 0);           // initialise skip counters
                        break;

                    /* ── close an If ─────────────────────────────────────────────── */
                    case ArrayCommandType.EndIf:
                        if (depth == 0)             // this EndIf closes an If that
                            break;                  // started *before* the chunk → ignore
                        depth--;
                        stack.Pop();                // matched pair – safe to pop
                        break;

                    /* ── pointer advances inside a still-open If ─────────────────── */
                    case ArrayCommandType.NextSource:
                        foreach (int idx in stack)
                            map[idx] = (map[idx].srcSkip + 1, map[idx].dstSkip);
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
