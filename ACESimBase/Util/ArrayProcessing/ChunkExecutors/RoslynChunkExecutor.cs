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
            if (!_compiled.ContainsKey(chunk))
                _scheduled.Add(chunk);
        }

        public override void PerformGeneration()
        {
            // This will be called only once. Execution will happen many times.

            if (_scheduled.Count == 0) return;

            _src.Clear();
            _src.AppendLine("using System; namespace CG { static class G {");

            GenerateSourceForChunks(_scheduled);

            _src.AppendLine("}} // CG.G");

            string code = _src.ToString();
            if (PreserveGeneratedCode)
                GeneratedCode = code;

            _cgType = StringToCode.LoadCode(code, "CG.G");
            foreach (var c in _scheduled)
            {
                var mi = _cgType!.GetMethod(FnName(c), BindingFlags.Static | BindingFlags.Public)!;
                _compiled[c] = (ArrayCommandChunkDelegate)Delegate.CreateDelegate(typeof(ArrayCommandChunkDelegate), mi);
            }
            _scheduled.Clear();
        }

        public override void Execute(ArrayCommandChunk chunk, double[] vs, double[] os, ref int cosi,
                                      ref bool cond)
        {
            _compiled[chunk](vs, os, ref cosi, ref cond);
            chunk.StartSourceIndices = cosi;
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
                $"public static void {fn}(double[]vs,double[]os,ref int i,ref bool cond){{");
            cb.Indent();

            for (int l = 0; l < _plan.LocalCount; l++)
                cb.AppendLine($"double l{l};");
            cb.AppendLine();

            if (ReuseLocals)
                EmitReusingBody(c, cb, depthMap, intervalIx, bind,
                                skipMap, ifStack, usedSlots);
            else
                EmitZeroReuseBody(c, cb, skipMap, ifStack, usedSlots);

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
                            cb.AppendLine($"vs[{flushSlot}] = l{local};");

                        cb.AppendLine($"l{local} = vs[{slot}];");

                        // remember first-time bindings that occur inside an IF
                        if (ifStack.Count > 0 && !ifStack.Peek().DirtyBefore[local])
                            ifStack.Peek().Initialises.Add((slot, local));

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
                        var ctx = ifStack.Pop();
                        cb.Unindent();
                        cb.AppendLine("} else {");

                        foreach (var (slot, local) in ctx.Flushes)
                            cb.AppendLine($"vs[{slot}] = l{local};");

                        foreach (var (slot, local) in ctx.Initialises)
                            cb.AppendLine($"l{local} = vs[{slot}];");

                        cb.AppendLine($"i+={ctx.SrcSkip}; }}");
                        break;
                    }

                // comments / blanks – ignore
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                case ArrayCommandType.IncrementDepth:
                case ArrayCommandType.DecrementDepth:
                    break;

                default:
                    throw new NotImplementedException($"Unhandled command {cmd.CommandType}");
            }
        }

    }
}
