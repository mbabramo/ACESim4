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
        public int NextCommandIndex => Recorder.NextCommandIndex;
        public int MaxCommandIndex;

        public double[] VirtualStack;
        // The virtual stack consists of original data (which may be mutated),
        // as well as scratch data for local variables. Thus, this is equal to the
        // size of the non-scratch data.
        public int SizeOfMainData;
        public Span<double> NonScratchData => VirtualStack.AsSpan(0, SizeOfMainData);
        public int NextArrayIndex => Recorder.NextArrayIndex;
        public int MaxArrayIndex => Recorder.MaxArrayIndex;
        public int VirtualStackSize => MaxArrayIndex + 1;

        // ──────────────────────────────────────────────────────────────────────
        //  Ordered‑buffer index lists (filled at author‑time only)
        // ──────────────────────────────────────────────────────────────────────
        public List<int> OrderedSourceIndices = new();

        // ──────────────────────────────────────────────────────────────────────
        //  Settings and feature flags
        // ──────────────────────────────────────────────────────────────────────
        public bool Parallelize = false;
        public int MaxCommandsPerSplittableChunk = 10_000;
        public bool ReuseScratchSlots => true;
        public bool RepeatIdenticalRanges => ReuseScratchSlots;

        // ──────────────────────────────────────────────────────────────────────
        //  Chunk tree
        // ──────────────────────────────────────────────────────────────────────
        public NWayTreeStorageInternal<ArrayCommandChunk> CommandTree;
        internal bool RecordCommandTreeString = false;
        internal string CommandTreeString => CommandTree.ToTreeString(chunk => "Node ");
        private List<byte> _currentPath = new();

        private NWayTreeStorageInternal<ArrayCommandChunk> CurrentNode =>
            (NWayTreeStorageInternal<ArrayCommandChunk>)CommandTree.GetNode(_currentPath);
        private ArrayCommandChunk CurrentChunk => CurrentNode.StoredValue;

        // ──────────────────────────────────────────────────────────────────────
        // Checkpoints: If we want to figure out why the compiled code is not working, we can use checkpoints. 
        // Wherever the command is copy to index -2, that will be interpreted as an instruction to add the value
        // to the checkpoints list. We can then, for example, compare the checkpoints with checkpoints from noncompiled
        // code or from code not using the ArrayCommandList to see where the values differ.
        // ──────────────────────────────────────────────────────────────────────
        public bool UseCheckpoints = false;
        public static int CheckpointTrigger = -2; // -1 is used for other purposes, and must be negative
        public List<(int Index, double Value)> Checkpoints = new();

        // ──────────────────────────────────────────────────────────────────────
        //  Author‑time helpers
        // ──────────────────────────────────────────────────────────────────────
        private Stack<int> _depthStartSlots = new();
        private Stack<int?> _repeatRangeStack = new();
        public bool RepeatingExistingCommandRange = false;
        private int _keepTogetherLevel = 0;
        private bool _rootChunkInitialised = false;


        // ──────────────────────────────────────────────────────────────────────
        //  Construction
        // ──────────────────────────────────────────────────────────────────────
        public ArrayCommandList(int maxNumCommands, int initialArrayIndex, int maxCommandsPerSplittableChunk = int.MaxValue)
        {
            UnderlyingCommands = new ArrayCommand[maxNumCommands];

            SizeOfMainData = initialArrayIndex;
            Recorder.NextArrayIndex = initialArrayIndex;
            Recorder.MaxArrayIndex = initialArrayIndex - 1;

            CommandTree = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            CommandTree.StoredValue = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                StartSourceIndices = 0,
            };
        }

        public ArrayCommandList Clone()
        {
            var clone = new ArrayCommandList(UnderlyingCommands.Length, SizeOfMainData)
            {
                UnderlyingCommands = (ArrayCommand[])UnderlyingCommands.Clone(),
                VirtualStack = (double[])VirtualStack.Clone(),
                MaxCommandIndex = MaxCommandIndex,
                SizeOfMainData = SizeOfMainData,
                OrderedSourceIndices = new List<int>(OrderedSourceIndices),
                Parallelize = Parallelize,
                MaxCommandsPerSplittableChunk = MaxCommandsPerSplittableChunk,
                CommandTree = CommandTree,
                RecordCommandTreeString = RecordCommandTreeString,
                _currentPath = new List<byte>(_currentPath),
                UseCheckpoints = UseCheckpoints,
                Checkpoints = new List<(int, double)>(Checkpoints),
                _depthStartSlots = new Stack<int>(_depthStartSlots.Reverse()),
                _repeatRangeStack = new Stack<int?>(_repeatRangeStack.Reverse()),
                RepeatingExistingCommandRange = RepeatingExistingCommandRange,
                _keepTogetherLevel = _keepTogetherLevel,
                _rootChunkInitialised = _rootChunkInitialised,
            };
            clone.Recorder = Recorder.Clone(clone);
            return clone;
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
                _rootChunkInitialised = true;
                return;
            }

            if (_keepTogetherLevel > 0 && !ignoreKeepTogether)
                return;

            if (RepeatIdenticalRanges && identicalStartCommandRange is int identical)
            {
                _repeatRangeStack.Push(identical);
                Recorder.NextCommandIndex = identical;
                RepeatingExistingCommandRange = true;
            }

            var parent = CurrentNode;
            byte branch = (byte)(parent.StoredValue.LastChild + 1);

            var child = new NWayTreeStorageInternal<ArrayCommandChunk>(parent);
            child.StoredValue = new ArrayCommandChunk
            {
                Name = name,
                StartCommandRange = NextCommandIndex,
                StartSourceIndices = OrderedSourceIndices.Count(),
            };

            parent.SetBranch(branch, child);
            parent.StoredValue.LastChild = branch;
            _currentPath.Add(branch);
        }

        public void EndCommandChunk(bool endingRepeatedChunk = false)
        {
            if (_currentPath.Count == 0)
            {
                var root = CurrentChunk;
                root.StartCommandRange = 0;
                root.EndCommandRangeExclusive = NextCommandIndex;
                root.StartSourceIndices = 0;
                root.EndSourceIndicesExclusive = OrderedSourceIndices.Count();
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

            _currentPath.RemoveAt(_currentPath.Count - 1);
            var parent = CurrentChunk;
            parent.EndCommandRangeExclusive = NextCommandIndex;
            parent.EndSourceIndicesExclusive = OrderedSourceIndices.Count;

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
        bool commandListCompleted = false;
        public void CompleteCommandList(bool hoistLargeIfBodies = true)
        {
            if (commandListCompleted)
                throw new InvalidOperationException("Command list already completed.");
            MaxCommandIndex = NextCommandIndex;
            VerifyCorrectness();
            while (_currentPath.Count > 0) EndCommandChunk();
            EndCommandChunk(); // root
            CompleteCommandTree(hoistLargeIfBodies);
            VirtualStack = new double[VirtualStackSize];
            commandListCompleted = true;
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

        public void CompleteCommandTree(bool hoistLargeIfBodies = true)
        {
            CommandTree.WalkTree(n =>
                {
                    var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                    InsertMissingBranches(node);
                    node.StoredValue.VirtualStack = VirtualStack; // for now, we're just sharing all virtual stacks
                });
            if (hoistLargeIfBodies)
                HoistAndSplitLargeIfBodies();
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

        public string CommandListString(int startCommandRange, int endCommandRangeExclusive)
        {
            if (UnderlyingCommands == null || UnderlyingCommands.Length == 0)
                return string.Empty;

            var stringBuilder = new StringBuilder();
            for (int i = startCommandRange; i < endCommandRangeExclusive; i++)
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

#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine(
                $"[BRANCHES] parent ID{node.StoredValue.ID}  lastChild={lastChild}");
#endif

            var children = new List<NWayTreeStorageInternal<ArrayCommandChunk>>();
            int curCmd = node.StoredValue.StartCommandRange;
            int curSrc = node.StoredValue.StartSourceIndices;

            for (byte c = 1; c <= lastChild; c++)
            {
                var branch = (NWayTreeStorageInternal<ArrayCommandChunk>)node.GetBranch(c);
                var bVal = branch.StoredValue;

                if (bVal.StartCommandRange > curCmd)
                {
                    var gap = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                    gap.StoredValue = new ArrayCommandChunk
                    {
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = bVal.StartCommandRange,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = bVal.StartSourceIndices,
                    };
                    children.Add(gap);

#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine(
                        $"    ↳ [GAP] ID{gap.StoredValue.ID} cmds=[{curCmd},{bVal.StartCommandRange})");
#endif
                }

                children.Add(branch);
                curCmd = bVal.EndCommandRangeExclusive;
                curSrc = bVal.EndSourceIndicesExclusive;

                if (c == lastChild &&
                    (curCmd < node.StoredValue.EndCommandRangeExclusive ||
                     curSrc < node.StoredValue.EndSourceIndicesExclusive))
                {
                    var tail = new NWayTreeStorageInternal<ArrayCommandChunk>(node);
                    tail.StoredValue = new ArrayCommandChunk
                    {
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = node.StoredValue.EndCommandRangeExclusive,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = node.StoredValue.EndSourceIndicesExclusive,
                    };
                    children.Add(tail);

#if OUTPUT_HOISTING_INFO
                    TabbedText.WriteLine(
                        $"    ↳ [TAIL] ID{tail.StoredValue.ID} cmds=[{curCmd},{tail.StoredValue.EndCommandRangeExclusive})");
#endif
                }
            }

            node.Branches = children.ToArray();

#if OUTPUT_HOISTING_INFO
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


        #region Command recorder

        // TODO: Eliminate the methods here, and then have the callers call the methods through the recorder.

        private CommandRecorder _recorder;
        public CommandRecorder Recorder
        {
            get
            {
                if (_recorder == null)
                    _recorder = new CommandRecorder(this);
                return _recorder;
            }
            set
            {
                _recorder = value;
            }
        }

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
        public int IncrementByProduct(int targetIdx, bool targetOriginal,
                                       int factor1Idx, int factor2Idx, bool reuseTmp = true) =>
            Recorder.IncrementByProduct(targetIdx, targetOriginal, factor1Idx, factor2Idx, reuseTmp);

        public void Decrement(int idx, int decIdx) => Recorder.Decrement(idx, decIdx);
        public void DecrementArrayBy(int[] targets, int decIdx) =>
            Recorder.DecrementArrayBy(targets, decIdx);
        public void DecrementArrayBy(int[] targets, int[] decIdxs) =>
            Recorder.DecrementArrayBy(targets, decIdxs);
        public void DecrementByProduct(int targetIdx, int factor1Idx, int factor2Idx, bool reuseTmp = true) =>
            Recorder.DecrementByProduct(targetIdx, factor1Idx, factor2Idx, reuseTmp);

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

        public void CreateCheckpoint(int sourceIndex)
        {
            if (UseCheckpoints)
                CopyToExisting(CheckpointTrigger, sourceIndex);
        }

        public void ResetCheckpoints()
        {
            if (!UseCheckpoints)
                return;
            Checkpoints = new();
        }

        public void LoadCheckpoints(double[] workingArray)
        {
            if (!UseCheckpoints || Checkpoints == null) return;

            foreach (var (idx, val) in Checkpoints)
            {
                if (idx >= workingArray.Length)
                    Array.Resize(ref workingArray, idx + 1);
                workingArray[idx] = val;
            }
        }

        #endregion

        #region Logging
        public void Debug_LogSourceStats()
        {
            int nextSource = UnderlyingCommands
                                .Take(MaxCommandIndex)
                                .Count(c => c.CommandType == ArrayCommandType.NextSource);

            int copyTo = UnderlyingCommands
                                .Take(MaxCommandIndex)
                                .Count(c => c.CommandType == ArrayCommandType.CopyTo);

            TabbedText.WriteLine($"[ACL-DBG] NextSource commands : {nextSource}");
            TabbedText.WriteLine($"[ACL-DBG] CopyTo commands     : {copyTo}");
            TabbedText.WriteLine($"[ACL-DBG] OrderedSourceIndices: {OrderedSourceIndices.Count}");
        }

        public void Debug_DumpCheckpoints(int max = 10)
        {
            if (!UseCheckpoints || Checkpoints is null || Checkpoints.Count == 0)
            {
                TabbedText.WriteLine("[ACL-DBG] No checkpoints recorded.");
                return;
            }

            TabbedText.WriteLine($"[ACL-DBG] First {Math.Min(max, Checkpoints.Count)} checkpoints:");
            foreach (var (idx, val) in Checkpoints.Take(max))
                TabbedText.WriteLine($"    idx {idx} → {val}");
        }

        #endregion
    }
}
