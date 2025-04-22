// RoslynChunkExecutor.cs – generated‑code executor

using System;
using System.Collections.Generic;
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

        public RoslynChunkExecutor(ArrayCommand[] commands, int start, int end,
                                   bool useCheckpoints, bool localVariableReuse = true)
            : base(commands, start, end)
        {
            _useCheckpoints = useCheckpoints;
            ReuseLocals = localVariableReuse;
            _plan = LocalVariablePlanner.PlanLocals(commands, start, end,
                                                    minUses: ReuseLocals ? 3 : 1);
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
            var depthMap = new DepthMap(Commands.ToArray(), c.StartCommandRange, c.EndCommandRangeExclusive);
            var intervalIdx = new IntervalIndex(_plan);
            var bind = new LocalBindingState(_plan.LocalCount);
            var cb = new CodeBuilder();

            var skipMap = PrecomputePointerSkips(c);
            var ifStack = new Stack<(int src, int dst)>();

            string fn = FnName(c);
            _src.AppendLine($"public static void {fn}(double[]vs,double[]os,double[]od,ref int i,ref int o,ref bool cond){{");
            cb.Indent();

            for (int l = 0; l < _plan.LocalCount; l++)
                cb.AppendLine($"double l{l};");
            cb.AppendLine();

            if (ReuseLocals)
                EmitReusingBody(c, cb, depthMap, intervalIdx, bind, skipMap, ifStack);
            else
                EmitZeroReuseBody(c, cb, skipMap, ifStack);

            cb.Unindent();
            _src.AppendLine(cb.ToString());
            _src.AppendLine("}");
        }

        private void EmitZeroReuseBody(ArrayCommandChunk c, CodeBuilder cb,
                                        Dictionary<int, (int src, int dst)> skipMap,
                                        Stack<(int src, int dst)> ifStack)
        {
            foreach (var kv in _plan.SlotToLocal)
                cb.AppendLine($"l{kv.Value} = vs[{kv.Key}];");

            Span<ArrayCommand> cmds = Commands;
            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
                EmitCmdBasic(ci, cmds[ci], cb, skipMap, ifStack);

            foreach (var kv in _plan.SlotToLocal)
                cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
        }

        private void EmitReusingBody(ArrayCommandChunk c, CodeBuilder cb, DepthMap depth,
                                      IntervalIndex intervalIdx, LocalBindingState bind,
                                      Dictionary<int, (int src, int dst)> skipMap,
                                      Stack<(int src, int dst)> ifStack)
        {
            Span<ArrayCommand> cmds = Commands;
            for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
            {
                int d = depth.GetDepth(ci);
                if (intervalIdx.TryStart(ci, out int slot))
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

                if (intervalIdx.TryEnd(ci, out int endSlot))
                {
                    int local = _plan.SlotToLocal[endSlot];
                    bind.Release(local);
                }
            }
            foreach (var kv in _plan.SlotToLocal)
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

        private Dictionary<int, (int src, int dst)> PrecomputePointerSkips(ArrayCommandChunk c)
        {
            var result = new Dictionary<int, (int src, int dst)>();
            var stack = new Stack<int>();
            var cmds = Commands;

            for (int i = c.StartCommandRange; i < c.EndCommandRangeExclusive; i++)
            {
                switch (cmds[i].CommandType)
                {
                    case ArrayCommandType.If:
                        stack.Push(i);
                        result[i] = (0, 0);
                        break;
                    case ArrayCommandType.EndIf:
                        stack.Pop();
                        break;
                    case ArrayCommandType.NextSource:
                        if (stack.Count > 0)
                        {
                            int ifIdx = stack.Peek();
                            var cur = result[ifIdx];
                            result[ifIdx] = (cur.src + 1, cur.dst);
                        }
                        break;
                    case ArrayCommandType.NextDestination:
                        if (stack.Count > 0)
                        {
                            int ifIdx = stack.Peek();
                            var cur = result[ifIdx];
                            result[ifIdx] = (cur.src, cur.dst + 1);
                        }
                        break;
                }
            }
            return result;
        }
    }
}
