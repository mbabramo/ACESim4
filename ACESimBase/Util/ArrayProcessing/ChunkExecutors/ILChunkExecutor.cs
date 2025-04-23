// ILChunkExecutor.cs – local-reuse fully enabled (steps 2-b + 3)
// -----------------------------------------------------------------------------
//  • binding-aware Load/Store/MarkWritten helpers (step 1)
//  • DepthMap & IntervalIndex scaffolding (step 2-a)
//  • interval-sweep + branch-skip flushing (steps 2-b + 3)
//  Behaviour:
//     – when ReuseLocals == false  → original zero-reuse fast path
//     – when ReuseLocals == true   → depth-aware local recycling (parity with Roslyn)
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;   // LocalBindingState, LocalsAllocationPlan

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        private readonly List<ArrayCommandChunk> _queue = new();
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private StringBuilder? _trace;

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

        // ------------------------------------------------------------------
        //  Public API
        // ------------------------------------------------------------------
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
                    _compiled[chunk] = (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
                    if (PreserveGeneratedCode) _trace!.Append(src);
                }
                _queue.Clear();
                if (PreserveGeneratedCode) GeneratedCode = _trace!.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error generating IL for chunk\n{_trace}", ex);
            }
        }

        public override void Execute(ArrayCommandChunk chunk,
                                     double[] vs, double[] os, double[] od,
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

        // ==================================================================
        //  IL generation – zero-reuse + reuse
        // ==================================================================
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
            void T(string s) => sb?.AppendLine(s);

            // ─── wrappers ───
            void E0(OpCode op) { il.Emit(op); T($"  {op}"); }
            void EI(OpCode op, int arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void ER(OpCode op, double arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void EL(OpCode op, Label lbl) { il.Emit(op, lbl); T($"  {op} L{lbl.GetHashCode():x}"); }
            void ELb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); T($"  {op} V_{lb.LocalIndex}"); }

            // temps
            var tmp = il.DeclareLocal(typeof(double));
            var tmpI = il.DeclareLocal(typeof(int));

            // ─── locals array ───
            LocalBuilder[]? locals = null;
            if (_plan.LocalCount > 0)
            {
                locals = new LocalBuilder[_plan.LocalCount];
                for (int l = 0; l < _plan.LocalCount; l++)
                    locals[l] = il.DeclareLocal(typeof(double));
            }

            bool zeroReuse = !ReuseLocals && locals != null;
            LocalBindingState? bind = ReuseLocals ? new LocalBindingState(_plan.LocalCount) : null;

            // ─── integer constants ───
            void LdcI4(int v)
            {
                switch (v)
                {
                    case -1: E0(OpCodes.Ldc_I4_M1); return;
                    case 0: E0(OpCodes.Ldc_I4_0); return;
                    case 1: E0(OpCodes.Ldc_I4_1); return;
                    case 2: E0(OpCodes.Ldc_I4_2); return;
                    case 3: E0(OpCodes.Ldc_I4_3); return;
                    case 4: E0(OpCodes.Ldc_I4_4); return;
                    case 5: E0(OpCodes.Ldc_I4_5); return;
                    case 6: E0(OpCodes.Ldc_I4_6); return;
                    case 7: E0(OpCodes.Ldc_I4_7); return;
                    case 8: E0(OpCodes.Ldc_I4_8); return;
                }
                if (v >= sbyte.MinValue && v <= sbyte.MaxValue)
                    EI(OpCodes.Ldc_I4_S, (sbyte)v);
                else
                    EI(OpCodes.Ldc_I4, v);
            }
            void LdcR8(double d) => ER(OpCodes.Ldc_R8, d);

            // ─── ref-int helpers ───
            void IncrementRefInt(int idx)
            {
                EI(OpCodes.Ldarg, idx);
                E0(OpCodes.Dup);
                E0(OpCodes.Ldind_I4);
                E0(OpCodes.Ldc_I4_1);
                E0(OpCodes.Add);
                E0(OpCodes.Stind_I4);
            }
            void AdvanceRefInt(int idx, int delta)
            {
                if (delta == 0) return;
                EI(OpCodes.Ldarg, idx);
                E0(OpCodes.Dup);
                E0(OpCodes.Ldind_I4);
                LdcI4(delta);
                E0(OpCodes.Add);
                E0(OpCodes.Stind_I4);
            }

            // ─── vs[] direct read ───
            void LoadVs(int slot)
            {
                E0(OpCodes.Ldarg_0);
                LdcI4(slot);
                E0(OpCodes.Ldelem_R8);
            }

            // ─── Load / Store / MarkWritten ───
            void MarkWritten(int slot)
            {
                if (bind != null && _plan.SlotToLocal.TryGetValue(slot, out int l))
                    bind.MarkWritten(l);
            }

            void Load(int slot)
            {
                if (bind != null &&
                    _plan.SlotToLocal.TryGetValue(slot, out int lb) &&
                    bind.IsBound(lb, slot))
                {
                    ELb(OpCodes.Ldloc, locals![lb]);
                    return;
                }
                if (zeroReuse && _plan.SlotToLocal.TryGetValue(slot, out int lz))
                {
                    ELb(OpCodes.Ldloc, locals![lz]);
                    return;
                }
                LoadVs(slot);
            }

            void Store(int slot)
            {
                ELb(OpCodes.Stloc, tmp);   // pop → tmp

                if (bind != null &&
                    _plan.SlotToLocal.TryGetValue(slot, out int lb) &&
                    bind.IsBound(lb, slot))
                {
                    ELb(OpCodes.Ldloc, tmp);
                    ELb(OpCodes.Stloc, locals![lb]);
                }
                else if (zeroReuse && _plan.SlotToLocal.TryGetValue(slot, out int lz))
                {
                    ELb(OpCodes.Ldloc, tmp);
                    ELb(OpCodes.Stloc, locals![lz]);
                }
                else
                {
                    E0(OpCodes.Ldarg_0);
                    LdcI4(slot);
                    ELb(OpCodes.Ldloc, tmp);
                    E0(OpCodes.Stelem_R8);
                }
                MarkWritten(slot);
            }

            // ─── preload dedicated locals (zero-reuse) ───
            if (zeroReuse)
            {
                foreach (var kv in _plan.SlotToLocal)
                {
                    E0(OpCodes.Ldarg_0);
                    LdcI4(kv.Key);
                    E0(OpCodes.Ldelem_R8);
                    ELb(OpCodes.Stloc, locals![kv.Value]);
                }
            }

            // ─── helpers for reuse mode ───
            DepthMap? depthMap = null;
            IntervalIndex? intervalIx = null;
            if (ReuseLocals)
            {
                depthMap = new DepthMap(Commands.ToArray(), chunk.StartCommandRange, chunk.EndCommandRangeExclusive);
                intervalIx = new IntervalIndex(_plan);
            }

            var skipMap = PrecomputePointerSkips(chunk);
            var ifStack = new Stack<IfContext>();

            // ------------------------------------------------------------------
            //  Emit commands
            // ------------------------------------------------------------------
            for (int ci = chunk.StartCommandRange; ci < chunk.EndCommandRangeExclusive; ci++)
            {
                // ───── interval starts (reuse mode) ─────
                if (ReuseLocals)
                {
                    int d = depthMap!.GetDepth(ci);
                    foreach (int slot in intervalIx!.StartSlots(ci))
                    {
                        int local = _plan.SlotToLocal[slot];
                        if (bind!.TryReuse(local, slot, d, out int flushSlot))
                        {
                            if (flushSlot != -1)
                            {
                                E0(OpCodes.Ldarg_0);
                                LdcI4(flushSlot);
                                ELb(OpCodes.Ldloc, locals![local]);
                                E0(OpCodes.Stelem_R8);
                            }
                            E0(OpCodes.Ldarg_0);
                            LdcI4(slot);
                            E0(OpCodes.Ldelem_R8);
                            ELb(OpCodes.Stloc, locals![local]);
                            bind.StartInterval(slot, local, d);
                        }
                    }
                }

                var cmd = Commands[ci];

                // ───── emit main op ─────
                switch (cmd.CommandType)
                {
                    // ── writes / R-M-W ──
                    case ArrayCommandType.Zero:
                        LdcR8(0.0); Store(cmd.Index); break;

                    case ArrayCommandType.CopyTo:
                        Load(cmd.SourceIndex); Store(cmd.Index); break;

                    case ArrayCommandType.NextSource:
                        E0(OpCodes.Ldarg_1);  // os
                        EI(OpCodes.Ldarg, 3); E0(OpCodes.Ldind_I4); E0(OpCodes.Ldelem_R8);
                        Store(cmd.Index);
                        IncrementRefInt(3);
                        break;

                    case ArrayCommandType.MultiplyBy:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Mul); Store(cmd.Index); break;
                    case ArrayCommandType.IncrementBy:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Add); Store(cmd.Index); break;
                    case ArrayCommandType.DecrementBy:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Sub); Store(cmd.Index); break;

                    // ── destination writes ──
                    case ArrayCommandType.NextDestination:
                        E0(OpCodes.Ldarg_2);
                        EI(OpCodes.Ldarg, 4); E0(OpCodes.Ldind_I4);
                        Load(cmd.SourceIndex);
                        E0(OpCodes.Stelem_R8);
                        IncrementRefInt(4);
                        break;

                    case ArrayCommandType.ReusedDestination:
                        E0(OpCodes.Ldarg_2); LdcI4(cmd.Index); E0(OpCodes.Ldelem_R8);
                        Load(cmd.SourceIndex); E0(OpCodes.Add); ELb(OpCodes.Stloc, tmp);
                        E0(OpCodes.Ldarg_2); LdcI4(cmd.Index); ELb(OpCodes.Ldloc, tmp); E0(OpCodes.Stelem_R8);
                        break;

                    // ── comparisons ──
                    case ArrayCommandType.EqualsOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Ceq); StoreCond(false); break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Ceq); StoreCond(true); break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Cgt); StoreCond(false); break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        Load(cmd.Index); Load(cmd.SourceIndex); E0(OpCodes.Clt); StoreCond(false); break;
                    case ArrayCommandType.EqualsValue:
                        Load(cmd.Index); LdcR8(cmd.SourceIndex); E0(OpCodes.Ceq); StoreCond(false); break;
                    case ArrayCommandType.NotEqualsValue:
                        Load(cmd.Index); LdcR8(cmd.SourceIndex); E0(OpCodes.Ceq); StoreCond(true); break;

                    // ── control-flow ──
                    case ArrayCommandType.If:
                        {
                            var elseLbl = il.DefineLabel();
                            var endLbl = il.DefineLabel();
                            EI(OpCodes.Ldarg, 5); E0(OpCodes.Ldind_I1); EL(OpCodes.Brfalse_S, elseLbl);

                            var sk = skipMap[ci];
                            bool[]? dirty = null;
                            List<(int slot, int local)>? flushes = null;
                            if (ReuseLocals)
                            {
                                dirty = new bool[_plan.LocalCount];
                                flushes = new List<(int, int)>();
                                for (int l = 0; l < _plan.LocalCount; l++)
                                    dirty[l] = bind!.NeedsFlushBeforeReuse(l, out _);
                            }
                            ifStack.Push(new IfContext(elseLbl, endLbl, sk.srcSkip, sk.dstSkip, dirty, flushes));
                            break;
                        }

                    case ArrayCommandType.EndIf:
                        {
                            var ctx = ifStack.Pop();
                            EL(OpCodes.Br_S, ctx.EndLabel);

                            il.MarkLabel(ctx.ElseLabel);
                            if (ctx.Flushes is not null)
                            {
                                foreach (var (slot, local) in ctx.Flushes)
                                {
                                    E0(OpCodes.Ldarg_0); LdcI4(slot);
                                    ELb(OpCodes.Ldloc, locals![local]); E0(OpCodes.Stelem_R8);
                                }
                            }
                            if (ctx.SrcSkip > 0) AdvanceRefInt(3, ctx.SrcSkip);
                            if (ctx.DstSkip > 0) AdvanceRefInt(4, ctx.DstSkip);

                            il.MarkLabel(ctx.EndLabel);
                            break;
                        }

                    // ── no-ops ──
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                        break;

                    default:
                        throw new NotImplementedException(cmd.CommandType.ToString());
                }

                // ───── interval ends (reuse mode) ─────
                if (ReuseLocals)
                {
                    foreach (int slot in intervalIx!.EndSlots(ci))
                    {
                        int local = _plan.SlotToLocal[slot];
                        if (bind!.NeedsFlushBeforeReuse(local, out int bound) && bound == slot)
                        {
                            E0(OpCodes.Ldarg_0); LdcI4(slot); ELb(OpCodes.Ldloc, locals![local]); E0(OpCodes.Stelem_R8);
                            foreach (var ctx in ifStack)
                                if (ctx.DirtyBefore?[local] == true)
                                    ctx.Flushes!.Add((slot, local));
                            bind.FlushLocal(local);
                        }
                        bind.Release(local);
                    }
                }
            }

            // ─── tail flush for zero-reuse ───
            if (zeroReuse)
            {
                foreach (var kv in _plan.SlotToLocal)
                {
                    E0(OpCodes.Ldarg_0); LdcI4(kv.Key); ELb(OpCodes.Ldloc, locals![kv.Value]); E0(OpCodes.Stelem_R8);
                }
            }

            E0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            // helper – write bool TOS into *cond (invert option)
            void StoreCond(bool invert)
            {
                if (invert) { E0(OpCodes.Ldc_I4_0); E0(OpCodes.Ceq); }
                ELb(OpCodes.Stloc, tmpI);
                EI(OpCodes.Ldarg, 5); ELb(OpCodes.Ldloc, tmpI); E0(OpCodes.Stind_I1);
            }
        }

        // =========================================================================
        //  IfContext
        // =========================================================================
        private sealed class IfContext
        {
            public readonly Label ElseLabel;
            public readonly Label EndLabel;
            public readonly int SrcSkip;
            public readonly int DstSkip;
            public readonly bool[]? DirtyBefore;
            public readonly List<(int slot, int local)>? Flushes;

            public IfContext(Label elseLbl, Label endLbl, int src, int dst, bool[]? dirty, List<(int, int)>? fl)
            {
                ElseLabel = elseLbl;
                EndLabel = endLbl;
                SrcSkip = src;
                DstSkip = dst;
                DirtyBefore = dirty;
                Flushes = fl;
            }
        }

        // =========================================================================
        //  Utilities identical to RoslynChunkExecutor
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
                        foreach (int idx in stack)
                            map[idx] = (map[idx].srcSkip + 1, map[idx].dstSkip);
                        break;
                    case ArrayCommandType.NextDestination:
                        foreach (int idx in stack)
                            map[idx] = (map[idx].srcSkip, map[idx].dstSkip + 1);
                        break;
                }
            }
            return map;
        }

        // =========================================================================
        //  DepthMap & IntervalIndex (minimal versions)
        // =========================================================================
        private sealed class DepthMap
        {
            private readonly int[] _depth;
            public DepthMap(ArrayCommand[] cmds, int start, int end)
            {
                _depth = new int[cmds.Length];
                int d = 0;
                for (int i = start; i < end; i++)
                {
                    if (cmds[i].CommandType == ArrayCommandType.EndIf) d--;
                    _depth[i] = d;
                    if (cmds[i].CommandType == ArrayCommandType.If) d++;
                }
            }
            public int GetDepth(int i) => _depth[i];
        }

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

            public IEnumerable<int> StartSlots(int idx) =>
                _starts.TryGetValue(idx, out var l) ? l : Enumerable.Empty<int>();

            public IEnumerable<int> EndSlots(int idx) =>
                _ends.TryGetValue(idx, out var l) ? l : Enumerable.Empty<int>();
        }
    }
}
