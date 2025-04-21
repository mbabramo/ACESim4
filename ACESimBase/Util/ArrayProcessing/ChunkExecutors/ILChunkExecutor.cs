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
    public class ILChunkExecutor : IChunkExecutor
    {
        private readonly ArrayCommand[] _commands;

        public ILChunkExecutor(ArrayCommand[] commands)
        {
            _commands = commands;
        }

        public void AddToGeneration(ArrayCommandChunk chunk)
        {
            // Placeholder: would queue for IL emit
        }

        public void PerformGeneration()
        {
            // Placeholder: would emit DynamicMethods here
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
            // For now, interpret only Zero commands
            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                var cmd = _commands[i];
                if (cmd.CommandType == ArrayCommandType.Zero)
                    virtualStack[cmd.Index] = 0.0;
                else
                    throw new NotImplementedException(
                        $"ILChunkExecutor: unsupported command {cmd.CommandType}");
            }
        }
    }
}
