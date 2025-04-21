using System;
using System.Collections.Generic;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Executes ArrayCommandChunks by interpreting each command in sequence.
    /// Currently supports only Zero commands (further command types will throw).
    /// </summary>
    public class InterpreterChunkExecutor : IChunkExecutor
    {
        private readonly ArrayCommand[] _commands;

        public InterpreterChunkExecutor(ArrayCommand[] commands)
        {
            _commands = commands;
        }

        public void AddToGeneration(ArrayCommandChunk chunk)
        {
            // No generation needed for interpretation
        }

        public void PerformGeneration()
        {
            // No-op
        }

        public void Execute(
            ArrayCommandChunk chunk,
            double[] virtualStack,
            double[] orderedSources,
            double[] orderedDestinations,
            ref int cosi,
            ref int codi,
            ref bool condition)
        {
            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                var cmd = _commands[i];
                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        virtualStack[cmd.Index] = 0.0;
                        break;
                    default:
                        throw new NotImplementedException(
                            $"Interpreter: unsupported command {cmd.CommandType}");
                }
            }
        }
    }
}
