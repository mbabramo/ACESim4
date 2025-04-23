// ------------------------------------------------------------------------------------
//  LocalsAllocationPlan & LocalVariablePlanner (depth‑aware)
//  --------------------------------------------------------
//  ACESimBase.Util.ArrayProcessing.ChunkExecutors
// ------------------------------------------------------------------------------------

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors;

using System;
using System.Collections.Generic;
using System.Linq;

// ============================================================================
//  LocalsAllocationPlan
// ============================================================================

/// <summary>
/// Immutable description of how VS (virtual‑stack) slots are mapped to C# local
/// variables for a contiguous slice of <see cref="ArrayCommand"/>s.  Produced by
/// <see cref="LocalVariablePlanner"/> and consumed by Roslyn / IL generators.
/// </summary>
public sealed class LocalsAllocationPlan
{
    /// <summary>
    /// A live‑interval for a single VS slot.
    /// </summary>
    public readonly record struct Interval(int Slot, int First, int Last, int BindDepth);

    // ----------------------------- core data -----------------------------
    public int LocalCount => _localCount;
    public IReadOnlyDictionary<int, int> SlotToLocal => _slotToLocal;
    public IReadOnlyList<Interval> Intervals => _intervals;

    // --------------------------- helper maps ----------------------------
    public IReadOnlyDictionary<int, int> FirstUseToSlot => _firstUseToSlot;
    public IReadOnlyDictionary<int, int> LastUseToSlot => _lastUseToSlot;
    public IReadOnlyDictionary<int, int> SlotBindDepth => _slotBindDepth;

    // ----------------------------- backing ------------------------------
    private readonly Dictionary<int, int> _slotToLocal = new();
    private readonly List<Interval> _intervals = new();
    private readonly Dictionary<int, int> _firstUseToSlot = new();
    private readonly Dictionary<int, int> _lastUseToSlot = new();
    private readonly Dictionary<int, int> _slotBindDepth = new();
    private int _localCount;

    internal void AddInterval(int slot, int first, int last, int bindDepth, int local)
    {
        _intervals.Add(new Interval(slot, first, last, bindDepth));
        _slotToLocal[slot] = local;
        _firstUseToSlot[first] = slot;
        _lastUseToSlot[last] = slot;
        _slotBindDepth[slot] = bindDepth;
        if (local + 1 > _localCount)
            _localCount = local + 1;
    }

    public override string ToString()
      => $"Locals={LocalCount}, Intervals=[" + string.Join(", ", _intervals) + "]";
}

// ============================================================================
//  LocalVariablePlanner
// ============================================================================

/// <summary>
/// Computes a <see cref="LocalsAllocationPlan"/> for a (start,end) slice of
/// <see cref="ArrayCommand"/>s, with control‑flow‑depth awareness so that a local
/// is never rebound to a different VS slot while still inside a deeper branch
/// than where it was originally bound.
/// </summary>
public static class LocalVariablePlanner
{
    public static LocalsAllocationPlan PlanNoReuse(ArrayCommand[] cmds, int start, int end)
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

    public static LocalsAllocationPlan PlanLocals(
        ArrayCommand[] commands,
        int start,
        int end,
        int minUses = 3,
        int? maxLocals = null)
    {
        if (start >= end)
            return new LocalsAllocationPlan();

        // ------------------------------------------------------------------
        // 1. Build depth map  (depthAt[i] == syntactic depth of command i)
        // ------------------------------------------------------------------
        var depthAt = new int[end];
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            var t = commands[i].CommandType;
            if (t == ArrayCommandType.EndIf) depth--;
            depthAt[i] = depth;
            if (t == ArrayCommandType.If) depth++;
        }

        // ------------------------------------------------------------------
        // 2. Collect use‑counts and first/last indices per slot
        // ------------------------------------------------------------------
        var useCount = new Dictionary<int, int>();
        var firstIdx = new Dictionary<int, int>();
        var lastIdx = new Dictionary<int, int>();

        for (int i = start; i < end; i++)
        {
            foreach (int slot in GetReadSlots(commands[i]).Concat(GetWriteSlots(commands[i])))
            {
                if (!useCount.ContainsKey(slot))
                {
                    useCount[slot] = 0;
                    firstIdx[slot] = i;
                }
                useCount[slot]++;
                lastIdx[slot] = i;
            }
        }

        // ------------------------------------------------------------------
        // 3. Filter by minUses and (optional) maxLocals
        // ------------------------------------------------------------------
        var intervals = useCount
            .Where(kv => kv.Value >= minUses)
            .Select(kv => new {
                Slot = kv.Key,
                Uses = kv.Value,
                First = firstIdx[kv.Key],
                Last = lastIdx[kv.Key],
                BindDepth = depthAt[firstIdx[kv.Key]]
            })
            .ToList();

        // Sort by hotness then first occurrence when capped
        if (maxLocals.HasValue && intervals.Count > maxLocals.Value)
        {
            intervals = intervals
                .OrderByDescending(iv => iv.Uses)
                .ThenBy(iv => iv.First)
                .Take(maxLocals.Value)
                .ToList();
        }

        // Sort by First for interval‑allocation sweep
        intervals.Sort((a, b) => a.First.CompareTo(b.First));

        // ------------------------------------------------------------------
        // 4. Depth‑aware linear‑scan allocation
        // ------------------------------------------------------------------
        var plan = new LocalsAllocationPlan();
        var active = new List<(int Slot, int End, int Local, int BindDepth)>();
        var freeList = new Stack<int>();
        int nextLocal = 0;

        foreach (var iv in intervals)
        {
            // Evict locals that have expired AND whose bind‑depth is shallower/equal
            for (int j = active.Count - 1; j >= 0; j--)
            {
                var a = active[j];
                if (a.End < iv.First && depthAt[iv.First] <= a.BindDepth)
                {
                    freeList.Push(a.Local);
                    active.RemoveAt(j);
                }
            }

            int local = freeList.Count > 0 ? freeList.Pop() : nextLocal++;
            plan.AddInterval(iv.Slot, iv.First, iv.Last, iv.BindDepth, local);
            active.Add((iv.Slot, iv.Last, local, iv.BindDepth));
        }


        return plan;
    }

    // ------------------------------ helpers ------------------------------
    private static IEnumerable<int> GetReadSlots(ArrayCommand cmd) => cmd.CommandType switch
    {
        ArrayCommandType.CopyTo => new[] { cmd.SourceIndex },
        ArrayCommandType.NextDestination => new[] { cmd.SourceIndex },
        ArrayCommandType.ReusedDestination => new[] { cmd.SourceIndex },
        ArrayCommandType.MultiplyBy or
        ArrayCommandType.IncrementBy or
        ArrayCommandType.DecrementBy => new[] { cmd.Index, cmd.SourceIndex },
        ArrayCommandType.EqualsOtherArrayIndex or
        ArrayCommandType.NotEqualsOtherArrayIndex or
        ArrayCommandType.GreaterThanOtherArrayIndex or
        ArrayCommandType.LessThanOtherArrayIndex => new[] { cmd.Index, cmd.SourceIndex },
        ArrayCommandType.EqualsValue or
        ArrayCommandType.NotEqualsValue => new[] { cmd.Index },
        _ => Array.Empty<int>()
    };

    private static IEnumerable<int> GetWriteSlots(ArrayCommand cmd) => cmd.CommandType switch
    {
        ArrayCommandType.Zero or
        ArrayCommandType.CopyTo or
        ArrayCommandType.NextSource => new[] { cmd.Index },
        ArrayCommandType.MultiplyBy or
        ArrayCommandType.IncrementBy or
        ArrayCommandType.DecrementBy => new[] { cmd.Index },
        _ => Array.Empty<int>()
    };
}
