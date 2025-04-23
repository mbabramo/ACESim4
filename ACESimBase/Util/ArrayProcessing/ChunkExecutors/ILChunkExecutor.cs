// ILChunkExecutor.cs

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        private readonly List<ArrayCommandChunk> _queue = new();
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private StringBuilder? _trace;

        public ILChunkExecutor(ArrayCommand[] cmds, int start, int end)
            : base(cmds, start, end, false) { }

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            if (!_compiled.ContainsKey(chunk))
                _queue.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_queue.Count == 0)
                return;

            if (PreserveGeneratedCode)
                _trace = new StringBuilder();

            try
            {
                foreach (var chunk in _queue)
                {
                    var dm = BuildDynamicMethod(chunk, out var src);
                    _compiled[chunk] = (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
                    if (PreserveGeneratedCode)
                        _trace!.Append(src);
                }

                _queue.Clear();
                if (PreserveGeneratedCode)
                    GeneratedCode = _trace!.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error generating IL for chunk\n{_trace}", ex);
            }
        }

        public override void Execute(ArrayCommandChunk chunk, double[] vs, double[] os, double[] od,
            ref int cosi, ref int codi, ref bool cond)
        {
            try
            {
                _compiled[chunk](vs, os, od, ref cosi, ref codi, ref cond);
                chunk.StartSourceIndices += cosi;
                chunk.StartDestinationIndices += codi;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error executing chunk {chunk.StartCommandRange}-{chunk.EndCommandRangeExclusive - 1}.\n{GeneratedCode}", ex);
            }
        }

        private DynamicMethod BuildDynamicMethod(ArrayCommandChunk chunk, out string? trace)
        {
            var dm = new DynamicMethod($"IL_{chunk.StartCommandRange}_{chunk.EndCommandRangeExclusive - 1}", typeof(void),
                new[]
                {
                    typeof(double[]),          // 0 vs
                    typeof(double[]),          // 1 os
                    typeof(double[]),          // 2 od
                    typeof(int).MakeByRefType(), // 3 cosi
                    typeof(int).MakeByRefType(), // 4 codi
                    typeof(bool).MakeByRefType() // 5 cond
                }, typeof(ILChunkExecutor).Module, true);

            var il = dm.GetILGenerator();
            var sb = PreserveGeneratedCode ? new StringBuilder() : null;
            void Trace(string s) => sb?.AppendLine(s);

            void Emit0(OpCode op) { il.Emit(op); Trace($"  {op}"); }
            void EmitI(OpCode op, int arg) { il.Emit(op, arg); Trace($"  {op} {arg}"); }
            void EmitR(OpCode op, double arg) { il.Emit(op, arg); Trace($"  {op} {arg}"); }
            void EmitL(OpCode op, Label lbl) { il.Emit(op, lbl); Trace($"  {op} L{lbl.GetHashCode():x}"); }
            void EmitLb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); Trace($"  {op} V_{lb.LocalIndex}"); }

            var tmp = il.DeclareLocal(typeof(double));

            void LdcI4(int v)
            {
                switch (v)
                {
                    case -1: Emit0(OpCodes.Ldc_I4_M1); break;
                    case 0: Emit0(OpCodes.Ldc_I4_0); break;
                    case 1: Emit0(OpCodes.Ldc_I4_1); break;
                    case 2: Emit0(OpCodes.Ldc_I4_2); break;
                    case 3: Emit0(OpCodes.Ldc_I4_3); break;
                    case 4: Emit0(OpCodes.Ldc_I4_4); break;
                    case 5: Emit0(OpCodes.Ldc_I4_5); break;
                    case 6: Emit0(OpCodes.Ldc_I4_6); break;
                    case 7: Emit0(OpCodes.Ldc_I4_7); break;
                    case 8: Emit0(OpCodes.Ldc_I4_8); break;
                    default:
                        if (v >= sbyte.MinValue && v <= sbyte.MaxValue)
                            EmitI(OpCodes.Ldc_I4_S, (sbyte)v);
                        else
                            EmitI(OpCodes.Ldc_I4, v);
                        break;
                }
            }

            void LdcR8(double d) => EmitR(OpCodes.Ldc_R8, d);

            void IncrementRefInt(int argIdx)
            {
                EmitI(OpCodes.Ldarg, argIdx);
                Emit0(OpCodes.Dup);
                Emit0(OpCodes.Ldind_I4);
                Emit0(OpCodes.Ldc_I4_1);
                Emit0(OpCodes.Add);
                Emit0(OpCodes.Stind_I4);
            }

            void AdvanceRefInt(int argIdx, int delta)
            {
                if (delta == 0) return;
                EmitI(OpCodes.Ldarg, argIdx);
                Emit0(OpCodes.Dup);
                Emit0(OpCodes.Ldind_I4);
                LdcI4(delta);
                Emit0(OpCodes.Add);
                Emit0(OpCodes.Stind_I4);
            }

            void LoadVs(int idx)
            {
                Emit0(OpCodes.Ldarg_0);
                LdcI4(idx);
                Emit0(OpCodes.Ldelem_R8);
            }

            void StoreVs(int idx)
            {
                EmitLb(OpCodes.Stloc, tmp);
                Emit0(OpCodes.Ldarg_0);
                LdcI4(idx);
                EmitLb(OpCodes.Ldloc, tmp);
                Emit0(OpCodes.Stelem_R8);
            }

            var skipMap = PrecomputePointerSkips(chunk);
            var ifStack = new Stack<(Label elseLbl, Label endLbl, int srcSkip, int dstSkip)>();

            for (int ci = chunk.StartCommandRange; ci < chunk.EndCommandRangeExclusive; ci++)
            {
                var cmd = Commands[ci];
                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        LdcR8(0.0);
                        StoreVs(cmd.Index);
                        break;

                    case ArrayCommandType.CopyTo:
                        LoadVs(cmd.SourceIndex);
                        StoreVs(cmd.Index);
                        break;

                    case ArrayCommandType.NextSource:
                        Emit0(OpCodes.Ldarg_1);
                        EmitI(OpCodes.Ldarg, 3);
                        Emit0(OpCodes.Ldind_I4);
                        Emit0(OpCodes.Ldelem_R8);
                        StoreVs(cmd.Index);
                        IncrementRefInt(3);
                        break;

                    case ArrayCommandType.MultiplyBy:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Mul);
                        StoreVs(cmd.Index);
                        break;

                    case ArrayCommandType.IncrementBy:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Add);
                        StoreVs(cmd.Index);
                        break;

                    case ArrayCommandType.DecrementBy:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Sub);
                        StoreVs(cmd.Index);
                        break;

                    case ArrayCommandType.NextDestination:
                        Emit0(OpCodes.Ldarg_2);
                        EmitI(OpCodes.Ldarg, 4);
                        Emit0(OpCodes.Ldind_I4);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Stelem_R8);
                        IncrementRefInt(4);
                        break;

                    case ArrayCommandType.ReusedDestination:
                        Emit0(OpCodes.Ldarg_2);
                        LdcI4(cmd.Index);
                        Emit0(OpCodes.Ldelem_R8);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Add);
                        EmitLb(OpCodes.Stloc, tmp);
                        Emit0(OpCodes.Ldarg_2);
                        LdcI4(cmd.Index);
                        EmitLb(OpCodes.Ldloc, tmp);
                        Emit0(OpCodes.Stelem_R8);
                        break;

                    case ArrayCommandType.EqualsOtherArrayIndex:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Ceq);
                        StoreCond(false);
                        break;

                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Ceq);
                        StoreCond(true);
                        break;

                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Cgt);
                        StoreCond(false);
                        break;

                    case ArrayCommandType.LessThanOtherArrayIndex:
                        LoadVs(cmd.Index);
                        LoadVs(cmd.SourceIndex);
                        Emit0(OpCodes.Clt);
                        StoreCond(false);
                        break;

                    case ArrayCommandType.EqualsValue:
                        LoadVs(cmd.Index);
                        LdcR8(cmd.SourceIndex);
                        Emit0(OpCodes.Ceq);
                        StoreCond(false);
                        break;

                    case ArrayCommandType.NotEqualsValue:
                        LoadVs(cmd.Index);
                        LdcR8(cmd.SourceIndex);
                        Emit0(OpCodes.Ceq);
                        StoreCond(true);
                        break;

                    case ArrayCommandType.If:
                        {
                            var elseLbl = il.DefineLabel();
                            var endLbl = il.DefineLabel();

                            EmitI(OpCodes.Ldarg, 5);
                            Emit0(OpCodes.Ldind_I1);
                            EmitL(OpCodes.Brfalse_S, elseLbl);

                            var skips = skipMap[ci];
                            ifStack.Push((elseLbl, endLbl, skips.srcSkip, skips.dstSkip));
                            break;
                        }

                    case ArrayCommandType.EndIf:
                        {
                            var ctx = ifStack.Pop();
                            EmitL(OpCodes.Br_S, ctx.endLbl);

                            il.MarkLabel(ctx.elseLbl);
                            if (ctx.srcSkip > 0)
                                AdvanceRefInt(3, ctx.srcSkip);
                            if (ctx.dstSkip > 0)
                                AdvanceRefInt(4, ctx.dstSkip);

                            il.MarkLabel(ctx.endLbl);
                            break;
                        }

                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                        break;

                    default:
                        throw new NotImplementedException(cmd.CommandType.ToString());
                }
            }

            Emit0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            void StoreCond(bool invert)
            {
                if (invert)
                {
                    Emit0(OpCodes.Ldc_I4_0);
                    Emit0(OpCodes.Ceq);
                }

                EmitI(OpCodes.Ldarg, 5);
                Emit0(OpCodes.Stind_I1);
            }
        }

        private Dictionary<int, (int srcSkip, int dstSkip)> PrecomputePointerSkips(ArrayCommandChunk chunk)
        {
            var map = new Dictionary<int, (int src, int dst)>();
            var stack = new Stack<int>();

            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                switch (Commands[i].CommandType)
                {
                    case ArrayCommandType.If:
                        stack.Push(i);
                        map[i] = (0, 0);
                        break;
                    case ArrayCommandType.EndIf:
                        stack.Pop();
                        break;
                    case ArrayCommandType.NextSource:
                        foreach (var idx in stack)
                            map[idx] = (map[idx].src + 1, map[idx].dst);
                        break;
                    case ArrayCommandType.NextDestination:
                        foreach (var idx in stack)
                            map[idx] = (map[idx].src, map[idx].dst + 1);
                        break;
                }
            }

            return map;
        }
    }
}
