// ------------------------------------------------------------------------------------
//  RoslynChunkExecutor.cs  –  simplified & dual‑mode (ReuseLocals flag)
// ------------------------------------------------------------------------------------

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

using ACESimBase.Util.ArrayProcessing;           // ArrayCommandChunk
using ACESimBase.Util.ArrayProcessing.ChunkExecutors; // helpers + plan
using ACESimBase.Util.CodeGen;

/// <summary>
/// Emits C# code for a command‑chunk using Roslyn.  Two modes:
///   • <see cref="ReuseLocals"/> = true  → aggressive reuse (dirty‑bit, flush‑before‑reuse)
///   • false → each slot keeps a unique local for the whole chunk (simpler, slower).
/// </summary>
internal sealed class RoslynChunkExecutor : ChunkExecutorBase
{
    // ---------------------------------------------------------------- fields
    private readonly bool _useCheckpoints;
    private readonly LocalsAllocationPlan _plan;
    private readonly List<ArrayCommandChunk> _scheduled = new();
    private readonly Dictionary<ArrayCommandChunk, ArrayCommandChunkDelegate> _compiled = new();
    private readonly StringBuilder _src = new();
    private Type _cgType;

    public bool ReuseLocals { get; init; } = true;

    // -------------------------------------------------------------- ctor
    public RoslynChunkExecutor(
        ArrayCommand[] commands,
        int start,
        int end,
        bool useCheckpoints,
        bool localVariableReuse = true)
        : base(commands, start, end)
    {
        _useCheckpoints = useCheckpoints;
        ReuseLocals = localVariableReuse;
        if (ReuseLocals)
            _plan = LocalVariablePlanner.PlanLocals(commands, start, end);
    }

    // -------------------------------------------------------------- IChunkExecutor
    public override void AddToGeneration(ArrayCommandChunk chunk)
    {
        if (!_compiled.ContainsKey(chunk))
            _scheduled.Add(chunk);
    }

    public override void PerformGeneration()
    {
        if (_scheduled.Count == 0) return;

        _src.Clear();
        _src.AppendLine("using System; using System.Collections.Generic; namespace CG { static class G {");

        foreach (var chunk in _scheduled)
            GenerateSourceForChunk(chunk);

        _src.AppendLine("}} // class+ns");

        _cgType = StringToCode.LoadCode(_src.ToString(), "CG.G");
        foreach (var c in _scheduled)
        {
            string fn = FnName(c);
            var mi = _cgType!.GetMethod(fn, BindingFlags.Static | BindingFlags.Public)!;
            _compiled[c] = (ArrayCommandChunkDelegate)Delegate.CreateDelegate(typeof(ArrayCommandChunkDelegate), mi);
        }
        _scheduled.Clear();
    }

    public override void Execute(ArrayCommandChunk chunk, double[] vs, double[] os, double[] od,
                                 ref int cosi, ref int codi, ref bool cond)
    {
        _compiled[chunk](vs, os, od, ref cosi, ref codi, ref cond);
    }

    // -------------------------------------------------------------- helpers
    private static string FnName(ArrayCommandChunk c) => $"S{c.StartCommandRange}_{c.EndCommandRangeExclusive - 1}";

    private void GenerateSourceForChunk(ArrayCommandChunk c)
    {
        var depth = new DepthMap(Commands.ToArray(), c.StartCommandRange, c.EndCommandRangeExclusive);
        var idx = new IntervalIndex(_plan);
        var bind = new LocalBindingState(_plan.LocalCount);
        var cb = new CodeBuilder();

        string fn = FnName(c);
        _src.AppendLine($"public static void {fn}(double[]vs,double[]os,double[]od,ref int i,ref int o,ref bool cond){{");
        cb.Indent();

        // --- declare locals --------------------------------------------
        for (int l = 0; l < _plan.LocalCount; l++)
            cb.AppendLine($"double l{l};");
        cb.AppendLine();

        if (ReuseLocals)
            EmitReusingBody(c, cb, depth, idx, bind);
        else
            EmitZeroReuseBody(c, cb);

        cb.Unindent();
        _src.AppendLine(cb.ToString());
        _src.AppendLine("}");
    }

    private void EmitZeroReuseBody(ArrayCommandChunk c, CodeBuilder cb)
    {
        // initialise each slot‑local once
        foreach (var kv in _plan.SlotToLocal)
            cb.AppendLine($"l{kv.Value} = vs[{kv.Key}];");

        Span<ArrayCommand> cmds = Commands;
        for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
        {
            var cmd = cmds[ci];
            EmitCmdBasic(cmd, cb);
        }
        // epilogue flush
        foreach (var kv in _plan.SlotToLocal)
            cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
    }

    private void EmitReusingBody(ArrayCommandChunk c, CodeBuilder cb, DepthMap depth,
                                 IntervalIndex idx, LocalBindingState bind)
    {
        Span<ArrayCommand> cmds = Commands;
        for (int ci = c.StartCommandRange; ci < c.EndCommandRangeExclusive; ci++)
        {
            int d = depth.GetDepth(ci);
            if (idx.TryStart(ci, out int slot))
            {
                int local = _plan.SlotToLocal[slot];
                if (bind.TryReuse(local, slot, d, out int flushSlot))
                {
                    if (flushSlot != -1)
                        cb.AppendLine($"vs[{flushSlot}] = l{local};");
                    cb.AppendLine($"l{local} = vs[{slot}];");
                    bind.StartInterval(slot, local, d);
                }
                else
                {
                    // allocate is impossible here because plan guaranteed capacity
                }
            }

            EmitCmdBasic(cmds[ci], cb);

            if (idx.TryEnd(ci, out int endSlot))
            {
                int local = _plan.SlotToLocal[endSlot];
                bind.Release(local);
            }
        }
        // flush all locals once
        foreach (var kv in _plan.SlotToLocal)
            cb.AppendLine($"vs[{kv.Key}] = l{kv.Value};");
    }

    private void EmitCmdBasic(ArrayCommand cmd, CodeBuilder cb)
    {
        string R(int slot) => _plan.SlotToLocal.TryGetValue(slot, out int l) ? $"l{l}" : $"vs[{slot}]";
        string W(int slot) => R(slot);
        switch (cmd.CommandType)
        {
            case ArrayCommandType.Zero:
                cb.AppendLine($"{W(cmd.Index)} = 0;");
                break;
            case ArrayCommandType.CopyTo:
                cb.AppendLine($"{W(cmd.Index)} = {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.MultiplyBy:
                cb.AppendLine($"{W(cmd.Index)} *= {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.IncrementBy:
                cb.AppendLine($"{W(cmd.Index)} += {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.DecrementBy:
                cb.AppendLine($"{W(cmd.Index)} -= {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.NextSource:
                cb.AppendLine($"{W(cmd.Index)} = os[i++];");
                break;
            case ArrayCommandType.NextDestination:
                cb.AppendLine($"od[o++] = {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.ReusedDestination:
                cb.AppendLine($"od[{cmd.Index}] += {R(cmd.SourceIndex)};");
                break;
            case ArrayCommandType.If:
                cb.AppendLine("if(cond){"); cb.Indent();
                break;
            case ArrayCommandType.EndIf:
                cb.Unindent(); cb.AppendLine("}");
                break;
            case ArrayCommandType.EqualsValue:
                cb.AppendLine($"cond = {R(cmd.Index)} == (double){cmd.SourceIndex};");
                break;
            case ArrayCommandType.NotEqualsValue:
                cb.AppendLine($"cond = {R(cmd.Index)} != (double){cmd.SourceIndex};");
                break;
            default:
                break;
        }
    }
}
