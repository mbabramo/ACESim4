// -----------------------------------------------------------------------------
//  ILChunkExecutor.cs – Reflection‑Emit back‑end for ArrayCommandChunk
// -----------------------------------------------------------------------------
//  Fully‑functional IL generator covering all ArrayCommandTypes exercised by
//  the ChunkExecutor tests.  It intentionally avoids any local‑variable
//  promotion: every read/write is done via the `vs` array.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        // ─────────────────── diagnostics ──────────────────────────
        public bool PreserveGeneratedCode { get; set; }
        public string GeneratedCode { get; private set; } = string.Empty;

        // ─────────────────── state ─────────────────────────────────
        private readonly List<ArrayCommandChunk> _queue = new();
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private StringBuilder? _traceCollector;

        public ILChunkExecutor(ArrayCommand[] cmds, int start, int end)
            : base(cmds, start, end) { }

        // ─────────────────── pipeline entrypoints ──────────────────
        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            if (!_compiled.ContainsKey(chunk))
                _queue.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_queue.Count == 0) return;
            if (PreserveGeneratedCode) _traceCollector = new StringBuilder();

            foreach (var ch in _queue)
            {
                var dm = BuildDynamicMethod(ch, out string? trace);
                _compiled[ch] = (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
                if (PreserveGeneratedCode && trace != null)
                    _traceCollector!.Append(trace);
            }
            _queue.Clear();
            if (PreserveGeneratedCode)
                GeneratedCode = _traceCollector!.ToString();
        }

        public override void Execute(ArrayCommandChunk chunk,
                                      double[] vs, double[] os, double[] od,
                                      ref int cosi, ref int codi, ref bool cond)
        {
            _compiled[chunk](vs, os, od, ref cosi, ref codi, ref cond);
            chunk.StartSourceIndices += cosi;
            chunk.StartDestinationIndices += codi;
        }

        // ─────────────────── IL generation ─────────────────────────
        private DynamicMethod BuildDynamicMethod(ArrayCommandChunk ch, out string? trace)
        {
            var dm = new DynamicMethod(
                name: $"IL_{ch.StartCommandRange}_{ch.EndCommandRangeExclusive - 1}",
                returnType: typeof(void),
                parameterTypes: new[] {
                    typeof(double[]),              // 0 : vs
                    typeof(double[]),              // 1 : os
                    typeof(double[]),              // 2 : od
                    typeof(int).MakeByRefType(),   // 3 : cosi
                    typeof(int).MakeByRefType(),   // 4 : codi
                    typeof(bool).MakeByRefType()   // 5 : cond
                },
                m: typeof(ILChunkExecutor).Module,
                skipVisibility: true);

            var il = dm.GetILGenerator();
            var sb = PreserveGeneratedCode ? new StringBuilder() : null;
            void T(string s) { if (sb != null) sb.AppendLine(s); }

            // Helper wrappers around ILGenerator.Emit.
            // Distinct names (Emit0 / EmitI / EmitL / EmitR) avoid local‑function
            // overloading, which isn’t allowed in modern C#.
            void Emit0(OpCode op) { il.Emit(op); T($"  {op}"); }
            void EmitI(OpCode op, int arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void EmitL(OpCode op, Label lbl) { il.Emit(op, lbl); T($"  {op} L{lbl.GetHashCode():x}"); }
            void EmitR(OpCode op, double val) { il.Emit(op, val); T($"  {op} {val}"); }

            // ────────── small helpers ───────────────────────────────
            void LdcI4(int v)
            {
                if (-1 <= v && v <= 8)
                    Emit0(v switch
                    {
                        -1 => OpCodes.Ldc_I4_M1,
                        0 => OpCodes.Ldc_I4_0,
                        1 => OpCodes.Ldc_I4_1,
                        2 => OpCodes.Ldc_I4_2,
                        3 => OpCodes.Ldc_I4_3,
                        4 => OpCodes.Ldc_I4_4,
                        5 => OpCodes.Ldc_I4_5,
                        6 => OpCodes.Ldc_I4_6,
                        7 => OpCodes.Ldc_I4_7,
                        8 => OpCodes.Ldc_I4_8,
                        _ => throw new InvalidOperationException()
                    });
                else
                    EmitI(OpCodes.Ldc_I4, v);
            }
            void LdcR8(double d) => EmitR(OpCodes.Ldc_R8, d);

            // ────────── pointer helpers ─────────────────────────────
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

            // ────────── vs helpers ─────────────────────────────────
            void LoadVs(int index)
            {
                Emit0(OpCodes.Ldarg_0);   // vs
                LdcI4(index);
                Emit0(OpCodes.Ldelem_R8);
            }
            void StoreVs(int index)
            {
                Emit0(OpCodes.Ldarg_0);
                LdcI4(index);
                Emit0(OpCodes.Stelem_R8);
            }
            void LoadSrc(in ArrayCommand cmd) => LoadVs(cmd.SourceIndex);
            void LoadSrcMaybeConst(in ArrayCommand cmd)
            {
                if (cmd.CommandType is ArrayCommandType.EqualsValue or ArrayCommandType.NotEqualsValue)
                    LdcR8(cmd.SourceIndex);
                else
                    LoadSrc(cmd);
            }

            // ────────── control‑flow bookkeeping ───────────────────
            var skipMap = PrecomputePointerSkips(ch);
            var ifStack = new Stack<(Label elseLbl, Label endLbl, int skipS, int skipD)>();

            // ────────── command loop ───────────────────────────────
            for (int ci = ch.StartCommandRange; ci < ch.EndCommandRangeExclusive; ci++)
            {
                ref var cmd = ref Commands[ci];
                switch (cmd.CommandType)
                {
                    // ────────── simple writes ──────────
                    case ArrayCommandType.Zero:
                        LdcR8(0.0);
                        StoreVs(cmd.Index);
                        break;
                    case ArrayCommandType.CopyTo:
                        LoadSrc(cmd);
                        StoreVs(cmd.Index);
                        break;

                    // ────────── ordered reads / writes ──────────
                    case ArrayCommandType.NextSource:
                        Emit0(OpCodes.Ldarg_1);      // os
                        EmitI(OpCodes.Ldarg, 3);     // &cosi
                        Emit0(OpCodes.Ldind_I4);
                        Emit0(OpCodes.Ldelem_R8);
                        Emit0(OpCodes.Dup);
                        StoreVs(cmd.Index);
                        IncrementRefInt(3);
                        break;
                    case ArrayCommandType.NextDestination:
                        Emit0(OpCodes.Ldarg_2);      // od
                        EmitI(OpCodes.Ldarg, 4);     // &codi
                        Emit0(OpCodes.Ldind_I4);
                        LoadSrc(cmd);
                        Emit0(OpCodes.Stelem_R8);
                        IncrementRefInt(4);
                        break;
                    case ArrayCommandType.ReusedDestination:
                        Emit0(OpCodes.Ldarg_2); LdcI4(cmd.Index); Emit0(OpCodes.Ldelem_R8);
                        LoadSrc(cmd);
                        Emit0(OpCodes.Add);
                        Emit0(OpCodes.Ldarg_2); LdcI4(cmd.Index); Emit0(OpCodes.Stelem_R8);
                        break;

                    // ────────── arithmetic ──────────
                    case ArrayCommandType.MultiplyBy:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Mul); StoreVs(cmd.Index);
                        break;
                    case ArrayCommandType.IncrementBy:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Add); StoreVs(cmd.Index);
                        break;
                    case ArrayCommandType.DecrementBy:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Sub); StoreVs(cmd.Index);
                        break;

                    // ────────── comparisons ──────────
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Ceq); StoreCond(false);
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Ceq); StoreCond(true);
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Cgt); StoreCond(false);
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        LoadVs(cmd.Index); LoadSrc(cmd); Emit0(OpCodes.Clt); StoreCond(false);
                        break;
                    case ArrayCommandType.EqualsValue:
                        LoadVs(cmd.Index); LdcR8(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(false);
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        LoadVs(cmd.Index); LdcR8(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(true);
                        break;

                    // ────────── control flow ──────────
                    case ArrayCommandType.If:
                        {
                            var elseLbl = il.DefineLabel();
                            var endLbl = il.DefineLabel();
                            var sk = skipMap.TryGetValue(ci, out var s) ? s : (0, 0);

                            // if (!cond) goto elseLbl
                            EmitI(OpCodes.Ldarg, 5); Emit0(OpCodes.Ldind_I1); EmitL(OpCodes.Brfalse, elseLbl);
                            ifStack.Push((elseLbl, endLbl, sk.Item1, sk.Item2));
                            break;
                        }
                    case ArrayCommandType.EndIf:
                        {
                            var ctx = ifStack.Pop();
                            EmitL(OpCodes.Br, ctx.endLbl);      // jump over ELSE
                            il.MarkLabel(ctx.elseLbl);
                            if (ctx.skipS != 0) AdvanceRefInt(3, ctx.skipS);
                            if (ctx.skipD != 0) AdvanceRefInt(4, ctx.skipD);
                            il.MarkLabel(ctx.endLbl);
                            break;
                        }

                    // ────────── no‑ops ──────────
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                        break;

                    default:
                        throw new NotImplementedException($"Unhandled {cmd.CommandType}");
                }
            }

            Emit0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            // ────────── nested helper (comparison result → cond) ───
            void StoreCond(bool invert)
            {
                if (invert) { Emit0(OpCodes.Ldc_I4_0); Emit0(OpCodes.Ceq); }  // logical NOT
                EmitI(OpCodes.Ldarg, 5);
                Emit0(OpCodes.Stind_I1);
            }
        }

        // ------------------------------------------------------------------
        //  Compute skipped os/od counts for IF/ELSE pointer adjustments
        // ------------------------------------------------------------------
        private Dictionary<int, (int src, int dst)> PrecomputePointerSkips(ArrayCommandChunk c)
        {
            var map = new Dictionary<int, (int src, int dst)>();
            var stack = new Stack<int>();

            for (int i = c.StartCommandRange; i < c.EndCommandRangeExclusive; i++)
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
                        foreach (int ifIdx in stack)
                            map[ifIdx] = (map[ifIdx].src + 1, map[ifIdx].dst);
                        break;

                    case ArrayCommandType.NextDestination:
                        foreach (int ifIdx in stack)
                            map[ifIdx] = (map[ifIdx].src, map[ifIdx].dst + 1);
                        break;
                }
            }
            return map;
        }
    }
}
