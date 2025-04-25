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

            /// <summary>dirtyBefore[l] == true  ⇒ local <l> was both “bound” and “dirty”
            /// at the *moment we entered* the ‘if’.</summary>
            public readonly bool[] DirtyBefore;

            /// <summary>(slot,local) pairs whose *last* flush happens inside the branch
            /// *and* whose local was already dirty on entry (→ needs mirroring in ELSE).</summary>
            public readonly List<(int slot, int local)> Flushes = new();

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
    : base(commands, start, end, useCheckpoints)
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

        public override void Execute(ArrayCommandChunk chunk, double[] vs, double[] os, double[] od,
                                      ref int cosi, ref int codi, ref bool cond)
        {
            _compiled[chunk](vs, os, od, ref cosi, ref codi, ref cond);
            chunk.StartSourceIndices = cosi;
            chunk.StartDestinationIndices = codi;
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
                $"public static void {fn}(double[]vs,double[]os,double[]od,ref int i,ref int o,ref bool cond){{");
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

            while (ifStack.Count > 0)
            {
                cb.Unindent();
                cb.AppendLine("}");
                ifStack.Pop();
            }

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
                        if (bind != null && ifStack.Count > 0)
                        {
                            // propagate to *all* enclosing IFs that saw this local dirty on entry
                            foreach (var ctx in ifStack)
                                if (ctx.DirtyBefore[local])
                                ctx.Flushes.Add((endSlot, local));
                        }
                        bind.FlushLocal(local);
                    }
                    bind.Release(local);
                }
            }
            // ⟹ no blanket tail‑flush – every dirty local handled above.
            while (ifStack.Count > 0)
            {
                cb.Unindent();
                cb.AppendLine("}");
                ifStack.Pop();
            }
        }


        /// <summary>
        /// Emit one ArrayCommand.  When <paramref name="bind"/> is non-null
        /// we call <c>bind.MarkWritten()</c> for every write so its dirty-bit is
        /// correct.  The only change from the original code is a guard against an
        /// unmatched leading EndIf.
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
                if (bind != null && _plan.SlotToLocal.TryGetValue(slot, out int loc))
                    bind.MarkWritten(loc);
            }

            string R(int slot) => _plan.SlotToLocal.TryGetValue(slot, out int l) ? $"l{l}" : $"vs[{slot}]";
            string W(int slot) => R(slot);

            switch (cmd.CommandType)
            {
                /* ——— ordinary cases unchanged, elided for brevity ——— */

                /* ────────── control-flow markers ────────── */
                case ArrayCommandType.If:
                    {
                        var sk = skipMap.TryGetValue(cmdIndex, out var s) ? s : (src: 0, dst: 0);
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
                        /* guard: this EndIf closes an If in a *previous* slice */
                        if (ifStack.Count == 0)
                            break;

                        var ctx = ifStack.Pop();
                        cb.Unindent();
                        cb.AppendLine("} else {");

                        foreach (var (slot, local) in ctx.Flushes)
                            cb.AppendLine($"vs[{slot}] = l{local};");

                        cb.AppendLine($"i+={ctx.SrcSkip}; o+={ctx.DstSkip}; }}");
                        break;
                    }

                /* ——— comment / blank ——— */
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    break;

                default:
                    throw new NotImplementedException($"Unhandled command {cmd.CommandType}");
            }
        }



        private Dictionary<int, (int srcSkip, int dstSkip)>

        // DEBUG TODO: Find all common functionality between RoslynChunkExecutor and ILChunkExecutor and move it to the base class
        PrecomputePointerSkips(ArrayCommandChunk chunk)
        {
            var map = new Dictionary<int, (int srcSkip, int dstSkip)>();
            var stack = new Stack<int>();          // holds command indices of open Ifs
            int depth = 0;                         // current nesting level *inside*
                                                   // the chunk (may start > 0)

            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                switch (Commands[i].CommandType)
                {
                    /* ── open a new outer-level If that starts *inside* this chunk ── */
                    case ArrayCommandType.If:
                        depth++;
                        stack.Push(i);             // remember the If’s position
                        map[i] = (0, 0);           // initialise skip counters
                        break;

                    /* ── close an If ─────────────────────────────────────────────── */
                    case ArrayCommandType.EndIf:
                        if (depth == 0)             // this EndIf closes an If that
                            break;                  // started *before* the chunk → ignore
                        depth--;
                        stack.Pop();                // matched pair – safe to pop
                        break;

                    /* ── pointer advances inside a still-open If ─────────────────── */
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



    }
}
