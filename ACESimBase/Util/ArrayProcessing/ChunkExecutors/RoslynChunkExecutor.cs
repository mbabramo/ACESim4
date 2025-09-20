// RoslynChunkExecutor.cs – generated‑code executor

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.CodeGen;
using Google.Protobuf.Reflection;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class RoslynChunkExecutor : ChunkExecutorBase
    {
        private readonly LocalsAllocationPlan _plan;
        private readonly List<ArrayCommandChunk> _scheduled = new();
        private readonly ConcurrentDictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private readonly Dictionary<string, List<ArrayCommandChunk>> _chunksByFn = new();
        private readonly StringBuilder _src = new();
        private Type _cgType;

        public bool ReuseLocals { get; init; } = true;

        // ──────────────────────────────────────────────────────────────────────────────
        // Tracks data while we are inside an `if(cond){…}` block.
        // ──────────────────────────────────────────────────────────────────────────────
        private sealed class IfContext
        {
            public readonly int SrcSkip;
            public readonly int DstSkip;

            public readonly bool[] DirtyBefore;

            // (slot,local) pairs whose *last* flush happens inside the branch
            public readonly List<(int slot, int local)> Flushes = new();

            // (slot,local) pairs bound for the first time inside the branch
            // → must be replayed in the ELSE leg to guarantee initialisation.
            public readonly List<(int slot, int local)> Initialises = new();

            public IfContext(int srcSkip, int dstSkip, bool[] dirtyBefore)
            {
                SrcSkip = srcSkip;
                DstSkip = dstSkip;
                DirtyBefore = dirtyBefore;
            }
        }



        public RoslynChunkExecutor(ArrayCommand[] commands,
                           int start, int end,
                           bool useCheckpoints,
                           bool localVariableReuse = true)
    : base(commands, start, end, useCheckpoints, arrayCommandListForCheckpoints: null)
        {
            ReuseLocals = localVariableReuse;

            _plan = ReuseLocals
                ? LocalVariablePlanner.PlanLocals(commands, start, end)
                : LocalVariablePlanner.PlanNoReuse(commands, start, end);
        }

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            // Already have a delegate for this exact chunk object?
            if (_compiled.ContainsKey(chunk))
                return;

            // Group by generated function name (i.e., by [start,end) span).
            string fn = FnName(chunk);
            if (!_chunksByFn.TryGetValue(fn, out var list))
            {
                list = new List<ArrayCommandChunk>(capacity: 1);
                _chunksByFn[fn] = list;

                // First time we see this span → schedule a single representative for codegen.
                _scheduled.Add(chunk);
            }

            // Keep all chunk objects that share this span so we can map them after codegen.
            list.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_scheduled.Count == 0)
                return;

            _src.Clear();
            _src.AppendLine("using System; namespace CG { static class G {");

            try
            {
                // Generate source once per unique span (the reps in _scheduled).
                GenerateSourceForChunks(_scheduled);
            }
            catch (Exception ex)
            {
                throw new Exception("Roslyn generate source failed", ex);
            }

            _src.AppendLine("}} // CG.G");

            string code = _src.ToString();
            if (PreserveGeneratedCode)
                GeneratedCode = code;

            // Compile once and wire the same delegate to *all* chunks sharing that span.
            _cgType = StringToCode.LoadCode(code, "CG.G");
            foreach (var rep in _scheduled)
            {
                var mi = _cgType!.GetMethod(FnName(rep), BindingFlags.Static | BindingFlags.Public)!;
                var del = (ArrayCommandChunkDelegate)Delegate.CreateDelegate(typeof(ArrayCommandChunkDelegate), mi);

                // Assign compiled delegate to every chunk object with the same span.
                var fn = FnName(rep);
                foreach (var chunk in _chunksByFn[fn])
                    _compiled[chunk] = del;
            }

            _scheduled.Clear();
            _chunksByFn.Clear();
        }


        public override void Execute(
            ArrayCommandChunk chunk,
            double[] vs,
            double[] os,
            double[] od,
            ref int cosi,
            ref int codi,
            ref bool cond)
        {
            if (chunk.EndCommandRangeExclusive <= chunk.StartCommandRange)
                return;

            try
            {
                _compiled[chunk](vs, os, od, ref cosi, ref codi, ref cond);
                // IMPORTANT: executors must not mutate chunk metadata here.
            }
            catch (Exception ex)
            {
                var code = GeneratedCode ?? "<no generated source preserved>";
                throw new InvalidOperationException(
                    $"Error executing chunk [{chunk.StartCommandRange},{chunk.EndCommandRangeExclusive}).",
                    ex);
            }
        }


        private static string FnName(ArrayCommandChunk c)
            => $"S{c.StartCommandRange}_{c.EndCommandRangeExclusive - 1}";

        private void GenerateSourceForChunks(IEnumerable<ArrayCommandChunk> chunks)
        {
            foreach (var c in chunks)
                GenerateSourceForChunk(c);
        }

        private void GenerateSourceForChunk(ArrayCommandChunk c)
        {
            var depthMap = new DepthMap(Commands.ToArray(),
                                          c.StartCommandRange,
                                          c.EndCommandRangeExclusive);
            var intervalIx = new IntervalIndex(_plan);
            var bind = new LocalBindingState(_plan.LocalCount);
            var cb = new CodeBuilder();

            var usedSlots = new HashSet<int>();
            for (int i = c.StartCommandRange; i < c.EndCommandRangeExclusive; i++)
            {
                var cmd = Commands[i];
                if (cmd.Index >= 0) usedSlots.Add(cmd.Index);
                if (cmd.SourceIndex >= 0) usedSlots.Add(cmd.SourceIndex);
            }

            var skipMap = PrecomputePointerSkips(c);
            var ifStack = new Stack<IfContext>();

            string fn = FnName(c);
            _src.AppendLine(
                $"public static void {fn}(double[]vs,double[]os,double[]od,ref int i,ref int codi,ref bool cond){{");

            cb.Indent();

            for (int l = 0; l < _plan.LocalCount; l++)
                cb.AppendLine($"double l{l} = 0;");
            cb.AppendLine();

            if (ReuseLocals)
                EmitReusingBody(c, cb, depthMap, intervalIx, bind,
                                skipMap, ifStack, usedSlots);
            else
                EmitZeroReuseBody(c, cb, skipMap, ifStack, usedSlots);

            while (ifStack.Count > 0)
            {
                var ctx = ifStack.Pop();
                cb.Unindent();
                cb.AppendLine("} else {");

                foreach (var (slot, local) in ctx.Flushes)
                    cb.AppendLine($"vs[{slot}] = l{local};");

                foreach (var (slot, local) in ctx.Initialises)
                    cb.AppendLine($"l{local} = vs[{slot}];");

                cb.AppendLine($"i += {ctx.SrcSkip};");
                cb.AppendLine($"codi += {ctx.DstSkip};");
                cb.AppendLine("}");

                foreach (var (slot, local) in ctx.Initialises)
                    if (bind != null &&
                        bind.NeedsFlushBeforeReuse(local, out int boundSlot) &&
                        boundSlot == slot)
                    {
                        cb.AppendLine($"vs[{slot}] = l{local};");
                        bind.FlushLocal(local);
                    }
            }

            cb.Unindent();
            _src.AppendLine(cb.ToString());
            _src.AppendLine("}");

        }



        private void EmitZeroReuseBody(
    ArrayCommandChunk c,
    CodeBuilder cb,
    Dictionary<int, (int src, int dst)> skipMap,
    Stack<IfContext> ifStack,
    HashSet<int> usedSlots)
        {
            // preload every slot that is ever used
            foreach (var kv in _plan.SlotToLocal)
                if (usedSlots.Contains(kv.Key))
                    cb.AppendLine($"l{kv.Value} = vs[{kv.Key}];");

            Span<ArrayCommand> cmds = Commands;
            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
                // bind == null → no dirty tracking in this mode
                EmitCmdBasic(ci, cmds[ci], cb, skipMap, ifStack, bind: null);

            // write all locals back – safe because mapping never changes
            foreach (var kv in _plan.SlotToLocal)
                if (usedSlots.Contains(kv.Key))
                    cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
        }

        /// <summary>
        /// Generate source for a chunk in local‑reuse mode, now flushing *all* slots
        /// whose intervals end on a given instruction.
        /// </summary>
        private void EmitReusingBody(
            ArrayCommandChunk c,
            CodeBuilder cb,
            DepthMap depth,
            IntervalIndex intervalIx,
            LocalBindingState bind,
            Dictionary<int, (int src, int dst)> skipMap,
            Stack<IfContext> ifStack,
            HashSet<int> usedSlots)
        {
            Span<ArrayCommand> cmds = Commands;

            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
            {
                int d = depth.GetDepth(ci);

                // ───── interval starts ─────
                foreach (int slot in intervalIx.StartSlots(ci))
                {
                    int local = _plan.SlotToLocal[slot];

                    if (bind.TryReuse(local, slot, d, out int flushSlot))
                    {
                        if (flushSlot != -1)
                        {
                            cb.AppendLine($"vs[{flushSlot}] = l{local};");
                            if (ifStack.Count > 0)
                                foreach (var ctx in ifStack)
                                    ctx.Flushes.Add((flushSlot, local));
                        }

                        cb.AppendLine($"l{local} = vs[{slot}];");
                        if (ifStack.Count > 0)
                            foreach (var ctx in ifStack)
                                ctx.Initialises.Add((slot, local));

                        bind.StartInterval(slot, local, d);
                    }
                }

                // ───── emit the command ─────
                EmitCmdBasic(ci, cmds[ci], cb, skipMap, ifStack, bind);

                // ───── interval ends ─────
                foreach (int endSlot in intervalIx.EndSlots(ci))
                {
                    int local = _plan.SlotToLocal[endSlot];

                    if (bind.NeedsFlushBeforeReuse(local, out int boundSlot) && boundSlot == endSlot)
                    {
                        cb.AppendLine($"vs[{endSlot}] = l{local};");
                        if (ifStack.Count > 0)
                            foreach (var ctx in ifStack)
                                if (ctx.DirtyBefore[local])
                                    ctx.Flushes.Add((endSlot, local));

                        bind.FlushLocal(local);
                    }

                    bind.Release(local);
                }
            }

            // ───── final flush for locals that stayed live to the end of the chunk ─────
            for (int local = 0; local < _plan.LocalCount; local++)
            {
                if (bind.NeedsFlushBeforeReuse(local, out int slot) && slot != -1)
                {
                    cb.AppendLine($"vs[{slot}] = l{local};");
                    bind.FlushLocal(local);
                }
            }
        }





        /// <summary>
        /// Emit one ArrayCommand.  When <paramref name="bind"/> is non‑null
        /// (i.e. local‑reuse mode) we call <c>bind.MarkWritten()</c> for every
        /// command that writes to its target VS slot so that the dirty‑bit is
        /// accurate.
        /// </summary>
        private void EmitCmdBasic(
            int cmdIndex,
            ArrayCommand cmd,
            CodeBuilder cb,
            Dictionary<int, (int src, int dst)> skipMap,
            Stack<IfContext> ifStack,
            LocalBindingState bind)
        {
            void MarkWritten(int slot)
            {
                if (bind != null && _plan.SlotToLocal.TryGetValue(slot, out int localId))
                    bind.MarkWritten(localId);
            }

            string R(int slot) => _plan.SlotToLocal.TryGetValue(slot, out int l) ? $"l{l}" : $"vs[{slot}]";
            string W(int slot) => R(slot);

            switch (cmd.CommandType)
            {
                // ────────── write-only / read-modify-write commands ──────────
                case ArrayCommandType.Zero:
                    cb.AppendLine($"{W(cmd.Index)} = 0;");
                    MarkWritten(cmd.Index);
                    break;

                case ArrayCommandType.CopyTo:
                    cb.AppendLine($"{W(cmd.Index)} = {R(cmd.SourceIndex)};");
                    MarkWritten(cmd.Index);
                    break;

                case ArrayCommandType.NextSource:
                    // If the slot is promoted to a local, update BOTH the local and vs[slot]
                    if (_plan.SlotToLocal.TryGetValue(cmd.Index, out int loc))
                    {
                        cb.AppendLine($"l{loc} = os[i++];");
                        cb.AppendLine($"vs[{cmd.Index}] = l{loc};");   // write‑through
                    }
                    else
                    {
                        cb.AppendLine($"vs[{cmd.Index}] = os[i++];");
                    }
                    MarkWritten(cmd.Index);
                    break;

                case ArrayCommandType.NextDestination:
                    cb.AppendLine($"od[codi++] += {R(cmd.SourceIndex)};");
                    break;

                case ArrayCommandType.MultiplyBy:
                    cb.AppendLine($"{W(cmd.Index)} *= {R(cmd.SourceIndex)};");
                    MarkWritten(cmd.Index);
                    break;

                case ArrayCommandType.IncrementBy:
                    {
                        // If the destination slot is promoted to a local variable we can
                        // safely use the compound operator.  When it’s still an array slot
                        // (`mem[...]`) the C# ‘+=’ translates to a *double read*
                        //   tmp = mem[i];                // 1st read
                        //   mem[i] = tmp + rhs;          // 2nd read (after we already wrote)
                        // …which breaks when the same slot is also being written elsewhere
                        // in the generated method.  Emit an explicit read‑modify‑write
                        // instead so the value is fetched exactly once.
                        bool local = _plan.SlotToLocal.ContainsKey(cmd.Index);
                        string lhs = W(cmd.Index);         // write‑target
                        string rhs = R(cmd.SourceIndex);   // value we’re adding

                        if (local)
                            cb.AppendLine($"{lhs} += {rhs};");
                        else
                            cb.AppendLine($"{lhs} = {lhs} + {rhs};");   // single‑read path

                        MarkWritten(cmd.Index);
                        break;
                    }

                case ArrayCommandType.DecrementBy:
                    {
                        bool local = _plan.SlotToLocal.ContainsKey(cmd.Index);
                        string lhs = W(cmd.Index);
                        string rhs = R(cmd.SourceIndex);

                        if (local)
                            cb.AppendLine($"{lhs} -= {rhs};");
                        else
                            cb.AppendLine($"{lhs} = {lhs} - {rhs};");

                        MarkWritten(cmd.Index);
                        break;
                    }

                // ────────── comparisons ──────────
                case ArrayCommandType.EqualsOtherArrayIndex:
                    cb.AppendLine($"cond = {R(cmd.Index)} == {R(cmd.SourceIndex)};");
                    break;
                case ArrayCommandType.NotEqualsOtherArrayIndex:
                    cb.AppendLine($"cond = {R(cmd.Index)} != {R(cmd.SourceIndex)};");
                    break;
                case ArrayCommandType.GreaterThanOtherArrayIndex:
                    cb.AppendLine($"cond = {R(cmd.Index)} > {R(cmd.SourceIndex)};");
                    break;
                case ArrayCommandType.LessThanOtherArrayIndex:
                    cb.AppendLine($"cond = {R(cmd.Index)} < {R(cmd.SourceIndex)};");
                    break;
                case ArrayCommandType.EqualsValue:
                    cb.AppendLine($"cond = {R(cmd.Index)} == (double){cmd.SourceIndex};");
                    break;
                case ArrayCommandType.NotEqualsValue:
                    cb.AppendLine($"cond = {R(cmd.Index)} != (double){cmd.SourceIndex};");
                    break;


                // ────────── control flow ──────────
                case ArrayCommandType.If:
                    {
                        var sk = skipMap.TryGetValue(cmdIndex, out var c)
                                    ? c
                                    : (src: 0, dst: 0);

                        var dirty = new bool[_plan.LocalCount];
                        if (bind != null)
                            for (int l = 0; l < _plan.LocalCount; l++)
                                dirty[l] = bind.NeedsFlushBeforeReuse(l, out _);

                        ifStack.Push(new IfContext(sk.src, sk.dst, dirty));
                        cb.AppendLine("if(cond){"); cb.Indent();
                        break;
                    }

                case ArrayCommandType.EndIf:
                    {
                        if (ifStack.Count == 0)
                        {
                            // Unmatched EndIf – skip
                            break;
                        }
                        var ctx = ifStack.Pop();

                        // Close the THEN block and open the ELSE block
                        cb.Unindent();
                        cb.AppendLine("} else {");

                        // Use ElseBlockBuilder to handle all flushes/reloads in the ELSE leg with correct ordering.
                        var elseBuilder = new ElseBlockBuilder(ctx.Flushes, ctx.Initialises);
                        elseBuilder.EmitElseBlock(cb);

                        // Advance source pointer past the THEN-branch commands, then close the ELSE block
                        cb.AppendLine($"i += {ctx.SrcSkip}; codi += {ctx.DstSkip}; }}");

                        // (Post-branch cleanup) Ensure any local first initialized in THEN is flushed if it became dirty.
                        // This prevents a dirty value from escaping when a nested branch is skipped.
                        foreach (var (slot, local) in ctx.Initialises)
                        {
                            if (bind != null &&
                                bind.NeedsFlushBeforeReuse(local, out int boundSlot) &&
                                boundSlot == slot)
                            {
                                cb.AppendLine($"vs[{slot}] = l{local};");
                                bind.FlushLocal(local);   // mark the local as clean in the binding state
                            }
                        }

                        break;
                    }


                // comments / blanks – ignore
                case ArrayCommandType.Checkpoint:
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                case ArrayCommandType.IncrementDepth:
                case ArrayCommandType.DecrementDepth:
                    break;

                default:
                    throw new NotImplementedException($"Unhandled command {cmd.CommandType}");
            }
        }

        /// <summary>
        /// Utility to build the else-block code that handles flushing and reinitializing locals.
        /// Ensures correct ordering of flushes (writing back to vs array) and reloads (reading from vs) 
        /// for each local variable when the THEN branch is skipped.
        /// </summary>
        private class ElseBlockBuilder
        {
            // Collections of flush and init actions recorded from the IF branch
            private readonly List<(int slot, int local)> _flushes;
            private readonly List<(int slot, int local)> _initializes;

            public ElseBlockBuilder(IEnumerable<(int slot, int local)> flushes,
                                     IEnumerable<(int slot, int local)> initializes)
            {
                // Copy lists to avoid modifying the original IfContext data
                _flushes = flushes.ToList();
                _initializes = initializes.ToList();
            }

            /// <summary>
            /// Emit the proper sequence of vs/local assignments for the ELSE block.
            /// For each local that had actions in the IF branch:
            /// - If the local was reused (appears in both flushes and initializes lists with different slots), 
            ///   flush its old slot value first, then reload the new slot’s value.
            /// - If a flush and initialize refer to the *same* slot (local’s entire life was inside the IF), 
            ///   perform a reload before flush (this ends up writing the original value back, preserving state).
            /// - Otherwise, perform any standalone flushes or initializes as needed.
            /// </summary>
            public void EmitElseBlock(CodeBuilder cb)
            {
                // Group flush and init actions by local variable for ordered emission
                var actionsByLocal = new Dictionary<int, (int? initSlot, List<int> flushSlots)>();
                foreach (var (slot, local) in _initializes)
                {
                    if (!actionsByLocal.ContainsKey(local))
                        actionsByLocal[local] = (initSlot: slot, flushSlots: new List<int>());
                    else
                        actionsByLocal[local] = (initSlot: slot, flushSlots: actionsByLocal[local].flushSlots);
                }
                foreach (var (slot, local) in _flushes)
                {
                    if (!actionsByLocal.ContainsKey(local))
                        actionsByLocal[local] = (initSlot: (int?)null, flushSlots: new List<int>());
                    actionsByLocal[local].flushSlots.Add(slot);
                }

                // Emit actions for each local in a stable order (e.g., increasing local id)
                foreach (int local in actionsByLocal.Keys.OrderBy(l => l))
                {
                    int? initSlot = actionsByLocal[local].initSlot;
                    var flushSlots = actionsByLocal[local].flushSlots;

                    bool hasInit = initSlot.HasValue;
                    bool hasFlush = flushSlots.Count > 0;

                    if (hasInit && hasFlush)
                    {
                        // This local had a value flushed in the IF and was also initialized to a new slot in the IF.
                        // Determine if the flush and init target the same slot or different slots.
                        int initTarget = initSlot.Value;
                        // Separate flush slots into "same as init" vs "others"
                        int? sameSlotFlush = flushSlots.Contains(initTarget) ? initTarget : (int?)null;
                        var otherFlushes = flushSlots.Where(s => s != initTarget);

                        // Flush any "other" slot first (preserve the old value before we reuse the local).
                        foreach (int slot in otherFlushes)
                        {
                            cb.AppendLine($"vs[{slot}] = l{local};");  // flush old value to its slot
                        }

                        if (sameSlotFlush.HasValue)
                        {
                            // If the local’s flush and init involve the same slot, reload before flushing.
                            // (This scenario typically means the slot’s entire lifetime was within the IF.)
                            cb.AppendLine($"l{local} = vs[{initTarget}];");   // reload the value from vs (ensure local is up to date)
                            cb.AppendLine($"vs[{initTarget}] = l{local};");  // flush it back (preserving the value in memory)
                        }
                        else
                        {
                            // Flush and init are for different slots (true reuse scenario).
                            cb.AppendLine($"l{local} = vs[{initTarget}];");  // now load the new slot’s value into the local
                        }
                    }
                    else if (hasFlush && !hasInit)
                    {
                        // Local wasn’t first bound in this IF (no new init), but had flushes inside the IF.
                        // Flush all such slots to memory, as those writes didn’t occur when the branch was skipped.
                        foreach (int slot in flushSlots)
                        {
                            cb.AppendLine($"vs[{slot}] = l{local};");
                        }
                    }
                    else if (hasInit && !hasFlush)
                    {
                        // Local was first initialized in the IF (and not flushed there), meaning its value is needed after the IF.
                        // Ensure it’s initialized in the else path as well.
                        cb.AppendLine($"l{local} = vs[{initSlot.Value}];");
                    }
                    // If neither flush nor init, nothing to do for this local in else (should not happen for locals in context).
                }
            }
        }
    }
}
