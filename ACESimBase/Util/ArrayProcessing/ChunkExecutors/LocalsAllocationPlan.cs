// File: Util/LocalVariablePlanner.cs

using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing.ChunkExecutors
{
    public class LocalAllocationPlan
    {
        public int LocalCount { get; internal set; }
        public Dictionary<int, int> SlotToLocal { get; } = new Dictionary<int, int>();
        public List<(int slot, int first, int last)> Intervals { get; }
            = new List<(int, int, int)>();
    }

    public static class LocalVariablePlanner
    {
        /// <summary>
        /// Analyze commands[start..end) and allocate up to maxLocals locals
        /// for VS slots used at least minUses times.  If more slots qualify
        /// than maxLocals, the hottest ones (by total uses) are chosen.
        /// </summary>
        public static LocalAllocationPlan PlanLocals(
            ArrayCommand[] commands,
            int start,
            int end,
            int minUses = 3,
            int? maxLocals = null)
        {
            // 1) Gather use counts & first/last indices
            var useCounts = new Dictionary<int, int>();
            var firstUse = new Dictionary<int, int>();
            var lastUse = new Dictionary<int, int>();
            for (int i = start; i < end; i++)
            {
                var cmd = commands[i];
                foreach (var slot in GetReadSlots(cmd).Concat(GetWriteSlots(cmd)))
                {
                    if (!useCounts.ContainsKey(slot))
                    {
                        useCounts[slot] = 0;
                        firstUse[slot] = i;
                    }
                    useCounts[slot]++;
                    lastUse[slot] = i;
                }
            }

            // 2) Build intervals for slots ≥ minUses
            var intervals = useCounts
                .Where(kv => kv.Value >= minUses)
                .Select(kv => (slot: kv.Key,
                               uses: kv.Value,
                               first: firstUse[kv.Key],
                               last: lastUse[kv.Key]))
                .ToList();

            // 3) If capped, pick the hottest maxLocals slots
            if (maxLocals.HasValue && intervals.Count > maxLocals.Value)
            {
                intervals = intervals
                    .OrderByDescending(iv => iv.uses)
                    .ThenBy(iv => iv.first)
                    .Take(maxLocals.Value)
                    .ToList();
            }

            // 4) Sort by start for interval allocation
            intervals.Sort((a, b) => a.first.CompareTo(b.first));

            var plan = new LocalAllocationPlan();
            plan.Intervals.AddRange(intervals.Select(iv => (iv.slot, iv.first, iv.last)));

            // 5) Allocate locals via a simple free‑list
            var active = new List<(int slot, int end, int local)>();
            var freeLocals = new Stack<int>();
            int nextLocal = 0;

            foreach (var (slot, first, last) in plan.Intervals)
            {
                // Evict expired
                for (int j = active.Count - 1; j >= 0; j--)
                {
                    if (active[j].end < first)
                    {
                        freeLocals.Push(active[j].local);
                        active.RemoveAt(j);
                    }
                }

                // Assign a local
                int local = freeLocals.Count > 0
                    ? freeLocals.Pop()
                    : nextLocal++;

                plan.SlotToLocal[slot] = local;
                active.Add((slot, last, local));
            }

            plan.LocalCount = nextLocal;
            return plan;
        }

        private static IEnumerable<int> GetReadSlots(ArrayCommand cmd)
        {
            switch (cmd.CommandType)
            {
                case ArrayCommandType.Zero:
                    return Array.Empty<int>();
                case ArrayCommandType.CopyTo:
                    return new[] { cmd.SourceIndex };
                case ArrayCommandType.NextSource:
                    return Array.Empty<int>();
                case ArrayCommandType.NextDestination:
                case ArrayCommandType.ReusedDestination:
                    return new[] { cmd.SourceIndex };
                case ArrayCommandType.MultiplyBy:
                case ArrayCommandType.IncrementBy:
                case ArrayCommandType.DecrementBy:
                    return new[] { cmd.Index, cmd.SourceIndex };
                case ArrayCommandType.EqualsOtherArrayIndex:
                case ArrayCommandType.NotEqualsOtherArrayIndex:
                case ArrayCommandType.GreaterThanOtherArrayIndex:
                case ArrayCommandType.LessThanOtherArrayIndex:
                    return new[] { cmd.Index, cmd.SourceIndex };
                case ArrayCommandType.EqualsValue:
                case ArrayCommandType.NotEqualsValue:
                    return new[] { cmd.Index };
                case ArrayCommandType.If:
                case ArrayCommandType.EndIf:
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    return Array.Empty<int>();
                default:
                    throw new NotImplementedException($"Unknown command {cmd.CommandType}");
            }
        }

        private static IEnumerable<int> GetWriteSlots(ArrayCommand cmd)
        {
            switch (cmd.CommandType)
            {
                case ArrayCommandType.Zero:
                case ArrayCommandType.CopyTo:
                case ArrayCommandType.NextSource:
                    return new[] { cmd.Index };
                case ArrayCommandType.NextDestination:
                case ArrayCommandType.ReusedDestination:
                    return Array.Empty<int>();
                case ArrayCommandType.MultiplyBy:
                case ArrayCommandType.IncrementBy:
                case ArrayCommandType.DecrementBy:
                    return new[] { cmd.Index };
                case ArrayCommandType.EqualsOtherArrayIndex:
                case ArrayCommandType.NotEqualsOtherArrayIndex:
                case ArrayCommandType.GreaterThanOtherArrayIndex:
                case ArrayCommandType.LessThanOtherArrayIndex:
                case ArrayCommandType.EqualsValue:
                case ArrayCommandType.NotEqualsValue:
                    return Array.Empty<int>();
                case ArrayCommandType.If:
                case ArrayCommandType.EndIf:
                case ArrayCommandType.Comment:
                case ArrayCommandType.Blank:
                    return Array.Empty<int>();
                default:
                    throw new NotImplementedException($"Unknown command {cmd.CommandType}");
            }
        }
    }
}
