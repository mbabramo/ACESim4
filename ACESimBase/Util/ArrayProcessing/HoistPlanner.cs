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
        public IList<PlanEntry> BuildPlan(
            NWayTreeStorageInternal<ArrayCommandChunk> root)
        {
            var plan = new List<PlanEntry>();

            root.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return;

                var info = leaf.StoredValue;
                int size = info.EndCommandRangeExclusive - info.StartCommandRange;
                if (size <= _max) return;

                var (ifIdx, endIfIdx) =
                    FindOutermostIf(info.StartCommandRange, info.EndCommandRangeExclusive);
                if (ifIdx != -1)
                {
                    TabbedText.WriteLine($"[HOIST‑CAND] leafID={info.ID} size={size} " +
                                    $"parentParall={info.ChildrenParallelizable}");
                    int bodyLen = (endIfIdx - ifIdx) - 1;
                    plan.Add(new PlanEntry(info.ID, ifIdx, endIfIdx, bodyLen));
                }
            });

            return plan;
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
