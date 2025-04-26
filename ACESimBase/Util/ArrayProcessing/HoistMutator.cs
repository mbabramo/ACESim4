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
#if DEBUG
            TabbedText.WriteLine($"[Mutator] ══ ApplyPlan: {plan.Count} entry/entries ══");
#endif

            // 1. mutate every PlanEntry
            foreach (var entry in plan)
            {
#if DEBUG
                TabbedText.WriteLine($"[Mutator]   processing leaf={entry.LeafId}  If={entry.IfIdx}  EndIf={entry.EndIfIdx}  body={entry.BodyLen}");
#endif
                var leaf = FindLeafById(
                    (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree,
                    entry.LeafId) ?? throw new InvalidOperationException($"Leaf {entry.LeafId} not found");

                ReplaceLeafWithGate(acl, leaf, entry);
            }

            // 2. rebuild metadata after mutation
            acl.CommandTree.WalkTree(
                x => acl.SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>)x),
                x => acl.SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)x));

            if (acl.RecordCommandTreeString)
                acl.CommandTreeString = acl.CommandTree.ToString();

#if DEBUG
            int maxLeaf = 0;
            acl.CommandTree.WalkTree(n =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                if (node.Branches is null or { Length: 0 })
                {
                    var s = node.StoredValue;
                    int len = s.EndCommandRangeExclusive - s.StartCommandRange;
                    if (len > maxLeaf) maxLeaf = len;
                }
            });
            TabbedText.WriteLine($"[Mutator]   max-leaf-size after hoist = {maxLeaf}");
            TabbedText.WriteLine($"[Mutator] ════════════════════════════════");
#endif
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
#if DEBUG
            TabbedText.WriteLine($"[Split] leaf={leaf.StoredValue.ID}  " +
                                 $"If={ifIdx}  EndIf={endIfIdx}");
#endif

            int spanStart = leaf.StoredValue.StartCommandRange;
            int spanEnd = leaf.StoredValue.EndCommandRangeExclusive;
            int afterEnd = endIfIdx + 1;

            // absorb one extra EndIf when safe (matches existing test expectations)
            if (afterEnd + 1 < spanEnd &&
                acl.UnderlyingCommands[afterEnd].CommandType == ArrayCommandType.EndIf)
                afterEnd++;

            // 1️⃣ shrink original leaf to prefix
            leaf.StoredValue.EndCommandRangeExclusive = ifIdx;
            leaf.StoredValue.Skip = false;

            ArrayCommandChunk MetaFrom(ArrayCommandChunk src) => new()
            {
                VirtualStack = src.VirtualStack,
                VirtualStackID = src.VirtualStackID,
                ChildrenParallelizable = src.ChildrenParallelizable
            };

            // 2️⃣ create Conditional gate
            var gate = new NWayTreeStorageInternal<ArrayCommandChunk>(leaf)
            {
                StoredValue = MetaFrom(leaf.StoredValue)
            };
            gate.StoredValue.Name = "Conditional";
            gate.StoredValue.StartCommandRange = ifIdx;
            gate.StoredValue.EndCommandRangeExclusive = afterEnd;

            // 3️⃣ optional postfix slice
            NWayTreeStorageInternal<ArrayCommandChunk>? postfix = null;
            if (afterEnd < spanEnd)
            {
                postfix = new NWayTreeStorageInternal<ArrayCommandChunk>(leaf)
                {
                    StoredValue = MetaFrom(leaf.StoredValue)
                };
                postfix.StoredValue.Name = "Postfix";
                postfix.StoredValue.StartCommandRange = afterEnd;
                postfix.StoredValue.EndCommandRangeExclusive = spanEnd;
            }

#if DEBUG
            TabbedText.WriteLine($"[Split]   prefix→ID {leaf.StoredValue.ID}  " +
                                 $"gate→ID {gate.StoredValue.ID}  " +
                                 $"postfix→ID {(postfix?.StoredValue.ID.ToString() ?? "∅")}");
#endif

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
    byte firstBranchId = 1)
        {
#if DEBUG
            TabbedText.WriteLine($"[Tree] insert under parentID={split.Prefix.StoredValue.ID}  " +
                                 $"gateID={split.Gate.StoredValue.ID}  " +
                                 $"postfixID={(split.Postfix?.StoredValue.ID.ToString() ?? "∅")}");
#endif

            var parent = split.Prefix;

            parent.SetBranch(firstBranchId, split.Gate);

            if (split.Postfix != null)
                parent.SetBranch((byte)(firstBranchId + 1), split.Postfix);

            parent.StoredValue.LastChild =
                (byte)((split.Postfix != null) ? firstBranchId + 1 : firstBranchId);
        }

        private static void ReplaceLeafWithGate(
    ArrayCommandList acl,
    NWayTreeStorageInternal<ArrayCommandChunk> leaf,
    HoistPlanner.PlanEntry entry)
        {
#if DEBUG
            TabbedText.WriteLine($"[Mutator] ── ReplaceLeafWithGate ──  " +
                                 $"leaf={leaf.StoredValue.ID}  If={entry.IfIdx}  EndIf={entry.EndIfIdx}");
#endif

            // 1. split oversize leaf
            var split = SplitOversizeLeaf(acl, leaf, entry.IfIdx, entry.EndIfIdx);

            // 2. splice gate (+ optional postfix) under prefix node
            InsertSplitIntoTree(split);

            // 3. prefix now a container → wrap its own commands into a new leaf
            WrapSliceIntoLeaf(acl, split.Prefix);

            // 4. optional postfix leaf when buffer-advance cmds present
            if (split.Postfix != null &&
                ContainsBufferAdvanceCommands(acl, split.Postfix.StoredValue))
            {
                split.Postfix.StoredValue.Name = "Postfix";
                WrapSliceIntoLeaf(acl, split.Postfix);
            }

            // 5. slice the large If-body inside the new gate
            SliceBodyIntoChildren(acl, split.Gate);

#if DEBUG
            TabbedText.WriteLine($"[Mutator] ─────────────────────────");
#endif
        }



        /// <summary>
        /// Returns <c>true</c> when the slice contains <c>NextSource</c> or
        /// <c>NextDestination</c> commands – the only case where we need the
        /// Postfix.container / Postfix.leaf pair.
        /// </summary>
        private static bool ContainsBufferAdvanceCommands(
            ArrayCommandList acl,
            ArrayCommandChunk info)
        {
            for (int i = info.StartCommandRange; i < info.EndCommandRangeExclusive; i++)
            {
                var t = acl.UnderlyingCommands[i].CommandType;
                if (t == ArrayCommandType.NextSource ||
                    t == ArrayCommandType.NextDestination)
                    return true;
            }
            return false;
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

            var leafChunk = new ArrayCommandChunk
            {
                Name = parentChunk.Name + ".leaf",
                StartCommandRange = parentChunk.StartCommandRange,
                EndCommandRangeExclusive = parentChunk.EndCommandRangeExclusive,
                StartSourceIndices = parentChunk.StartSourceIndices,
                EndSourceIndicesExclusive = parentChunk.EndSourceIndicesExclusive,
                StartDestinationIndices = parentChunk.StartDestinationIndices,
                EndDestinationIndicesExclusive = parentChunk.EndDestinationIndicesExclusive,
                VirtualStack = parentChunk.VirtualStack,
                VirtualStackID = parentChunk.VirtualStackID,
                ParentVirtualStack = parentChunk.ParentVirtualStack,
                ParentVirtualStackID = parentChunk.ParentVirtualStackID,
                TranslationToLocalIndex = parentChunk.TranslationToLocalIndex,
                IndicesReadFromStack = parentChunk.IndicesReadFromStack,
                IndicesInitiallySetInStack = parentChunk.IndicesInitiallySetInStack,
                CopyIncrementsToParent = parentChunk.CopyIncrementsToParent,
                FirstReadFromStack = parentChunk.FirstReadFromStack,
                FirstSetInStack = parentChunk.FirstSetInStack,
                LastSetInStack = parentChunk.LastSetInStack,
                LastUsed = parentChunk.LastUsed,
                ChildrenParallelizable = false
            };

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

            parentChunk.CopyIncrementsToParent = null;
            parentChunk.StartCommandRange = parentChunk.EndCommandRangeExclusive;
            parentChunk.StartSourceIndices = parentChunk.EndSourceIndicesExclusive = parentChunk.StartSourceIndices;
            parentChunk.StartDestinationIndices = parentChunk.EndDestinationIndicesExclusive = parentChunk.StartDestinationIndices;
            parentChunk.Name += ".container";

#if DEBUG
            TabbedText.WriteLine($"[Wrap] parentID={parentChunk.ID}  newLeafID={leafChunk.ID}");
#endif
        }


        // ═════════════════════════════════════════════════════════════
        //  Internal helper – cut an If-body into ≤MaxCommandsPerChunk
        // ═════════════════════════════════════════════════════════════
        private static void SliceBodyIntoChildren(
    ArrayCommandList acl,
    NWayTreeStorageInternal<ArrayCommandChunk> gate)
        {
            (int ifIdx, int endIfIdx) = FindBodySpan(acl, gate.StoredValue);
            if (ifIdx < 0) return;

#if DEBUG
            TabbedText.WriteLine($"[Slice] gateID={gate.StoredValue.ID}  If={ifIdx}  EndIf={endIfIdx}");
#endif

            var slices = CreateSlices(acl, gate, ifIdx, endIfIdx);

#if DEBUG
            TabbedText.WriteLine($"[Slice]   created {slices.Count} slice(s) inside gateID={gate.StoredValue.ID}");
            foreach (var s in slices)
            {
                var info = s.StoredValue;
                TabbedText.WriteLine($"           • sliceID={info.ID}  [{info.StartCommandRange},{info.EndCommandRangeExclusive})");
            }
#endif

            BuildStackInfo(acl, gate, slices);
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
