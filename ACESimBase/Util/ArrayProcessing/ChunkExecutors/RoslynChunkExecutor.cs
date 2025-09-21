using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.CodeGen;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    internal sealed class RoslynChunkExecutor : ChunkExecutorBase
    {
        private readonly List<ArrayCommandChunk> _scheduled = new();
        private readonly ConcurrentDictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private readonly Dictionary<string, List<ArrayCommandChunk>> _chunksByFn = new();
        private readonly StringBuilder _sourceUnit = new();

        public bool ReuseLocals { get; init; } = true;

        public RoslynChunkExecutor(
            ArrayCommand[] commands,
            int start,
            int end,
            bool useCheckpoints,
            bool localVariableReuse = true)
            : base(commands, start, end, useCheckpoints, arrayCommandListForCheckpoints: null)
        {
            ReuseLocals = localVariableReuse;
        }

        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            if (_compiled.ContainsKey(chunk))
                return;

            string fn = FnName(chunk);
            if (!_chunksByFn.TryGetValue(fn, out var list))
            {
                list = new List<ArrayCommandChunk>(capacity: 1);
                _chunksByFn[fn] = list;
                _scheduled.Add(chunk);
            }
            list.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_scheduled.Count == 0)
                return;

            _sourceUnit.Clear();
            _sourceUnit.AppendLine("using System; namespace CG { static class G {");

            foreach (var chunk in _scheduled)
            {
                var method = new ChunkMethodBuilder(
                    commands: UnderlyingCommands,
                    chunk: chunk,
                    reuseLocals: ReuseLocals,
                    precomputedSkips: PrecomputePointerSkips(chunk));

                _sourceUnit.AppendLine(method.Build());
            }

            _sourceUnit.AppendLine("}} // CG.G");

            string code = _sourceUnit.ToString();
            if (PreserveGeneratedCode)
                GeneratedCode = code;

            var cgType = StringToCode.LoadCode(code, "CG.G");

            foreach (var rep in _scheduled)
            {
                var mi = cgType.GetMethod(FnName(rep), BindingFlags.Static | BindingFlags.Public)!;
                var del = (ArrayCommandChunkDelegate)Delegate.CreateDelegate(typeof(ArrayCommandChunkDelegate), mi);
                foreach (var c in _chunksByFn[FnName(rep)])
                    _compiled[c] = del;
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
            }
            catch (Exception ex)
            {
                var code = GeneratedCode ?? "<no generated source preserved>";
                throw new InvalidOperationException(
                    $"Error executing chunk [{chunk.StartCommandRange},{chunk.EndCommandRangeExclusive}).",
                    ex);
            }
        }

        private static string FnName(ArrayCommandChunk c) => $"S{c.StartCommandRange}_{c.EndCommandRangeExclusive - 1}";

        // ───────────────────────────────────────────────────────────────────────────
        //  ChunkMethodBuilder
        // ───────────────────────────────────────────────────────────────────────────

        private sealed class ChunkMethodBuilder
        {
            private readonly ArrayCommand[] _cmds;
            private readonly ArrayCommandChunk _chunk;
            private readonly bool _reuseLocals;
            private readonly Dictionary<int, (int src, int dst)> _skipMap;
            private readonly CodeBuilder _cb = new();

            public ChunkMethodBuilder(
                ArrayCommand[] commands,
                ArrayCommandChunk chunk,
                bool reuseLocals,
                Dictionary<int, (int src, int dst)> precomputedSkips)
            {
                _cmds = commands;
                _chunk = chunk;
                _reuseLocals = reuseLocals;
                _skipMap = precomputedSkips ?? new Dictionary<int, (int src, int dst)>();
            }

            public string Build()
            {
                var plan = _reuseLocals
                    ? LocalVariablePlanner.PlanLocals(_cmds, _chunk.StartCommandRange, _chunk.EndCommandRangeExclusive)
                    : LocalVariablePlanner.PlanNoReuse(_cmds, _chunk.StartCommandRange, _chunk.EndCommandRangeExclusive);

                var method = new CodeBuilder();
                string fn = FnName(_chunk);
                method.AppendLine($"public static void {fn}(double[] vs, double[] os, double[] od, ref int i, ref int codi, ref bool cond){{");
                _cb.Indent();

                for (int l = 0; l < plan.LocalCount; l++)
                    _cb.AppendLine($"double l{l} = 0;");

                _cb.AppendLine();

                if (_reuseLocals)
                {
                    var depth = new DepthMap(_cmds, _chunk.StartCommandRange, _chunk.EndCommandRangeExclusive);
                    var (starts, ends) = IntervalCursor.Build(plan);
                    EmitReusingBody(plan, depth, starts, ends);
                }
                else
                {
                    EmitZeroReuseBody(plan);
                }

                // Defensive close for any unterminated IFs in leaf slicing.
                CloseAnyPendingIfs(_chunk, plan);

                _cb.Unindent();
                method.AppendLine(_cb.ToString());
                method.AppendLine("}");
                return method.ToString();
            }

            private void EmitZeroReuseBody(LocalsAllocationPlan plan)
            {
                var usedSlots = CollectUsedSlots(_cmds, _chunk.StartCommandRange, _chunk.EndCommandRangeExclusive);
                foreach (var kv in plan.SlotToLocal)
                    if (usedSlots.Contains(kv.Key))
                        _cb.AppendLine($"l{kv.Value} = vs[{kv.Key}];");

                var ifStack = new Stack<IfContext>();

                for (int ci = _chunk.StartCommandRange; ci < _chunk.EndCommandRangeExclusive; ci++)
                    EmitCmdBasic(ci, plan, _cmds[ci], ifStack, bind: null);

                // Close any IFs that started in this chunk but didn’t end inside it.
                while (ifStack.Count > 0)
                {
                    var ctx = ifStack.Pop();

                    _cb.Unindent();
                    _cb.AppendLine("} else {");

                    // Reproduce ELSE‑leg effects just like the inline EndIf case.
                    ElseBlockBuilder.Emit(_cb, ctx.Flushes, ctx.Initialises);

                    _cb.AppendLine($"i += {ctx.SrcSkip}; codi += {ctx.DstSkip};");
                    _cb.AppendLine("}");
                }

                foreach (var kv in plan.SlotToLocal)
                    if (usedSlots.Contains(kv.Key))
                        _cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
            }


            private void EmitReusingBody(
                LocalsAllocationPlan plan,
                DepthMap depth,
                IntervalCursor.Event[] starts,
                IntervalCursor.Event[] ends)
            {
                var bind = new LocalBindingState(plan.LocalCount);
                var ifStack = new Stack<IfContext>();

                int sPtr = 0, ePtr = 0, sLen = starts.Length, eLen = ends.Length;

                for (int ci = _chunk.StartCommandRange; ci < _chunk.EndCommandRangeExclusive; ci++)
                {
                    int d = depth.GetDepth(ci);

                    // Start intervals at this instruction
                    while (sPtr < sLen && starts[sPtr].Cmd == ci)
                    {
                        int slot = starts[sPtr].Slot;
                        sPtr++;

                        if (!plan.SlotToLocal.TryGetValue(slot, out int local))
                            continue;

                        if (bind.TryReuse(local, slot, d, out int flushSlot))
                        {
                            if (flushSlot != -1)
                            {
                                _cb.AppendLine($"vs[{flushSlot}] = l{local};");
                                if (ifStack.Count > 0)
                                    foreach (var ctx in ifStack)
                                        ctx.Flushes.Add((flushSlot, local));
                            }

                            _cb.AppendLine($"l{local} = vs[{slot}];");
                            if (ifStack.Count > 0)
                                foreach (var ctx in ifStack)
                                    ctx.Initialises.Add((slot, local));

                            bind.StartInterval(slot, local, d);
                        }
                    }

                    // Emit this command
                    EmitCmdBasic(ci, plan, _cmds[ci], ifStack, bind);

                    // End intervals at this instruction
                    while (ePtr < eLen && ends[ePtr].Cmd == ci)
                    {
                        int endSlot = ends[ePtr].Slot;
                        ePtr++;

                        if (!plan.SlotToLocal.TryGetValue(endSlot, out int local))
                            continue;

                        if (bind.NeedsFlushBeforeReuse(local, out int boundSlot) && boundSlot == endSlot)
                        {
                            _cb.AppendLine($"vs[{endSlot}] = l{local};");
                            if (ifStack.Count > 0)
                                foreach (var ctx in ifStack)
                                    if (ctx.DirtyBefore.Length > local && ctx.DirtyBefore[local])
                                        ctx.Flushes.Add((endSlot, local));
                            bind.FlushLocal(local);
                        }

                        bind.Release(local);
                    }
                }

                // Final flush for any locals still live at chunk end
                for (int local = 0; local < plan.LocalCount; local++)
                {
                    if (bind.NeedsFlushBeforeReuse(local, out int slot) && slot != -1)
                    {
                        _cb.AppendLine($"vs[{slot}] = l{local};");
                        bind.FlushLocal(local);
                    }
                }

                // Close any IFs that started in this chunk but didn’t end inside it.
                while (ifStack.Count > 0)
                {
                    var ctx = ifStack.Pop();

                    _cb.Unindent();
                    _cb.AppendLine("} else {");

                    // Reproduce ELSE‑leg effects just like the inline EndIf case.
                    ElseBlockBuilder.Emit(_cb, ctx.Flushes, ctx.Initialises);

                    _cb.AppendLine($"i += {ctx.SrcSkip}; codi += {ctx.DstSkip};");
                    _cb.AppendLine("}");

                    // Mirror the inline EndIf post‑else flush for locals initialised in the IF
                    foreach (var (slot, local) in ctx.Initialises)
                    {
                        if (bind.NeedsFlushBeforeReuse(local, out int boundSlot) && boundSlot == slot)
                        {
                            _cb.AppendLine($"vs[{slot}] = l{local};");
                            bind.FlushLocal(local);
                        }
                    }
                }
            }


            private void CloseAnyPendingIfs(ArrayCommandChunk c, LocalsAllocationPlan plan)
            {
                // No-op here; pending-IF closure is handled inline during emission
                // via the EndIf cases plus leaf slicing guarantees.
            }

            private void EmitCmdBasic(
                int cmdIndex,
                LocalsAllocationPlan plan,
                ArrayCommand cmd,
                Stack<IfContext> ifStack,
                LocalBindingState bind)
            {
                string R(int slot) => plan.SlotToLocal.TryGetValue(slot, out int l) ? $"l{l}" : $"vs[{slot}]";
                string W(int slot) => R(slot);

                void MarkWritten(int slot)
                {
                    if (bind != null && plan.SlotToLocal.TryGetValue(slot, out int localId))
                        bind.MarkWritten(localId);
                }

                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        _cb.AppendLine($"{W(cmd.Index)} = 0;");
                        MarkWritten(cmd.Index);
                        break;

                    case ArrayCommandType.CopyTo:
                        _cb.AppendLine($"{W(cmd.Index)} = {R(cmd.SourceIndex)};");
                        MarkWritten(cmd.Index);
                        break;

                    case ArrayCommandType.NextSource:
                        if (plan.SlotToLocal.TryGetValue(cmd.Index, out int loc))
                        {
                            _cb.AppendLine($"l{loc} = os[i++];");
                            _cb.AppendLine($"vs[{cmd.Index}] = l{loc};");
                        }
                        else
                        {
                            _cb.AppendLine($"vs[{cmd.Index}] = os[i++];");
                        }
                        MarkWritten(cmd.Index);
                        break;


                    case ArrayCommandType.NextDestination:
                        _cb.AppendLine($"od[codi++] += {R(cmd.SourceIndex)};");
                        break;

                    case ArrayCommandType.MultiplyBy:
                        _cb.AppendLine($"{W(cmd.Index)} *= {R(cmd.SourceIndex)};");
                        MarkWritten(cmd.Index);
                        break;

                    case ArrayCommandType.IncrementBy:
                    {
                        bool local = plan.SlotToLocal.ContainsKey(cmd.Index);
                        string lhs = W(cmd.Index);
                        string rhs = R(cmd.SourceIndex);
                        if (local) _cb.AppendLine($"{lhs} += {rhs};");
                        else       _cb.AppendLine($"{lhs} = {lhs} + {rhs};");
                        MarkWritten(cmd.Index);
                        break;
                    }

                    case ArrayCommandType.DecrementBy:
                    {
                        bool local = plan.SlotToLocal.ContainsKey(cmd.Index);
                        string lhs = W(cmd.Index);
                        string rhs = R(cmd.SourceIndex);
                        if (local) _cb.AppendLine($"{lhs} -= {rhs};");
                        else       _cb.AppendLine($"{lhs} = {lhs} - {rhs};");
                        MarkWritten(cmd.Index);
                        break;
                    }

                    case ArrayCommandType.EqualsOtherArrayIndex:
                        _cb.AppendLine($"cond = {R(cmd.Index)} == {R(cmd.SourceIndex)};");
                        break;
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                        _cb.AppendLine($"cond = {R(cmd.Index)} != {R(cmd.SourceIndex)};");
                        break;
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                        _cb.AppendLine($"cond = {R(cmd.Index)} > {R(cmd.SourceIndex)};");
                        break;
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        _cb.AppendLine($"cond = {R(cmd.Index)} < {R(cmd.SourceIndex)};");
                        break;
                    case ArrayCommandType.EqualsValue:
                        _cb.AppendLine($"cond = {R(cmd.Index)} == (double){cmd.SourceIndex};");
                        break;
                    case ArrayCommandType.NotEqualsValue:
                        _cb.AppendLine($"cond = {R(cmd.Index)} != (double){cmd.SourceIndex};");
                        break;

                    case ArrayCommandType.If:
                    {
                        var sk = _skipMap.TryGetValue(cmdIndex, out var tup) ? tup : (src: 0, dst: 0);
                        var dirty = new bool[bind is null ? 0 : plan.LocalCount];
                        if (bind != null)
                            for (int l = 0; l < plan.LocalCount; l++)
                                dirty[l] = bind.NeedsFlushBeforeReuse(l, out _);

                        ifStack.Push(new IfContext(sk.src, sk.dst, dirty));
                        _cb.AppendLine("if (cond) {"); _cb.Indent();
                        break;
                    }

                    case ArrayCommandType.EndIf:
                    {
                        if (ifStack.Count == 0) break;
                        var ctx = ifStack.Pop();

                        _cb.Unindent();
                        _cb.AppendLine("} else {");

                        ElseBlockBuilder.Emit(_cb, ctx.Flushes, ctx.Initialises);

                        _cb.AppendLine($"i += {ctx.SrcSkip}; codi += {ctx.DstSkip};");
                        _cb.AppendLine("}");

                        foreach (var (slot, local) in ctx.Initialises)
                        {
                            if (bind != null &&
                                bind.NeedsFlushBeforeReuse(local, out int boundSlot) &&
                                boundSlot == slot)
                            {
                                _cb.AppendLine($"vs[{slot}] = l{local};");
                                bind.FlushLocal(local);
                            }
                        }
                        break;
                    }


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

            private static HashSet<int> CollectUsedSlots(ArrayCommand[] cmds, int start, int end)
            {
                var set = new HashSet<int>();
                for (int i = start; i < end; i++)
                {
                    var c = cmds[i];
                    if (c.Index >= 0) set.Add(c.Index);
                    if (c.SourceIndex >= 0) set.Add(c.SourceIndex);
                }
                return set;
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        //  IntervalCursor
        // ───────────────────────────────────────────────────────────────────────────

        private static class IntervalCursor
        {
            internal readonly struct Event
            {
                public readonly int Cmd;
                public readonly int Slot;
                public Event(int cmd, int slot) { Cmd = cmd; Slot = slot; }
            }

            public static (Event[] starts, Event[] ends) Build(LocalsAllocationPlan plan)
            {
                int n = plan.Intervals.Count;
                var starts = new Event[n];
                var ends   = new Event[n];

                int i = 0;
                foreach (var iv in plan.Intervals)
                {
                    starts[i] = new Event(iv.First, iv.Slot);
                    ends[i]   = new Event(iv.Last,  iv.Slot);
                    i++;
                }

                Array.Sort(starts, (a, b) => a.Cmd.CompareTo(b.Cmd));
                Array.Sort(ends,   (a, b) => a.Cmd.CompareTo(b.Cmd));
                return (starts, ends);
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        //  IfContext & ElseBlockBuilder
        // ───────────────────────────────────────────────────────────────────────────

        private sealed class IfContext
        {
            public readonly int SrcSkip;
            public readonly int DstSkip;
            public readonly bool[] DirtyBefore;
            public readonly List<(int slot, int local)> Flushes = new();
            public readonly List<(int slot, int local)> Initialises = new();

            public IfContext(int srcSkip, int dstSkip, bool[] dirtyBefore)
            {
                SrcSkip = srcSkip;
                DstSkip = dstSkip;
                DirtyBefore = dirtyBefore ?? Array.Empty<bool>();
            }
        }

        private static class ElseBlockBuilder
        {
            public static void Emit(
                CodeBuilder cb,
                IEnumerable<(int slot, int local)> flushes,
                IEnumerable<(int slot, int local)> initializes)
            {
                var byLocal = new Dictionary<int, (int? initSlot, List<int> flushSlots)>();

                foreach (var (slot, local) in initializes)
                {
                    if (!byLocal.ContainsKey(local))
                        byLocal[local] = (initSlot: slot, flushSlots: new List<int>());
                    else
                        byLocal[local] = (slot, byLocal[local].flushSlots);
                }

                foreach (var (slot, local) in flushes)
                {
                    if (!byLocal.ContainsKey(local))
                        byLocal[local] = (initSlot: (int?)null, flushSlots: new List<int>());
                    byLocal[local].flushSlots.Add(slot);
                }

                foreach (int local in byLocal.Keys.OrderBy(l => l))
                {
                    int? initSlot = byLocal[local].initSlot;
                    var flushSlots = byLocal[local].flushSlots;

                    bool hasInit = initSlot.HasValue;
                    bool hasFlush = flushSlots.Count > 0;

                    if (hasInit && hasFlush)
                    {
                        int initTarget = initSlot.Value;
                        int? sameSlot = flushSlots.Contains(initTarget) ? initTarget : (int?)null;
                        var otherFlushes = flushSlots.Where(s => s != initTarget);

                        foreach (int slot in otherFlushes)
                            cb.AppendLine($"vs[{slot}] = l{local};");

                        if (sameSlot.HasValue)
                        {
                            cb.AppendLine($"l{local} = vs[{initTarget}];");
                            cb.AppendLine($"vs[{initTarget}] = l{local};");
                        }
                        else
                        {
                            cb.AppendLine($"l{local} = vs[{initTarget}];");
                        }
                    }
                    else if (hasFlush && !hasInit)
                    {
                        foreach (int slot in flushSlots)
                            cb.AppendLine($"vs[{slot}] = l{local};");
                    }
                    else if (hasInit && !hasFlush)
                    {
                        cb.AppendLine($"l{local} = vs[{initSlot.Value}];");
                    }
                }
            }
        }
    }
}
