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
            int start = leaf.StartCommandRange;
            int end = leaf.EndCommandRangeExclusive;

            var conditionals = FindOversizeConditionals(start, end).ToList();
            if (conditionals.Count > 0)
            {
                foreach (var (s, e) in conditionals)
                    yield return new PlanEntry(leaf.ID, SplitKind.Conditional, s, e);
                yield break;
            }

            var depthRegions = FindOversizeDepthRegions(start, end).ToList();
            if (depthRegions.Count > 0)
            {
                foreach (var (s, e) in depthRegions)
                    yield return new PlanEntry(leaf.ID, SplitKind.Depth, s, e);
            }
        }

        private IEnumerable<(int start, int end)> FindOversizeConditionals(int from, int to)
        {
            var stack = new Stack<int>();
            var pairs = new List<(int, int)>();

            for (int i = from; i < to; i++)
            {
                var t = _cmds[i].CommandType;
                if (t == ArrayCommandType.If)
                    stack.Push(i);
                else if (t == ArrayCommandType.EndIf && stack.Count > 0)
                {
                    int open = stack.Pop();
                    int bodyLen = (i - open) - 1;
                    if (bodyLen >= _max)
                        pairs.Add((open, i));
                }
            }

            // Ignore the degenerate case: exactly one oversize pair that
            // starts at 'from' and ends at 'to-1'  (i.e. the whole leaf)
            if (pairs.Count == 0 ||
                (pairs.Count == 1 &&
                 pairs[0].Item1 == from &&
                 pairs[0].Item2 == to - 1))
                yield break;

            pairs.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var filtered = new List<(int, int)>();
            foreach (var p in pairs)
            {
                while (filtered.Count > 0 &&
                       filtered[^1].Item1 <= p.Item1 &&
                       filtered[^1].Item2 >= p.Item2)
                    filtered.RemoveAt(filtered.Count - 1);
                filtered.Add(p);
            }

            foreach (var (s, e) in filtered)
                yield return (s, e + 1);
        }

        private IEnumerable<(int start, int end)> FindOversizeDepthRegions(int from, int to)
        {
            var openings = new Stack<int>();
            var regions = new List<(int, int)>();
            int depth = 0;

            for (int i = from; i < to; i++)
            {
                var t = _cmds[i].CommandType;
                if (t == ArrayCommandType.IncrementDepth)
                {
                    if (depth == 0)
                        openings.Push(i);
                    depth++;
                }
                else if (t == ArrayCommandType.DecrementDepth)
                {
                    depth--;
                    if (depth == 0 && openings.Count > 0)
                    {
                        int start = openings.Pop();
                        int end = i + 1;
                        if (end - start >= _max)
                            regions.Add((start, end));
                    }
                }
            }

            return regions;
        }
    }
}
