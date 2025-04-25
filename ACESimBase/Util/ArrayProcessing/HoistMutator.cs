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
                    (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree,
                    entry.LeafId);

                if (leaf == null)
                    throw new InvalidOperationException($"Leaf {entry.LeafId} not found");

                // splice in gate + children
                ReplaceLeafWithGate(acl, leaf, entry);
            }

            // ── 2. rebuild virtual‑stack metadata & CoSI/CoDI after mutation ─────────
            acl.CommandTree.WalkTree(
                x => acl.SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>)x),
                x => acl.SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)x));

            if (acl.RecordCommandTreeString)
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
                if (acl.CommandTreeString == null && acl.RecordCommandTreeString)
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
                EndDestinationIndicesExclusive = 0,
                Skip = true
            };
            acl.CommandTree = root;
            if (acl.RecordCommandTreeString)
                acl.CommandTreeString = acl.CommandTree.ToString();
            return root;
        }

        private static NWayTreeStorageInternal<ArrayCommandChunk>
            FindLeafById(NWayTreeStorageInternal<ArrayCommandChunk> root,
                         int id)
        {
            NWayTreeStorageInternal<ArrayCommandChunk> found = null;

            root.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (n.StoredValue.ID == id)
                    found = n;
            });

            return found;
        }

        internal record LeafSplit(
            NWayTreeStorageInternal<ArrayCommandChunk> Prefix,
            NWayTreeStorageInternal<ArrayCommandChunk> Gate,
            NWayTreeStorageInternal<ArrayCommandChunk> Postfix);

        internal static LeafSplit SplitOversizeLeaf(
            ArrayCommandList acl,
            NWayTreeStorageInternal<ArrayCommandChunk> leaf,
            int ifIdx,
            int endIfIdx)
        {
            TabbedText.WriteLine($"[SPLIT] leaf={leaf.StoredValue.ID}  "
              + $"gateParall={leaf.StoredValue.ChildrenParallelizable}");

            int spanStart = leaf.StoredValue.StartCommandRange;
            int spanEnd = leaf.StoredValue.EndCommandRangeExclusive;
            int afterEnd = endIfIdx + 1;

            /* ── 1. shrink original node to the prefix BEFORE the If ─────────── */
            leaf.StoredValue.EndCommandRangeExclusive = ifIdx;

            /* ‼ Do NOT mark the prefix for skipping – it contains essential
                   initialisation work (e.g. CopyToNew) that must still run. */
#if DEBUG
            TabbedText.WriteLine($"[FIX] Prefix slice {leaf.StoredValue.ID} will execute (Skip=false)");
#endif
            leaf.StoredValue.Skip = false;             // ← changed line

            /* helper to copy basic meta‑data */
            ArrayCommandChunk MetaFrom(ArrayCommandChunk src)
            {
                var copy = new ArrayCommandChunk
                {
                    VirtualStack = src.VirtualStack,
                    VirtualStackID = src.VirtualStackID,
                    ChildrenParallelizable = src.ChildrenParallelizable
                };
                
                TabbedText.WriteLine(
                    $"[FLAG‑SET] id={copy.ID,4}  ChildrenParallelizable={copy.ChildrenParallelizable}  "
                  + $"(copied from parentID={src.ID,4}) in MetaFrom");

                return copy;
            }

            /* ── 2. create the Conditional gate ──────────────────────────────── */
            var gate = new NWayTreeStorageInternal<ArrayCommandChunk>(leaf);
            gate.StoredValue = MetaFrom(leaf.StoredValue);
            gate.StoredValue.Name = "Conditional";
            gate.StoredValue.StartCommandRange = ifIdx;
            gate.StoredValue.EndCommandRangeExclusive = afterEnd;

#if DEBUG
            TabbedText.WriteLine($"[PARA‑CHK] leafID={leaf.StoredValue.ID,4}  "
                          + $"parent.ChildrenParallelizable={leaf.StoredValue.ChildrenParallelizable}  "
                          + $"gateID={gate.StoredValue.ID,4}  "
                          + $"gate.ChildrenParallelizable={gate.StoredValue.ChildrenParallelizable}");
#endif

            TabbedText.WriteLine($"[SPLIT‑GATE] parentLeafID={leaf.StoredValue.ID}  "
              + $"gateID={gate.StoredValue.ID}  "
              + $"parent.ChildrenParallelizable={leaf.StoredValue.ChildrenParallelizable}  "
              + $"gate.ChildrenParallelizable={gate.StoredValue.ChildrenParallelizable}");

            /* ── 3. optional postfix slice ───────────────────────────────────── */
            NWayTreeStorageInternal<ArrayCommandChunk> postfix = null;
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


        /// <summary>
        /// Replaces an oversize <paramref name="leaf"/> with a three‑slice split
        /// (prefix + gate + postfix), then slices the body inside the gate.
        /// </summary>
        private static void ReplaceLeafWithGate(
    ArrayCommandList acl,
    NWayTreeStorageInternal<ArrayCommandChunk> leaf,
    HoistPlanner.PlanEntry entry)
        {
            /* split the oversize leaf */
            var split = SplitOversizeLeaf(acl, leaf, entry.IfIdx, entry.EndIfIdx);

            /* attach gate & postfix FIRST */
            InsertSplitIntoTree(split);

            /* now wrap prefix (and postfix) so they become true leaves
               – prefix is inserted at branch 0, before the gate          */
            if (split.Prefix != null)
                WrapSliceIntoLeaf(acl, split.Prefix);

            if (split.Postfix != null)
                WrapSliceIntoLeaf(acl, split.Postfix);

            /* finally slice the big body inside the gate */
            SliceBodyIntoChildren(acl, split.Gate);
        }





        /// <summary>
        /// Turns <paramref name="slice"/>‑parent commands into a real leaf so that the
        /// parent becomes a pure container.  Intended for the Prefix or Postfix slice
        /// created by <see cref="SplitOversizeLeaf"/> when that slice still owns data‑
        /// bearing commands *and* now has child branches.
        /// </summary>
        private static void WrapSliceIntoLeaf(
    ArrayCommandList acl,
    NWayTreeStorageInternal<ArrayCommandChunk> sliceParent)
        {
            var parentChunk = sliceParent.StoredValue;
            if (parentChunk == null) return;
            if (parentChunk.StartCommandRange >= parentChunk.EndCommandRangeExclusive) return;

            /* 1️⃣  clone the metadata */
            var leafChunk = new ArrayCommandChunk
            {
                Name = parentChunk.Name + ".leaf",
                StartCommandRange = parentChunk.StartCommandRange,
                EndCommandRangeExclusive = parentChunk.EndCommandRangeExclusive,
                StartSourceIndices = parentChunk.StartSourceIndices,
                EndSourceIndicesExclusive = parentChunk.EndSourceIndicesExclusive,
                StartDestinationIndices = parentChunk.StartDestinationIndices,
                EndDestinationIndicesExclusive = parentChunk.EndDestinationIndicesExclusive,

                // virtual‑stack wiring
                VirtualStack = parentChunk.VirtualStack,
                VirtualStackID = parentChunk.VirtualStackID,
                ParentVirtualStack = parentChunk.ParentVirtualStack,
                ParentVirtualStackID = parentChunk.ParentVirtualStackID,

                // carry over analysis metadata unchanged
                TranslationToLocalIndex = parentChunk.TranslationToLocalIndex,
                IndicesReadFromStack = parentChunk.IndicesReadFromStack,
                IndicesInitiallySetInStack = parentChunk.IndicesInitiallySetInStack,
                CopyIncrementsToParent = parentChunk.CopyIncrementsToParent,
                FirstReadFromStack = parentChunk.FirstReadFromStack,
                FirstSetInStack = parentChunk.FirstSetInStack,
                LastSetInStack = parentChunk.LastSetInStack,
                LastUsed = parentChunk.LastUsed,

                // NEW: a real leaf executes sequentially → not parallelizable
                ChildrenParallelizable = false
            };

            /* 2️⃣ insert the leaf at branch 1 (before the gate) */
            const byte insertId = 1;
            if (parentChunk.LastChild >= insertId)
            {
                for (int b = parentChunk.LastChild; b >= insertId; b--)
                    sliceParent.SetBranch((byte)(b + 1), sliceParent.GetBranch((byte)b));
            }

            var newLeafNode = new NWayTreeStorageInternal<ArrayCommandChunk>(sliceParent)
            {
                StoredValue = leafChunk
            };
            sliceParent.SetBranch(insertId, newLeafNode);
            parentChunk.LastChild++;

            /* 3️⃣ parent becomes a pure container (unchanged from original code) */
            parentChunk.CopyIncrementsToParent = null;
            parentChunk.StartCommandRange = parentChunk.EndCommandRangeExclusive;
            parentChunk.StartSourceIndices =
                parentChunk.EndSourceIndicesExclusive = parentChunk.StartSourceIndices;
            parentChunk.StartDestinationIndices =
                parentChunk.EndDestinationIndicesExclusive = parentChunk.StartDestinationIndices;
            parentChunk.Name += ".container";

#if DEBUG
            TabbedText.WriteLine(
                $"[WRAP] parentID={parentChunk.ID}  newLeafID={leafChunk.ID}  "
              + $"insert@{insertId}  range=[{leafChunk.StartCommandRange},{leafChunk.EndCommandRangeExclusive})");
#endif
        }

        // ═════════════════════════════════════════════════════════════
        //  Internal helper – cut an If-body into ≤MaxCommandsPerChunk
        // ═════════════════════════════════════════════════════════════
        private static void SliceBodyIntoChildren(
            ArrayCommandList acl,
            NWayTreeStorageInternal<ArrayCommandChunk> gate)
        {
            /* locate the outermost If…EndIf inside this gate */
            (int ifIdx, int endIfIdx) = FindBodySpan(acl, gate.StoredValue);
            if (ifIdx < 0) return;

            /* 1️⃣ slice the body and wire children under the gate */
            var slices = CreateSlices(acl, gate, ifIdx, endIfIdx);

            /* 2️⃣ ensure every node (gate + slices) has stack metadata */
            BuildStackInfo(acl, gate, slices);

            /* 3️⃣ add CopyIncrementsToParent where a slice owns its own stack */
            AttachCopyLists(gate.StoredValue, slices);
        }

        /*──────────────────────── helper trio ───────────────────────*/

        private static (int ifIdx, int endIfIdx) FindBodySpan(
                ArrayCommandList acl, ArrayCommandChunk g)
            => FindOutermostIf(acl, g.StartCommandRange, g.EndCommandRangeExclusive);

        private static IList<NWayTreeStorageInternal<ArrayCommandChunk>> CreateSlices(
        ArrayCommandList acl,
        NWayTreeStorageInternal<ArrayCommandChunk> gate,
        int ifIdx,
        int endIfIdxExcl)
        {
            int bodyStart = ifIdx + 1, bodyEnd = endIfIdxExcl;         // EndIf excluded
            if (bodyStart >= bodyEnd)
                return Array.Empty<NWayTreeStorageInternal<ArrayCommandChunk>>();

            var slices = new List<NWayTreeStorageInternal<ArrayCommandChunk>>();
            var gInfo = gate.StoredValue;

            int curSrcIdx = gInfo.StartSourceIndices;
            int curDstIdx = gInfo.StartDestinationIndices;

            int slicePos = bodyStart;
            byte bId = 1;                                         // 0 = prefix slice

            while (slicePos < bodyEnd && bId < byte.MaxValue)
            {
                /* cut only at outer-level (depth-1) boundaries */
                int sliceEnd = FindSliceEnd(acl, slicePos, bodyEnd, acl.MaxCommandsPerChunk);

                /* if that fragment itself exceeds the chunk limit, recurse */
                if (sliceEnd - slicePos > acl.MaxCommandsPerChunk)
                {
                    var oversize = MakeChildChunk(acl, gate, gInfo, slicePos, sliceEnd);
                    gate.SetBranch(bId++, oversize);
                    SliceBodyIntoChildren(acl, oversize);          // ← recursive split
                    slicePos = sliceEnd;
                    continue;
                }

                /* count pointer advances inside this slice */
                int srcIncr = 0, dstIncr = 0;
                for (int i = slicePos; i < sliceEnd; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.NextSource) srcIncr++;
                    if (t == ArrayCommandType.NextDestination) dstIncr++;
                }

                /* create child slice & wire into tree */
                var child = MakeChildChunk(acl, gate, gInfo, slicePos, sliceEnd);
                var info = child.StoredValue;

                info.RequiresPrivateStack = true;
                info.StartSourceIndices = curSrcIdx;
                info.EndSourceIndicesExclusive = curSrcIdx + srcIncr;
                info.StartDestinationIndices = curDstIdx;
                info.EndDestinationIndicesExclusive = curDstIdx + dstIncr;

                gate.SetBranch(bId++, child);
                slices.Add(child);

                curSrcIdx += srcIncr;
                curDstIdx += dstIncr;
                slicePos = sliceEnd;
            }

            /* gate now spans If … EndIf inclusive */
            gInfo.EndCommandRangeExclusive = endIfIdxExcl + 1;
            gInfo.LastChild = (byte)(bId - 1);
            gInfo.Skip = false;    // gate must run once

            return slices;
        }


        private static void BuildStackInfo(
                ArrayCommandList acl,
                NWayTreeStorageInternal<ArrayCommandChunk> gate,
                IEnumerable<NWayTreeStorageInternal<ArrayCommandChunk>> slices)
        {
            acl.SetupVirtualStack(gate);
            acl.SetupVirtualStackRelationships(gate);

            foreach (var s in slices)
            {
                acl.SetupVirtualStack(s);
                acl.SetupVirtualStackRelationships(s);
            }
        }

        private static void AttachCopyLists(
                ArrayCommandChunk gateInfo,
                IList<NWayTreeStorageInternal<ArrayCommandChunk>> slices)
        {
            var unionForGate = new HashSet<int>();

            foreach (var slice in slices)
            {
                var info = slice.StoredValue;

                if (ReferenceEquals(info.VirtualStack, gateInfo.ParentVirtualStack))
                    continue;                   // shared root stack → no merge list

                bool sharesParent = ReferenceEquals(info.VirtualStack, info.ParentVirtualStack);
                bool neverWrites = info.LastSetInStack == null;

                if (sharesParent || info.ParentVirtualStack == null || neverWrites)
                    continue;

                var toCopy = new List<int>();
                for (int i = 0; i < info.LastSetInStack.Length; i++)
                    if (info.LastSetInStack[i] != null) toCopy.Add(i);

                if (toCopy.Count == 0) continue;

                info.CopyIncrementsToParent = toCopy.ToArray();
                foreach (int idx in toCopy) unionForGate.Add(idx);
            }

            bool gateHasPrivateStack =
                    gateInfo.ParentVirtualStack != null &&
                   !ReferenceEquals(gateInfo.VirtualStack, gateInfo.ParentVirtualStack);

            if (unionForGate.Count > 0 && gateHasPrivateStack)
                gateInfo.CopyIncrementsToParent = unionForGate.ToArray();
        }

        /*──────────────────────── leaf helpers ──────────────────────*/

        /// Decide the end (exclusive) of the next slice.
        ///
        ///  •  slicePos ........ first cmd of body still to place (depth == 1)
        ///  •  bodyEnd ......... index of the gate’s matching EndIf  (depth→0)
        ///  •  limit ........... MaxCommandsPerChunk
        ///
        /// Returns the last command *exclusive* so that
        ///   – cut is at depth 1
        ///   – cut is **not** an EndIf
        ///   – the slice has ≤ limit commands, or it spans to bodyEnd if no
        ///     depth-1 boundary exists inside the limit.
        /*──────────────────────── leaf helpers ──────────────────────*/
        private static int FindSliceEnd(
                ArrayCommandList acl, int sliceStart, int bodyEnd, int max)
        {
            int depth = 1;                 // we are inside the outer If-body
            int seen = 0;                 // commands since sliceStart
            int lastOuter = sliceStart;        // last depth-1, non-EndIf boundary
            Span<ArrayCommand> cmds = acl.UnderlyingCommands;

            for (int i = sliceStart; i < bodyEnd; i++)
            {
                var t = cmds[i].CommandType;

                if (t == ArrayCommandType.If) depth++;
                if (t == ArrayCommandType.EndIf) depth--;

                seen++;

                /* remember the most recent OUTER-LEVEL position that isn’t an EndIf */
                if (depth == 1 && t != ArrayCommandType.EndIf)
                    lastOuter = i + 1;            // +1 because slice end is *exclusive*

                /* size limit reached → cut at safest remembered boundary            */
                if (seen >= max && lastOuter > sliceStart)
                    return lastOuter;

                /* reached the matching EndIf → body finished                        */
                if (depth == 0)
                    return i;                     // slice ends right before EndIf
            }
            return bodyEnd;                       // fallback (shouldn’t occur)
        }



        private static NWayTreeStorageInternal<ArrayCommandChunk> MakeChildChunk(
                ArrayCommandList acl,
                NWayTreeStorageInternal<ArrayCommandChunk> gate,
                ArrayCommandChunk gInfo,
                int sliceStart,
                int sliceEnd)
        {
            var child = new NWayTreeStorageInternal<ArrayCommandChunk>(gate);
            child.StoredValue = new ArrayCommandChunk
            {
                ChildrenParallelizable = false,
                StartCommandRange = sliceStart,
                EndCommandRangeExclusive = sliceEnd,

                StartSourceIndices = gInfo.StartSourceIndices,
                EndSourceIndicesExclusive = gInfo.EndSourceIndicesExclusive,
                StartDestinationIndices = gInfo.StartDestinationIndices,
                EndDestinationIndicesExclusive = gInfo.EndDestinationIndicesExclusive,

                ExecId = acl.NextExecId()   // see accessor note below
            };
            return child;
        }

        internal static (int ifIdx, int endIfIdx) FindOutermostIf(
        ArrayCommandList acl, int fromCmd, int toCmd)
        {
            int depth = 0;
            int ifIdx = -1;

            var cmds = acl.UnderlyingCommands;

            for (int i = fromCmd; i < toCmd; i++)
            {
                var t = cmds[i].CommandType;

                if (t == ArrayCommandType.If)
                {
                    if (depth == 0 && ifIdx == -1) ifIdx = i;   // first outer-level If
                    depth++;
                }
                else if (t == ArrayCommandType.EndIf)
                {
                    depth--;
                    if (depth == 0 && ifIdx != -1)
                        return (ifIdx, i);                      // found matching EndIf
                }
            }
            return (-1, -1);                                    // no pair found
        }

    }
}
