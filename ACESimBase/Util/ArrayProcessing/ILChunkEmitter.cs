using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using System.Collections.Generic;
using System.Reflection.Emit;
using System;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ILChunkEmitter
    {
        private readonly ArrayCommandChunk _chunk;
        private readonly ArrayCommand[] _commands;  // The entire command array
        private readonly int _startIndex;
        private readonly int _endIndexExclusive;

        // IL generator stuff
        private ILGenerator _il;

        // We store references to locals
        private LocalBuilder _localCosi;       // int
        private LocalBuilder _localCodi;       // int
        private LocalBuilder _localCondition;  // bool

        // We track nested If blocks with a stack of IfBlockInfo
        private Stack<IfBlockInfo> _ifBlocks = new Stack<IfBlockInfo>();

        // Constructor
        public ILChunkEmitter(ArrayCommandChunk chunk, ArrayCommand[] allCommands)
        {
            _chunk = chunk;
            _commands = allCommands;
            _startIndex = chunk.StartCommandRange;
            _endIndexExclusive = chunk.EndCommandRangeExclusive;
        }

        /// <summary>
        /// Creates a DynamicMethod for this chunk and returns the compiled delegate.
        /// Signature: void (double[] vs, double[] os, double[] od,
        ///                  ref int cosi, ref int codi, ref bool condition)
        /// </summary>
        public ArrayCommandChunkDelegate EmitMethod(string methodName, out int ilBytes)
        {
            if (methodName == null)
                methodName = $"Chunk_{_startIndex}_{_endIndexExclusive - 1}";

            var parameters = new[]
            {
        typeof(double[]),               // 0  vs
        typeof(double[]),               // 1  os
        typeof(double[]),               // 2  od
        typeof(int).MakeByRefType(),    // 3  cosi  (ref)
        typeof(int).MakeByRefType(),    // 4  codi  (ref)
        typeof(bool).MakeByRefType()    // 5  condition (ref)  ← NEW
    };

            var dm = new DynamicMethod(
                methodName,
                returnType: null,                         // void
                parameterTypes: parameters,
                m: typeof(ArrayCommandList).Module,
                skipVisibility: true);

            _il = dm.GetILGenerator();

            /* locals: 0‑cosi, 1‑codi, 2‑condition */
            _localCosi = _il.DeclareLocal(typeof(int));
            _localCodi = _il.DeclareLocal(typeof(int));
            _localCondition = _il.DeclareLocal(typeof(bool));

            /* localCosi = *cosi */
            _il.Emit(OpCodes.Ldarg_3);
            _il.Emit(OpCodes.Ldind_I4);
            _il.Emit(OpCodes.Stloc, _localCosi);

            /* localCodi = *codi */
            _il.Emit(OpCodes.Ldarg_S, 4);
            _il.Emit(OpCodes.Ldind_I4);
            _il.Emit(OpCodes.Stloc, _localCodi);

            /* localCondition = *condition */
            _il.Emit(OpCodes.Ldarg_S, 5);
            _il.Emit(OpCodes.Ldind_I1);
            _il.Emit(OpCodes.Stloc, _localCondition);

            /* emit IL for every command in the slice */
            for (int cmd = _startIndex; cmd < _endIndexExclusive; cmd++)
                EmitCommand(_commands[cmd]);

            /* *cosi = localCosi */
            _il.Emit(OpCodes.Ldarg_3);
            _il.Emit(OpCodes.Ldloc, _localCosi);
            _il.Emit(OpCodes.Stind_I4);

            /* *codi = localCodi */
            _il.Emit(OpCodes.Ldarg_S, 4);
            _il.Emit(OpCodes.Ldloc, _localCodi);
            _il.Emit(OpCodes.Stind_I4);

            /* *condition = localCondition */
            _il.Emit(OpCodes.Ldarg_S, 5);
            _il.Emit(OpCodes.Ldloc, _localCondition);
            _il.Emit(OpCodes.Stind_I1);

            if (_ifBlocks.Count != 0)
                throw new InvalidOperationException(
                    $"Unclosed If block(s) in chunk {_startIndex}–{_endIndexExclusive - 1}");

            _il.Emit(OpCodes.Ret);

            ilBytes = _il.ILOffset;
            return (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
        }


        /// <summary>
        /// Master switch on the command type. 
        /// You can split out sub-methods for big categories to keep this readable.
        /// </summary>
        private void EmitCommand(ArrayCommand command)
        {
            // Basic guard for commands that will do array access
            // (If you do use certain negative values as special flags, exclude them here.)
            switch (command.CommandType)
            {
                case ArrayCommandType.Zero:
                case ArrayCommandType.CopyTo:
                case ArrayCommandType.IncrementBy:
                case ArrayCommandType.DecrementBy:
                case ArrayCommandType.MultiplyBy:
                    // Here, Index is a target in vs[]. Must be >= 0.
                    if (command.Index < 0)
                    {
                        throw new InvalidOperationException(
                            $"Negative Index={command.Index} in {command.CommandType} command is invalid for vs[].");
                    }
                    // Also, SourceIndex is used if this command actually needs a second array read:
                    //   - Zero does not need SourceIndex 
                    //   - The others do
                    // If your code uses e.g. Zero's SourceIndex == -1, skip checking for Zero.
                    if (command.CommandType != ArrayCommandType.Zero)
                    {
                        if (command.SourceIndex < 0)
                        {
                            throw new InvalidOperationException(
                                $"Negative SourceIndex={command.SourceIndex} in {command.CommandType} is invalid for vs[].");
                        }
                    }
                    break;

                case ArrayCommandType.NextSource:
                    // Index is the target in vs[]. Must be >= 0
                    if (command.Index < 0)
                    {
                        throw new InvalidOperationException(
                            $"Negative Index={command.Index} in NextSource is invalid for vs[].");
                    }
                    // SourceIndex is not used, so we skip checks (often -1).
                    break;

                case ArrayCommandType.NextDestination:
                    // Index is not used, often -1
                    // SourceIndex is the read from vs[]. Must be >= 0
                    if (command.SourceIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Negative SourceIndex={command.SourceIndex} in NextDestination is invalid for vs[].");
                    }
                    break;

                case ArrayCommandType.ReusedDestination:
                    // Index is for the od[] array if your code expects od[<index>].
                    // If that's always a valid index, require >= 0 
                    if (command.Index < 0)
                    {
                        throw new InvalidOperationException(
                            $"Negative Index={command.Index} in ReusedDestination is invalid for od[].");
                    }
                    // SourceIndex is the read from vs[], must be >= 0
                    if (command.SourceIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Negative SourceIndex={command.SourceIndex} in ReusedDestination is invalid for vs[].");
                    }
                    break;

                case ArrayCommandType.If:
                case ArrayCommandType.EndIf:
                case ArrayCommandType.EqualsOtherArrayIndex:
                case ArrayCommandType.NotEqualsOtherArrayIndex:
                case ArrayCommandType.GreaterThanOtherArrayIndex:
                case ArrayCommandType.LessThanOtherArrayIndex:
                case ArrayCommandType.EqualsValue:
                case ArrayCommandType.NotEqualsValue:
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    // Typically we skip checks. Or if e.g. you do "vs[index]" inside these commands, 
                    // then you'd check index >= 0 / sourceIndex >= 0. 
                    // But if your code sets them to -1 for no usage, do not fail.
                    break;
            }

            switch (command.CommandType)
            {
                // ---------------
                // Array element ops
                // ---------------
                case ArrayCommandType.Zero:
                    EmitZero(command.Index);
                    break;

                case ArrayCommandType.CopyTo:
                    EmitCopyTo(command.Index, command.SourceIndex);
                    break;

                case ArrayCommandType.NextSource:
                    EmitNextSource(command.Index);
                    break;

                case ArrayCommandType.NextDestination:
                    EmitNextDestination(command.SourceIndex);
                    break;

                case ArrayCommandType.ReusedDestination:
                    EmitReusedDestination(command.Index, command.SourceIndex);
                    break;

                // ---------------
                // Arithmetic
                // ---------------
                case ArrayCommandType.MultiplyBy:
                    EmitMultiplyBy(command.Index, command.SourceIndex);
                    break;

                case ArrayCommandType.IncrementBy:
                    EmitIncrementBy(command.Index, command.SourceIndex);
                    break;

                case ArrayCommandType.DecrementBy:
                    EmitDecrementBy(command.Index, command.SourceIndex);
                    break;

                // ---------------
                // Comparisons
                // ---------------
                case ArrayCommandType.EqualsOtherArrayIndex:
                    EmitComparison_Indices(command.Index, command.SourceIndex, compareType: "eq");
                    break;

                case ArrayCommandType.NotEqualsOtherArrayIndex:
                    EmitComparison_Indices(command.Index, command.SourceIndex, compareType: "ne");
                    break;

                case ArrayCommandType.GreaterThanOtherArrayIndex:
                    EmitComparison_Indices(command.Index, command.SourceIndex, compareType: "gt");
                    break;

                case ArrayCommandType.LessThanOtherArrayIndex:
                    EmitComparison_Indices(command.Index, command.SourceIndex, compareType: "lt");
                    break;

                // Comparisons vs. constant
                case ArrayCommandType.EqualsValue:
                    EmitComparison_Value(command.Index, (double)command.SourceIndex, compareType: "eq");
                    break;

                case ArrayCommandType.NotEqualsValue:
                    EmitComparison_Value(command.Index, (double)command.SourceIndex, compareType: "ne");
                    break;

                // ---------------
                // Flow control
                // ---------------
                case ArrayCommandType.If:
                    EmitIf();
                    break;

                case ArrayCommandType.EndIf:
                    EmitEndIf();
                    break;

                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    // no-op
                    break;

                default:
                    throw new NotImplementedException($"No IL emitter for command type {command.CommandType}");
            }
        }

        #region Array Element Operations

        private void EmitZero(int targetIndex)
        {
            // vs[targetIndex] = 0.0
            //   ldarg_0 (vs)
            //   ldc.i4 targetIndex
            //   ldc.r8 0.0
            //   stelem.r8
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            _il.Emit(OpCodes.Ldc_R8, 0.0);
            _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitCopyTo(int targetIndex, int sourceIndex)
        {
            // vs[targetIndex] = vs[sourceIndex]
            //   ldarg_0
            //   ldc.i4 targetIndex
            //   ldarg_0
            //   ldc.i4 sourceIndex
            //   ldelem.r8
            //   stelem.r8
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitNextSource(int targetIndex)
        {
            // vs[targetIndex] = os[localCosi++];
            // Steps:
            //   1) load vs
            //   2) push targetIndex
            //   3) load os
            //   4) load localCosi
            //   5) ldelem.r8
            //   6) stelem.r8
            //   7) increment localCosi
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);

            // load os[ localCosi ]
            _il.Emit(OpCodes.Ldarg_1); // os
            _il.Emit(OpCodes.Ldloc, _localCosi);
            _il.Emit(OpCodes.Ldelem_R8);

            _il.Emit(OpCodes.Stelem_R8);

            // localCosi++
            _il.Emit(OpCodes.Ldloc, _localCosi);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Stloc, _localCosi);
        }

        private void EmitNextDestination(int sourceIndex)
        {
            // od[localCodi++] = vs[sourceIndex];
            // Steps:
            //   load od
            //   load localCodi
            //   load vs[sourceIndex]
            //   stelem.r8
            //   localCodi++
            _il.Emit(OpCodes.Ldarg_2);     // od
            _il.Emit(OpCodes.Ldloc, _localCodi);

            // load vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // stelem.r8
            _il.Emit(OpCodes.Stelem_R8);

            // localCodi++
            _il.Emit(OpCodes.Ldloc, _localCodi);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Stloc, _localCodi);
        }

        private void EmitReusedDestination(int reusedDestIndex, int sourceIndex)
        {
            // od[reusedDestIndex] += vs[sourceIndex];
            // Steps:
            //   load od
            //   load reusedDestIndex
            //   ldelem.r8
            //   load vs[sourceIndex]
            //   add
            //   stelem.r8
            _il.Emit(OpCodes.Ldarg_2); // od
            _il.Emit(OpCodes.Ldc_I4, reusedDestIndex);
            _il.Emit(OpCodes.Ldarg_2);
            _il.Emit(OpCodes.Ldc_I4, reusedDestIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // add
            _il.Emit(OpCodes.Add);

            // stelem.r8
            _il.Emit(OpCodes.Stelem_R8);
        }

        #endregion

        #region Arithmetic

        private void EmitMultiplyBy(int targetIndex, int sourceIndex)
        {
            // vs[targetIndex] *= vs[sourceIndex];
            //   vs[targetIndex] = vs[targetIndex] * vs[sourceIndex]
            //   load vs
            //   load targetIndex
            //   load vs[targetIndex]
            //   load vs[sourceIndex]
            //   mul
            //   stelem.r8
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            // load vs[targetIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            _il.Emit(OpCodes.Ldelem_R8);
            // load vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);
            // mul
            _il.Emit(OpCodes.Mul);
            // stelem.r8
            _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitIncrementBy(int targetIndex, int sourceIndex)
        {
            // vs[targetIndex] += vs[sourceIndex];
            //   vs[targetIndex] = vs[targetIndex] + vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);

            // vs[targetIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // add
            _il.Emit(OpCodes.Add);

            // stelem.r8
            _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitDecrementBy(int targetIndex, int sourceIndex)
        {
            // vs[targetIndex] -= vs[sourceIndex];
            //   vs[targetIndex] = vs[targetIndex] - vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);

            // vs[targetIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, targetIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // vs[sourceIndex]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, sourceIndex);
            _il.Emit(OpCodes.Ldelem_R8);

            // sub
            _il.Emit(OpCodes.Sub);

            // stelem.r8
            _il.Emit(OpCodes.Stelem_R8);
        }

        #endregion

        #region Comparisons

        private void EmitComparison_Indices(int idx, int src, string compareType)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, idx);
            _il.Emit(OpCodes.Ldelem_R8);

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, src);
            _il.Emit(OpCodes.Ldelem_R8);

            _il.Emit(OpCodes.Sub);

            Label labelTrue = _il.DefineLabel();
            Label labelEnd = _il.DefineLabel();

            _il.Emit(OpCodes.Ldc_R8, 0.0);

            switch (compareType)
            {
                case "eq": _il.Emit(OpCodes.Beq, labelTrue); break;   // CHANGED
                case "ne": _il.Emit(OpCodes.Bne_Un, labelTrue); break;   // CHANGED
                case "gt": _il.Emit(OpCodes.Bgt, labelTrue); break;   // CHANGED
                case "lt": _il.Emit(OpCodes.Blt, labelTrue); break;   // CHANGED
            }

            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, _localCondition);
            _il.Emit(OpCodes.Br, labelEnd);                               // CHANGED

            _il.MarkLabel(labelTrue);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, _localCondition);

            _il.MarkLabel(labelEnd);
        }


        /// <summary>
        /// Compares vs[idx] and a constant double, with eq/ne, storing into localCondition.
        /// </summary>
        private void EmitComparison_Value(int idx, double value, string compareType)
        {
            // load vs[idx]
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, idx);
            _il.Emit(OpCodes.Ldelem_R8);

            // load constant
            _il.Emit(OpCodes.Ldc_R8, value);

            // Then do the same style sub and check
            switch (compareType)
            {
                case "eq":
                    // ...
                    // For brevity, do same approach as eq above
                    // ...
                    EmitComparison_IndicesUsingSubRoutine("eq");
                    break;
                case "ne":
                    EmitComparison_IndicesUsingSubRoutine("ne");
                    break;
                default:
                    throw new NotImplementedException($"Only eq/ne shown for constant compares. Got {compareType}.");
            }
        }

        /// <summary>
        /// Helper for eq/ne style. Expects top of stack to be (leftValue, rightValue).
        /// We do leftValue-rightValue, check ==0 or !=0, store in localCondition.
        /// We re-use the approach from above. In real code, you might unify them fully.
        /// </summary>
        private void EmitComparison_IndicesUsingSubRoutine(string compareType)
        {
            Label labelTrue = _il.DefineLabel();
            Label labelEnd = _il.DefineLabel();

            _il.Emit(OpCodes.Sub);
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_R8, 0.0);

            if (compareType == "eq")
                _il.Emit(OpCodes.Beq, labelTrue);                     // CHANGED
            else
                _il.Emit(OpCodes.Bne_Un, labelTrue);                     // CHANGED

            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, _localCondition);
            _il.Emit(OpCodes.Br, labelEnd);                              // CHANGED

            _il.MarkLabel(labelTrue);
            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, _localCondition);

            _il.MarkLabel(labelEnd);
        }

        #endregion

        #region Flow Control

        private void EmitIf()
        {
            var info = new IfBlockInfo { SkipLabel = _il.DefineLabel(), WasMarked = false };

            // branch
            _il.Emit(OpCodes.Ldloc, _localCondition);
            _il.Emit(OpCodes.Brfalse, info.SkipLabel);

            _ifBlocks.Push(info);
        }

        private void EmitEndIf()
        {
            if (_ifBlocks.Count == 0)
                throw new InvalidOperationException(
                    "EndIf encountered with empty If‑stack (unmatched EndIf).");

            var blk = _ifBlocks.Pop();

            // Mark the label **once** (double‑marking also throws)
            if (blk.WasMarked)
                throw new InvalidOperationException(
                    "SkipLabel already marked – duplicate EndIf?");
            blk.WasMarked = true;               // record
            _il.MarkLabel(blk.SkipLabel);
        }

        #endregion
    }
}
