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
        // size of the non-stagecostatch data.
        public int SizeOfMainData;
        public Span<double> NonScratchData => VirtualStack.AsSpan(0, SizeOfMainData);
        public int NextArrayIndex => Recorder.NextArrayIndex;
        public int MaxArrayIndex => Recorder.MaxArrayIndex;
        public int VirtualStackSize
        {
            get
            {
                int maxExplicit = -1;
                if (OrderedSourceIndices is { Count: > 0 })
                    maxExplicit = Math.Max(maxExplicit, OrderedSourceIndices.Max(os => os.Value));
                if (OrderedDestinationIndices is { Count: > 0 })
                    maxExplicit = Math.Max(maxExplicit, OrderedDestinationIndices.Max(od => od.Value));
                int maxIndex = Math.Max(Recorder.MaxArrayIndex, maxExplicit);
                return maxIndex + 1;

            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Ordered‑buffer index lists (filled at author‑time only)
        // ──────────────────────────────────────────────────────────────────────
        public bool UseOrderedSourcesAndDestinations = false;
        public List<OsIndex> OrderedSourceIndices = new();
        public List<OdIndex> OrderedDestinationIndices = new();

        private readonly Dictionary<int, int> _originalRangeScratchStarts = new(); // DEBUG?
        private readonly Dictionary<int, int> _originalRangeStartScratchIndex = new();



        // ──────────────────────────────────────────────────────────────────────
        //  Settings and feature flags
        // ──────────────────────────────────────────────────────────────────────
        public bool Parallelize = false;
        public int MaxCommandsPerSplittableChunk = 10_000;
        public bool ReuseScratchSlots { get; set; } = true;
        public bool RepeatIdenticalRanges => true;
        public bool TemplateControlsRepetition { get; set; } = true;
        public bool EmitComments { get; set; } = false;

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
        private Stack<int> _repeatRangeStack = new();
        private Stack<int> _repeatRangeEndStack = new();
        private Stack<bool> _repeatChildIsRepeatedStack = new();
        // Map a chunk's StartCommandRange → its EndCommandRangeExclusive
        private readonly Dictionary<int, int> _originalRangeEnds = new();
        // Scratch-index snapshots for repeat bodies (separate from depth)
        private readonly Stack<int> _repeatScratchIndexStack = new();


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
                StartDestinationIndices = 0,
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

                // Ensure both ordered lists are cloned — destination was previously omitted.
                UseOrderedSourcesAndDestinations = UseOrderedSourcesAndDestinations,
                OrderedSourceIndices = new List<OsIndex>(OrderedSourceIndices),
                OrderedDestinationIndices = new List<OdIndex>(OrderedDestinationIndices),

                Parallelize = Parallelize,
                MaxCommandsPerSplittableChunk = MaxCommandsPerSplittableChunk,
                CommandTree = CommandTree,
                RecordCommandTreeString = RecordCommandTreeString,
                _currentPath = new List<byte>(_currentPath),
                UseCheckpoints = UseCheckpoints,
                Checkpoints = new List<(int, double)>(Checkpoints),
                _depthStartSlots = new Stack<int>(_depthStartSlots.Reverse()),
                _repeatRangeStack = new Stack<int>(_repeatRangeStack.Reverse()),
                _repeatRangeEndStack = new Stack<int>(_repeatRangeEndStack.Reverse()),
                _repeatChildIsRepeatedStack = new Stack<bool>(_repeatChildIsRepeatedStack.Reverse()),
                RepeatingExistingCommandRange = RepeatingExistingCommandRange,
                _keepTogetherLevel = _keepTogetherLevel,
                _rootChunkInitialised = _rootChunkInitialised,
            };
            clone.Recorder = Recorder.Clone(clone);
            foreach (var kv in _originalRangeStartScratchIndex)
                clone._originalRangeStartScratchIndex[kv.Key] = kv.Value;
            foreach (var kv in _originalRangeEnds)
                clone._originalRangeEnds[kv.Key] = kv.Value;
            return clone;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Chunk helpers
        // ──────────────────────────────────────────────────────────────────────
        public void StartCommandChunk(bool runChildrenInParallel, int? identicalStartCommandRange, string name = "", bool ignoreKeepTogether = false)
        {
            if (_currentPath.Count == 0
                && !_rootChunkInitialised
                && string.Equals(name, "root", StringComparison.OrdinalIgnoreCase))
            {
                var root = CurrentChunk;
                if (!string.IsNullOrEmpty(name))
                    root.Name = name;
                _rootChunkInitialised = true;
                return;
            }

            if (_keepTogetherLevel > 0 && !ignoreKeepTogether)
                return;

            bool childIsRepeat = false;
            int scratchAtEntry = Recorder.NextArrayIndex;

            if (RepeatIdenticalRanges && identicalStartCommandRange is int identical)
            {
                _repeatRangeStack.Push(identical);
                _repeatScratchIndexStack.Push(scratchAtEntry);
                Recorder.NextCommandIndex = identical;

                if (!_originalRangeStartScratchIndex.TryGetValue(identical, out int startScratch))
                    throw new InvalidOperationException("Original start scratch index not recorded for repeated chunk.");

                Recorder.NextArrayIndex = startScratch;

                RepeatingExistingCommandRange = true;
                childIsRepeat = true;
            }
            else if (RepeatIdenticalRanges &&
                     RepeatingExistingCommandRange &&
                     identicalStartCommandRange is null &&
                     !TemplateControlsRepetition) // ← gate implicit nested-repeat inference
            {
                int candidate = Recorder.NextCommandIndex;

                if (_repeatRangeStack.Count > 0)
                {
                    int parentStart = _repeatRangeStack.Peek();
                    if (_originalRangeEnds.TryGetValue(parentStart, out int parentEnd) &&
                        candidate >= parentStart && candidate < parentEnd &&
                        _originalRangeEnds.ContainsKey(candidate))
                    {
                        _repeatRangeStack.Push(candidate);
                        _repeatScratchIndexStack.Push(scratchAtEntry);

                        if (!_originalRangeStartScratchIndex.TryGetValue(candidate, out int nestedStartScratch))
                            throw new InvalidOperationException("Original start scratch index not recorded for nested repeated chunk.");

                        Recorder.NextArrayIndex = nestedStartScratch;

                        childIsRepeat = true;
                    }
                }
            }

            Recorder.Mode = childIsRepeat ? (IEmissionMode)ReplayMode.Instance : RecordingMode.Instance;

            var parent = CurrentNode;
            byte branch = (byte)(parent.StoredValue.LastChild + 1);

            var child = new NWayTreeStorageInternal<ArrayCommandChunk>(parent);
            child.StoredValue = new ArrayCommandChunk
            {
                Name = name,
                StartCommandRange = NextCommandIndex,
                StartSourceIndices = OrderedSourceIndices.Count(),
                StartDestinationIndices = OrderedDestinationIndices.Count(),
            };

            parent.SetBranch(branch, child);
            parent.StoredValue.LastChild = branch;
            _currentPath.Add(branch);

            if (!childIsRepeat)
                _originalRangeStartScratchIndex[child.StoredValue.StartCommandRange] = scratchAtEntry;

            _repeatChildIsRepeatedStack.Push(childIsRepeat);
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
                root.StartDestinationIndices = 0;
                root.EndDestinationIndicesExclusive = OrderedDestinationIndices.Count();
                return;
            }
            if (_keepTogetherLevel > 0)
                return;

            var finished = CurrentChunk;

            static int CountOps(ArrayCommand[] cmds, int start, int end, ArrayCommandType t)
            {
                int c = 0;
                for (int i = start; i < end; i++)
                    if (cmds[i].CommandType == t) c++;
                return c;
            }

            bool childWasRepeated = _repeatChildIsRepeatedStack.Count > 0 && _repeatChildIsRepeatedStack.Pop();

            if (RepeatIdenticalRanges && childWasRepeated)
            {
                if (_repeatRangeStack.Count == 0)
                    throw new InvalidOperationException("Repeat-child flagged but repeat range stack is empty.");

                int repeatStart = _repeatRangeStack.Pop();

                int scratchRewind = _repeatScratchIndexStack.Pop();
                Recorder.NextArrayIndex = scratchRewind;

                if (!_originalRangeEnds.TryGetValue(repeatStart, out int repeatEnd))
                    throw new InvalidOperationException("Original range end not recorded for repeated chunk.");

                finished.StartCommandRange = repeatStart;
                finished.EndCommandRangeExclusive = repeatEnd;

                int srcInRange = CountOps(UnderlyingCommands, repeatStart, repeatEnd, ArrayCommandType.NextSource);
                finished.EndSourceIndicesExclusive = finished.StartSourceIndices + srcInRange;

                int dstInRange = CountOps(UnderlyingCommands, repeatStart, repeatEnd, ArrayCommandType.NextDestination);
                finished.EndDestinationIndicesExclusive = finished.StartDestinationIndices + dstInRange;

                if (_repeatRangeStack.Count == 0)
                {
                    RepeatingExistingCommandRange = false;
                    Recorder.Mode = RecordingMode.Instance;
                }

            }
            else
            {
                finished.EndCommandRangeExclusive = NextCommandIndex;
                finished.EndSourceIndicesExclusive = OrderedSourceIndices.Count;
                finished.EndDestinationIndicesExclusive = OrderedDestinationIndices.Count;

                _originalRangeEnds[finished.StartCommandRange] = finished.EndCommandRangeExclusive;
            }

            _currentPath.RemoveAt(_currentPath.Count - 1);
            var parent = CurrentChunk;

            parent.EndCommandRangeExclusive = Math.Max(parent.EndCommandRangeExclusive, finished.EndCommandRangeExclusive);
            parent.EndSourceIndicesExclusive = Math.Max(parent.EndSourceIndicesExclusive, finished.EndSourceIndicesExclusive);
            parent.EndDestinationIndicesExclusive = Math.Max(parent.EndDestinationIndicesExclusive, finished.EndDestinationIndicesExclusive);
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
            VerifyCorrectness2();
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
        private void VerifyCorrectness2()
        {

            long totalCmdNextSourceAcrossLeaves = 0;
            long totalCmdNextDestinationAcrossLeaves = 0;
            long totalMetaNextSourceAcrossLeaves = 0;
            long totalMetaNextDestinationAcrossLeaves = 0;

            // After CompleteCommandTree(), the tree includes gap/tail leaves too.
            // For each *leaf*:
            //   1) Assert its metadata matches its command slice.
            //   2) Accumulate both the slice opcode counts and the metadata counts.
            CommandTree.WalkTree(nObj =>
            {
                var node = (ACESimBase.Util.NWayTreeStorage.NWayTreeStorageInternal<ArrayCommandChunk>)nObj;
                var c = node.StoredValue;

                // Skip non‑leaf and empty placeholders
                bool isLeaf = node.Branches is null || node.Branches.Length == 0;
                if (!isLeaf) return;
                if (c.EndCommandRangeExclusive <= c.StartCommandRange) return;

                int sliceNextSource = CountOpsInRange(ArrayCommandType.NextSource,      c.StartCommandRange, c.EndCommandRangeExclusive);
                int sliceNextDest   = CountOpsInRange(ArrayCommandType.NextDestination, c.StartCommandRange, c.EndCommandRangeExclusive);

                int metaNextSource = c.EndSourceIndicesExclusive      - c.StartSourceIndices;
                int metaNextDest   = c.EndDestinationIndicesExclusive - c.StartDestinationIndices;

                if (sliceNextSource != metaNextSource)
                    throw new InvalidOperationException(
                        $"Chunk [{c.StartCommandRange},{c.EndCommandRangeExclusive}) NextSource mismatch: commands={sliceNextSource} metadata={metaNextSource}.");

                if (sliceNextDest != metaNextDest)
                    throw new InvalidOperationException(
                        $"Chunk [{c.StartCommandRange},{c.EndCommandRangeExclusive}) NextDestination mismatch: commands={sliceNextDest} metadata={metaNextDest}.");

                totalCmdNextSourceAcrossLeaves  += sliceNextSource;
                totalCmdNextDestinationAcrossLeaves += sliceNextDest;

                totalMetaNextSourceAcrossLeaves += metaNextSource;
                totalMetaNextDestinationAcrossLeaves += metaNextDest;
            });

            int recordedSources = OrderedSourceIndices?.Count ?? 0;
            int recordedDests   = OrderedDestinationIndices?.Count ?? 0;

            // FINAL global checks: compare ordered lists against the *metadata* totals,
            // because the executors and branch‑skipping logic advance cosi/codi using metadata.
            if (totalMetaNextSourceAcrossLeaves != recordedSources)
                throw new InvalidOperationException(
                    $"Mismatch between total NextSource consumptions across leaf chunks (by metadata: {totalMetaNextSourceAcrossLeaves}) and OrderedSourceIndices.Count ({recordedSources}).");

            if (totalMetaNextDestinationAcrossLeaves != recordedDests)
                throw new InvalidOperationException(
                    $"Mismatch between total NextDestination consumptions across leaf chunks (by metadata: {totalMetaNextDestinationAcrossLeaves}) and OrderedDestinationIndices.Count ({recordedDests}).");
        }

        
        // Count a specific opcode in a [start,end) slice
        int CountOpsInRange(ArrayCommandType t, int start, int end)
        {
            int count = 0;
            var cmds = UnderlyingCommands;
            int max = Math.Min(end, MaxCommandIndex);
            for (int i = start; i < max; i++)
                if (cmds[i].CommandType == t)
                    count++;
            return count;
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
                    int gapCmdEnd = bVal.StartCommandRange;

                    int gapSrcEnd = curSrc + CountOpsInRange(ArrayCommandType.NextSource,      curCmd, gapCmdEnd);
                    int gapDstEnd = curDst + CountOpsInRange(ArrayCommandType.NextDestination, curCmd, gapCmdEnd);

                    gap.StoredValue = new ArrayCommandChunk
                    {
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = gapCmdEnd,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = gapSrcEnd,
                        StartDestinationIndices = curDst,
                        EndDestinationIndicesExclusive = gapDstEnd,
                    };
                    children.Add(gap);

                    curCmd = gapCmdEnd;
                    curSrc = gapSrcEnd;
                    curDst = gapDstEnd;
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
                    int tailCmdEnd = node.StoredValue.EndCommandRangeExclusive;

                    int tailSrcEnd = curSrc + CountOpsInRange(ArrayCommandType.NextSource,      curCmd, tailCmdEnd);
                    int tailDstEnd = curDst + CountOpsInRange(ArrayCommandType.NextDestination, curCmd, tailCmdEnd);

                    tail.StoredValue = new ArrayCommandChunk
                    {
                        StartCommandRange = curCmd,
                        EndCommandRangeExclusive = tailCmdEnd,
                        StartSourceIndices = curSrc,
                        EndSourceIndicesExclusive = tailSrcEnd,
                        StartDestinationIndices = curDst,
                        EndDestinationIndicesExclusive = tailDstEnd,
                    };
                    children.Add(tail);

                    curCmd = tailCmdEnd;
                    curSrc = tailSrcEnd;
                    curDst = tailDstEnd;
                }
            }

            node.Branches = children.ToArray();
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
