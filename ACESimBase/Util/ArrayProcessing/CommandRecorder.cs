using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Records commands, allocates scratch slots and writes <see cref="ArrayCommand"/>
    /// instances into the owning <see cref="ArrayCommandList"/>.  The class mirrors
    /// the counters in the ACL while building, then hands off execution to
    /// <see cref="ArrayCommandListRunner"/>.
    /// <para><strong>Thread-safety:</strong> one <see cref="CommandRecorder"/> <em>must not</em>
    /// be shared across threads; all members are unsynchronised.</para>
    /// </summary>
    public sealed class CommandRecorder
    {
        private readonly ArrayCommandList _acl;

        // Counters mirrored into the ACL for introspection / later execution
        public int NextArrayIndex;
        public int MaxArrayIndex;
        public int NextCommandIndex;

        // Depth-based scratch rewind
        private Stack<int> _depthStartSlots = new();

        public CommandRecorder(ArrayCommandList owner)
        {
            _acl = owner ?? throw new ArgumentNullException(nameof(owner));
            NextArrayIndex = 0;
            MaxArrayIndex = -1;
            NextCommandIndex = 0;
        }

        public CommandRecorder Clone(ArrayCommandList acl2) =>
            new CommandRecorder(acl2)
            {
                NextArrayIndex = NextArrayIndex,
                MaxArrayIndex = MaxArrayIndex,
                NextCommandIndex = NextCommandIndex,
                _depthStartSlots = new Stack<int>(_depthStartSlots.Reverse())
            };

        #region InternalHelpers

        private void AddCommand(ArrayCommand cmd)
        {
            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                if (!cmd.Equals(expected))
                    ThrowRepeatMismatch(cmd, expected);

                NextCommandIndex++;
                return;
            }

            const int HARD_LIMIT = 1_000_000_000; // 1B safety cap
            if (NextCommandIndex >= _acl.UnderlyingCommands.Length)
            {
                if (_acl.UnderlyingCommands.Length >= HARD_LIMIT)
                    throw new InvalidOperationException("Command buffer exceeded hard limit.");
                Array.Resize(ref _acl.UnderlyingCommands, _acl.UnderlyingCommands.Length * 2);
            }

            _acl.UnderlyingCommands[NextCommandIndex++] = cmd;
            if (NextArrayIndex > MaxArrayIndex) MaxArrayIndex = NextArrayIndex;
        }

        // Add these helpers in CommandRecorder
        private void ThrowRepeatMismatch(in ArrayCommand newCmd, in ArrayCommand recorded)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("Repeat-range mismatch");
            sb.AppendLine($"  at cmd index: {NextCommandIndex}");
            sb.AppendLine($"  recorded: {RenderCommand(recorded)}");
            sb.AppendLine($"  new     : {RenderCommand(newCmd)}");
            sb.AppendLine();

            // Recorder / ACL state that influences repeat determinism
            sb.AppendLine("Recorder/ACL state:");
            sb.AppendLine($"  UseOrderedSourcesAndDestinations: {_acl.UseOrderedSourcesAndDestinations}");
            sb.AppendLine($"  RepeatIdenticalRanges           : {_acl.RepeatIdenticalRanges}");
            sb.AppendLine($"  RepeatingExistingCommandRange   : {_acl.RepeatingExistingCommandRange}");
            sb.AppendLine($"  ReuseScratchSlots               : {_acl.ReuseScratchSlots}");
            sb.AppendLine($"  NextArrayIndex / MaxArrayIndex  : {NextArrayIndex} / {MaxArrayIndex}");
            sb.AppendLine($"  Depth frames                    : {_depthStartSlots.Count}");
            sb.AppendLine();

            // Nearest prior comment (often names decision/range)
            int priorCommentIdx = -1;
            for (int i = NextCommandIndex - 1; i >= 0 && i >= NextCommandIndex - 400; i--)
            {
                var c = _acl.UnderlyingCommands[i];
                if (c.CommandType == ArrayCommandType.Comment)
                {
                    priorCommentIdx = i;
                    break;
                }
            }
            if (priorCommentIdx >= 0)
            {
                var c = _acl.UnderlyingCommands[priorCommentIdx];
                string note = (c.SourceIndex >= 0 && c.SourceIndex < CommentTable.Count)
                    ? CommentTable[c.SourceIndex]
                    : "<unavailable>";
                sb.AppendLine("Nearest prior comment:");
                sb.AppendLine($"  @{priorCommentIdx}: {note}");
                sb.AppendLine();
            }

            // Nearby recorded commands for context
            int windowBefore = 16, windowAfter = 8;
            int start = Math.Max(0, NextCommandIndex - windowBefore);
            int end   = Math.Min(_acl.UnderlyingCommands.Length - 1, NextCommandIndex + windowAfter);

            sb.AppendLine("Recorded commands near mismatch:");
            for (int i = start; i <= end; i++)
            {
                var c = _acl.UnderlyingCommands[i];
                // Stop early if we run into an uninitialized slot
                if (i > NextCommandIndex && c.CommandType == ArrayCommandType.Blank && c.Index == -1 && c.SourceIndex == -1)
                    break;

                string row = $"{i:D6}: {RenderCommand(c)}";
                if (c.CommandType == ArrayCommandType.Comment)
                {
                    string txt = (c.SourceIndex >= 0 && c.SourceIndex < CommentTable.Count)
                        ? CommentTable[c.SourceIndex]
                        : "<unavailable>";
                    row += $"    // {txt}";
                }
                if (i == NextCommandIndex) row += "    << mismatch here";
                sb.AppendLine(row);
            }
            sb.AppendLine();

            // Ordered-source/destination tails (helpful for NextSource/NextDestination alignment)
            static string Tail(System.Collections.Generic.IReadOnlyList<int> xs, int k = 12)
                => xs == null || xs.Count == 0 ? "(empty)"
                   : string.Join(", ", xs.Skip(Math.Max(0, xs.Count - k)));

            sb.AppendLine("Ordered indices tails:");
            sb.AppendLine($"  OrderedSourceIndices      : {Tail(_acl.OrderedSourceIndices)}");
            sb.AppendLine($"  OrderedDestinationIndices : {Tail(_acl.OrderedDestinationIndices)}");

            throw new InvalidOperationException(sb.ToString());
        }

        private static string RenderCommand(in ArrayCommand c) =>
            $"{c.CommandType} (idx={c.Index}, src={c.SourceIndex})";


        #endregion

        #region ScratchAllocation

        /// <summary>Create one scratch slot initialised to 0.</summary>
        public int NewZero()
        {
            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                if (expected.CommandType != ArrayCommandType.Zero)
                    throw new InvalidOperationException($"Repeat-range mismatch at cmd {NextCommandIndex}: recorded {expected.CommandType} vs new Zero.");
                int slot = expected.Index;
                // Ensure our scratch pointer never lags behind the recorded slot.
                if (slot + 1 > NextArrayIndex) NextArrayIndex = slot + 1;
                AddCommand(new ArrayCommand(ArrayCommandType.Zero, slot, -1)); // advances NextCommandIndex via AddCommand
                return slot;
            }

            int fresh = NextArrayIndex++;
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, fresh, -1));
            return fresh;
        }


        /// <summary>Create <paramref name="size"/> consecutive scratch slots initialised to 0.</summary>
        public int[] NewZeroArray(int size) => Enumerable.Range(0, size).Select(_ => NewZero()).ToArray();

        /// <summary>Reserve a scratch slot without initialising its value.</summary>
        public int NewUninitialized() => NextArrayIndex++;

        public int[] NewUninitializedArray(int size) =>
            Enumerable.Range(0, size).Select(_ => NewUninitialized()).ToArray();
        #endregion

        #region CopyArithmeticPrimitives
        /// <summary>Copy value from <paramref name="sourceIdx"/> into a <em>new</em> scratch slot.</summary>

        /// <summary>Copy value from <paramref name="sourceIdx"/> into a <em>new</em> scratch slot.</summary>
        public int CopyToNew(int sourceIdx, bool fromOriginalSources)
        {
            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                // In ordered-source mode the first recording will have emitted NextSource; otherwise CopyTo.
                bool expectNextSource = expected.CommandType == ArrayCommandType.NextSource;
                bool expectCopyTo     = expected.CommandType == ArrayCommandType.CopyTo;
                if (!expectNextSource && !expectCopyTo)
                    throw new InvalidOperationException($"Repeat-range mismatch at cmd {NextCommandIndex}: recorded {expected.CommandType} vs new {(fromOriginalSources && _acl.UseOrderedSourcesAndDestinations ? "NextSource" : "CopyTo")}.");

                int target = expected.Index;
                if (target + 1 > NextArrayIndex) NextArrayIndex = target + 1;

                if (expectNextSource)
                {
                    // Keep ordered-source side effect identical on repeats.
                    _acl.OrderedSourceIndices.Add(sourceIdx);
                    AddCommand(new ArrayCommand(ArrayCommandType.NextSource, target, -1));
                }
                else
                {
                    AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, target, sourceIdx));
                }
                return target;
            }

            int fresh = NextArrayIndex++;
            if (fromOriginalSources && _acl.UseOrderedSourcesAndDestinations)
            {
                _acl.OrderedSourceIndices.Add(sourceIdx);
                AddCommand(new ArrayCommand(ArrayCommandType.NextSource, fresh, -1));
            }
            else
            {
                AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, fresh, sourceIdx));
            }
            return fresh;
        }



        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources) =>
            sourceIndices.Select(idx => CopyToNew(idx, fromOriginalSources)).ToArray();

        public void CopyToExisting(int index, int sourceIndex)
        {
            if (index == ArrayCommandList.CheckpointTrigger)
                AddCommand(new ArrayCommand(ArrayCommandType.Checkpoint, ArrayCommandList.CheckpointTrigger,  sourceIndex));
            else
                AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }

        public void CopyToExisting(int[] indices, int[] sourceIndices)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                CopyToExisting(indices[i], sourceIndices[i]);
            }
        }

        /// <summary>Multiply slot <paramref name="idx"/> by <paramref name="multIdx"/>.</summary>
        public void MultiplyBy(int idx, int multIdx) =>
            AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, idx, multIdx));

        /// <summary>Increment or stage-increment slot <paramref name="idx"/> by value from <paramref name="incIdx"/>.</summary>
        public void Increment(int idx, bool targetOriginal, int incIdx)
        {
            if (targetOriginal && _acl.UseOrderedSourcesAndDestinations)
            {
                _acl.OrderedDestinationIndices.Add(idx);
                AddCommand(new ArrayCommand(ArrayCommandType.NextDestination, -1, incIdx));
            }
            else
            {
                AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, idx, incIdx));
            }
        }

        public void Decrement(int idx, int decIdx) =>
            AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, idx, decIdx));
        #endregion

        #region ZeroHelpers
        public void ZeroExisting(int index) =>
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, index, -1));

        public void ZeroExisting(int[] indices)
        {
            foreach (var i in indices) ZeroExisting(i);
        }
        #endregion

        #region ProduceNewSlotHelpers
        public int AddToNew(int idx1, bool fromOriginalSources, int idx2)
        {
            int res = CopyToNew(idx1, fromOriginalSources);
            Increment(res, false, idx2);
            return res;
        }

        public int MultiplyToNew(int idx1, bool fromOriginalSources, int idx2)
        {
            int res = CopyToNew(idx1, fromOriginalSources);
            MultiplyBy(res, idx2);
            return res;
        }
        #endregion

        #region ArrayArithmeticHelpers
        private static void EnsureEqualLengths(string aName, int aLen,
                                               string bName, int bLen)
        {
            if (aLen != bLen)
                throw new ArgumentException(
                    $"{aName} length {aLen} ≠ {bName} length {bLen}");
        }

        public void MultiplyArrayBy(int[] targets, int multiplierIdx)
        {
            foreach (var t in targets) MultiplyBy(t, multiplierIdx);
        }

        public void MultiplyArrayBy(int[] targets, int[] multipliers)
        {
            EnsureEqualLengths(nameof(targets), targets.Length,
                               nameof(multipliers), multipliers.Length);
            for (int i = 0; i < targets.Length; i++)
                MultiplyBy(targets[i], multipliers[i]);
        }

        public void IncrementArrayBy(int[] targets, bool targetOriginals, int incIdx)
        {
            foreach (var t in targets) Increment(t, targetOriginals, incIdx);
        }

        public void IncrementArrayBy(int[] targets, bool targetOriginals, int[] incIdxs)
        {
            EnsureEqualLengths(nameof(targets), targets.Length,
                               nameof(incIdxs), incIdxs.Length);
            for (int i = 0; i < targets.Length; i++)
                Increment(targets[i], targetOriginals, incIdxs[i]);
        }

        public int IncrementByProduct(int targetIdx, bool targetOriginal,
                                       int factor1Idx, int factor2Idx,
                                       bool reuseTmp = true)
        {
            int tmp = CopyToNew(factor1Idx, false);
            MultiplyBy(tmp, factor2Idx);
            Increment(targetIdx, targetOriginal, tmp);
            if (reuseTmp && _acl.ReuseScratchSlots) NextArrayIndex--; // reclaim
            return tmp; // solely for the purpose of determining how many virtual stack slots are needed
        }

        public void DecrementArrayBy(int[] targets, int decIdx)
        {
            foreach (var t in targets) Decrement(t, decIdx);
        }

        public void DecrementArrayBy(int[] targets, int[] decIdxs)
        {
            EnsureEqualLengths(nameof(targets), targets.Length,
                               nameof(decIdxs), decIdxs.Length);
            for (int i = 0; i < targets.Length; i++)
                Decrement(targets[i], decIdxs[i]);
        }

        public void DecrementByProduct(int targetIdx, int factor1Idx, int factor2Idx,
                                       bool reuseTmp = true)
        {
            int tmp = CopyToNew(factor1Idx, false);
            MultiplyBy(tmp, factor2Idx);
            Decrement(targetIdx, tmp);
            if (reuseTmp && _acl.ReuseScratchSlots) NextArrayIndex--; // reclaim
        }
        #endregion

        #region ComparisonFlowControl
        public void InsertIf() =>
            AddCommand(new ArrayCommand(ArrayCommandType.If, -1, -1));

        public void InsertEndIf() =>
            AddCommand(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

        public void InsertEqualsOtherArrayIndexCommand(int i1, int i2) =>
            AddCommand(new ArrayCommand(ArrayCommandType.EqualsOtherArrayIndex, i1, i2));

        public void InsertNotEqualsOtherArrayIndexCommand(int i1, int i2) =>
            AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex, i1, i2));

        public void InsertGreaterThanOtherArrayIndexCommand(int i1, int i2) =>
            AddCommand(new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex, i1, i2));

        public void InsertLessThanOtherArrayIndexCommand(int i1, int i2) =>
            AddCommand(new ArrayCommand(ArrayCommandType.LessThanOtherArrayIndex, i1, i2));

        public void InsertEqualsValueCommand(int idx, int v) =>
            AddCommand(new ArrayCommand(ArrayCommandType.EqualsValue, idx, v));

        public void InsertNotEqualsValueCommand(int idx, int v) =>
            AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsValue, idx, v));
        #endregion  // ComparisonFlowControl


        #region DepthAndChunkFacade   // (internal – builder-only)

        internal void IncrementDepth()
        {
            _depthStartSlots.Push(NextArrayIndex);
            InsertIncrementDepthCommand();
        }

        internal void DecrementDepth(bool completeCommandList = false)
        {
            InsertDecrementDepthCommand();
            int rewind = _depthStartSlots.Pop();
            if (_acl.RepeatIdenticalRanges && _acl.ReuseScratchSlots)
                NextArrayIndex = rewind;

            if (_depthStartSlots.Count == 0 && completeCommandList)
                _acl.CompleteCommandList();
        }

        internal void StartCommandChunk(bool runChildrenParallel,
                                        int? identicalStartCmdRange,
                                        string name = null,
                                        bool ignoreKeepTogether = false) =>
            _acl.StartCommandChunk(runChildrenParallel,
                                   identicalStartCmdRange,
                                   name,
                                   ignoreKeepTogether);

        internal void EndCommandChunk(
                                      bool endingRepeatedChunk = false) =>
            _acl.EndCommandChunk(endingRepeatedChunk);

        #endregion  // DepthAndChunkFacade

        #region Comments and blank commands

        public List<string> CommentTable = new List<string>();
        public void InsertComment(string text)
        {
            int id = CommentTable.Count;
            CommentTable.Add(text);
            // we store the comment’s row‑id in SourceIndex
            AddCommand(new ArrayCommand(ArrayCommandType.Comment, -1, id));
        }

        /// <summary>
        /// Insert a placeholder “Blank” command and return its position in the command buffer
        /// (note: this is a *command* index, not an array index).
        /// </summary>
        public int InsertBlankCommand()
        {
            int cmdIndex = NextCommandIndex;                 // position before adding the blank
            AddCommand(new ArrayCommand(ArrayCommandType.Blank, -1, -1));
            return cmdIndex;
        }


        public int InsertIncrementDepthCommand()
        {
            int cmdIndex = NextCommandIndex;                 // position before adding the blank
            AddCommand(new ArrayCommand(ArrayCommandType.IncrementDepth, -1, -1));
            return cmdIndex;
        }


        public int InsertDecrementDepthCommand()
        {
            int cmdIndex = NextCommandIndex;                 // position before adding the blank
            AddCommand(new ArrayCommand(ArrayCommandType.DecrementDepth, -1, -1));
            return cmdIndex;
        }

        #endregion
    }
}
