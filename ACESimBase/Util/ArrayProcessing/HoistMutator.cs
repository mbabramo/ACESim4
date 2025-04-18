using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimBase.Util.ArrayProcessing
{
    // The HoistMutator **consumes** the plan produced by HoistPlanner and applies
    // it to the live command tree.  For each plan entry it locates the target leaf
    // node, replaces that leaf with a new “Conditional” gate node, and then slices
    // the original If‑body into child leaves whose sizes are all ≤ MaxCommandsPerChunk
    // (preserving the original virtual‑stack metadata in the surrounding slices).
    // After all replacements are done it rebuilds the tree’s cached structures so
    // subsequent compilation or interpretation can run exactly as before, only now
    // with balanced, size‑bounded leaves that respect the original If/EndIf logic.
    public static class HoistMutator
    {
        /// <summary>
        /// Walk the <paramref name="plan"/> list produced by <see cref="HoistPlanner"/>
        /// and mutate <paramref name="acl.CommandTree"/> in‑place.
        /// </summary>
        public static void ApplyPlan(
            ArrayCommandList acl,
            IList<HoistPlanner.PlanEntry> plan)
        {
            // ── 1. apply every PlanEntry ─────────────────────────────────────────────
            foreach (var entry in plan)
            {
                // locate oversize leaf
                var leaf = FindLeafById(
                    (NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>)acl.CommandTree,
                    entry.LeafId);

                if (leaf == null)
                    throw new InvalidOperationException($"Leaf {entry.LeafId} not found");

                // splice in gate + children
                ReplaceLeafWithGate(acl, leaf, entry);
            }

            // ── 2. rebuild virtual‑stack metadata & CoSI/CoDI after mutation ─────────
            acl.CommandTree.WalkTree(
                x => acl.SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>)x),
                x => acl.SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>)x));

            acl.CommandTreeString = acl.CommandTree.ToString();   // keep string snapshot fresh
        }

        /// <summary>
        /// Ensure <paramref name="acl"/> has a CommandTree.  
        /// If it is still null we create a synthetic root leaf that spans the
        /// whole command list – just enough for the mutator and for ExecuteAll.
        /// </summary>
        public static NWayTreeStorageInternal<ArrayCommandChunk> EnsureTreeExists(ArrayCommandList acl)
        {
            if (acl.CommandTree != null)
            {
                if (acl.CommandTreeString == null)
                    acl.CommandTreeString = acl.CommandTree.ToString();
                return (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            }

            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(parent: null);
            root.StoredValue = new ArrayCommandChunk
            {
                ID = 0,
                StartCommandRange = 0,
                EndCommandRangeExclusive = acl.NextCommandIndex,
                StartSourceIndices = 0,
                EndSourceIndicesExclusive = 0,
                StartDestinationIndices = 0,
                EndDestinationIndicesExclusive = 0
            };
            acl.CommandTree = root;
            acl.CommandTreeString = acl.CommandTree.ToString();
            return root;
        }

        private static NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>
            FindLeafById(NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> root,
                         int id)
        {
            NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> found = null;

            root.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>)nodeObj;
                if (n.StoredValue.ID == id)
                    found = n;
            });

            return found;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Replace the oversize LEAF by a Conditional‑gate node, keeping the same
        //  branch‑index it originally occupied in its parent.
        // ─────────────────────────────────────────────────────────────────────────────
        /* drop‑in replacement – same signature */
        /* drop‑in replacement */
        private static void ReplaceLeafWithGate(
                ArrayCommandList acl,
                NWayTreeStorageInternal<ArrayCommandChunk> leaf,
                HoistPlanner.PlanEntry planEntry)
        {
            bool isRoot = leaf.Parent == null;
            NWayTreeStorageInternal<ArrayCommandChunk> gateNode;

            /* ────────────────────────── 0. pre‑diag ─────────────────────────── */
            Debug.WriteLine(
                $"[REPL]  leafID={leaf.StoredValue.ID}  " +
                $"range=[{leaf.StoredValue.StartCommandRange},{leaf.StoredValue.EndCommandRangeExclusive})"); // DEBUG

            /* ─────────────── 1. create (or reuse) the Conditional gate ───────── */
            if (isRoot)
            {
                gateNode = leaf;
                gateNode.StoredValue.Name = "Conditional";
                gateNode.StoredValue.ChildrenParallelizable = false;
                Debug.WriteLine("        (root replaced in‑place)"); // DEBUG
            }
            else
            {
                var parent = (NWayTreeStorageInternal<ArrayCommandChunk>)leaf.Parent;

                gateNode = new NWayTreeStorageInternal<ArrayCommandChunk>(parent);
                gateNode.StoredValue = new ArrayCommandChunk
                {
                    Name = "Conditional",
                    StartCommandRange = leaf.StoredValue.StartCommandRange,
                    EndCommandRangeExclusive = leaf.StoredValue.EndCommandRangeExclusive,
                    StartSourceIndices = leaf.StoredValue.StartSourceIndices,
                    EndSourceIndicesExclusive = leaf.StoredValue.EndSourceIndicesExclusive,
                    StartDestinationIndices = leaf.StoredValue.StartDestinationIndices,
                    EndDestinationIndicesExclusive = leaf.StoredValue.EndDestinationIndicesExclusive,
                    ChildrenParallelizable = false,
                    /* critical: preserve stack reference */
                    VirtualStack = leaf.StoredValue.VirtualStack
                };

                // swap into the exact same branch slot
                byte slot = 0;
                for (byte i = 1; i <= parent.StoredValue.LastChild; i++)
                    if (ReferenceEquals(parent.GetBranch(i), leaf)) { slot = i; break; }

                if (slot == 0) throw new InvalidOperationException("leaf not found in parent");
                parent.SetBranch(slot, gateNode);
                Debug.WriteLine($"        inserted gate into parent‑slot {slot}"); // DEBUG
            }

            /* ─────────────── 2. slice the If‑body into child leaves ──────────── */
            int max = acl.MaxCommandsPerChunk;
            int bodyStart = planEntry.IfIdx + 1;   // first cmd inside body
            int bodyEnd = planEntry.EndIfIdx;    // EndIf *not* included

            Debug.WriteLine(
                $"[SLICE] gateID={gateNode.StoredValue.ID}  body=[{bodyStart},{bodyEnd})  max={max}"); // DEBUG

            byte childId = 1;
            for (int s = bodyStart; s < bodyEnd;)
            {
                int e = Math.Min(s + max, bodyEnd);

                var child = new NWayTreeStorageInternal<ArrayCommandChunk>(gateNode);
                child.StoredValue = new ArrayCommandChunk
                {
                    StartCommandRange = s,
                    EndCommandRangeExclusive = e,
                    ChildrenParallelizable = false,
                    /* inherit same stack ref so lengths line up */
                    VirtualStack = leaf.StoredValue.VirtualStack
                };

                gateNode.SetBranch(childId, child);

                Debug.WriteLine(
                    $"        • child {childId}  range=[{s},{e})  " +
                    $"stackLen={(child.StoredValue.VirtualStack?.Length ?? 0)}");// DEBUG

                childId++;
                s = e;
            }
            gateNode.StoredValue.LastChild = (byte)(childId - 1);

            /* ────────────────────────── post‑diag ────────────────────────────── */
            Debug.WriteLine(
                $"[DONE]  gateID={gateNode.StoredValue.ID}  " +
                $"children={gateNode.StoredValue.LastChild}\n"); // DEBUG
        }




        /// Helper to create a new node with minimal metadata
        private static NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>
            NewNode(NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> parent,
                    string name, int startCmd, int endCmd)
        {
            var node = new NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>(parent);
            node.StoredValue = new ArrayCommandList.ArrayCommandChunk
            {
                ID = ArrayCommandList.ArrayCommandChunk.NextID++,
                Name = name,
                StartCommandRange = startCmd,
                EndCommandRangeExclusive = endCmd
            };
            return node;
        }

        /// Locate which branch index in <parent> points to <child>
        private static byte FindSlotInParent(
            NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> parent,
            NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> child)
        {
            for (byte i = 0; i <= parent.StoredValue.LastChild; i++)
                if (parent.GetBranch(i) == child)
                    return i;

            throw new InvalidOperationException("child not found in parent branches");
        }
    }
}
