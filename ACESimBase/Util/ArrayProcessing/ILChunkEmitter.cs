using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using System.Collections.Generic;
using System.Reflection.Emit;
using System;

namespace ACESimBase.Util.ArrayProcessing
{
    public class ILChunkEmitter
    {
        private readonly ArrayCommandChunk _chunk;
        private readonly ArrayCommand[] _commands;
        private readonly int _startIndex;
        private readonly int _endIndexExclusive;

        private ILGenerator _il;
        private LocalBuilder _localCosi;
        private LocalBuilder _localCodi;
        private LocalBuilder _localCondition;
        private readonly Stack<IfBlockInfo> _ifBlocks = new();

        public ILChunkEmitter(ArrayCommandChunk chunk, ArrayCommand[] allCommands)
        {
            _chunk = chunk;
            _commands = allCommands;
            _startIndex = chunk.StartCommandRange;
            _endIndexExclusive = chunk.EndCommandRangeExclusive;
        }

        public ArrayCommandChunkDelegate EmitMethod(string name, out int ilBytes)
        {
            if (name == null)
                name = $"Chunk_{_startIndex}_{_endIndexExclusive - 1}";

            var dm = new DynamicMethod(
                name,
                null,
                new[] {
                    typeof(double[]),               // 0 vs
                    typeof(double[]),               // 1 os
                    typeof(double[]),               // 2 od
                    typeof(int).MakeByRefType(),    // 3 cosi
                    typeof(int).MakeByRefType(),    // 4 codi
                    typeof(bool).MakeByRefType()    // 5 cond
                },
                typeof(ArrayCommandList).Module,
                true);

            _il = dm.GetILGenerator();

            _localCosi = _il.DeclareLocal(typeof(int));
            _localCodi = _il.DeclareLocal(typeof(int));
            _localCondition = _il.DeclareLocal(typeof(bool));

            /* prologue – copy by-ref inputs into locals */
            _il.Emit(OpCodes.Ldarg_3); _il.Emit(OpCodes.Ldind_I4); _il.Emit(OpCodes.Stloc, _localCosi);
            _il.Emit(OpCodes.Ldarg_S, 4); _il.Emit(OpCodes.Ldind_I4); _il.Emit(OpCodes.Stloc, _localCodi);
            _il.Emit(OpCodes.Ldarg_S, 5); _il.Emit(OpCodes.Ldind_I1); _il.Emit(OpCodes.Stloc, _localCondition);

            /* body */
            for (int i = _startIndex; i < _endIndexExclusive; i++)
                EmitCommand(_commands[i]);

            /* epilogue – write locals back */
            _il.Emit(OpCodes.Ldarg_3); _il.Emit(OpCodes.Ldloc, _localCosi); _il.Emit(OpCodes.Stind_I4);
            _il.Emit(OpCodes.Ldarg_S, 4); _il.Emit(OpCodes.Ldloc, _localCodi); _il.Emit(OpCodes.Stind_I4);
            _il.Emit(OpCodes.Ldarg_S, 5); _il.Emit(OpCodes.Ldloc, _localCondition); _il.Emit(OpCodes.Stind_I1);

            if (_ifBlocks.Count != 0) throw new InvalidOperationException("Unmatched If/EndIf");
            _il.Emit(OpCodes.Ret);

            ilBytes = _il.ILOffset;
            return (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
        }

        // ───────────────────────── command dispatcher ─────────────────────────
        private void EmitCommand(ArrayCommand c)
        {
            switch (c.CommandType)
            {
                case ArrayCommandType.Zero: EmitZero(c.Index); break;
                case ArrayCommandType.CopyTo: EmitCopyTo(c.Index, c.SourceIndex); break;
                case ArrayCommandType.NextSource: EmitNextSource(c.Index); break;
                case ArrayCommandType.NextDestination: EmitNextDestination(c.SourceIndex); break;
                case ArrayCommandType.ReusedDestination: EmitReusedDestination(c.Index, c.SourceIndex); break;
                case ArrayCommandType.MultiplyBy: EmitMultiplyBy(c.Index, c.SourceIndex); break;
                case ArrayCommandType.IncrementBy: EmitIncrementBy(c.Index, c.SourceIndex); break;
                case ArrayCommandType.DecrementBy: EmitDecrementBy(c.Index, c.SourceIndex); break;
                case ArrayCommandType.EqualsOtherArrayIndex: EmitCmpIdx(c.Index, c.SourceIndex, "eq"); break;
                case ArrayCommandType.NotEqualsOtherArrayIndex: EmitCmpIdx(c.Index, c.SourceIndex, "ne"); break;
                case ArrayCommandType.GreaterThanOtherArrayIndex: EmitCmpIdx(c.Index, c.SourceIndex, "gt"); break;
                case ArrayCommandType.LessThanOtherArrayIndex: EmitCmpIdx(c.Index, c.SourceIndex, "lt"); break;
                case ArrayCommandType.EqualsValue: EmitCmpVal(c.Index, c.SourceIndex, "eq"); break;
                case ArrayCommandType.NotEqualsValue: EmitCmpVal(c.Index, c.SourceIndex, "ne"); break;
                case ArrayCommandType.If: EmitIf(); break;
                case ArrayCommandType.EndIf: EmitEndIf(); break;
            }
        }

        // ───────────────────────── array ops (fixed order) ────────────────────
        private void EmitZero(int dst)
        {
            // Correct operand order for stelem.r8:  array, index, value
            _il.Emit(OpCodes.Ldarg_0);       // array  (vs)
            _il.Emit(OpCodes.Ldc_I4, dst);   // index
            _il.Emit(OpCodes.Ldc_R8, 0.0);   // value
            _il.Emit(OpCodes.Stelem_R8);     // vs[dst] = 0.0
        }

        private void EmitCopyTo(int dst, int src)
        {
            _il.Emit(OpCodes.Ldarg_0);       // array
            _il.Emit(OpCodes.Ldc_I4, dst);   // index
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitNextSource(int dst)
        {
            _il.Emit(OpCodes.Ldarg_0);       // array
            _il.Emit(OpCodes.Ldc_I4, dst);   // index
            _il.Emit(OpCodes.Ldarg_1); _il.Emit(OpCodes.Ldloc, _localCosi); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Stelem_R8);
            _il.Emit(OpCodes.Ldloc, _localCosi); _il.Emit(OpCodes.Ldc_I4_1); _il.Emit(OpCodes.Add); _il.Emit(OpCodes.Stloc, _localCosi);
        }

        private void EmitNextDestination(int src)
        {
            _il.Emit(OpCodes.Ldarg_2);       // od
            _il.Emit(OpCodes.Ldloc, _localCodi);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Stelem_R8);
            _il.Emit(OpCodes.Ldloc, _localCodi); _il.Emit(OpCodes.Ldc_I4_1); _il.Emit(OpCodes.Add); _il.Emit(OpCodes.Stloc, _localCodi);
        }

        private void EmitReusedDestination(int dst, int src)
        {
            _il.Emit(OpCodes.Ldarg_2); _il.Emit(OpCodes.Ldc_I4, dst);
            _il.Emit(OpCodes.Ldarg_2); _il.Emit(OpCodes.Ldc_I4, dst); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Stelem_R8);
        }

        // ───────────────────────── arithmetic (order fixed) ───────────────────
        private void EmitMultiplyBy(int dst, int src)
        {
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Mul); _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitIncrementBy(int dst, int src)
        {
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Add); _il.Emit(OpCodes.Stelem_R8);
        }

        private void EmitDecrementBy(int dst, int src)
        {
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, dst); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, src); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Sub); _il.Emit(OpCodes.Stelem_R8);
        }

        // ───────────────────────── comparisons (unchanged) ────────────────────
        private void EmitCmpIdx(int a, int b, string op)
        {
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, a); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, b); _il.Emit(OpCodes.Ldelem_R8);
            EmitCmpCore(op);
        }

        private void EmitCmpVal(int idx, double val, string op)
        {
            _il.Emit(OpCodes.Ldarg_0); _il.Emit(OpCodes.Ldc_I4, idx); _il.Emit(OpCodes.Ldelem_R8);
            _il.Emit(OpCodes.Ldc_R8, val);
            EmitCmpCore(op);
        }

        private void EmitCmpCore(string op)
        {
            switch (op)
            {
                case "eq": _il.Emit(OpCodes.Ceq); break;
                case "ne": _il.Emit(OpCodes.Ceq); _il.Emit(OpCodes.Ldc_I4_0); _il.Emit(OpCodes.Ceq); break;
                case "gt": _il.Emit(OpCodes.Cgt); break;
                case "lt": _il.Emit(OpCodes.Clt); break;
            }
            var t = _il.DefineLabel();
            var e = _il.DefineLabel();
            _il.Emit(OpCodes.Brtrue, t);
            _il.Emit(OpCodes.Ldc_I4_0); _il.Emit(OpCodes.Stloc, _localCondition); _il.Emit(OpCodes.Br, e);
            _il.MarkLabel(t); _il.Emit(OpCodes.Ldc_I4_1); _il.Emit(OpCodes.Stloc, _localCondition);
            _il.MarkLabel(e);
        }

        // ───────────────────────── flow control (unchanged) ───────────────────
        private void EmitIf()
        {
            var blk = new IfBlockInfo { SkipLabel = _il.DefineLabel() };
            _il.Emit(OpCodes.Ldloc, _localCondition);
            _il.Emit(OpCodes.Brfalse, blk.SkipLabel);
            _ifBlocks.Push(blk);
        }

        private void EmitEndIf()
        {
            if (_ifBlocks.Count == 0) throw new InvalidOperationException("EndIf without If");
            var blk = _ifBlocks.Pop();
            _il.MarkLabel(blk.SkipLabel);
        }

        private sealed class IfBlockInfo { public Label SkipLabel; }
    }
}
