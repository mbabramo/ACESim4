using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Records commands, allocates scratch slots and manages author‑time state
    /// for an owning <see cref="ArrayCommandList"/> instance.  After building is
    /// complete the command list can be executed by <see cref="ArrayCommandListRunner"/>.
    /// </summary>
    public sealed class CommandRecorder
    {
        private readonly ArrayCommandList _acl;

        // local author‑time counters (kept in sync with the ACL for introspection)
        private int _nextArrayIndex;
        private int _maxArrayIndex;
        private int _nextCommandIndex;

        // depth‑based scratch rewind support
        private readonly Stack<int> _depthStartSlots = new();

        public CommandRecorder(ArrayCommandList owner)
        {
            _acl = owner ?? throw new ArgumentNullException(nameof(owner));
            _nextArrayIndex = _acl.NextArrayIndex;
            _maxArrayIndex = _acl.MaxArrayIndex;
            _nextCommandIndex = _acl.NextCommandIndex;
        }

        // ───────────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ───────────────────────────────────────────────────────────────────────────────
        private void SyncCounters()
        {
            _acl.NextArrayIndex = _nextArrayIndex;
            _acl.MaxArrayIndex = _maxArrayIndex;
            _acl.NextCommandIndex = _nextCommandIndex;
        }

        private void AddCommand(ArrayCommand cmd)
        {
            // Support repeat‑identical‑range optimisation
            if (_acl.RepeatingExistingCommandRange)
            {
                var existing = _acl.UnderlyingCommands[_nextCommandIndex];
                if (!cmd.Equals(existing))
                    throw new InvalidOperationException("Command mismatch in RepeatIdenticalRanges block.");
                _nextCommandIndex++;
                SyncCounters();
                return;
            }

            // Ensure capacity
            if (_nextCommandIndex >= _acl.UnderlyingCommands.Length)
                Array.Resize(ref _acl.UnderlyingCommands, _acl.UnderlyingCommands.Length * 2);

            _acl.UnderlyingCommands[_nextCommandIndex++] = cmd;

            // Track highest scratch slot
            if (_nextArrayIndex > _maxArrayIndex)
                _maxArrayIndex = _nextArrayIndex;

            SyncCounters();
        }

        // ─────────────────────────────────── scratch‑slot allocation ─────────────────────────────────────
        public int NewZero()
        {
            int slot = _nextArrayIndex++;
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, slot, -1));
            return slot;
        }

        public int NewUninitialized() => _nextArrayIndex++;

        public int[] NewZeroArray(int size)
        {
            var arr = new int[size];
            for (int i = 0; i < size; i++) arr[i] = NewZero();
            return arr;
        }

        public int[] NewUninitializedArray(int size)
        {
            var arr = new int[size];
            for (int i = 0; i < size; i++) arr[i] = NewUninitialized();
            return arr;
        }

        // ───────────────────────────────────── copy / arithmetic helpers ─────────────────────────────────
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

        public int[] CopyToNew(int[] srcs, bool fromOriginal)
            => srcs.Select(s => CopyToNew(s, fromOriginal)).ToArray();

        public void MultiplyBy(int idx, int multIdx)
            => AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, idx, multIdx));

        public void Increment(int idx, bool targetOriginal, int incIdx)
        {
            if (targetOriginal && _acl.UseOrderedDestinations)
            {
                if (_acl.ReuseDestinations &&
                    _acl.ReusableOrderedDestinationIndices.TryGetValue(idx, out int existingOd))
                {
                    AddCommand(new ArrayCommand(ArrayCommandType.ReusedDestination, existingOd, incIdx));
                }
                else
                {
                    _acl.OrderedDestinationIndices.Add(idx);
                    if (_acl.ReuseDestinations)
                        _acl.ReusableOrderedDestinationIndices[idx] = _acl.OrderedDestinationIndices.Count - 1;
                    AddCommand(new ArrayCommand(ArrayCommandType.NextDestination, -1, incIdx));
                }
            }
            else
            {
                AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, idx, incIdx));
            }
        }

        public void Decrement(int idx, int decIdx)
            => AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, idx, decIdx));

        // ───────────────────────────────────── flow‑control helpers ─────────────────────────────────────
        public void InsertIf() => AddCommand(new ArrayCommand(ArrayCommandType.If, -1, -1));
        public void InsertEndIf() => AddCommand(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

        // ───────────────────────────────────── depth management ─────────────────────────────────────────
        public void IncrementDepth() => _depthStartSlots.Push(_nextArrayIndex);

        public void DecrementDepth(bool completeCommandList = false)
        {
            if (_depthStartSlots.Count == 0)
                throw new InvalidOperationException("DecrementDepth called without matching IncrementDepth");

            int rewind = _depthStartSlots.Pop();
            if (_acl.RepeatIdenticalRanges && _acl.ReuseScratchSlots)
                _nextArrayIndex = rewind;

            SyncCounters();

            if (_depthStartSlots.Count == 0 && completeCommandList)
                _acl.CompleteCommandList();
        }

        // ───────────────────────────────────── chunk helpers (delegated) ────────────────────────────────
        public void StartCommandChunk(bool runChildrenParallel, int? identicalStartCmdRange, string? name = null, bool ignoreKeepTogether = false)
            => _acl.StartCommandChunk(runChildrenParallel, identicalStartCmdRange, name, ignoreKeepTogether);

        public void EndCommandChunk(int[]? copyIncrements = null, bool endingRepeatedChunk = false)
            => _acl.EndCommandChunk(copyIncrements, endingRepeatedChunk);

        // ───────────────────────────────── ordered‑buffer hooks (rarely used directly) ──────────────────
        public void RegisterSourceIndex(int idx) => _acl.OrderedSourceIndices.Add(idx);
        public void RegisterDestinationIndex(int idx) => _acl.OrderedDestinationIndices.Add(idx);
    }
}
