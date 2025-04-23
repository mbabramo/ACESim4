// -----------------------------------------------------------------------------
//  ChunkExecutorHelpers.cs
//  Helper types shared by RoslynChunkExecutor and ILChunkEmitter.
// -----------------------------------------------------------------------------

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors;

using System;
using System.Collections.Generic;

/// <summary>
/// Computes control‑flow depth for every command index.
/// Depth 0 = top‑level method body; increases inside each If..EndIf pair.
/// </summary>
internal sealed class DepthMap
{
    private readonly int[] _depthAt;
    private readonly int _offset;          // start index supplied by caller

    public DepthMap(ArrayCommand[] commands, int start, int end)
    {
        _offset = start;
        _depthAt = new int[end - start];

        int depth = 0;
        for (int i = start, j = 0; i < end; i++, j++)
        {
            _depthAt[j] = depth;
            switch (commands[i].CommandType)
            {
                case ArrayCommandType.If: depth++; break;
                case ArrayCommandType.EndIf: depth = Math.Max(0, depth - 1); break;
            }
        }
    }

    public int GetDepth(int commandIndex) => _depthAt[commandIndex - _offset];
}

/// <summary>
/// Fast look‑ups for “interval starts/ends at this command?”
/// Supports *multiple* VS slots beginning or ending on the same instruction.
/// </summary>
internal sealed class IntervalIndex
{
    private readonly Dictionary<int, List<int>> _first;
    private readonly Dictionary<int, List<int>> _last;

    public IntervalIndex(int capacity = 16)
    {
        _first = new Dictionary<int, List<int>>(capacity);
        _last = new Dictionary<int, List<int>>(capacity);
    }

    public IntervalIndex(LocalsAllocationPlan plan)
    {
        _first = new();
        _last = new();

        foreach (var iv in plan.Intervals)
        {
            (_first.TryGetValue(iv.First, out var fl) ? fl : _first[iv.First] = new())
                .Add(iv.Slot);

            (_last.TryGetValue(iv.Last, out var ll) ? ll : _last[iv.Last] = new())
                .Add(iv.Slot);
        }
    }

    public void AddStart(int cmdIndex, int slot) =>
        (_first.TryGetValue(cmdIndex, out var list) ? list : _first[cmdIndex] = new())
            .Add(slot);

    public void AddEnd(int cmdIndex, int slot) =>
        (_last.TryGetValue(cmdIndex, out var list) ? list : _last[cmdIndex] = new())
            .Add(slot);

    /// <summary>All VS slots whose interval *starts* at <paramref name="cmd"/>.</summary>
    public IEnumerable<int> StartSlots(int cmd) =>
        _first.TryGetValue(cmd, out var list) ? list : Array.Empty<int>();

    /// <summary>All VS slots whose interval *ends*   at <paramref name="cmd"/>.</summary>
    public IEnumerable<int> EndSlots(int cmd) =>
        _last.TryGetValue(cmd, out var list) ? list : Array.Empty<int>();

    /* --------------------------------------------------------------------
       Legacy helpers (still used elsewhere): return the *first* slot only.
       ------------------------------------------------------------------*/
    public bool TryStart(int cmd, out int slot)
    {
        if (_first.TryGetValue(cmd, out var list) && list.Count > 0)
        {
            slot = list[0];
            return true;
        }
        slot = default;
        return false;
    }

    public bool TryEnd(int cmd, out int slot)
    {
        if (_last.TryGetValue(cmd, out var list) && list.Count > 0)
        {
            slot = list[0];
            return true;
        }
        slot = default;
        return false;
    }
}


/// <summary>
/// Tracks which C# <c>localId</c> is bound to which VS slot, whether it is dirty,
/// and enforces the “no reuse in deeper scope” rule.
/// </summary>
internal sealed class LocalBindingState
{
    private readonly int?[] _localToSlot;   // null ⇒ free
    private readonly bool[] _dirty;
    private readonly int[] _bindDepth;     // depth where slot was first bound

    public LocalBindingState(int maxLocals)
    {
        _localToSlot = new int?[maxLocals];
        _dirty = new bool[maxLocals];
        _bindDepth = new int[maxLocals];
    }

    /*──────────── public helpers expected by unit‑tests ────────────*/

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="local"/> is *currently*
    /// bound to <paramref name="slot"/>.
    /// </summary>
    public bool IsBound(int local, int slot) => _localToSlot[local] == slot;

    public void StartInterval(int slot, int local, int bindDepth)
        => Bind(local, slot, bindDepth);

    public void MarkWritten(int local) => _dirty[local] = true;

    public bool NeedsFlushBeforeReuse(int local, out int slot)
    {
        slot = _localToSlot[local] ?? -1;
        return slot != -1 && _dirty[local];
    }

    public void FlushLocal(int local) => _dirty[local] = false;

    /// <summary>
    /// Attempt to reuse <paramref name="local"/> for <paramref name="newSlot"/>.
    /// Returns <c>true</c> if reuse is allowed at <paramref name="bindDepth"/>;
    /// otherwise <c>false</c> and <paramref name="flushSlot"/> = ‑1.
    /// If reuse is permitted and the local is dirty, caller must flush
    /// <paramref name="flushSlot"/> before binding.
    /// </summary>
    public bool TryReuse(
        int local, int newSlot, int bindDepth, out int flushSlot)
    {
        flushSlot = -1;
        int? current = _localToSlot[local];
        if (current is null) return true;               // was free – safe to use

        /* depth rule: only reuse when we are at SAME or SHALLOWER depth */
        if (bindDepth > _bindDepth[local]) return false;

        flushSlot = _dirty[local] ? current.Value : -1;
        return true;
    }

    /// <summary>
    /// Flush all locals whose <see cref=\"_bindDepth\"/> equals <paramref name=\"depth\"/>
    /// and are dirty. Returns the list of VS slots that were flushed.
    /// </summary>
    public int[] FlushDirtyForDepth(int depth)
    {
        var list = new List<int>();
        for (int l = 0; l < _localToSlot.Length; l++)
        {
            if (_localToSlot[l] is int slot &&
                _bindDepth[l] == depth &&
                _dirty[l])
            {
                list.Add(slot);
                _dirty[l] = false;
            }
        }
        return list.ToArray();
    }
    public void Release(int localId)
    {
        _localToSlot[localId] = null;
        _dirty[localId] = false;
        _bindDepth[localId] = 0;
    }

    /*──────────── internal helper used by the generator ────────────*/
    private void Bind(int localId, int slot, int depth)
    {
        _localToSlot[localId] = slot;
        _dirty[localId] = false;  // freshly loaded
        _bindDepth[localId] = depth;
    }
}

/// <summary>
/// Tiny indentation helper for readable generated source.
/// </summary>
internal sealed class CodeBuilder
{
    private readonly System.Text.StringBuilder _sb = new();
    private int _indent;

    public void Indent() => _indent++;
    public void Unindent() => _indent = Math.Max(0, _indent - 1);

    public void AppendLine(string line = "")
    {
        if (line.Length > 0) _sb.Append(' ', _indent * 4);
        _sb.AppendLine(line);
    }

    public override string ToString() => _sb.ToString();
}
