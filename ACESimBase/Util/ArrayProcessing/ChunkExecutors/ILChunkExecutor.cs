using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    /// <summary>
    /// JIT‑emits a <see cref="DynamicMethod"/> per unique <see cref="ArrayCommandChunk"/> span.
    /// First‑wave optimizations:
    ///   • Compile once per unique [Start,End) span (dedupe repeated leaves)
    ///   • Per‑chunk local allocation (declare only locals used by the slice)
    ///   • No‑reuse path operates directly on VS (no bulk preload/writeback)
    /// </summary>
    internal sealed class ILChunkExecutor : ChunkExecutorBase
    {
        // ──────────────────────────────────────────────────────────────
        //  State
        // ──────────────────────────────────────────────────────────────

        // Cache: one compiled delegate per unique slice (Start, End)
        private readonly Dictionary<(int start, int end), ArrayCommandChunkDelegate> _compiledBySpan = new();

        // Queue: representative chunk per not‑yet‑compiled span
        private readonly Dictionary<(int start, int end), ArrayCommandChunk> _repBySpan = new();

        private StringBuilder _trace;

        /// <summary>
        /// When true, we attempt to reuse IL locals across intervals via <see cref="LocalVariablePlanner.PlanLocals"/>.
        /// When false, we emit IL that reads/writes the VS array directly (no bulk preload/writeback).
        /// </summary>
        public bool ReuseLocals { get; init; }

        public ILChunkExecutor(ArrayCommand[] cmds,
                               int start, int end,
                               bool localVariableReuse = false)
            : base(cmds, start, end, useCheckpoints: false,
                   arrayCommandListForCheckpoints: null)
        {
            ReuseLocals = localVariableReuse;
        }

        // ──────────────────────────────────────────────────────────────
        //  IChunkExecutor
        // ──────────────────────────────────────────────────────────────

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            var key = (chunk.StartCommandRange, chunk.EndCommandRangeExclusive);
            if (_compiledBySpan.ContainsKey(key))
                return; // already compiled

            if (!_repBySpan.ContainsKey(key))
                _repBySpan[key] = chunk; // remember representative for this span
        }

        public override void PerformGeneration()
        {
            if (_repBySpan.Count == 0)
                return;

            if (PreserveGeneratedCode)
                _trace = new StringBuilder();

            foreach (var (key, rep) in _repBySpan)
            {
                try
                {
                    var dm = BuildDynamicMethod(rep, out var srcDump);
                    var del = (ArrayCommandChunkDelegate)dm.CreateDelegate(typeof(ArrayCommandChunkDelegate));
                    _compiledBySpan[key] = del;

                    if (PreserveGeneratedCode && _trace != null)
                        _trace.Append(srcDump);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Error generating IL for chunk [{rep.StartCommandRange},{rep.EndCommandRangeExclusive}).",
                        ex);
                }
            }

            _repBySpan.Clear();

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

            var key = (chunk.StartCommandRange, chunk.EndCommandRangeExclusive);
            if (!_compiledBySpan.TryGetValue(key, out var del))
                throw new InvalidOperationException(
                    $"Attempted to execute uncompiled span. " +
                    "Ensure AddToGeneration/PerformGeneration were called.");

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
        //  IL generation
        // ──────────────────────────────────────────────────────────────

        private DynamicMethod BuildDynamicMethod(ArrayCommandChunk chunk, out string trace)
        {
            var dm = new DynamicMethod(
                $"IL_{chunk.StartCommandRange}_{chunk.EndCommandRangeExclusive - 1}",
                typeof(void),
                new[]
                {
                    typeof(double[]),              // 0 vs
                    typeof(double[]),              // 1 os
                    typeof(double[]),              // 2 od
                    typeof(int).MakeByRefType(),   // 3 cosi
                    typeof(int).MakeByRefType(),   // 4 codi
                    typeof(bool).MakeByRefType(),  // 5 cond
                },
                typeof(ILChunkExecutor).Module,
                skipVisibility: true);

            var il = dm.GetILGenerator();

            // optional tracing buffer
            var sb = PreserveGeneratedCode ? new StringBuilder() : null;
            void T(string s) => sb?.AppendLine(s);

            // ───── emit wrappers ─────
            void E0(OpCode op) { il.Emit(op); T($"  {op}"); }
            void EI(OpCode op, int arg) { il.Emit(op, arg); T($"  {op} {arg}"); }
            void ER(OpCode op, double d) { il.Emit(op, d); T($"  {op} {d}"); }
            void EL(OpCode op, Label l) { il.Emit(op, l); T($"  {op} L{l.GetHashCode():x}"); }
            void ELb(OpCode op, LocalBuilder lb) { il.Emit(op, lb); T($"  {op} V_{lb.LocalIndex}"); }

            // ───── utility IL snippets ─────
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
            void StoreVsFromTop(int slot)
            {
                E0(OpCodes.Ldarg_0);
                LdcI4(slot);
                E0(OpCodes.Stelem_R8);
            }

            // ───── locals ─────
            var tmp = il.DeclareLocal(typeof(double)); // scratch for R‑M‑W stores
            var tmpI = il.DeclareLocal(typeof(int));    // scratch for integer pointers

            // Per‑chunk plan (only slots relevant to this slice)
            LocalsAllocationPlan plan = null;
            if (ReuseLocals)
            {
                plan = LocalVariablePlanner.PlanLocals(UnderlyingCommands,
                                                       chunk.StartCommandRange,
                                                       chunk.EndCommandRangeExclusive);
            }

            LocalBuilder[] locals = null;
            var bind = (LocalBindingState)null;
            var slotMap = (IReadOnlyDictionary<int, int>)null;

            if (ReuseLocals && plan.LocalCount > 0)
            {
                locals = new LocalBuilder[plan.LocalCount];
                for (int i = 0; i < plan.LocalCount; i++)
                    locals[i] = il.DeclareLocal(typeof(double));
                bind = new LocalBindingState(plan.LocalCount);
                slotMap = plan.SlotToLocal;
            }

            bool usingReuse = bind != null; // convenience flag

            // ───── pointer skip map for IFs ─────
            var skipMap = PrecomputePointerSkips(chunk);

            // ───── helpers that honor reuse/no‑reuse ─────
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
                // value is on stack → store into tmp
                ELb(OpCodes.Stloc, tmp);

                if (usingReuse && slotMap.TryGetValue(slot, out var lR) && bind.IsBound(lR, slot))
                {
                    ELb(OpCodes.Ldloc, tmp);
                    ELb(OpCodes.Stloc, locals[lR]);
                }
                else
                {
                    // vs[slot] = tmp
                    E0(OpCodes.Ldarg_0);
                    LdcI4(slot);
                    ELb(OpCodes.Ldloc, tmp);
                    E0(OpCodes.Stelem_R8);
                }

                MarkWrittenIfReuse(slot);
            }

            // ───── IF context ─────
            var ifStack = new Stack<IfContext>();

            // Minimal depth info for this chunk (used by reuse binder)
            int GetDepthAt(int cmdIndex)
            {
                int depth = 0;
                for (int i = chunk.StartCommandRange; i < cmdIndex; i++)
                {
                    var t = UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) depth++;
                    else if (t == ArrayCommandType.EndIf) depth = Math.Max(0, depth - 1);
                }
                return depth;
            }

            // ───── main emission loop ─────
            for (int ci = chunk.StartCommandRange; ci < chunk.EndCommandRangeExclusive; ci++)
            {
                var cmd = UnderlyingCommands[ci];

                // Bind any intervals that start here (reuse mode)
                if (usingReuse && plan.FirstUseToSlot.TryGetValue(ci, out var startSlot))
                {
                    // NOTE: PlanLocals assigns exactly one local per slot; multiple intervals
                    // can start on the same instruction only if different slots start there.
                    // Walk all that start here.
                    foreach (var kv in plan.Intervals.Where(iv => iv.First == ci))
                    {
                        int slot = kv.Slot;
                        if (!slotMap.TryGetValue(slot, out int local))
                            continue;

                        int depth = GetDepthAt(ci);
                        if (bind.TryReuse(local, slot, depth, out int flushSlot))
                        {
                            if (flushSlot != -1)
                            {
                                // Flush previous content of that local before reuse
                                E0(OpCodes.Ldarg_0);          // vs
                                LdcI4(flushSlot);
                                ELb(OpCodes.Ldloc, locals[local]);
                                E0(OpCodes.Stelem_R8);

                                // record for ELSE leg
                                foreach (var ctx in ifStack) ctx.Flushes.Add((flushSlot, local));
                            }

                            // Initialize l{local} = vs[slot]
                            LoadVs(slot);
                            ELb(OpCodes.Stloc, locals[local]);

                            // record for ELSE leg (must replay init when IF is skipped)
                            foreach (var ctx in ifStack) ctx.Initialises.Add((slot, local));

                            bind.StartInterval(slot, local, depth);
                        }
                    }
                }

                // Emit command IL
                switch (cmd.CommandType)
                {
                    /* writes / R-M-W */
                    case ArrayCommandType.Zero:
                        LdcR8(0.0);
                        StoreSlotFromTop(cmd.Index);
                        break;

                    case ArrayCommandType.CopyTo:
                        LoadSlot(cmd.SourceIndex);
                        StoreSlotFromTop(cmd.Index);
                        break;

                    case ArrayCommandType.NextSource:
                        // vs[idx] = os[cosi++]
                        E0(OpCodes.Ldarg_1);        // os[]
                        EI(OpCodes.Ldarg, 3);       // ref cosi
                        E0(OpCodes.Ldind_I4);       // cosi
                        E0(OpCodes.Ldelem_R8);      // os[cosi]
                        StoreSlotFromTop(cmd.Index);
                        IncrementRefInt(3);         // cosi++
                        break;

                    case ArrayCommandType.NextDestination:
                    {
                        // int tmpI = codi;
                        EI(OpCodes.Ldarg, 4);
                        E0(OpCodes.Ldind_I4);
                        ELb(OpCodes.Stloc, tmpI);

                        // double acc = od[tmpI];
                        E0(OpCodes.Ldarg_2);
                        ELb(OpCodes.Ldloc, tmpI);
                        E0(OpCodes.Ldelem_R8);

                        // acc += <VS value from SourceIndex>
                        LoadSlot(cmd.SourceIndex);
                        E0(OpCodes.Add);

                        // od[tmpI] = acc;
                        ELb(OpCodes.Stloc, tmp);
                        E0(OpCodes.Ldarg_2);
                        ELb(OpCodes.Ldloc, tmpI);
                        ELb(OpCodes.Ldloc, tmp);
                        E0(OpCodes.Stelem_R8);

                        // codi++;
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

                    /* comparisons → cond */
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

                    /* control-flow */
                    case ArrayCommandType.If:
                    {
                        var elseLbl = il.DefineLabel();
                        var endLbl  = il.DefineLabel();

                        // if (!cond) goto elseLbl;
                        EI(OpCodes.Ldarg, 5);
                        E0(OpCodes.Ldind_I1);
                        EL(OpCodes.Brfalse, elseLbl);

                        // Track skip counts and dirty‑before status for reuse locals
                        var sk = skipMap.TryGetValue(ci, out var tuple) ? tuple : (src: 0, dst: 0);
                        bool[] dirtyBefore = null;
                        var flushes = (List<(int slot, int local)>)null;

                        if (usingReuse)
                        {
                            dirtyBefore = new bool[plan.LocalCount];
                            flushes = new List<(int, int)>();
                            for (int l = 0; l < plan.LocalCount; l++)
                                dirtyBefore[l] = bind.NeedsFlushBeforeReuse(l, out _);
                        }

                        ifStack.Push(new IfContext(elseLbl, endLbl, sk.src, sk.dst, dirtyBefore, flushes));
                        break;
                    }

                    case ArrayCommandType.EndIf:
                    {
                        if (ifStack.Count == 0)
                            break;

                        var ctx = ifStack.Pop();

                        // THEN end → fall into ELSE via unconditional branch
                        EL(OpCodes.Br, ctx.EndLabel);

                        // ELSE: replay flushes/initializations and advance pointers
                        il.MarkLabel(ctx.ElseLabel);

                        if (ctx.Flushes != null)
                        {
                            foreach (var (slot, local) in ctx.Flushes)
                            {
                                // vs[slot] = l{local};
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
                                // l{local} = vs[slot];
                                LoadVs(slot);
                                ELb(OpCodes.Stloc, locals[local]);
                            }
                        }

                        if (ctx.SrcSkip > 0) AdvanceRefInt(3, ctx.SrcSkip);
                        if (ctx.DstSkip > 0) AdvanceRefInt(4, ctx.DstSkip);

                        il.MarkLabel(ctx.EndLabel);
                        break;
                    }

                    /* ignored */
                    case ArrayCommandType.Checkpoint:
                    case ArrayCommandType.Comment:
                    case ArrayCommandType.Blank:
                    case ArrayCommandType.IncrementDepth:
                    case ArrayCommandType.DecrementDepth:
                        break;

                    default:
                        throw new NotImplementedException(cmd.CommandType.ToString());
                }

                // Release any intervals that end here (reuse mode)
                if (usingReuse && plan.LastUseToSlot.TryGetValue(ci, out var endSlotKey))
                {
                    foreach (var iv in plan.Intervals.Where(iv => iv.Last == ci))
                    {
                        int endSlot = iv.Slot;
                        if (!slotMap.TryGetValue(endSlot, out int local))
                            continue;

                        if (bind.NeedsFlushBeforeReuse(local, out int boundSlot) && boundSlot == endSlot)
                        {
                            // vs[endSlot] = l{local}
                            E0(OpCodes.Ldarg_0);
                            LdcI4(endSlot);
                            ELb(OpCodes.Ldloc, locals[local]);
                            E0(OpCodes.Stelem_R8);

                            // record flush in ELSE only if dirty prior to IF
                            foreach (var ctx in ifStack)
                                if (ctx.DirtyBefore != null && ctx.DirtyBefore[local])
                                    ctx.Flushes.Add((endSlot, local));

                            bind.FlushLocal(local);
                        }

                        bind.Release(local);
                    }
                }
            }

            // Final flush for any still‑dirty locals at chunk end (reuse only)
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

            // Close any unmatched IFs defensively (mirrors old behavior)
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

            E0(OpCodes.Ret);
            trace = sb?.ToString();
            return dm;

            // ───── helper: write cond back to ref bool ─────
            void StoreCond(bool invert)
            {
                if (invert)
                {
                    E0(OpCodes.Ldc_I4_0);
                    E0(OpCodes.Ceq);
                }

                ELb(OpCodes.Stloc, tmpI);  // stash int 0/1
                EI(OpCodes.Ldarg, 5);      // ref bool cond
                ELb(OpCodes.Ldloc, tmpI);
                E0(OpCodes.Stind_I1);
            }
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
