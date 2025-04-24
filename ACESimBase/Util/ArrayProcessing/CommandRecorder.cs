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
        private int _nextArrayIndex;
        private int _maxArrayIndex;
        private int _nextCommandIndex;

        // Depth-based scratch rewind
        private readonly Stack<int> _depthStartSlots = new();

        public CommandRecorder(ArrayCommandList owner)
        {
            _acl = owner ?? throw new ArgumentNullException(nameof(owner));
            _nextArrayIndex = owner.NextArrayIndex;
            _maxArrayIndex = owner.MaxArrayIndex;
            _nextCommandIndex = owner.NextCommandIndex;
        }

        #region InternalHelpers
        private void SyncCounters()
        {
            _acl.NextArrayIndex = _nextArrayIndex;
            _acl.MaxArrayIndex = _maxArrayIndex;
            _acl.NextCommandIndex = _nextCommandIndex;
        }

        private void AddCommand(ArrayCommand cmd)
        {
            // Repeat-identical-range optimisation → verify identity
            if (_acl.RepeatingExistingCommandRange)
            {
                var existing = _acl.UnderlyingCommands[_nextCommandIndex];
                if (!cmd.Equals(existing))
                    throw new InvalidOperationException("Command mismatch in RepeatIdenticalRanges block.");
                _nextCommandIndex++;
                SyncCounters();
                return;
            }

            // Ensure capacity (hard ceiling = 1 billion commands to prevent OOM)
            const int HARD_LIMIT = 1_000_000_000;
            if (_nextCommandIndex >= _acl.UnderlyingCommands.Length)
            {
                if (_acl.UnderlyingCommands.Length >= HARD_LIMIT)
                    throw new InvalidOperationException("Command buffer exceeded hard limit.");
                Array.Resize(ref _acl.UnderlyingCommands, _acl.UnderlyingCommands.Length * 2);
            }

            _acl.UnderlyingCommands[_nextCommandIndex++] = cmd;
            if (_nextArrayIndex > _maxArrayIndex) _maxArrayIndex = _nextArrayIndex;
            SyncCounters();
        }
        #endregion

        #region ScratchAllocation
        /// <summary>Create one scratch slot initialised to 0.</summary>
        public int NewZero()
        {
            int slot = _nextArrayIndex++;
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, slot, -1));
            return slot;
        }

        /// <summary>Create <paramref name="size"/> consecutive scratch slots initialised to 0.</summary>
        public int[] NewZeroArray(int size) => Enumerable.Range(0, size).Select(_ => NewZero()).ToArray();

        /// <summary>Reserve a scratch slot without initialising its value.</summary>
        public int NewUninitialized() => _nextArrayIndex++;

        public int[] NewUninitializedArray(int size) =>
            Enumerable.Range(0, size).Select(_ => NewUninitialized()).ToArray();
        #endregion

        #region CopyArithmeticPrimitives
        /// <summary>Copy value from <paramref name="sourceIdx"/> into a <em>new</em> scratch slot.</summary>
        public int CopyToNew(int sourceIdx, bool fromOriginalSources)
        {
            int target = _nextArrayIndex++;
            if (fromOriginalSources && _acl.UseOrderedSources)
            {
                _acl.OrderedSourceIndices.Add(sourceIdx);
                AddCommand(new ArrayCommand(ArrayCommandType.NextSource, target, -1));
            }
            else
            {
                AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, target, sourceIdx));
            }
            return target;
        }

        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources) =>
    sourceIndices.Select(idx => CopyToNew(idx, fromOriginalSources)).ToArray();

        public void CopyToExisting(int index, int sourceIndex)
        {
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
            if (targetOriginal && _acl.UseOrderedDestinations)
            {
                if (_acl.ReuseDestinations &&
                    _acl.ReusableOrderedDestinationIndices.TryGetValue(idx, out int existing))
                {
                    AddCommand(new ArrayCommand(ArrayCommandType.ReusedDestination, existing, incIdx));
                }
                else
                {
                    _acl.OrderedDestinationIndices.Add(idx);
                    if (_acl.ReuseDestinations)
                        _acl.ReusableOrderedDestinationIndices[idx] =
                            _acl.OrderedDestinationIndices.Count - 1;
                    AddCommand(new ArrayCommand(ArrayCommandType.NextDestination, -1, incIdx));
                }
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

        public void IncrementByProduct(int targetIdx, bool targetOriginal,
                                       int factor1Idx, int factor2Idx,
                                       bool reuseTmp = true)
        {
            int tmp = CopyToNew(factor1Idx, false);
            MultiplyBy(tmp, factor2Idx);
            Increment(targetIdx, targetOriginal, tmp);
            if (reuseTmp && _acl.ReuseScratchSlots) _nextArrayIndex--; // reclaim
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
            if (reuseTmp && _acl.ReuseScratchSlots) _nextArrayIndex--; // reclaim
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

        internal void IncrementDepth() => _depthStartSlots.Push(_nextArrayIndex);

        internal void DecrementDepth(bool completeCommandList = false)
        {
            int rewind = _depthStartSlots.Pop();
            if (_acl.RepeatIdenticalRanges && _acl.ReuseScratchSlots)
                _nextArrayIndex = rewind;

            SyncCounters();
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

        internal void EndCommandChunk(int[] copyToParent = null,
                                      bool endingRepeatedChunk = false) =>
            _acl.EndCommandChunk(copyToParent, endingRepeatedChunk);

        #endregion  // DepthAndChunkFacade


        #region OrderedBufferHooks   // rarely used directly

        internal void RegisterSourceIndex(int idx) =>
            _acl.OrderedSourceIndices.Add(idx);

        internal void RegisterDestinationIndex(int idx) =>
            _acl.OrderedDestinationIndices.Add(idx);

        #endregion


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
            int cmdIndex = _nextCommandIndex;                 // position before adding the blank
            AddCommand(new ArrayCommand(ArrayCommandType.Blank, -1, -1));
            return cmdIndex;
        }

        #endregion
    }
}
