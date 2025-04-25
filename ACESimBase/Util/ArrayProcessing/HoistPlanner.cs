using ACESimBase.Util.Debugging;
using ACESimBase.Util.NWayTreeStorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing
{
    // The HoistPlanner performs a **read‑only sweep** over the current command
    // tree to discover “problem” leaves:  those whose linear sequence of commands
    // exceeds MaxCommandsPerChunk *and* contains an outer‑level If … EndIf pair.
    // For every such leaf it records a **plan entry** that stores the leaf‑ID and
    // the exact span of the oversized If‑body (indices of the If, matching EndIf,
    // and the body length).  No mutations are made; the output is simply a
    // deterministic, reproducible list of places where a Conditional gate needs to
    // be inserted so the tree can be split safely without breaking control‑flow.
    public sealed class HoistPlanner
    {
        private readonly ArrayCommand[] _cmds;
        private readonly int _max;

        public record PlanEntry(
            int LeafId,
            int IfIdx,
            int EndIfIdx,
            int BodyLen);

        public HoistPlanner(ArrayCommand[] cmds, int maxCommandsPerChunk)
        {
            _cmds = cmds;
            _max = maxCommandsPerChunk;
        }

        /// <summary>
        /// Return a deterministic list of oversize leaves that need hoisting.
        /// </summary>
        public IList<PlanEntry> BuildPlan(NWayTreeStorageInternal<ArrayCommandChunk> root)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));

            var plan = new List<PlanEntry>();

            root.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches is { Length: > 0 }) return;          // skip non-leaf

                var info = leaf.StoredValue;
                int size = info.EndCommandRangeExclusive - info.StartCommandRange;
                if (size <= _max) return;                              // leaf within limit

                foreach (var (ifIdx, endIfIdx, bodyLen) in
                         FindInnermostOversizeBodies(info.StartCommandRange,
                                                     info.EndCommandRangeExclusive))
                {
                    plan.Add(new PlanEntry(info.ID, ifIdx, endIfIdx, bodyLen));
                }
            });

            return plan;
        }

        /// <summary>
        /// Enumerates every <em>innermost</em> If … EndIf pair whose body exceeds the
        /// size threshold inside the span <c>[start, end)</c>.  
        /// Sibling oversize blocks are all returned; nested chains keep only the
        /// deepest (smallest-span) member.
        /// </summary>
        private IEnumerable<(int ifIdx, int endIfIdx, int bodyLen)>
        FindInnermostOversizeBodies(int start, int end)
        {
            var raw = new List<(int ifIdx, int endIfIdx)>();
            var open = new Stack<int>();

            for (int i = start; i < end; i++)
            {
                switch (_cmds[i].CommandType)
                {
                    case ArrayCommandType.If:
                        open.Push(i);
                        break;

                    case ArrayCommandType.EndIf when open.Count > 0:
                        int ifIdx = open.Pop();
                        int bodyLen = (i - ifIdx) - 1;
                        if (bodyLen >= _max)                          // inclusive check
                            raw.Add((ifIdx, i));
                        break;
                }
            }

            if (raw.Count == 0)
            {
                // If no complete oversize pairs were found, check for an open If 
                // that spans beyond this leaf (unclosed within [start,end)).
                if (open.Count > 0)
                {
                    int openIfIdx = open.Pop();  // innermost unclosed If
                                                 // Find the matching EndIf in the remaining commands (beyond 'end')
                    int depth = 1;
                    int j = end;
                    while (j < _cmds.Length && depth > 0)
                    {
                        if (_cmds[j].CommandType == ArrayCommandType.If) depth++;
                        if (_cmds[j].CommandType == ArrayCommandType.EndIf) depth--;
                        j++;
                    }
                    if (depth == 0)
                    {
                        int endIfIdx = j - 1;
                        int bodyLen = (endIfIdx - openIfIdx) - 1;
                        // Only plan hoist if this open If is not the only one (i.e., 
                        // there was another If outside it), or if it indeed exceeds size.
                        if (bodyLen >= _max && open.Count > 0)
                            yield return (openIfIdx, endIfIdx, bodyLen);
                    }
                }
                yield break;
            }

            // ... (existing filtering logic below remains unchanged) ...
            raw.Sort((a, b) => a.ifIdx.CompareTo(b.ifIdx));
            var filtered = new List<(int ifIdx, int endIfIdx)>();
            foreach (var cand in raw)
            {
                while (filtered.Count > 0 &&
                       filtered[^1].ifIdx <= cand.ifIdx &&
                       filtered[^1].endIfIdx >= cand.endIfIdx)
                {
                    filtered.RemoveAt(filtered.Count - 1);           // drop ancestor
                }
                filtered.Add(cand);
            }

            foreach (var (ifIdx, endIfIdx) in filtered)
                yield return (ifIdx, endIfIdx, (endIfIdx - ifIdx) - 1);
        }






        /// <summary>
        /// Scan [_start, _end) and return indices of the first outer‑level
        /// If and its matching EndIf.  Returns (-1,-1) if none.
        /// </summary>
        private (int ifIdx, int endIfIdx) FindOutermostIf(int start, int end)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                switch (_cmds[i].CommandType)
                {
                    case ArrayCommandType.If:
                        if (depth == 0)
                        {
                            // find its matching EndIf
                            int j = i + 1;
                            int d = 1;
                            while (j < end && d > 0)
                            {
                                if (_cmds[j].CommandType == ArrayCommandType.If) d++;
                                if (_cmds[j].CommandType == ArrayCommandType.EndIf) d--;
                                j++;
                            }
                            if (d == 0)
                                return (i, j - 1); // j stepped past EndIf
                        }
                        depth++;
                        break;

                    case ArrayCommandType.EndIf:
                        depth--;
                        break;
                }
            }
            return (-1, -1); // none
        }
    }
}
