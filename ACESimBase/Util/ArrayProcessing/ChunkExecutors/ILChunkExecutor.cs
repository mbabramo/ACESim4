using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// Emits JIT methods that execute ArrayCommandChunk spans.
    /// Compiles once per unique [Start, End) span, supports depth‑aware local reuse,
    /// and can batch many spans into a single DynamicMethod with a switch dispatcher.
    /// </summary>
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        // ──────────────────────────────────────────────────────────────
        //  Configuration
        // ──────────────────────────────────────────────────────────────
        private const int MaxBatchSize = 32;

        // ──────────────────────────────────────────────────────────────
        //  State
        // ──────────────────────────────────────────────────────────────

        // Compiled delegate per unique span
        private readonly Dictionary<(int start, int end), ArrayCommandChunkDelegate> _compiledBySpan = new();

        // Compiled delegate per chunk object (direct map for Execute)
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiledByChunk = new();

        // Representative chunk per not‑yet‑compiled span
        private readonly Dictionary<(int start, int end), ArrayCommandChunk> _repBySpan = new();

        // Chunks queued during AddToGeneration (used to bind _compiledByChunk after generation)
        private readonly List<ArrayCommandChunk> _queuedChunks = new();

        // Optional source trace (for debugging)
        private StringBuilder _trace;

        /// <summary>
        /// When true, emit depth‑aware local reuse using a locals allocation plan.
        /// When false, operate directly on VS (no bulk preload/writeback).
        /// </summary>
        public bool ReuseLocals { get; init; }

        // Global analysis across the executor window
        private readonly LocalsAllocationPlan _globalPlan;
        private readonly DepthMap _globalDepth;

        // Sub‑plans per span to avoid recomputation
        private readonly Dictionary<(int start, int end), LocalsAllocationPlan> _subPlanCache = new();

        // Batched invoker signature (extra leafId)
        private delegate void BatchedInvoker(double[] vs, double[] os, double[] od,
                                             ref int cosi, ref int codi, ref bool cond,
                                             int leafId);

        public ILChunkExecutor(ArrayCommand[] cmds,
                               int start, int end,
                               bool localVariableReuse = false)
            : base(cmds, start, end, useCheckpoints: false, arrayCommandListForCheckpoints: null)
        {
            ReuseLocals = localVariableReuse;

            _globalPlan = ReuseLocals
                ? LocalVariablePlanner.PlanLocals(cmds, start, end)
                : LocalVariablePlanner.PlanNoReuse(cmds, start, end); // maintained for symmetry

            _globalDepth = new DepthMap(cmds, start, end);
        }

        // ──────────────────────────────────────────────────────────────
        //  IChunkExecutor
        // ──────────────────────────────────────────────────────────────

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            var key = (chunk.StartCommandRange, chunk.EndCommandRangeExclusive);

            if (_compiledBySpan.TryGetValue(key, out var existing))
            {
                _compiledByChunk[chunk] = existing;
                return;
            }

            if (!_repBySpan.ContainsKey(key))
                _repBySpan[key] = chunk;

            _queuedChunks.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_repBySpan.Count == 0 && _queuedChunks.Count == 0)
                return;

            if (PreserveGeneratedCode)
                _trace = new StringBuilder();

            // Batch compilation
            var reps = new List<ArrayCommandChunk>(_repBySpan.Values);
            int total = reps.Count;
            int cursor = 0;

            while (cursor < total)
            {
                int count = Math.Min(MaxBatchSize, total - cursor);
                var batch = reps.GetRange(cursor, count);

                // Build a single DynamicMethod with a switch over leafId
                var dm = BuildBatchDynamicMethod(batch, out var srcDump);
                var invoker = (BatchedInvoker)dm.CreateDelegate(typeof(BatchedInvoker));

                // For each span in the batch, create a tiny stub that passes a constant leafId
                for (int i = 0; i < batch.Count; i++)
                {
                    int leafId = i; // capture stable id for this span
                    ArrayCommandChunk rep = batch[i];

                    ArrayCommandChunkDelegate stub = (double[] vs, double[] os, double[] od,
                                                      ref int cosi, ref int codi, ref bool cond) =>
                    {
                        invoker(vs, os, od, ref cosi, ref codi, ref cond, leafId);
                    };

                    _compiledBySpan[(rep.StartCommandRange, rep.EndCommandRangeExclusive)] = stub;
                }

                if (PreserveGeneratedCode && _trace != null)
                    _trace.Append(srcDump);

                cursor += count;
            }

            // Bind per‑chunk map to avoid span key creation in Execute
            foreach (var ch in _queuedChunks)
            {
                var key = (ch.StartCommandRange, ch.EndCommandRangeExclusive);
                if (_compiledBySpan.TryGetValue(key, out var del))
                    _compiledByChunk[ch] = del;
            }

            _repBySpan.Clear();
            _queuedChunks.Clear();

            try
            {
                if (PreserveGeneratedCode && _trace != null)
                    GeneratedCode = _trace.ToString();
            }
            catch (OutOfMemoryException)
            {
                GeneratedCode = "Insufficient memory to keep generated code.";
                throw;
            }
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
            if (chunk.EndCommandRangeExclusive <= chunk.StartCommandRange)
                return;

            if (!_compiledByChunk.TryGetValue(chunk, out var del))
                throw new InvalidOperationException("Attempted to execute an unprepared chunk. Ensure generation has been performed.");

            try
            {
                del(virtualStack, orderedSources, orderedDestinations, ref cosi, ref codi, ref condition);
            }
            catch (Exception ex)
            {
                var ilDump = GeneratedCode ?? "<no IL trace available>";
                throw new InvalidOperationException(
                    $"Error executing chunk {chunk.StartCommandRange}-{chunk.EndCommandRangeExclusive - 1}.\nIL:\n{ilDump}",
                    ex);
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Batched IL generation
        // ──────────────────────────────────────────────────────────────

        private DynamicMethod BuildBatchDynamicMethod(IReadOnlyList<ArrayCommandChunk> batch, out string trace)
        {
            var dm = new DynamicMethod(
                $"ILB_{batch[0].StartCommandRange}_{batch[^1].EndCommandRangeExclusive - 1}",
                typeof(void),
                new[]
                {
                    typeof(double[]),              // 0 vs
                    typeof(double[]),              // 1 os
                    typeof(double[]),              // 2 od
                    typeof(int).MakeByRefType(),   // 3 cosi
                    typeof(int).MakeByRefType(),   // 4 codi
                    typeof(bool).MakeByRefType(),  // 5 cond
                    typeof(int),                   // 6 leafId
                },
                typeof(ILChunkExecutor).Module,
                skipVisibility: true);

            var il = dm.GetILGenerator();

            var sb = PreserveGeneratedCode ? new StringBuilder() : null;
            void T(string s) => sb?.AppendLine(s);

            void E0(OpCode op) { il.Emit(op); T($"  {op}"); }
            void EI(OpCode op, int arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void ER(OpCode op, double d) { il.Emit(op, d); T($"  {op} {d}"); }
            void EL(OpCode op, Label l) { il.Emit(op, l); T($"  {op} L{l.GetHashCode():x}"); }
            void ELb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); T($"  {op} V_{lb.LocalIndex}"); }

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

            void IncrementRefInt(int argIdx)
            {
                EI(OpCodes.Ldarg, argIdx);
                E0(OpCodes.Dup);
                E0(OpCodes.Ldind_I4);
                E0(OpCodes.Ldc_I4_1);
                E0(OpCodes.Add);
                E0(OpCodes.Stind_I4);
            }
            void AdvanceRefInt(int argIdx, int delta)
            {
                if (delta == 0) return;
                EI(OpCodes.Ldarg, argIdx);
                E0(OpCodes.Dup);
                E0(OpCodes.Ldind_I4);
                LdcI4(delta);
                E0(OpCodes.Add);
                E0(OpCodes.Stind_I4);
            }
            void LoadVs(int slot)
            {
                E0(OpCodes.Ldarg_0);
                LdcI4(slot);
                E0(OpCodes.Ldelem_R8);
            }

            var tmp = il.DeclareLocal(typeof(double));
            var tmpI = il.DeclareLocal(typeof(int));

            // Switch over leafId to jump to each span block
            var labels = new Label[batch.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = il.DefineLabel();
            var defaultLabel = il.DefineLabel();

            EI(OpCodes.Ldarg, 6);
            il.Emit(OpCodes.Switch, labels);
            EL(OpCodes.Br, defaultLabel);

            // Emit each span body under its label
            for (int leaf = 0; leaf < batch.Count; leaf++)
            {
                var chunk = batch[leaf];
                il.MarkLabel(labels[leaf]);

                // Per‑leaf plan and helpers
                LocalsAllocationPlan plan = null;
                if (ReuseLocals)
                    plan = GetOrBuildSubPlan(chunk);

                LocalBuilder[] locals = null;
                LocalBindingState bind = null;
                IReadOnlyDictionary<int, int> slotMap = null;
                IntervalIndex intervalIx = null;

                if (ReuseLocals && plan.LocalCount > 0)
                {
                    locals = new LocalBuilder[plan.LocalCount];
                    for (int i = 0; i < plan.LocalCount; i++)
                        locals[i] = il.DeclareLocal(typeof(double));
                    bind = new LocalBindingState(plan.LocalCount);
                    slotMap = plan.SlotToLocal;
                    intervalIx = new IntervalIndex(plan);
                }

                bool usingReuse = bind != null;
                var ifStack = new Stack<IfContext>();

                void MarkWrittenIfReuse(int slot)
                {
                    if (usingReuse && slotMap.TryGetValue(slot, out var l))
                        bind.MarkWritten(l);
                }

                void LoadSlot(int slot)
                {
                    if (usingReuse && slotMap.TryGetValue(slot, out var lR) && bind.IsBound(lR, slot))
                    {
                        ELb(OpCodes.Ldloc, locals[lR]);
                    }
                    else
                    {
                        LoadVs(slot);
                    }
                }

                void StoreSlotFromTop(int slot)
                {
                    ELb(OpCodes.Stloc, tmp);

                    if (usingReuse && slotMap.TryGetValue(slot, out var lR) && bind.IsBound(lR, slot))
                    {
                        ELb(OpCodes.Ldloc, tmp);
                        ELb(OpCodes.Stloc, locals[lR]);
                    }
                    else
                    {
                        E0(OpCodes.Ldarg_0);
                        LdcI4(slot);
                        ELb(OpCodes.Ldloc, tmp);
                        E0(OpCodes.Stelem_R8);
                    }

                    MarkWrittenIfReuse(slot);
                }

                for (int ci = chunk.StartCommandRange; ci < chunk.EndCommandRangeExclusive; ci++)
                {
                    if (usingReuse)
                    {
                        foreach (int slot in intervalIx.StartSlots(ci))
                        {
                            if (!slotMap.TryGetValue(slot, out int local))
                                continue;

                            int depth = _globalDepth.GetDepth(ci);
                            if (bind.TryReuse(local, slot, depth, out int flushSlot))
                            {
                                if (flushSlot != -1)
                                {
                                    E0(OpCodes.Ldarg_0);
                                    LdcI4(flushSlot);
                                    ELb(OpCodes.Ldloc, locals[local]);
                                    E0(OpCodes.Stelem_R8);

                                    foreach (var ctx in ifStack)
                                        ctx.Flushes.Add((flushSlot, local));
                                }

                                LoadVs(slot);
                                ELb(OpCodes.Stloc, locals[local]);

                                foreach (var ctx in ifStack)
                                    ctx.Initialises.Add((slot, local));

                                bind.StartInterval(slot, local, depth);
                            }
                        }
                    }

                    var cmd = Commands[ci];

                    switch (cmd.CommandType)
                    {
                        case ArrayCommandType.Zero:
                            LdcR8(0.0);
                            StoreSlotFromTop(cmd.Index);
                            break;

                        case ArrayCommandType.CopyTo:
                            LoadSlot(cmd.SourceIndex);
                            StoreSlotFromTop(cmd.Index);
                            break;

                        case ArrayCommandType.NextSource:
                            E0(OpCodes.Ldarg_1);
                            EI(OpCodes.Ldarg, 3);
                            E0(OpCodes.Ldind_I4);
                            E0(OpCodes.Ldelem_R8);
                            StoreSlotFromTop(cmd.Index);
                            IncrementRefInt(3);
                            break;

                        case ArrayCommandType.NextDestination:
                        {
                            EI(OpCodes.Ldarg, 4);
                            E0(OpCodes.Ldind_I4);
                            ELb(OpCodes.Stloc, tmpI);

                            E0(OpCodes.Ldarg_2);
                            ELb(OpCodes.Ldloc, tmpI);
                            E0(OpCodes.Ldelem_R8);

                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Add);

                            ELb(OpCodes.Stloc, tmp);
                            E0(OpCodes.Ldarg_2);
                            ELb(OpCodes.Ldloc, tmpI);
                            ELb(OpCodes.Ldloc, tmp);
                            E0(OpCodes.Stelem_R8);

                            IncrementRefInt(4);
                            break;
                        }

                        case ArrayCommandType.MultiplyBy:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Mul);
                            StoreSlotFromTop(cmd.Index);
                            break;

                        case ArrayCommandType.IncrementBy:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Add);
                            StoreSlotFromTop(cmd.Index);
                            break;

                        case ArrayCommandType.DecrementBy:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Sub);
                            StoreSlotFromTop(cmd.Index);
                            break;

                        case ArrayCommandType.EqualsOtherArrayIndex:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Ceq);
                            StoreCond(invert: false);
                            break;

                        case ArrayCommandType.NotEqualsOtherArrayIndex:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Ceq);
                            StoreCond(invert: true);
                            break;

                        case ArrayCommandType.GreaterThanOtherArrayIndex:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Cgt);
                            StoreCond(invert: false);
                            break;

                        case ArrayCommandType.LessThanOtherArrayIndex:
                            LoadSlot(cmd.Index);
                            LoadSlot(cmd.SourceIndex);
                            E0(OpCodes.Clt);
                            StoreCond(invert: false);
                            break;

                        case ArrayCommandType.EqualsValue:
                            LoadSlot(cmd.Index);
                            LdcR8(cmd.SourceIndex);
                            E0(OpCodes.Ceq);
                            StoreCond(invert: false);
                            break;

                        case ArrayCommandType.NotEqualsValue:
                            LoadSlot(cmd.Index);
                            LdcR8(cmd.SourceIndex);
                            E0(OpCodes.Ceq);
                            StoreCond(invert: true);
                            break;

                        case ArrayCommandType.If:
                        {
                            var elseLbl = il.DefineLabel();
                            var endLbl  = il.DefineLabel();

                            EI(OpCodes.Ldarg, 5);
                            E0(OpCodes.Ldind_I1);
                            EL(OpCodes.Brfalse, elseLbl);

                            var (srcSkip, dstSkip) = CountPointerSkips(UnderlyingCommands, ci, chunk.EndCommandRangeExclusive);

                            bool[] dirtyBefore = null;
                            var flushes = (List<(int slot, int local)>)null;

                            if (usingReuse)
                            {
                                dirtyBefore = new bool[plan.LocalCount];
                                flushes = new List<(int, int)>();
                                for (int l = 0; l < plan.LocalCount; l++)
                                    dirtyBefore[l] = bind.NeedsFlushBeforeReuse(l, out _);
                            }

                            ifStack.Push(new IfContext(elseLbl, endLbl, srcSkip, dstSkip, dirtyBefore, flushes));
                            break;
                        }

                        case ArrayCommandType.EndIf:
                        {
                            if (ifStack.Count == 0)
                                break;

                            var ctx = ifStack.Pop();

                            EL(OpCodes.Br, ctx.EndLabel);

                            il.MarkLabel(ctx.ElseLabel);

                            if (ctx.Flushes != null)
                            {
                                foreach (var (slot, local) in ctx.Flushes)
                                {
                                    E0(OpCodes.Ldarg_0);
                                    LdcI4(slot);
                                    ELb(OpCodes.Ldloc, locals[local]);
                                    E0(OpCodes.Stelem_R8);
                                }
                            }

                            if (ctx.Initialises != null)
                            {
                                foreach (var (slot, local) in ctx.Initialises)
                                {
                                    LoadVs(slot);
                                    ELb(OpCodes.Stloc, locals[local]);
                                }
                            }

                            if (ctx.SrcSkip > 0) AdvanceRefInt(3, ctx.SrcSkip);
                            if (ctx.DstSkip > 0) AdvanceRefInt(4, ctx.DstSkip);

                            il.MarkLabel(ctx.EndLabel);
                            break;
                        }

                        case ArrayCommandType.Checkpoint:
                        case ArrayCommandType.Comment:
                        case ArrayCommandType.Blank:
                        case ArrayCommandType.IncrementDepth:
                        case ArrayCommandType.DecrementDepth:
                            break;

                        default:
                            throw new NotImplementedException(cmd.CommandType.ToString());
                    }

                    if (usingReuse)
                    {
                        foreach (int endSlot in intervalIx.EndSlots(ci))
                        {
                            if (!slotMap.TryGetValue(endSlot, out int local))
                                continue;

                            if (bind.NeedsFlushBeforeReuse(local, out int boundSlot) && boundSlot == endSlot)
                            {
                                E0(OpCodes.Ldarg_0);
                                LdcI4(endSlot);
                                ELb(OpCodes.Ldloc, locals[local]);
                                E0(OpCodes.Stelem_R8);

                                foreach (var ctx in ifStack)
                                    if (ctx.DirtyBefore != null && ctx.DirtyBefore[local])
                                        ctx.Flushes.Add((endSlot, local));

                                bind.FlushLocal(local);
                            }

                            bind.Release(local);
                        }
                    }
                }

                if (usingReuse)
                {
                    for (int local = 0; local < plan.LocalCount; local++)
                    {
                        if (bind.NeedsFlushBeforeReuse(local, out int slot) && slot != -1)
                        {
                            E0(OpCodes.Ldarg_0);
                            LdcI4(slot);
                            ELb(OpCodes.Ldloc, locals[local]);
                            E0(OpCodes.Stelem_R8);
                            bind.FlushLocal(local);
                        }
                    }
                }

                while (ifStack.Count > 0)
                {
                    var ctx = ifStack.Pop();

                    EL(OpCodes.Br, ctx.EndLabel);

                    il.MarkLabel(ctx.ElseLabel);

                    if (ctx.Flushes != null)
                        foreach (var (slot, local) in ctx.Flushes)
                        {
                            E0(OpCodes.Ldarg_0);
                            LdcI4(slot);
                            ELb(OpCodes.Ldloc, locals[local]);
                            E0(OpCodes.Stelem_R8);
                        }

                    if (ctx.Initialises != null)
                        foreach (var (slot, local) in ctx.Initialises)
                        {
                            LoadVs(slot);
                            ELb(OpCodes.Stloc, locals[local]);
                        }

                    if (ctx.SrcSkip > 0) AdvanceRefInt(3, ctx.SrcSkip);
                    if (ctx.DstSkip > 0) AdvanceRefInt(4, ctx.DstSkip);

                    il.MarkLabel(ctx.EndLabel);
                }

                E0(OpCodes.Ret); // return after executing this leaf
            }

            il.MarkLabel(defaultLabel);
            E0(OpCodes.Ret);

            trace = sb?.ToString();
            return dm;

            void StoreCond(bool invert)
            {
                if (invert)
                {
                    E0(OpCodes.Ldc_I4_0);
                    E0(OpCodes.Ceq);
                }

                ELb(OpCodes.Stloc, tmpI);
                EI(OpCodes.Ldarg, 5);
                ELb(OpCodes.Ldloc, tmpI);
                E0(OpCodes.Stind_I1);
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Sub‑plan slicing from global plan
        // ──────────────────────────────────────────────────────────────

        private LocalsAllocationPlan GetOrBuildSubPlan(ArrayCommandChunk chunk)
        {
            var key = (chunk.StartCommandRange, chunk.EndCommandRangeExclusive);
            if (_subPlanCache.TryGetValue(key, out var cached))
                return cached;

            var sub = new LocalsAllocationPlan();

            var localRemap = new Dictionary<int, int>();
            int nextLocal = 0;

            int s = chunk.StartCommandRange;
            int e = chunk.EndCommandRangeExclusive - 1;

            foreach (var iv in _globalPlan.Intervals)
            {
                if (iv.Last < s || iv.First > e)
                    continue;

                int first = Math.Max(iv.First, s);
                int last  = Math.Min(iv.Last, e);
                int oldLocal = _globalPlan.SlotToLocal[iv.Slot];

                if (!localRemap.TryGetValue(oldLocal, out int newLocal))
                    localRemap[oldLocal] = newLocal = nextLocal++;

                sub.AddInterval(iv.Slot, first, last, iv.BindDepth, newLocal);
            }

            _subPlanCache[key] = sub;
            return sub;
        }

        // ──────────────────────────────────────────────────────────────
        //  Inline pointer‑skip analysis for IF bodies
        // ──────────────────────────────────────────────────────────────

        private static (int src, int dst) CountPointerSkips(ArrayCommand[] cmds, int ifIndex, int endExclusive)
        {
            int src = 0, dst = 0;
            int depth = 1;

            for (int i = ifIndex + 1; i < endExclusive; i++)
            {
                var t = cmds[i].CommandType;

                if (t == ArrayCommandType.If) { depth++; continue; }
                if (t == ArrayCommandType.EndIf)
                {
                    depth--;
                    if (depth == 0) break;
                    continue;
                }

                if (depth == 1)
                {
                    if (t == ArrayCommandType.NextSource) src++;
                    else if (t == ArrayCommandType.NextDestination) dst++;
                }
            }

            return (src, dst);
        }

        // ──────────────────────────────────────────────────────────────
        //  IfContext
        // ──────────────────────────────────────────────────────────────

        private sealed class IfContext
        {
            public readonly Label ElseLabel;
            public readonly Label EndLabel;
            public readonly int SrcSkip;
            public readonly int DstSkip;
            public readonly bool[] DirtyBefore;
            public readonly List<(int slot, int local)> Flushes;
            public readonly List<(int slot, int local)> Initialises;

            public IfContext(Label elseLbl, Label endLbl, int srcSkip, int dstSkip,
                             bool[] dirtyBefore, List<(int slot, int local)> flushes)
            {
                ElseLabel = elseLbl;
                EndLabel = endLbl;
                SrcSkip = srcSkip;
                DstSkip = dstSkip;
                DirtyBefore = dirtyBefore;
                Flushes = flushes ?? new List<(int, int)>();
                Initialises = new List<(int, int)>();
            }
        }
    }
}
