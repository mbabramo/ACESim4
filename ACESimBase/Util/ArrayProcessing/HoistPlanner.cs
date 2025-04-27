using ACESimBase.Util.Debugging;
using ACESimBase.Util.NWayTreeStorage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// HoistPlanner scans executable leaves and produces a deterministic plan
    /// to split oversize leaves at balanced If/EndIf or IncrementDepth/DecrementDepth
    /// boundaries so every resulting leaf respects the configured command limit.
    /// </summary>
    public sealed class HoistPlanner
    {
        private readonly ArrayCommand[] _cmds;
        private readonly int _max;

        public enum SplitKind
        {
            Conditional,
            Depth
        }

        public record PlanEntry(
            int LeafId,
            SplitKind Kind,
            int StartIdx,
            int EndIdxExclusive);

        public HoistPlanner(ArrayCommand[] cmds, int maxCommandsPerChunk)
        {
            _cmds = cmds ?? throw new ArgumentNullException(nameof(cmds));
            _max = maxCommandsPerChunk;
        }

        public IList<PlanEntry> BuildPlan(NWayTreeStorageInternal<ArrayCommandChunk> root)
        {
#if DEBUG
            TabbedText.WriteLine($"[Planner] BuildPlan (max={_max})");
#endif
            var plan = new List<PlanEntry>();

            root.WalkTree(nObj =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)nObj;
                if (node.Branches is { Length: > 0 }) return;

                var info = node.StoredValue;
                int len = info.EndCommandRangeExclusive - info.StartCommandRange;

#if DEBUG
                if (len > _max)
                    TabbedText.WriteLine($"  • leaf ID{info.ID} len={len}");
#endif
                if (len <= _max) return;

                foreach (var entry in BuildPlanForLeaf(info))
                    plan.Add(entry);
            });

            plan.Sort((a, b) => a.LeafId != b.LeafId ? a.LeafId.CompareTo(b.LeafId)
                                                     : a.StartIdx.CompareTo(b.StartIdx));

#if DEBUG
            foreach (var e in plan)
                TabbedText.WriteLine($"    → plan ID{e.LeafId} {e.Kind} [{e.StartIdx},{e.EndIdxExclusive})");
#endif
            return plan;
        }


        private IEnumerable<PlanEntry> BuildPlanForLeaf(ArrayCommandChunk leaf)
        {
            int from = leaf.StartCommandRange;
            int to = leaf.EndCommandRangeExclusive;

            var stackIf = new Stack<int>();
            var stackDepth = new Stack<int>();
            var blocks = new List<(int start, int end, SplitKind kind)>();

            //----------------------------------------------------------------------
            // ①  Single sweep — collect every balanced block whose body > _max
            //----------------------------------------------------------------------
            for (int i = from; i < to; i++)
            {
                var t = _cmds[i].CommandType;
                switch (t)
                {
                    case ArrayCommandType.If:
                        stackIf.Push(i);
                        break;

                    case ArrayCommandType.EndIf when stackIf.Count > 0:
                        {
                            int open = stackIf.Pop();
                            int bodyLen = (i - open) - 1;              // exclude If/EndIf
                            if (bodyLen >= _max)
                                blocks.Add((open, i + 1, SplitKind.Conditional));
                            break;
                        }

                    case ArrayCommandType.IncrementDepth:
                        stackDepth.Push(i);
                        break;

                    case ArrayCommandType.DecrementDepth when stackDepth.Count > 0:
                        {
                            int open = stackDepth.Pop();
                            int end = i + 1;                          // include DecDepth
                            if (end - open >= _max)
                                blocks.Add((open, end, SplitKind.Depth));
                            break;
                        }
                }
            }

            if (blocks.Count == 0)
                yield break;

            // Ignore degenerate “whole-leaf” block
            if (blocks.Count == 1 && blocks[0].start == from && blocks[0].end == to)
                yield break;

            //----------------------------------------------------------------------
            // ②  Keep the **innermost** oversized blocks (remove outer containers)
            //----------------------------------------------------------------------
            blocks.Sort((a, b) => a.start.CompareTo(b.start));
            var filtered = new List<(int start, int end, SplitKind kind)>();
            foreach (var b in blocks)
            {
                while (filtered.Count > 0 &&
                       filtered[^1].start <= b.start &&
                       filtered[^1].end >= b.end)
                    filtered.RemoveAt(filtered.Count - 1);          // discard outer block
                filtered.Add(b);
            }

            //----------------------------------------------------------------------
            // ③  Emit plan entries
            //----------------------------------------------------------------------
            foreach (var (s, e, k) in filtered)
                yield return new PlanEntry(leaf.ID, k, s, e);
        }

    }
}
