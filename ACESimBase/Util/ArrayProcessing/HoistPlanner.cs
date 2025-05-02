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
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine($"[Planner] BuildPlan (max={_max})");
#endif
            var plan = new List<PlanEntry>();

            root.WalkTree(nObj =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)nObj;
                if (node.Branches is { Length: > 0 }) return;

                var info = node.StoredValue;
                int len = info.EndCommandRangeExclusive - info.StartCommandRange;

#if OUTPUT_HOISTING_INFO
                if (len > _max)
                    TabbedText.WriteLine($"  • leaf ID{info.ID} len={len}");
#endif
                if (len <= _max) return;

                foreach (var entry in BuildPlanForLeaf(info))
                    plan.Add(entry);
            });

            plan.Sort((a, b) => a.LeafId != b.LeafId ? a.LeafId.CompareTo(b.LeafId)
                                                     : a.StartIdx.CompareTo(b.StartIdx));

#if OUTPUT_HOISTING_INFO
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
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine($"FindOversizeConditionals  span=[{from},{to})  _max={_max}");
            TabbedText.TabIndent();
#endif
            var stack = new Stack<int>();
            var pairs = new List<(int, int)>();

            for (int i = from; i < to; i++)
            {
                var t = _cmds[i].CommandType;
                if (t == ArrayCommandType.If)
                {
                    stack.Push(i);
#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine($"IF   @ {i}");
#endif
                }
                else if (t == ArrayCommandType.EndIf && stack.Count > 0)
                {
                    int open = stack.Pop();
                    int bodyLen = (i - open) - 1;
#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine($"ENDIF@ {i}   bodyLen={bodyLen}");
#endif
                    if (bodyLen >= _max)
                    {
#if OUTPUT_HOISTING_INFO
                        TabbedText.WriteLine($"  ➜ oversize IF pair [{open},{i}]");
#endif
                        pairs.Add((open, i));
                    }
                }
            }

            // Ignore degenerate whole-leaf pair
            if (pairs.Count == 0 ||
                (pairs.Count == 1 &&
                 pairs[0].Item1 == from &&
                 pairs[0].Item2 == to - 1))
            {
#if OUTPUT_HOISTING_INFO
                TabbedText.WriteLine("no non-degenerate oversize IF bodies");
                TabbedText.TabUnindent();
#endif
                yield break;
            }

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
            {
#if OUTPUT_HOISTING_INFO
                TabbedText.WriteLine($"emit   IF slice [{s},{e + 1})");
#endif
                yield return (s, e + 1);   // end is exclusive
            }
#if OUTPUT_HOISTING_INFO
            TabbedText.TabUnindent();
#endif
        }

        private IEnumerable<(int start, int end)> FindOversizeDepthRegions(int from, int to)
        {
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine($"FindOversizeDepthRegions span=[{from},{to})  _max={_max}");
            TabbedText.TabIndent();
#endif
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
#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine($"INC  @ {i}   depth→{depth}");
#endif
                }
                else if (t == ArrayCommandType.DecrementDepth)
                {
                    depth--;
#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine($"DEC  @ {i}   depth→{depth}");
#endif
                    if (depth == 0 && openings.Count > 0)
                    {
                        int start = openings.Pop();
                        int end = i + 1;                // exclusive
                        if (end - start >= _max)
                        {
#if OUTPUT_HOISTING_INFO
                            TabbedText.WriteLine($"  ➜ oversize depth region [{start},{end})");
#endif
                            regions.Add((start, end));
                        }
                    }
                }
            }

#if OUTPUT_HOISTING_INFO
            foreach (var (s, e) in regions)
                TabbedText.WriteLine($"emit depth slice [{s},{e})");
            TabbedText.TabUnindent();
#endif
            return regions;
        }

    }
}
