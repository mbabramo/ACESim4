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

                var entry = BuildPlanForLeaf(info);
                if (entry != null)
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
        

        /// <summary>
        /// Finds the balanced if/endif or incrementdepth/decrementdepth subregion within [from,to)
        /// that among those at least <see cref="_max"/> commands long is as close as possible to
        /// half the size of the entire region.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        private PlanEntry BuildPlanForLeaf(ArrayCommandChunk leaf)
        {
            int from = leaf.StartCommandRange;
            int to = leaf.EndCommandRangeExclusive;
            int lengthWholeRegion = to - from;
            if (lengthWholeRegion < _max) 
                return null;
            int targetLength = lengthWholeRegion / 2;
            PlanEntry result = null;
            int resultDistanceFromTargetLength = int.MaxValue;
            Stack<(int index, bool isIfRegion)> openings = new();
            // Go through the command list and find every balanced region.
            // When we find the end of a region, see if it is greater than max and smaller
            // than the current result, if any.
            for (int i = from; i < to; i++)
            {
                var t = _cmds[i].CommandType;
                if (t == ArrayCommandType.If || t == ArrayCommandType.IncrementDepth)
                {
                    openings.Push((i, t == ArrayCommandType.If));
                }
                else if (t == ArrayCommandType.EndIf || t == ArrayCommandType.DecrementDepth)
                {
                    if (openings.Count > 0)
                    {
                        var (start, isIfRegion) = openings.Pop();
                        if (isIfRegion != (t == ArrayCommandType.EndIf))
                        {
                            throw new Exception("Improperly formed command region");
                        }
                        int endExc = i + 1; // exclusive -- we want to include the closing delimeter in the region.
                        int len = endExc - start;
                        int distanceFromTarget = Math.Abs(len - targetLength);
                        if (len >= _max && len < lengthWholeRegion && (result == null || distanceFromTarget < resultDistanceFromTargetLength))
                        {
                            result = new PlanEntry(leaf.ID, isIfRegion ? SplitKind.Conditional : SplitKind.Depth, start, endExc);
                            resultDistanceFromTargetLength = distanceFromTarget;
                        }
                    }
                    else
                        throw new Exception("Improperly formed command region.");
                }
            }
            return result;
        }

    }
}
