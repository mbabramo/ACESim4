// RoslynChunkExecutor.cs – generated‑code executor

using System;
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
        private readonly bool _useCheckpoints;
        private readonly LocalsAllocationPlan _plan;
        private readonly List<ArrayCommandChunk> _scheduled = new();
        private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
        private readonly StringBuilder _src = new();
        private Type _cgType;

        public bool ReuseLocals { get; init; } = true;

        public RoslynChunkExecutor(ArrayCommand[] commands,
                           int start, int end,
                           bool useCheckpoints,
                           bool localVariableReuse = true)
    : base(commands, start, end)
        {
            _useCheckpoints = useCheckpoints;
            ReuseLocals = localVariableReuse;

            _plan = ReuseLocals
                ? LocalVariablePlanner.PlanLocals(commands, start, end)
                : BuildNoReusePlan(commands, start, end);
        }

        private static LocalsAllocationPlan BuildNoReusePlan(ArrayCommand[] cmds, int start, int end)
        {
            static IEnumerable<int> GetSlots(ArrayCommand c)
            {
                // ----- write slots -----
                switch (c.CommandType)
                {
                    // commands that *only* write to c.Index
                    case ArrayCommandType.Zero:
                    case ArrayCommandType.NextSource:

                    // read‑write commands – they still write to c.Index
                    case ArrayCommandType.CopyTo:
                    case ArrayCommandType.MultiplyBy:
                    case ArrayCommandType.IncrementBy:
                    case ArrayCommandType.DecrementBy:
                        if (c.Index >= 0)
                            yield return c.Index;
                        break;

                    // all other commands do not write to a VS slot
                    default:
                        break;
                }

                // ----- read slots -----
                switch (c.CommandType)
                {
                    case ArrayCommandType.CopyTo:
                    case ArrayCommandType.NextDestination:
                    case ArrayCommandType.ReusedDestination:
                    case ArrayCommandType.MultiplyBy:
                    case ArrayCommandType.IncrementBy:
                    case ArrayCommandType.DecrementBy:
                    case ArrayCommandType.EqualsOtherArrayIndex:
                    case ArrayCommandType.NotEqualsOtherArrayIndex:
                    case ArrayCommandType.GreaterThanOtherArrayIndex:
                    case ArrayCommandType.LessThanOtherArrayIndex:
                        if (c.SourceIndex >= 0) yield return c.SourceIndex;
                        break;

                        // EqualsValue / NotEqualsValue use SourceIndex as a *constant* → nothing to add
                }
            }

            var plan = new LocalsAllocationPlan();
            var slots = new HashSet<int>();

            for (int i = start; i < end; i++)
                foreach (int s in GetSlots(cmds[i]))
                    slots.Add(s);

            int local = 0;
            foreach (int slot in slots.OrderBy(s => s))
                plan.AddInterval(slot, first: start, last: end - 1, bindDepth: 0, local: local++);

            return plan;
        }




        public override void AddToGeneration(ArrayCommandChunk chunk)
        {
            if (!_compiled.ContainsKey(chunk))
                _scheduled.Add(chunk);
        }

        public override void PerformGeneration()
        {
            if (_scheduled.Count == 0) return;

            _src.Clear();
            _src.AppendLine("using System; namespace CG { static class G {");

            foreach (var chunk in _scheduled)
                GenerateSourceForChunk(chunk);

            _src.AppendLine("}} // CG.G");

            _cgType = StringToCode.LoadCode(_src.ToString(), "CG.G");
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
            chunk.StartSourceIndices += cosi;
            chunk.StartDestinationIndices += codi;
        }

        private static string FnName(ArrayCommandChunk c)
            => $"S{c.StartCommandRange}_{c.EndCommandRangeExclusive - 1}";

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
            var ifStack = new Stack<(int src, int dst)>();

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
    Stack<(int src, int dst)> ifStack,
    HashSet<int> usedSlots)
        {
            foreach (var kv in _plan.SlotToLocal)
                if (usedSlots.Contains(kv.Key))
                    cb.AppendLine($"l{kv.Value} = vs[{kv.Key}];");

            Span<ArrayCommand> cmds = Commands;
            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
                EmitCmdBasic(ci, cmds[ci], cb, skipMap, ifStack);

            foreach (var kv in _plan.SlotToLocal)
                if (usedSlots.Contains(kv.Key))
                    cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
        }



        private void EmitReusingBody(
    ArrayCommandChunk c,
    CodeBuilder cb,
    DepthMap depth,
    IntervalIndex intervalIx,
    LocalBindingState bind,
    Dictionary<int, (int src, int dst)> skipMap,
    Stack<(int src, int dst)> ifStack,
    HashSet<int> usedSlots)
        {
            Span<ArrayCommand> cmds = Commands;
            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
            {
                int d = depth.GetDepth(ci);

                if (intervalIx.TryStart(ci, out int slot))
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

                EmitCmdBasic(ci, cmds[ci], cb, skipMap, ifStack);

                if (intervalIx.TryEnd(ci, out int endSlot))
                {
                    int local = _plan.SlotToLocal[endSlot];
                    bind.Release(local);
                }
            }

            foreach (var kv in _plan.SlotToLocal)
                if (usedSlots.Contains(kv.Key))
                    cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
        }



        private void EmitCmdBasic(int cmdIndex, ArrayCommand cmd, CodeBuilder cb,
                                   Dictionary<int, (int src, int dst)> skipMap,
                                   Stack<(int src, int dst)> ifStack)
        {
            string R(int slot) => _plan.SlotToLocal.TryGetValue(slot, out int l) ? $"l{l}" : $"vs[{slot}]";
            string W(int slot) => R(slot);

            switch (cmd.CommandType)
            {
                case ArrayCommandType.Zero: cb.AppendLine($"{W(cmd.Index)} = 0;"); break;
                case ArrayCommandType.CopyTo: cb.AppendLine($"{W(cmd.Index)} = {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.MultiplyBy: cb.AppendLine($"{W(cmd.Index)} *= {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.IncrementBy: cb.AppendLine($"{W(cmd.Index)} += {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.DecrementBy: cb.AppendLine($"{W(cmd.Index)} -= {R(cmd.SourceIndex)};"); break;

                case ArrayCommandType.NextSource: cb.AppendLine($"{W(cmd.Index)} = os[i++];"); break;
                case ArrayCommandType.NextDestination: cb.AppendLine($"od[o++] = {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.ReusedDestination: cb.AppendLine($"od[{cmd.Index}] += {R(cmd.SourceIndex)};"); break;

                case ArrayCommandType.EqualsOtherArrayIndex: cb.AppendLine($"cond = {R(cmd.Index)} == {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.NotEqualsOtherArrayIndex: cb.AppendLine($"cond = {R(cmd.Index)} != {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.GreaterThanOtherArrayIndex: cb.AppendLine($"cond = {R(cmd.Index)} > {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.LessThanOtherArrayIndex: cb.AppendLine($"cond = {R(cmd.Index)} < {R(cmd.SourceIndex)};"); break;
                case ArrayCommandType.EqualsValue: cb.AppendLine($"cond = {R(cmd.Index)} == (double){cmd.SourceIndex};"); break;
                case ArrayCommandType.NotEqualsValue: cb.AppendLine($"cond = {R(cmd.Index)} != (double){cmd.SourceIndex};"); break;

                case ArrayCommandType.If:
                    ifStack.Push(skipMap.TryGetValue(cmdIndex, out var c) ? c : (0, 0));
                    cb.AppendLine("if(cond){");
                    cb.Indent();
                    break;
                case ArrayCommandType.EndIf:
                    var counts = ifStack.Pop();
                    cb.Unindent();
                    cb.AppendLine($"}} else {{ i+={counts.src}; o+={counts.dst}; }}");
                    break;

                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    break;
                default:
                    throw new NotImplementedException($"Unhandled command {cmd.CommandType}");
            }
        }
        private Dictionary<int, (int src, int dst)> PrecomputePointerSkips(
    ArrayCommandChunk c)
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
