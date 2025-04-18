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

        internal record LeafSplit(
            NWayTreeStorageInternal<ArrayCommandChunk> Prefix,
            NWayTreeStorageInternal<ArrayCommandChunk> Gate,
            NWayTreeStorageInternal<ArrayCommandChunk>? Postfix);

        internal static LeafSplit SplitOversizeLeaf(
            ArrayCommandList acl,
            NWayTreeStorageInternal<ArrayCommandChunk> leaf,
            int ifIdx,          // index of the “If” token
            int endIfIdx)       // index of the matching “EndIf” token
        {
            int spanStart = leaf.StoredValue.StartCommandRange;
            int spanEnd = leaf.StoredValue.EndCommandRangeExclusive;
            int afterEnd = endIfIdx + 1;

            // ── prefix: shrink existing node
            leaf.StoredValue.EndCommandRangeExclusive = ifIdx;

            // helper to clone basic metadata + stack ref
            ArrayCommandChunk MetaFrom(ArrayCommandChunk src) => new()
            {
                VirtualStack = src.VirtualStack,
                VirtualStackID = src.VirtualStackID,
                ChildrenParallelizable = false
            };

            // ── gate node
            var gate = new NWayTreeStorageInternal<ArrayCommandChunk>(leaf);
            gate.StoredValue = MetaFrom(leaf.StoredValue);
            gate.StoredValue.Name = "Conditional";
            gate.StoredValue.StartCommandRange = ifIdx;
            gate.StoredValue.EndCommandRangeExclusive = afterEnd;

            // ── postfix slice (optional)
            NWayTreeStorageInternal<ArrayCommandChunk>? postfix = null;
            if (afterEnd < spanEnd)
            {
                postfix = new NWayTreeStorageInternal<ArrayCommandChunk>(leaf);
                postfix.StoredValue = MetaFrom(leaf.StoredValue);
                postfix.StoredValue.Name = "Postfix";
                postfix.StoredValue.StartCommandRange = afterEnd;
                postfix.StoredValue.EndCommandRangeExclusive = spanEnd;
            }

            return new LeafSplit(leaf, gate, postfix);
        }

        /// <summary>
        /// Attach <paramref name="split"/> to the original leaf’s parent so the new
        /// branch‑IDs become
        ///     • 0 – prefix  (already in place)
        ///     • 1 – Conditional gate
        ///     • 2 – Postfix  (only if it exists)
        /// and update <c>LastChild</c> accordingly.
        /// </summary>
        internal static void InsertSplitIntoTree(
            LeafSplit split,
            byte firstBranchId = 1)   // we always put the gate at branch‑ID 1
        {
            var parent = split.Prefix;

            // Branch‑ID 1 → Conditional gate
            parent.SetBranch(firstBranchId, split.Gate);

            // Branch‑ID 2 → Postfix slice (when present)
            if (split.Postfix != null)
                parent.SetBranch((byte)(firstBranchId + 1), split.Postfix);

            /* ── update LastChild to the highest ID we just used ────────────── */
            parent.StoredValue.LastChild =
                (byte)((split.Postfix != null) ? firstBranchId + 1   // gate + postfix
                                               : firstBranchId);     // gate only
        }


        private static void ReplaceLeafWithGate(
                ArrayCommandList acl,
                NWayTreeStorageInternal<ArrayCommandChunk> leaf,
                HoistPlanner.PlanEntry entry)
        {
            var split = SplitOversizeLeaf(acl, leaf, entry.IfIdx, entry.EndIfIdx);
            InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);   // unchanged helper – still private
        }
    }
}
