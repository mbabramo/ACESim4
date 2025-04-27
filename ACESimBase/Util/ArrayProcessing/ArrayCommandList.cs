// -----------------------------------------------------------------------------
//  ArrayCommandList.cs
// -----------------------------------------------------------------------------
//  Holds the command buffer while it is being authored and builds the hierarchical
//  "chunk" tree that execution‑time components traverse.  No run‑time execution
//  happens in this class.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.Util.ArrayProcessing
{
    [Serializable]
    public partial class ArrayCommandList
    {
        // ──────────────────────────────────────────────────────────────────────
        //  Command buffer and scratch‑slot counters
        // ──────────────────────────────────────────────────────────────────────
        public ArrayCommand[] UnderlyingCommands;
        public int NextCommandIndex;
        public int MaxCommandIndex;

        public int FirstScratchIndex;
        public int NextArrayIndex;
        public int MaxArrayIndex;
        public int FullArraySize => FirstScratchIndex + (DoParallel ? 0 : MaxArrayIndex);

        // ──────────────────────────────────────────────────────────────────────
        //  Ordered‑buffer index lists (filled at author‑time only)
        // ──────────────────────────────────────────────────────────────────────
        public List<int> OrderedSourceIndices = new();
        public List<int> OrderedDestinationIndices = new();
        public Dictionary<int, int> ReusableOrderedDestinationIndices = new();

        // ──────────────────────────────────────────────────────────────────────
        //  Settings and feature flags
        // ──────────────────────────────────────────────────────────────────────
        public bool DisableAdvancedFeatures = false;
        public bool Parallelize = false;
        public int MaxCommandsPerSplittableChunk = 1_000;
        public bool UseOrderedSources => !DisableAdvancedFeatures;
        public bool UseOrderedDestinations => !DisableAdvancedFeatures;
        public bool DoParallel => !DisableAdvancedFeatures && Parallelize;
        public bool ReuseScratchSlots => MaxCommandsPerSplittableChunk == int.MaxValue;
        public bool ReuseDestinations = false;
        public bool RepeatIdenticalRanges => ReuseScratchSlots && !DisableAdvancedFeatures;

        // ──────────────────────────────────────────────────────────────────────
        //  Chunk tree
        // ──────────────────────────────────────────────────────────────────────
        public NWayTreeStorageInternal<ArrayCommandChunk> CommandTree;
        internal bool RecordCommandTreeString = false;
        internal string CommandTreeString;
        private readonly List<byte> _currentPath = new();
        private int _nextVirtualStackID = 0;

        private NWayTreeStorageInternal<ArrayCommandChunk> CurrentNode =>
            (NWayTreeStorageInternal<ArrayCommandChunk>)CommandTree.GetNode(_currentPath);
        private ArrayCommandChunk CurrentChunk => CurrentNode.StoredValue;

        // ──────────────────────────────────────────────────────────────────────
        // Checkpoints: If we want to figure out why the compiled code is not working, we can use checkpoints. 
        // Wherever the command is copy to index -2, that will be interpreted as an instruction to add the value
        // to the checkpoints list. We can then, for example, compare the checkpoints with checkpoints from noncompiled
        // code or from code not using the ArrayCommandList to see where the values differ.
        // ──────────────────────────────────────────────────────────────────────
        // NOTE: This is not currently implemented.
        public bool UseCheckpoints = false;
        public static int CheckpointTrigger = -2; // -1 is used for other purposes, and must be negative
        public List<double> Checkpoints;
        private int _nextExecId = 0;
        internal int NextExecId() => _nextExecId++;

        // ──────────────────────────────────────────────────────────────────────
        //  Author‑time helpers
        // ──────────────────────────────────────────────────────────────────────
        private readonly Stack<int> _depthStartSlots = new();
        private readonly Stack<int?> _repeatRangeStack = new();
        public bool RepeatingExistingCommandRange = false;
        private int _keepTogetherLevel = 0;
        private bool _rootChunkInitialised = false;


        // ──────────────────────────────────────────────────────────────────────
        //  Construction
        // ──────────────────────────────────────────────────────────────────────
        public ArrayCommandList(int maxNumCommands, int initialArrayIndex, bool parallelize)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];

            FirstScratchIndex = initialArrayIndex;
            NextArrayIndex = initialArrayIndex;
            MaxArrayIndex = initialArrayIndex - 1;

            Parallelize = parallelize;

            CommandTree = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            CommandTree.StoredValue = new ArrayCommandChunk
            {
                ChildrenParallelizable = false,
                StartCommandRange = 0,
                StartSourceIndices = 0,
                StartDestinationIndices = 0
            };
        }


        // ──────────────────────────────────────────────────────────────────────
        //  Chunk helpers
        // ──────────────────────────────────────────────────────────────────────
        public void StartCommandChunk(bool runChildrenInParallel, int? identicalStartCommandRange, string name = "", bool ignoreKeepTogether = false)
        {
            if (_currentPath.Count == 0
                && !_rootChunkInitialised
                && string.Equals(name, "root", StringComparison.OrdinalIgnoreCase))  // ← tighten guard
            {
                var root = CurrentChunk;
                if (!string.IsNullOrEmpty(name))
                    root.Name = name;
                root.ChildrenParallelizable = runChildrenInParallel;
                _rootChunkInitialised = true;
                return;
            }

            if (_keepTogetherLevel > 0 && !ignoreKeepTogether)
                return;

            if (RepeatIdenticalRanges && identicalStartCommandRange is int identical)
            {
                _repeatRangeStack.Push(identical);
                NextCommandIndex = identical;
                RepeatingExistingCommandRange = true;
            }

            var parent = CurrentNode;
            byte branch = (byte)(parent.StoredValue.LastChild + 1);

            var child = new NWayTreeStorageInternal<ArrayCommandChunk>(parent);
            child.StoredValue = new ArrayCommandChunk
            {
                Name = name,
                ChildrenParallelizable = runChildrenInParallel,
                StartCommandRange = NextCommandIndex,
                StartSourceIndices = OrderedSourceIndices.Count,
                StartDestinationIndices = OrderedDestinationIndices.Count
            };

            parent.SetBranch(branch, child);
            parent.StoredValue.LastChild = branch;
            _currentPath.Add(branch);
        }

        public void EndCommandChunk(int[] copyIncrementsToParent = null, bool endingRepeatedChunk = false)
        {
            if (_currentPath.Count == 0)
            {
                var root = CurrentChunk;
                root.EndCommandRangeExclusive = NextCommandIndex;
                root.EndSourceIndicesExclusive = OrderedSourceIndices.Count;
                root.EndDestinationIndicesExclusive = OrderedDestinationIndices.Count;
                return;            // nothing else to pop
            }
            if (_keepTogetherLevel > 0)
                return;

            if (_currentPath.Count == 0)
            {
                // We’re already at the root; nothing to pop or update.
                return;
            }

            var finished = CurrentChunk;
            finished.EndCommandRangeExclusive = NextCommandIndex;
            finished.EndSourceIndicesExclusive = OrderedSourceIndices.Count;
            finished.EndDestinationIndicesExclusive = OrderedDestinationIndices.Count;
            finished.CopyIncrementsToParent = copyIncrementsToParent;

            _currentPath.RemoveAt(_currentPath.Count - 1);
            var parent = CurrentChunk;
            parent.EndCommandRangeExclusive = NextCommandIndex;
            parent.EndSourceIndicesExclusive = OrderedSourceIndices.Count;
            parent.EndDestinationIndicesExclusive = OrderedDestinationIndices.Count;

            if (endingRepeatedChunk && RepeatIdenticalRanges && _repeatRangeStack.Any())
            {
                _repeatRangeStack.Pop();
                if (_repeatRangeStack.Count == 0) RepeatingExistingCommandRange = false;
            }
        }

        public void KeepCommandsTogether() => _keepTogetherLevel++;
        public void EndKeepCommandsTogether() => _keepTogetherLevel--;

        // ──────────────────────────────────────────────────────────────────────
        //  Finalisation passes
        // ──────────────────────────────────────────────────────────────────────
        public void CompleteCommandList()
        {
            MaxCommandIndex = NextCommandIndex;
            VerifyCorrectness();
            while (_currentPath.Count > 0) EndCommandChunk();
            CompleteCommandTree();
        }



        /// <summary>
        /// Ensures structural soundness of the recorded command buffer.
        /// Rules checked:
        ///   ① the number of <c>If</c> commands equals the number of <c>EndIf</c> commands;
        ///   ② the number of <c>IncrementDepth</c> equals <c>DecrementDepth</c>;
        ///   ③ every comparison command is immediately followed by an <c>If</c>.
        /// </summary>
        public void VerifyCorrectness()
        {
            int ifs = 0, endIfs = 0;
            int incDepths = 0, decDepths = 0;

            bool IsComparison(ArrayCommandType t) => t switch
            {
                ArrayCommandType.EqualsOtherArrayIndex or
                ArrayCommandType.NotEqualsOtherArrayIndex or
                ArrayCommandType.GreaterThanOtherArrayIndex or
                ArrayCommandType.LessThanOtherArrayIndex or
                ArrayCommandType.EqualsValue or
                ArrayCommandType.NotEqualsValue => true,
                _ => false
            };

            for (int idx = 0; idx < NextCommandIndex; idx++)
            {
                var t = UnderlyingCommands[idx].CommandType;

                switch (t)
                {
                    case ArrayCommandType.If: ifs++; break;
                    case ArrayCommandType.EndIf: endIfs++; break;
                    case ArrayCommandType.IncrementDepth: incDepths++; break;
                    case ArrayCommandType.DecrementDepth: decDepths++; break;
                }

                if (IsComparison(t))
                {
                    bool followedByIf =
                        idx + 1 < NextCommandIndex &&
                        UnderlyingCommands[idx + 1].CommandType == ArrayCommandType.If;

                    if (!followedByIf)
                        throw new InvalidOperationException(
                            $"Comparison at command {idx} is not immediately followed by an If.");
                }
            }

            if (ifs != endIfs)
                throw new InvalidOperationException($"Mismatch: If={ifs}  EndIf={endIfs}");

            if (incDepths != decDepths)
                throw new InvalidOperationException(
                    $"Mismatch: IncrementDepth={incDepths}  DecrementDepth={decDepths}");
        }

        public void FinaliseCommandTree()
        {
            CommandTree.WalkTree(n => InsertMissingBranches((NWayTreeStorageInternal<ArrayCommandChunk>)n));
            HoistAndSplitLargeIfBodies();
            CommandTree.WalkTree(null, n => SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>)n));
            CommandTree.WalkTree(n => SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)n));
        }

        private void CompleteCommandTree()
        {
            CommandTree.WalkTree(n => InsertMissingBranches((NWayTreeStorageInternal<ArrayCommandChunk>)n));
            HoistAndSplitLargeIfBodies();
            CommandTree.WalkTree(null, n => SetupVirtualStack((NWayTreeStorageInternal<ArrayCommandChunk>)n));
            CommandTree.WalkTree(n => SetupVirtualStackRelationships((NWayTreeStorageInternal<ArrayCommandChunk>)n));
        }

        public string CommandListString()
        {
            if (UnderlyingCommands == null || UnderlyingCommands.Length == 0)
                return string.Empty;

            var stringBuilder = new StringBuilder();
            for (int i = 0; i < NextCommandIndex; i++)
            {
                stringBuilder.AppendLine($"{i}: {UnderlyingCommands[i]}");
            }

            return stringBuilder.ToString();
        }


        // ──────────────────────────────────────────────────────────────────────
        //  Tree‑maintenance helpers (copied from original implementation)
        // ──────────────────────────────────────────────────────────────────────
        // fills the Branches array with existing children plus any gap/tail nodes
        private void InsertMissingBranches(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            byte lastChild = node.StoredValue.LastChild;
            if (lastChild == 0) return;

#if DEBUG
            TabbedText.WriteLine(
                $"[BRANCHES] parent ID{node.StoredValue.ID}  lastChild={lastChild}");
#endif

            var children = new List<NWayTreeStorageInternal<ArrayCommandChunk>>();
            int curCmd = node.StoredValue.StartCommandRange;
            int curSrc = node.StoredValue.StartSourceIndices;
            int curDst = node.StoredValue.StartDestinationIndices;

            for (byte c = 1; c <= lastChild; c++)
            {
                var branch = (NWayTreeStorageInternal<ArrayCommandChunk>)node.GetBranch(c);
                var bVal = branch.StoredValue;

                if (bVal.StartCommandRange > curCmd)
                {
                    var gap = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                    gap.StoredValue = new ArrayCommandChunk
                    {
                        ChildrenParallelizable = false,
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = bVal.StartCommandRange,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = bVal.StartSourceIndices,
                        StartDestinationIndices = curDst,
                        EndDestinationIndicesExclusive = bVal.StartDestinationIndices
                    };
                    children.Add(gap);

#if DEBUG
                    TabbedText.WriteLine(
                        $"    ↳ [GAP] ID{gap.StoredValue.ID} cmds=[{curCmd},{bVal.StartCommandRange})");
#endif
                }

                children.Add(branch);
                curCmd = bVal.EndCommandRangeExclusive;
                curSrc = bVal.EndSourceIndicesExclusive;
                curDst = bVal.EndDestinationIndicesExclusive;

                if (c == lastChild &&
                    (curCmd < node.StoredValue.EndCommandRangeExclusive ||
                     curSrc < node.StoredValue.EndSourceIndicesExclusive ||
                     curDst < node.StoredValue.EndDestinationIndicesExclusive))
                {
                    var tail = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                    tail.StoredValue = new ArrayCommandChunk
                    {
                        ChildrenParallelizable = false,
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = node.StoredValue.EndCommandRangeExclusive,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = node.StoredValue.EndSourceIndicesExclusive,
                        StartDestinationIndices = curDst,
                        EndDestinationIndicesExclusive = node.StoredValue.EndDestinationIndicesExclusive
                    };
                    children.Add(tail);

#if DEBUG
                    TabbedText.WriteLine(
                        $"    ↳ [TAIL] ID{tail.StoredValue.ID} cmds=[{curCmd},{tail.StoredValue.EndCommandRangeExclusive})");
#endif
                }
            }

            node.Branches = children.ToArray();

#if DEBUG
            TabbedText.WriteLine(
                $"[BRANCHES-END] parent ID{node.StoredValue.ID}  branches={node.Branches.Length}");
#endif
        }


        /// <summary>
        /// Hoist all oversize <c>If … EndIf</c> bodies until every executable
        /// leaf is ≤ <see cref="MaxCommandsPerSplittableChunk"/>.  
        /// The loop re-runs the planner after each mutation so newly created
        /// slices are re-examined, guaranteeing convergence without deep recursion.
        /// </summary>
        private void HoistAndSplitLargeIfBodies()
        {
            if (MaxCommandsPerSplittableChunk == int.MaxValue)
                return;               // hoisting disabled

            HoistMutator.MutateUntilAsBalancedAsPossible(this);
        }



        // ──────────────────────────────────────────────────────────────────────
        //  Virtual‑stack allocation and relationships
        // ──────────────────────────────────────────────────────────────────────
        public void SetupVirtualStack(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            var c = node.StoredValue;

            c.VirtualStack = new double[MaxArrayIndex + 1];
            c.VirtualStackID = _nextVirtualStackID++;

            if (node.Branches == null || node.Branches.Length == 0)
            {
                c.FirstReadFromStack = new int?[MaxArrayIndex + 1];
                c.FirstSetInStack = new int?[MaxArrayIndex + 1];
                c.LastSetInStack = new int?[MaxArrayIndex + 1];
                c.LastUsed = new int?[MaxArrayIndex + 1];
                c.TranslationToLocalIndex = new int?[MaxArrayIndex + 1];

                (c.IndicesReadFromStack, c.IndicesInitiallySetInStack) =
                    DetermineWhenIndicesFirstLastUsed(
                        c.StartCommandRange,
                        c.EndCommandRangeExclusive,
                        c.FirstReadFromStack,
                        c.FirstSetInStack,
                        c.LastSetInStack,
                        c.LastUsed,
                        c.TranslationToLocalIndex);
            }
            else
            {
                DetermineSourcesUsedFromChildren(node);
            }
        }

        public void SetupVirtualStackRelationships(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            var parentNode = (NWayTreeStorageInternal<ArrayCommandChunk>)node.Parent;
            if (parentNode == null) return;

            var child = node.StoredValue;
            var parent = parentNode.StoredValue;

            // share only when *both* slices are single-threaded
            bool share = !parent.ChildrenParallelizable
                         && !child.ChildrenParallelizable
                         && !child.RequiresPrivateStack;

            if (share)
            {
                child.VirtualStack = parent.VirtualStack;
                child.VirtualStackID = parent.VirtualStackID;
            }

            child.ParentVirtualStack = parent.VirtualStack;
            child.ParentVirtualStackID = parent.VirtualStackID;
        }

        private void DetermineSourcesUsedFromChildren(NWayTreeStorageInternal<ArrayCommandChunk> node)
        {
            var c = node.StoredValue;
            var used = new HashSet<int>();

            if (node.Branches == null) return;

            foreach (var br in node.Branches)
            {
                if (br?.StoredValue?.IndicesReadFromStack == null) continue;
                foreach (int idx in br.StoredValue.IndicesReadFromStack)
                    used.Add(idx);
            }

            c.IndicesReadFromStack = used.OrderBy(x => x).ToArray();
            c.IndicesInitiallySetInStack = null;
        }

        private (int[] indicesReadFromStack, int[] indicesSetInStack) DetermineWhenIndicesFirstLastUsed(
            int startRange,
            int endRangeExclusive,
            int?[] firstRead,
            int?[] firstSet,
            int?[] lastSet,
            int?[] lastUsed,
            int?[] transToLocal)
        {
            var used = new HashSet<int>();

            for (int cmdIdx = startRange; cmdIdx < endRangeExclusive; cmdIdx++)
            {
                int src = UnderlyingCommands[cmdIdx].GetSourceIndexIfUsed();
                if (src != -1)
                {
                    if (firstRead[src] == null && firstSet[src] == null)
                        firstRead[src] = cmdIdx;
                    lastUsed[src] = cmdIdx;
                    used.Add(src);
                }

                int tgt = UnderlyingCommands[cmdIdx].GetTargetIndexIfUsed();
                if (tgt != -1)
                {
                    if (firstRead[tgt] == null && firstSet[tgt] == null)
                        firstSet[tgt] = cmdIdx;
                    lastSet[tgt] = cmdIdx;
                    lastUsed[tgt] = cmdIdx;
                    used.Add(tgt);
                }
            }

            var firstLast = used.Select(i => (idx: i, first: (firstRead[i] ?? firstSet[i]).Value, last: lastUsed[i].Value)).ToList();

            var cmdToUses = new Dictionary<int, (HashSet<int> first, HashSet<int> last)>();
            foreach (var (idx, first, last) in firstLast)
            {
                if (!cmdToUses.ContainsKey(first)) cmdToUses[first] = (new(), new());
                if (!cmdToUses.ContainsKey(last)) cmdToUses[last] = (new(), new());
                cmdToUses[first].first.Add(idx);
                cmdToUses[last].last.Add(idx);
            }

            int lastLocal = -1;
            var avail = new Stack<int>();
            for (int cmd = startRange; cmd < endRangeExclusive; cmd++)
            {
                if (!cmdToUses.ContainsKey(cmd)) continue;
                var (firstUses, lastUses) = cmdToUses[cmd];
                foreach (int vIdx in firstUses)
                {
                    if (avail.Any())
                        transToLocal[vIdx] = avail.Pop();
                    else
                        transToLocal[vIdx] = ++lastLocal;
                }
                foreach (int vIdx in lastUses)
                    avail.Push(transToLocal[vIdx].Value);
            }

            int[] read = Enumerable.Range(0, firstRead.Length).Where(i => firstRead[i] != null).ToArray();
            int[] set = Enumerable.Range(0, firstSet.Length).Where(i => firstSet[i] != null).ToArray();
            return (read, set);
        }

        #region Command recorder

        // TODO: Eliminate the methods here, and then have the callers call the methods through the recorder.

        private CommandRecorder _recorder;
        public CommandRecorder Recorder => _recorder ??= new CommandRecorder(this);

        public int NewZero() => Recorder.NewZero();
        public int[] NewZeroArray(int size) => Recorder.NewZeroArray(size);
        public int NewUninitialized() => Recorder.NewUninitialized();
        public int[] NewUninitializedArray(int size) => Recorder.NewUninitializedArray(size);

        public int CopyToNew(int sourceIdx, bool fromOriginalSources) =>
            Recorder.CopyToNew(sourceIdx, fromOriginalSources);
        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources) =>
            Recorder.CopyToNew(sourceIndices, fromOriginalSources);

        public void CopyToExisting(int index, int sourceIndex) => Recorder.CopyToExisting(index, sourceIndex);
        public void CopyToExisting(int[] indices, int[] sourceIndices) => Recorder.CopyToExisting(indices, sourceIndices);

        public int AddToNew(int idx1, bool fromOriginalSources, int idx2) =>
            Recorder.AddToNew(idx1, fromOriginalSources, idx2);
        public int MultiplyToNew(int idx1, bool fromOriginalSources, int idx2) =>
            Recorder.MultiplyToNew(idx1, fromOriginalSources, idx2);

        public void ZeroExisting(int index) => Recorder.ZeroExisting(index);
        public void ZeroExisting(int[] indices) => Recorder.ZeroExisting(indices);

        public void MultiplyBy(int idx, int multIdx) => Recorder.MultiplyBy(idx, multIdx);
        public void MultiplyArrayBy(int[] targets, int multiplierIdx) =>
            Recorder.MultiplyArrayBy(targets, multiplierIdx);
        public void MultiplyArrayBy(int[] targets, int[] multipliers) =>
            Recorder.MultiplyArrayBy(targets, multipliers);

        public void Increment(int idx, bool targetOriginal, int indexOfIncrement) =>
            Recorder.Increment(idx, targetOriginal, indexOfIncrement);
        public void IncrementArrayBy(int[] targets, bool targetOriginals, int indexOfIncrement) =>
            Recorder.IncrementArrayBy(targets, targetOriginals, indexOfIncrement);
        public void IncrementArrayBy(int[] targets, bool targetOriginals, int[] indicesOfIncrements) =>
            Recorder.IncrementArrayBy(targets, targetOriginals, indicesOfIncrements);
        public void IncrementByProduct(int targetIdx, bool targetOriginal,
                                       int factor1Idx, int factor2Idx, bool reuseTmp = true) =>
            Recorder.IncrementByProduct(targetIdx, targetOriginal, factor1Idx, factor2Idx, reuseTmp);

        public void Decrement(int idx, int decIdx) => Recorder.Decrement(idx, decIdx);
        public void DecrementArrayBy(int[] targets, int decIdx) =>
            Recorder.DecrementArrayBy(targets, decIdx);
        public void DecrementArrayBy(int[] targets, int[] decIdxs) =>
            Recorder.DecrementArrayBy(targets, decIdxs);
        public void DecrementByProduct(int targetIdx, int factor1Idx, int factor2Idx, bool reuseTmp = true) =>
            Recorder.DecrementByProduct(targetIdx, factor1Idx, factor2Idx, reuseTmp);


        public void InsertTrueCommand() => Recorder.InsertTrue();
        public void InsertIfCommand() => Recorder.InsertIf();
        public void InsertEndIfCommand() => Recorder.InsertEndIf();

        public void InsertEqualsOtherArrayIndexCommand(int i1, int i2) =>
            Recorder.InsertEqualsOtherArrayIndexCommand(i1, i2);
        public void InsertNotEqualsOtherArrayIndexCommand(int i1, int i2) =>
            Recorder.InsertNotEqualsOtherArrayIndexCommand(i1, i2);
        public void InsertGreaterThanOtherArrayIndexCommand(int i1, int i2) =>
            Recorder.InsertGreaterThanOtherArrayIndexCommand(i1, i2);
        public void InsertLessThanOtherArrayIndexCommand(int i1, int i2) =>
            Recorder.InsertLessThanOtherArrayIndexCommand(i1, i2);

        public void InsertEqualsValueCommand(int idx, int v) =>
            Recorder.InsertEqualsValueCommand(idx, v);
        public void InsertNotEqualsValueCommand(int idx, int v) =>
            Recorder.InsertNotEqualsValueCommand(idx, v);

        public void InsertComment(string comment) => Recorder.InsertComment(comment);
        public void InsertBlankCommand() => Recorder.InsertBlankCommand();

        public void IncrementDepth() => Recorder.IncrementDepth();

        public void DecrementDepth(bool completeCommandList = false) => Recorder.DecrementDepth(completeCommandList);

        #endregion

        #region Checkpoints

        // Note: We have not yet implemented this since the chunk executors were factored out. This is here to give a sense of what the checkpoints did, in case we should reimplement it.
        public void CreateCheckpoint(int sourceIndex)
        {
            if (UseCheckpoints)
                CopyToExisting(CheckpointTrigger, sourceIndex);
        }

        public void ResetCheckpoints()
        {
            //if (!UseCheckpoints)
            //    return;

            //if (AutogenerateCode)
            //{
            //    var method = AutogeneratedCodeType.GetMethod("ResetCheckpoints");
            //    method.Invoke(null, null);
            //}
            //else
            //{
            //    Checkpoints = new List<double>();
            //}
        }

        public void LoadCheckpoints()
        {
            //if (!UseCheckpoints)
            //    return;
            //if (AutogenerateCode)
            //{
            //    var fields = AutogeneratedCodeType.GetFields();
            //    Checkpoints = (List<double>)fields[0].GetValue(null);
            //}
        }
        #endregion
    }
}
