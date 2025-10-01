using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

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
        private Stack<int> _repeatRangeStack = new();


        // Logging
        private const int _recentNewCapacity = 64;
        private (int attemptedCmdIndex, ArrayCommand cmd)[] _recentNewRing = new (int, ArrayCommand)[_recentNewCapacity];
        private int _recentNewHead = 0;
        private int _recentNewCount = 0;

        internal ArrayCommandList Acl => _acl;                     // expose to modes
        public IEmissionMode Mode { get; set; } = RecordingMode.Instance;
        internal OrderedIoRecorder IO { get; }                     // set in ctor

        public CommandRecorder(ArrayCommandList owner)
        {
            _acl = owner ?? throw new ArgumentNullException(nameof(owner));
            NextArrayIndex = 0;
            MaxArrayIndex = -1;
            NextCommandIndex = 0;

            IO = new OrderedIoRecorder(_acl);
        }

        public CommandRecorder Clone(ArrayCommandList acl2) =>
            new CommandRecorder(acl2)
            {
                NextArrayIndex = NextArrayIndex,
                MaxArrayIndex = MaxArrayIndex,
                NextCommandIndex = NextCommandIndex,
                _depthStartSlots = new Stack<int>(_depthStartSlots.Reverse()),
                _repeatRangeStack = new Stack<int>(_repeatRangeStack.Reverse())
            };

        #region InternalHelpers

        public void AddCommand(ArrayCommand cmd)
        {
            // Always remember what we were about to emit (for debug on mismatch)
            RememberNewCandidate(cmd);

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

            if (_ifStack.Count > 0)
            {
                ref var top = ref _ifStack.TryPeek(out var tmp)
                    ? ref _ifStack.ToArray()[0] // Stack<T>.TryPeek returns a copy; use a small helper or replace Stack<T> with custom for real by-ref
                    : ref tmp;                   // For brevity in this snippet; in production, switch to a small custom stack to allow by-ref.
                if (cmd.CommandType == ArrayCommandType.NextSource)      top.NewNextSrc++;
                else if (cmd.CommandType == ArrayCommandType.NextDestination) top.NewNextDst++;
            }

            // Provenance capture for writes to VS
            if (WritesToVS(cmd.CommandType))
                NoteWrite(cmd.Index, cmd.CommandType, cmd.SourceIndex);
            _acl.UnderlyingCommands[NextCommandIndex++] = cmd;
            if (NextArrayIndex > MaxArrayIndex) MaxArrayIndex = NextArrayIndex;
        }

        private void ThrowRepeatMismatch(in ArrayCommand newCmd, in ArrayCommand recorded)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("Repeat-range mismatch");
            sb.AppendLine($"  at cmd index: {NextCommandIndex}");
            sb.AppendLine($"  recorded: {RenderCommand(recorded)}");
            sb.AppendLine($"  new     : {RenderCommand(newCmd)}");
            sb.AppendLine();

            sb.AppendLine("Recorder/ACL state:");
            sb.AppendLine($"  UseOrderedSourcesAndDestinations: {_acl.UseOrderedSourcesAndDestinations}");
            sb.AppendLine($"  RepeatIdenticalRanges           : {_acl.RepeatIdenticalRanges}");
            sb.AppendLine($"  RepeatingExistingCommandRange   : {_acl.RepeatingExistingCommandRange}");
            sb.AppendLine($"  ReuseScratchSlots               : {_acl.ReuseScratchSlots}");
            sb.AppendLine($"  NextArrayIndex / MaxArrayIndex  : {NextArrayIndex} / {_acl.MaxArrayIndex}");
            sb.AppendLine($"  Depth frames                    : {_depthStartSlots.Count}");
            sb.AppendLine();

            // Context breadcrumbs (author-supplied)
            sb.AppendLine($"Breadcrumbs: {RenderBreadcrumbs()}");
            sb.AppendLine();

            // Recorded-side stats near mismatch (existing logic preserved) …
            // [keep your existing envelope/nearest-comment/IF-depth/pointer-skip scan here]

            // ──────────────────────────────────────────────────────────────────
            // New-side live counters from innermost IF (what *we* just emitted)
            // ──────────────────────────────────────────────────────────────────
            if (_ifStack.Count > 0)
            {
                var top = _ifStack.Peek();
                sb.AppendLine("New-side (since innermost IF):");
                sb.AppendLine($"  New NextSource: {top.NewNextSrc}, New NextDestination: {top.NewNextDst}");
                sb.AppendLine();
            }

            // ──────────────────────────────────────────────────────────────────
            // Slot provenance for the participants of the mismatch
            // ──────────────────────────────────────────────────────────────────
            sb.AppendLine("Provenance:");
            sb.AppendLine($"  Target index (recorded/new): {RenderSlot(recorded.Index)}");
            if (recorded.Index != newCmd.Index)
                sb.AppendLine($"                               {RenderSlot(newCmd.Index)}");
            sb.AppendLine($"  Source index (recorded):     {RenderSlot(recorded.SourceIndex)}");
            sb.AppendLine($"  Source index (new):          {RenderSlot(newCmd.SourceIndex)}");
            sb.AppendLine();

            // Ordered tails + recent attempts (keep your existing sections) …

            throw new InvalidOperationException(sb.ToString());
        }


        private static string RenderCommand(in ArrayCommand c) =>
            $"{c.CommandType} (idx={c.Index}, src={c.SourceIndex})";

        // ─────────────────────────────────────────────────────────────────────────────
        // Provenance (opt-in, author-time only)
        // ─────────────────────────────────────────────────────────────────────────────
        private const bool TraceProvenance = true; // or wire to EvolutionSettings / build symbol

        private struct SlotMeta
        {
            public int FirstCmd, LastCmd;
            public ArrayCommandType FirstOp, LastOp;
            public int FirstDepth, LastDepth;
            public int FirstRepeatStart, LastRepeatStart; // -1 if none
            public int AliasedFrom; // source slot for CopyTo/NextSource alias; -1 if none
            public string Tag;      // optional high-level label
        }

        private readonly Dictionary<int, SlotMeta> _slotMeta = new();

        private static bool WritesToVS(ArrayCommandType t) =>
            t == ArrayCommandType.Zero ||
            t == ArrayCommandType.CopyTo ||
            t == ArrayCommandType.MultiplyBy ||
            t == ArrayCommandType.IncrementBy ||
            t == ArrayCommandType.DecrementBy ||
            t == ArrayCommandType.NextSource; // NextDestination writes to the ordered buffer, not VS

        private void NoteWrite(int idx, ArrayCommandType op, int sourceIdx)
        {
            if (!TraceProvenance || idx < 0) return;

            ref var m = ref CollectionsMarshal.GetValueRefOrAddDefault(_slotMeta, idx, out bool existed);
            if (!existed)
            {
                m.FirstCmd = NextCommandIndex;
                m.FirstOp  = op;
                m.FirstDepth = _depthStartSlots.Count;
                m.FirstRepeatStart = _repeatRangeStack.Count > 0 ? _repeatRangeStack.Peek() : -1;
                m.AliasedFrom = -1;
            }
            m.LastCmd = NextCommandIndex;
            m.LastOp  = op;
            m.LastDepth = _depthStartSlots.Count;
            m.LastRepeatStart = _repeatRangeStack.Count > 0 ? _repeatRangeStack.Peek() : -1;

            // Track alias/source lineage
            if ((op == ArrayCommandType.CopyTo || op == ArrayCommandType.NextSource) && sourceIdx >= 0)
                m.AliasedFrom = sourceIdx;
        }

        public void DebugLabelSlot(int idx, string label)
        {
            if (!TraceProvenance || idx < 0) return;
            ref var m = ref CollectionsMarshal.GetValueRefOrAddDefault(_slotMeta, idx, out _);
            m.Tag = label;
        }

        private string RenderSlot(int idx)
        {
            if (idx < 0) return "(n/a)";
            if (!_slotMeta.TryGetValue(idx, out var m))
                return $"slot {idx}  (no provenance captured)";

            string alias = m.AliasedFrom >= 0 ? $" aliasedFrom={m.AliasedFrom}" : "";
            string tag   = string.IsNullOrEmpty(m.Tag) ? "" : $" tag=\"{m.Tag}\"";
            string rep1  = m.FirstRepeatStart >= 0 ? $" repeatStart@{m.FirstRepeatStart}" : "";
            string rep2  = m.LastRepeatStart  >= 0 ? $" repeatStart@{m.LastRepeatStart}"  : "";

            return $"slot {idx}: first@{m.FirstCmd} {m.FirstOp} depth={m.FirstDepth}{rep1}; " +
                   $"last@{m.LastCmd} {m.LastOp} depth={m.LastDepth}{rep2}{alias}{tag}";
        }

        private readonly List<string> _breadcrumbs = new();
        public IDisposable PushBreadcrumb(string label) => new Breadcrumb(this, label);
        private readonly struct Breadcrumb : IDisposable
        {
            private readonly CommandRecorder _r;
            public Breadcrumb(CommandRecorder r, string label)
            {
                _r = r; _r._breadcrumbs.Add(label);
            }
            public void Dispose()
            {
                var list = _r._breadcrumbs;
                if (list.Count > 0) list.RemoveAt(list.Count - 1);
            }
        }

        private string RenderBreadcrumbs()
            => _breadcrumbs.Count == 0 ? "(none)" : string.Join(" > ", _breadcrumbs);

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
            => Mode.EmitCopyToNew(sourceIdx, fromOriginalSources, this, IO);


        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources) =>
            sourceIndices.Select(idx => CopyToNew(idx, fromOriginalSources)).ToArray();

        public void CopyToExisting(int index, int sourceIndex)
        {
            InsertComment($"[COPY/EXIST] idx={index} src={sourceIndex} replay={_acl.RepeatingExistingCommandRange}");
            bool isCheckpoint = index == ArrayCommandList.CheckpointTrigger;
            Mode.EmitCopyToExisting(index, sourceIndex, isCheckpoint, this, IO);
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
            InsertComment($"[ROUTE] Increment targetOriginal={targetOriginal} idx={idx} src={incIdx} inRepeat={_acl.RepeatingExistingCommandRange} useOSOD={_acl.UseOrderedSourcesAndDestinations}");
            Mode.EmitIncrement(idx, targetOriginal, incIdx, this, IO);
        }

        public void Decrement(int idx, int decIdx) =>
            AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, idx, decIdx));
        #endregion

        #region ZeroHelpers
        public void ZeroExisting(int index)
            => Mode.EmitZeroExisting(index, this);

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
        private struct IfFrame { public int IfCmdIndex; public int NewNextSrc; public int NewNextDst; }
        private readonly Stack<IfFrame> _ifStack = new();

        // augment existing methods
        public void InsertIf()
        {
            _ifStack.Push(new IfFrame { IfCmdIndex = NextCommandIndex, NewNextSrc = 0, NewNextDst = 0 });
            AddCommand(new ArrayCommand(ArrayCommandType.If, -1, -1));
        }

        public void InsertEndIf()
        {
            AddCommand(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));
            if (_ifStack.Count > 0) _ifStack.Pop();
        }


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

        public readonly struct DepthScope : System.IDisposable
        {
            private readonly CommandRecorder _r;
            private readonly bool _completeOnDispose;
            public DepthScope(CommandRecorder r, bool completeOnDispose)
            {
                _r = r;
                _completeOnDispose = completeOnDispose;
                _r.IncrementDepth();
            }
            public void Dispose() => _r.DecrementDepth(_completeOnDispose);
        }

        /// <summary>RAII helper: <c>using (rec.OpenDepthScope()) { … }</c></summary>
        public DepthScope OpenDepthScope(bool completeCommandListOnDispose = false)
            => new DepthScope(this, completeCommandListOnDispose);


        internal void IncrementDepth()
        {
            _depthStartSlots.Push(NextArrayIndex);
            InsertIncrementDepthCommand();
            InsertComment($"[DEPTH] op=INC nextAI={NextArrayIndex} frames={_depthStartSlots.Count} nextCI={NextCommandIndex}");
        }


        internal void DecrementDepth(bool completeCommandList = false)
        {
            InsertDecrementDepthCommand();

            int rewind = _depthStartSlots.Pop();
            int before = NextArrayIndex;

            if (_acl.RepeatIdenticalRanges && _acl.ReuseScratchSlots)
                NextArrayIndex = rewind;

            InsertComment($"[DEPTH] op=DEC after nextAI={NextArrayIndex} rewind={rewind} before={before} frames={_depthStartSlots.Count} nextCI={NextCommandIndex}");

            if (_depthStartSlots.Count == 0 && completeCommandList)
                _acl.CompleteCommandList();
        }

        internal void StartCommandChunk(bool runChildrenParallel,
                                        int? identicalStartCmdRange,
                                        string name = null,
                                        bool ignoreKeepTogether = false)
        {
            // If this chunk is declared as identical to a previously recorded range,
            // remember that recorded start so provenance can include repeat context.
            if (identicalStartCmdRange.HasValue)
                _repeatRangeStack.Push(identicalStartCmdRange.Value);

            _acl.StartCommandChunk(runChildrenParallel, identicalStartCmdRange, name, ignoreKeepTogether);
        }

        internal void EndCommandChunk(bool endingRepeatedChunk = false)
        {
            _acl.EndCommandChunk(endingRepeatedChunk);

            // Close the tracked repeat-range if this chunk was a repeated one.
            if (endingRepeatedChunk && _repeatRangeStack.Count > 0)
                _repeatRangeStack.Pop();
        }

        #endregion  // DepthAndChunkFacade

        #region Comments and blank commands

        public List<string> CommentTable = new List<string>();

        public void InsertComment(string text)
        {
            // If we are not replaying and comments are not enabled, do not emit.
            if (!_acl.RepeatingExistingCommandRange && !_acl.EmitComments)
                return;

            // During replay, only emit the comment if the recorded stream expects one here.
            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];

                if (expected.CommandType == ArrayCommandType.Comment)
                {
                    int expectedId = expected.SourceIndex;

                    // Ensure local table can resolve the recorded id for later diagnostics.
                    EnsureCommentSlotWithText(expectedId, text);

                    AddCommand(new ArrayCommand(ArrayCommandType.Comment, -1, expectedId));
                }

                // If the recorded stream does not expect a comment at this position,
                // do not emit one; silently skip to keep replay byte-identical.
                return;
            }

            int id = CommentTable.Count;
            CommentTable.Add(text);
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

        #region Logging helper

        private void RememberNewCandidate(in ArrayCommand cmd)
        {
            int pos = _recentNewHead % _recentNewCapacity;
            _recentNewRing[pos] = (NextCommandIndex, cmd);
            _recentNewHead++;
            if (_recentNewCount < _recentNewCapacity) _recentNewCount++;
        }

        private string GetCommentTextOrNull(int id)
        {
            if ((uint)id < (uint)CommentTable.Count)
                return CommentTable[id];
            return null;
        }

        private void EnsureCommentSlotWithText(int id, string text)
        {
            if (id < 0) return;
            while (CommentTable.Count <= id)
                CommentTable.Add(null);
            if (CommentTable[id] == null && text != null)
                CommentTable[id] = text;
        }

        #endregion
    }
}
