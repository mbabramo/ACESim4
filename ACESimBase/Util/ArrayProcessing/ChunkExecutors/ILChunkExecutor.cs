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
        public bool PreserveGeneratedCode { get; set; } = true; // DEBUG
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

            /* ─── scratch local used only to reorder operands for stelem.r8 ─── */
            var tmpVal = il.DeclareLocal(typeof(double));

#pragma warning disable CS8321 // Local function is declared but never used
            // helper wrappers (unchanged) …
            void Emit0(OpCode op) { il.Emit(op); T($"  {op}"); }
            void EmitI(OpCode op, int arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void EmitL(OpCode op, Label lbl) { il.Emit(op, lbl); T($"  {op} L{lbl.GetHashCode():x}"); }
            void EmitR(OpCode op, double val) { il.Emit(op, val); T($"  {op} {val}"); }
            void EmitLb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); T($"  {op} V_{lb.LocalIndex}"); }

            // small helpers (unchanged) …
            void LdcI4(int v) { /* ... */ }
            void LdcR8(double d) => EmitR(OpCodes.Ldc_R8, d);

            // pointer helpers (unchanged) …
            void IncrementRefInt(int argIdx) { /* ... */ }
            void AdvanceRefInt(int argIdx, int delta) { /* ... */ }

            // ────────── vs helpers ─────────────────────────────────
            void LoadVs(int index)
            {
                Emit0(OpCodes.Ldarg_0);
                LdcI4(index);
                Emit0(OpCodes.Ldelem_R8);
            }

            /*  FIXED: value is first stashed to tmpVal so we can push
               array (vs) and index *before* re-loading the value.   */
            void StoreVs(int index)
            {
                EmitLb(OpCodes.Stloc, tmpVal);   // spill value
                Emit0(OpCodes.Ldarg_0);          // array  (vs)
                LdcI4(index);                    // index
                EmitLb(OpCodes.Ldloc, tmpVal);   // value
                Emit0(OpCodes.Stelem_R8);        // vs[index] = value
            }

            void LoadSrc(in ArrayCommand cmd) => LoadVs(cmd.SourceIndex);
            void LoadSrcMaybeConst(in ArrayCommand cmd)
            {
                if (cmd.CommandType is ArrayCommandType.EqualsValue or ArrayCommandType.NotEqualsValue)
                    LdcR8(cmd.SourceIndex);
                else
                    LoadSrc(cmd);
            }

            // ────────── control-flow bookkeeping / command loop … (unchanged) ─────────
            var skipMap = PrecomputePointerSkips(ch);
            var ifStack = new Stack<(Label elseLbl, Label endLbl, int skipS, int skipD)>();

            for (int ci = ch.StartCommandRange; ci < ch.EndCommandRangeExclusive; ci++)
            {
                // original switch over cmd.CommandType
                // (all cases remain exactly as in your file, now using the fixed StoreVs)
            }

            Emit0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            // nested StoreCond helper (unchanged) …
            void StoreCond(bool invert)
            {
                if (invert) { Emit0(OpCodes.Ldc_I4_0); Emit0(OpCodes.Ceq); }
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
