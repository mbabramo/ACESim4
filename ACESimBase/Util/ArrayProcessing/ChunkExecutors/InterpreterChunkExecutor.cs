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
                           int start, int end, bool useCheckpoints, ArrayCommandList arrayCommandListForCheckpoints)
            : base(commands, start, end, useCheckpoints, arrayCommandListForCheckpoints)
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

        private void RecordCheckpoint(int sourceIndex, double value)
        {
            ArrayCommandListForCheckpoints.Checkpoints.Add((sourceIndex, value));
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
            for (int idx = chunk.StartCommandRange; idx < chunk.EndCommandRangeExclusive; idx++)
            {
                var cmd = Commands[idx];
                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        virtualStack[cmd.Index] = 0.0;
                        break;

                    case ArrayCommandType.CopyTo:
                        if (UseCheckpoints && cmd.Index == CheckpointTrigger)
                        {
                            ArrayCommandListForCheckpoints.Checkpoints.Add((cmd.SourceIndex, virtualStack[cmd.SourceIndex]));
                        }
                        else
                        {
                            virtualStack[cmd.Index] = virtualStack[cmd.SourceIndex];
                        }
                        break;

                    case ArrayCommandType.NextSource:
                        virtualStack[cmd.Index] = orderedSources[cosi++];
                        break;

                    case ArrayCommandType.NextDestination:
                    {
                        int odLen = orderedDestinations?.Length ?? -1;

                        if (odLen <= 0 || (uint)codi >= (uint)odLen)
                            throw new IndexOutOfRangeException(
                                $"Interpreter NextDestination OOB: ip={idx}, codi={codi}, odLen={odLen}, srcVS={cmd.SourceIndex}");

                        // Accumulate into the ordered-destination value buffer; indices are queued elsewhere.
                        orderedDestinations[codi] += virtualStack[cmd.SourceIndex];
                        codi++;
                        break;
                    }

                    case ArrayCommandType.MultiplyBy:
                        virtualStack[cmd.Index] *= virtualStack[cmd.SourceIndex];
                        break;

                    case ArrayCommandType.IncrementBy:
                        virtualStack[cmd.Index] += virtualStack[cmd.SourceIndex];
                        break;

                    case ArrayCommandType.DecrementBy:
                        virtualStack[cmd.Index] -= virtualStack[cmd.SourceIndex];
                        break;

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

                    case ArrayCommandType.If:
                        if (!condition)
                        {
                            int depth = 1;
                            // Advance source/destination pointers for commands we *skip*,
                            // but only within this chunk’s range.
                            while (depth > 0 && idx + 1 < chunk.EndCommandRangeExclusive)
                            {
                                idx++;
                                var t = Commands[idx].CommandType;
                                if      (t == ArrayCommandType.If)           depth++;
                                else if (t == ArrayCommandType.EndIf)        depth--;
                                else if (t == ArrayCommandType.NextSource)   cosi++;
                                else if (t == ArrayCommandType.NextDestination) codi++;
                            }
                        }
                        break;

                    case ArrayCommandType.Checkpoint:
                        if (UseCheckpoints)
                        {
                            ArrayCommandListForCheckpoints.Checkpoints.Add((cmd.SourceIndex, virtualStack[cmd.SourceIndex]));
                        }
                        break;

                    case ArrayCommandType.EndIf:
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                    case ArrayCommandType.IncrementDepth:
                    case ArrayCommandType.DecrementDepth:
                        break;

                    default:
                        throw new NotImplementedException($"Interpreter: unsupported command {cmd.CommandType}");
                }
            }
        }


    }
}
