// File: ChunkExecutors/InterpreterChunkExecutor.cs

using System;
using System.Collections.Generic;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Executes ArrayCommandChunks by interpreting each command in sequence.
    /// </summary>
    public class InterpreterChunkExecutor : ChunkExecutorBase
    {
        public InterpreterChunkExecutor(ArrayCommand[] commands,
                           int start, int end, bool useCheckpoints)
            : base(commands, start, end, useCheckpoints)
        {
        }

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            // Nothing to do for interpretation
        }

        public override void PerformGeneration()
        {
            // No-op
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
            // condition carries the result of the last comparison
            for (int idx = chunk.StartCommandRange; idx < chunk.EndCommandRangeExclusive; idx++)
            {
                var cmd = Commands[idx];
                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        virtualStack[cmd.Index] = 0.0;
                        break;

                    case ArrayCommandType.CopyTo:
                        virtualStack[cmd.Index] = virtualStack[cmd.SourceIndex];
                        break;

                    case ArrayCommandType.NextSource:
                        virtualStack[cmd.Index] = orderedSources[cosi++];
                        break;

                    case ArrayCommandType.NextDestination:
                        {
                            double value = virtualStack[cmd.SourceIndex];
                            orderedDestinations[codi++] = value;
                        }
                        break;

                    case ArrayCommandType.ReusedDestination:
                        {
                            double value = virtualStack[cmd.SourceIndex];
                            orderedDestinations[cmd.Index] += value;
                        }
                        break;

                    case ArrayCommandType.MultiplyBy:
                        virtualStack[cmd.Index] *= virtualStack[cmd.SourceIndex];
                        break;

                    case ArrayCommandType.IncrementBy:
                        {
                            double oldVal = virtualStack[cmd.Index];
                            double delta = virtualStack[cmd.SourceIndex];
                            virtualStack[cmd.Index] = oldVal + delta;
                        }
                        break;

                    case ArrayCommandType.DecrementBy:
                        {
                            double oldVal = virtualStack[cmd.Index];
                            double delta = virtualStack[cmd.SourceIndex];
                            virtualStack[cmd.Index] = oldVal - delta;
                        }
                        break;

                    // comparisons set the branch condition
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        condition = virtualStack[cmd.Index] == virtualStack[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        condition = virtualStack[cmd.Index] != virtualStack[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        condition = virtualStack[cmd.Index] > virtualStack[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        condition = virtualStack[cmd.Index] < virtualStack[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.EqualsValue:
                        condition = virtualStack[cmd.Index] == cmd.SourceIndex;
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        condition = virtualStack[cmd.Index] != cmd.SourceIndex;
                        break;

                    // flow‑control: skip until matching EndIf if condition is false
                    case ArrayCommandType.If:
                        if (!condition)
                        {
                            int depth = 1;
                            while (depth > 0)
                            {
                                idx++;
                                var ct = Commands[idx].CommandType;
                                if (ct == ArrayCommandType.If) depth++;
                                else if (ct == ArrayCommandType.EndIf) depth--;
                                else if (ct == ArrayCommandType.NextSource) cosi++;
                                else if (ct == ArrayCommandType.NextDestination) codi++;
                            }
                        }
                        break;

                    case ArrayCommandType.EndIf:
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                        // no action
                        break;

                    default:
                        throw new NotImplementedException(
                            $"Interpreter: unsupported command {cmd.CommandType}");
                }
            }
            chunk.StartSourceIndices = cosi;
            chunk.StartDestinationIndices = codi;
        }
    }
}
