// ILChunkExecutor.cs – step 4 → step 5a
// -----------------------------------------------------------------------------
//  * Step-1:   refactor Load/Store helpers to understand LocalBindingState
//  * Step-2a:  add DepthMap & IntervalIndex scaffolding (not yet used at runtime)
//  Behaviour is still identical to the original zero-reuse IL path: the new
//  helpers remain dormant until we flip ReuseLocals == true in a later step.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;   // LocalsAllocationPlan, LocalBindingState

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        private readonly List<ArrayCommandChunk> _queue = new();
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private StringBuilder? _trace;

        // ──────────────────────────────────────────────────────────────
        //  Locals-reuse scaffolding
        // ──────────────────────────────────────────────────────────────
        public bool ReuseLocals { get; init; } = false;
        private readonly LocalsAllocationPlan _plan;

        public ILChunkExecutor(ArrayCommand[] cmds, int start, int end, bool localVariableReuse = false)
            : base(cmds, start, end, /*useCheckpoints*/ false)
        {
            ReuseLocals = localVariableReuse;
            _plan = ReuseLocals
                ? LocalVariablePlanner.PlanLocals(cmds, start, end)
                : LocalVariablePlanner.PlanNoReuse(cmds, start, end);
        }

        // ----------------------------------------------------------------------
        //  Chunk-generation public API
        // ----------------------------------------------------------------------
        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            if (!_compiled.ContainsKey(chunk))
                _queue.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_queue.Count == 0) return;

            if (PreserveGeneratedCode)
                _trace = new StringBuilder();

            try
            {
                foreach (var chunk in _queue)
                {
                    var dm = BuildDynamicMethod(chunk, out var src);
                    _compiled[chunk] =
                        (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
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

        public override void Execute(ArrayCommandChunk chunk,
                                     double[] vs,
                                     double[] os,
                                     double[] od,
                                     ref int cosi,
                                     ref int codi,
                                     ref bool cond)
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

        // ======================================================================
        //  IL generation – zero-reuse path (ReuseLocals==false) – unchanged
        //  Helpers are now future-proof for the reuse path but remain inactive.
        // ======================================================================
        private DynamicMethod BuildDynamicMethod(ArrayCommandChunk chunk, out string? trace)
        {
            var dm = new DynamicMethod(
                name: $"IL_{chunk.StartCommandRange}_{chunk.EndCommandRangeExclusive - 1}",
                returnType: typeof(void),
                parameterTypes: new[]
                {
                    typeof(double[]),              // 0 vs
                    typeof(double[]),              // 1 os
                    typeof(double[]),              // 2 od
                    typeof(int).MakeByRefType(),   // 3 cosi
                    typeof(int).MakeByRefType(),   // 4 codi
                    typeof(bool).MakeByRefType()   // 5 cond
                },
                m: typeof(ILChunkExecutor).Module,
                skipVisibility: true);

            var il = dm.GetILGenerator();
            var sb = PreserveGeneratedCode ? new StringBuilder() : null;
            void TraceIL(string s) => sb?.AppendLine(s);

            // ── readability wrappers
            void Emit0(OpCode op) { il.Emit(op); TraceIL($"  {op}"); }
            void EmitI(OpCode op, int arg) { il.Emit(op, arg); TraceIL($"  {op} {arg}"); }
            void EmitR(OpCode op, double arg) { il.Emit(op, arg); TraceIL($"  {op} {arg}"); }
            void EmitL(OpCode op, Label lbl) { il.Emit(op, lbl); TraceIL($"  {op} L{lbl.GetHashCode():x}"); }
            void EmitLb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); TraceIL($"  {op} V_{lb.LocalIndex}"); }

            // legacy temps
            var tmp = il.DeclareLocal(typeof(double));
            var tmpI = il.DeclareLocal(typeof(int));

            // ────────────────────────────────────────────────────────────────
            //  LocalBuilder array from the plan
            // ────────────────────────────────────────────────────────────────
            LocalBuilder[]? locals = null;
            if (_plan.LocalCount > 0)
            {
                locals = new LocalBuilder[_plan.LocalCount];
                for (int l = 0; l < _plan.LocalCount; l++)
                    locals[l] = il.DeclareLocal(typeof(double));
            }

            bool useLocals = !ReuseLocals && locals != null;    // ← zero-reuse only

            // placeholder binding table – will be used when ReuseLocals==true
            LocalBindingState? bind = ReuseLocals ? new LocalBindingState(_plan.LocalCount) : null;

            // ------------------------------------------------------------------
            //  Centralised helpers
            // ------------------------------------------------------------------
            void Load(int slot)
            {
                // (1) live bound local (inactive for now)
                if (bind != null &&
                    _plan.SlotToLocal.TryGetValue(slot, out int l1) &&
                    bind.IsBound(l1, slot))
                {
                    EmitLb(OpCodes.Ldloc, locals![l1]);
                    return;
                }

                // (2) zero-reuse dedicated local
                if (!ReuseLocals && _plan.SlotToLocal.TryGetValue(slot, out int l2))
                {
                    EmitLb(OpCodes.Ldloc, locals![l2]);
                    return;
                }

                // (3) fallback: vs[slot]
                LoadVs(slot);
            }

            void Store(int slot)
            {
                EmitLb(OpCodes.Stloc, tmp); // pop → tmp

                if (bind != null &&
                    _plan.SlotToLocal.TryGetValue(slot, out int l1) &&
                    bind.IsBound(l1, slot))
                {
                    EmitLb(OpCodes.Ldloc, tmp);
                    EmitLb(OpCodes.Stloc, locals![l1]);
                }
                else if (!ReuseLocals && _plan.SlotToLocal.TryGetValue(slot, out int l2))
                {
                    EmitLb(OpCodes.Ldloc, tmp);
                    EmitLb(OpCodes.Stloc, locals![l2]);
                }
                else
                {
                    Emit0(OpCodes.Ldarg_0);       // vs
                    LdcI4(slot);
                    EmitLb(OpCodes.Ldloc, tmp);
                    Emit0(OpCodes.Stelem_R8);
                }

                MarkWritten(slot);
            }

            void MarkWritten(int slot)
            {
                if (bind != null && _plan.SlotToLocal.TryGetValue(slot, out int l))
                    bind.MarkWritten(l);
            }

            // ------------------------------------------------------------------
            //  Array helpers
            // ------------------------------------------------------------------
            void LdcI4(int v)
            {
                switch (v)
                {
                    case -1: Emit0(OpCodes.Ldc_I4_M1); return;
                    case 0: Emit0(OpCodes.Ldc_I4_0); return;
                    case 1: Emit0(OpCodes.Ldc_I4_1); return;
                    case 2: Emit0(OpCodes.Ldc_I4_2); return;
                    case 3: Emit0(OpCodes.Ldc_I4_3); return;
                    case 4: Emit0(OpCodes.Ldc_I4_4); return;
                    case 5: Emit0(OpCodes.Ldc_I4_5); return;
                    case 6: Emit0(OpCodes.Ldc_I4_6); return;
                    case 7: Emit0(OpCodes.Ldc_I4_7); return;
                    case 8: Emit0(OpCodes.Ldc_I4_8); return;
                }
                if (v >= sbyte.MinValue && v <= sbyte.MaxValue)
                    EmitI(OpCodes.Ldc_I4_S, (sbyte)v);
                else
                    EmitI(OpCodes.Ldc_I4, v);
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

            // ------------------------------------------------------------------
            //  Pre-load dedicated locals (zero-reuse only)
            // ------------------------------------------------------------------
            if (useLocals)
            {
                foreach (var kv in _plan.SlotToLocal)
                {
                    Emit0(OpCodes.Ldarg_0);
                    LdcI4(kv.Key);
                    Emit0(OpCodes.Ldelem_R8);
                    EmitLb(OpCodes.Stloc, locals![kv.Value]);
                }
            }

            // ------------------------------------------------------------------
            //  Main chunk body
            // ------------------------------------------------------------------
            var skipMap = PrecomputePointerSkips(chunk);
            var ifStack = new Stack<(Label elseLbl, Label endLbl, int srcSkip, int dstSkip)>();

            for (int ci = chunk.StartCommandRange; ci < chunk.EndCommandRangeExclusive; ci++)
            {
                var cmd = Commands[ci];
                switch (cmd.CommandType)
                {
                    // ─────── writes / R-M-W ───────
                    case ArrayCommandType.Zero:
                        LdcR8(0.0); Store(cmd.Index); break;

                    case ArrayCommandType.CopyTo:
                        Load(cmd.SourceIndex); Store(cmd.Index); break;

                    case ArrayCommandType.NextSource:
                        Emit0(OpCodes.Ldarg_1);        // os
                        EmitI(OpCodes.Ldarg, 3);       // &cosi
                        Emit0(OpCodes.Ldind_I4);
                        Emit0(OpCodes.Ldelem_R8);
                        Store(cmd.Index);
                        IncrementRefInt(3);
                        break;

                    case ArrayCommandType.MultiplyBy:
                        Load(cmd.Index);
                        Load(cmd.SourceIndex);
                        Emit0(OpCodes.Mul);
                        Store(cmd.Index);
                        break;

                    case ArrayCommandType.IncrementBy:
                        Load(cmd.Index);
                        Load(cmd.SourceIndex);
                        Emit0(OpCodes.Add);
                        Store(cmd.Index);
                        break;

                    case ArrayCommandType.DecrementBy:
                        Load(cmd.Index);
                        Load(cmd.SourceIndex);
                        Emit0(OpCodes.Sub);
                        Store(cmd.Index);
                        break;

                    // ─────── destination writes ───────
                    case ArrayCommandType.NextDestination:
                        Emit0(OpCodes.Ldarg_2);        // od
                        EmitI(OpCodes.Ldarg, 4);       // &codi
                        Emit0(OpCodes.Ldind_I4);
                        Load(cmd.SourceIndex);
                        Emit0(OpCodes.Stelem_R8);
                        IncrementRefInt(4);
                        break;

                    case ArrayCommandType.ReusedDestination:
                        Emit0(OpCodes.Ldarg_2);
                        LdcI4(cmd.Index);
                        Emit0(OpCodes.Ldelem_R8);
                        Load(cmd.SourceIndex);
                        Emit0(OpCodes.Add);
                        EmitLb(OpCodes.Stloc, tmp);
                        Emit0(OpCodes.Ldarg_2);
                        LdcI4(cmd.Index);
                        EmitLb(OpCodes.Ldloc, tmp);
                        Emit0(OpCodes.Stelem_R8);
                        break;

                    // ─────── comparisons ───────
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(false); break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(true); break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); Emit0(OpCodes.Cgt); StoreCond(false); break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); Emit0(OpCodes.Clt); StoreCond(false); break;
                    case ArrayCommandType.EqualsValue:
                        Load(cmd.Index); LdcR8(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(false); break;
                    case ArrayCommandType.NotEqualsValue:
                        Load(cmd.Index); LdcR8(cmd.SourceIndex); Emit0(OpCodes.Ceq); StoreCond(true); break;

                    // ─────── control-flow ───────
                    case ArrayCommandType.If:
                        {
                            var elseLbl = il.DefineLabel();
                            var endLbl = il.DefineLabel();

                            EmitI(OpCodes.Ldarg, 5);
                            Emit0(OpCodes.Ldind_I1);
                            EmitL(OpCodes.Brfalse_S, elseLbl);

                            var sk = skipMap[ci];
                            ifStack.Push((elseLbl, endLbl, sk.srcSkip, sk.dstSkip));
                            break;
                        }

                    case ArrayCommandType.EndIf:
                        {
                            var ctx = ifStack.Pop();
                            EmitL(OpCodes.Br_S, ctx.endLbl);

                            il.MarkLabel(ctx.elseLbl);
                            if (ctx.srcSkip > 0) AdvanceRefInt(3, ctx.srcSkip);
                            if (ctx.dstSkip > 0) AdvanceRefInt(4, ctx.dstSkip);

                            il.MarkLabel(ctx.endLbl);
                            break;
                        }

                    // ─────── no-ops ───────
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                        break;

                    default:
                        throw new NotImplementedException(cmd.CommandType.ToString());
                }
            }

            // ------------------------------------------------------------------
            //  Tail-flush dedicated locals (zero-reuse only)
            // ------------------------------------------------------------------
            if (useLocals)
            {
                foreach (var kv in _plan.SlotToLocal)
                {
                    Emit0(OpCodes.Ldarg_0);          // vs
                    LdcI4(kv.Key);
                    EmitLb(OpCodes.Ldloc, locals![kv.Value]);
                    Emit0(OpCodes.Stelem_R8);
                }
            }

            Emit0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            // helper – write TOS bool into *cond (possible inversion)
            void StoreCond(bool invert)
            {
                if (invert)
                {
                    Emit0(OpCodes.Ldc_I4_0);
                    Emit0(OpCodes.Ceq);
                }

                EmitLb(OpCodes.Stloc, tmpI);
                EmitI(OpCodes.Ldarg, 5);
                EmitLb(OpCodes.Ldloc, tmpI);
                Emit0(OpCodes.Stind_I1);
            }
        }

        // =========================================================================
        //  Utilities identical in spirit to RoslynChunkExecutor
        // =========================================================================
        private Dictionary<int, (int srcSkip, int dstSkip)>
            PrecomputePointerSkips(ArrayCommandChunk chunk)
        {
            var map = new Dictionary<int, (int srcSkip, int dstSkip)>();
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
                            map[idx] = (map[idx].srcSkip + 1, map[idx].dstSkip);
                        break;

                    case ArrayCommandType.NextDestination:
                        foreach (var idx in stack)
                            map[idx] = (map[idx].srcSkip, map[idx].dstSkip + 1);
                        break;
                }
            }
            return map;
        }

        // ======================================================================
        //  Step 2-a scaffolding – minimal versions of Roslyn helpers
        // ======================================================================

        /// <summary>Lightweight depth map (syntactic If/EndIf nesting depth).</summary>
        private sealed class DepthMap
        {
            private readonly int[] _depth;
            public DepthMap(ArrayCommand[] cmds, int start, int end)
            {
                _depth = new int[cmds.Length];
                int d = 0;
                for (int i = start; i < end; i++)
                {
                    if (cmds[i].CommandType == ArrayCommandType.EndIf)
                        d--;
                    _depth[i] = d;
                    if (cmds[i].CommandType == ArrayCommandType.If)
                        d++;
                }
            }
            public int GetDepth(int i) => _depth[i];
        }

        /// <summary>
        /// Index of interval start/end slots for fast lookup during generation.
        /// Only used once we enable local-reuse.
        /// </summary>
        private sealed class IntervalIndex
        {
            private readonly Dictionary<int, List<int>> _starts = new();
            private readonly Dictionary<int, List<int>> _ends = new();

            public IntervalIndex(LocalsAllocationPlan plan)
            {
                foreach (var iv in plan.Intervals)
                {
                    if (!_starts.ContainsKey(iv.First)) _starts[iv.First] = new();
                    if (!_ends.ContainsKey(iv.Last)) _ends[iv.Last] = new();
                    _starts[iv.First].Add(iv.Slot);
                    _ends[iv.Last].Add(iv.Slot);
                }
            }

            public IEnumerable<int> StartSlots(int cmdIdx) =>
                _starts.TryGetValue(cmdIdx, out var list) ? list : Enumerable.Empty<int>();

            public IEnumerable<int> EndSlots(int cmdIdx) =>
                _ends.TryGetValue(cmdIdx, out var list) ? list : Enumerable.Empty<int>();
        }
    }
}
