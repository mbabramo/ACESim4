using ACESimBase.Util.Collections;
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
        internal ArrayCommandList Acl => _acl; 

        public IEmissionMode Mode { get; set; } = RecordingMode.Instance;
        internal OrderedIoRecorder IO { get; }     
        public VirtualStack VS { get; }
        private readonly CommandEmitter _emitter;
        private readonly DecorationEmitter _decoration;



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

        public Action<int, ArrayCommand, bool> OnEmit;           // (cmdIndex, cmd, isReplay)
        public Func<int, ArrayCommand, bool> BreakOnPredicate;   // return true → Debugger.Break()


        public CommandRecorder(ArrayCommandList owner)
        {
            VS = new VirtualStack(this);
            _emitter = new CommandEmitter(this);
            _decoration = new DecorationEmitter(this);
            _acl = owner ?? throw new ArgumentNullException(nameof(owner));
            IO = new OrderedIoRecorder(_acl);
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
                _depthStartSlots = new Stack<int>(_depthStartSlots.Reverse()),
                _repeatRangeStack = new Stack<int>(_repeatRangeStack.Reverse())
            };

        public void AddCommand(ArrayCommand cmd)
        {
            _emitter.Emit(cmd);
        }

        #region Diagnostics

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
            sb.AppendLine($"  Repeat-range frames             : {_repeatRangeStack.Count} (topStart={(_repeatRangeStack.Count > 0 ? _repeatRangeStack.Peek() : -1)})");
            sb.AppendLine();

            sb.AppendLine($"Breadcrumbs: {RenderBreadcrumbs()}");
            sb.AppendLine();

            // New-side counters since innermost IF (from the live IF frame if present)
            if (_ifStack.Count > 0)
            {
                ref var top = ref _ifStack.PeekRef();
                sb.AppendLine("New-side (since innermost IF):");
                sb.AppendLine($"  New NextSource: {top.NewNextSrc}, New NextDestination: {top.NewNextDst}");
                sb.AppendLine();
            }

            // Recorded-side counters since the corresponding IF (scans recorded stream)
            AppendRecordedCountersSinceIf(sb);

            // Provenance and lineage snapshots
            sb.AppendLine("Provenance:");
            sb.AppendLine($"  Target index (recorded/new): {RenderSlot(recorded.Index)}");
            if (recorded.Index != newCmd.Index)
                sb.AppendLine($"                               {RenderSlot(newCmd.Index)}");
            sb.AppendLine($"  Source index (recorded):     {RenderSlot(recorded.SourceIndex)}");
            sb.AppendLine($"  Source index (new):          {RenderSlot(newCmd.SourceIndex)}");
            sb.AppendLine();

            sb.AppendLine("Lineage:");
            sb.AppendLine($"  Target:  {RenderLineageChain(recorded.Index)}");
            sb.AppendLine($"  Src(rec):{RenderLineageChain(recorded.SourceIndex)}");
            sb.AppendLine($"  Src(new):{RenderLineageChain(newCmd.SourceIndex)}");
            sb.AppendLine();

            // Recorded stream window around the mismatch
            DumpRecordedWindow(sb, windowRadius: 16);

            // Most recent attempted emissions on the "new" side
            DumpRecentNewAttempts(sb, maxEntries: 24);

            throw new InvalidOperationException(sb.ToString());
        }


        private void AppendRecordedCountersSinceIf(System.Text.StringBuilder sb)
        {
            // Find the innermost IF that surrounds the current recorded position, if any.
            int ifStart = -1;
            for (int i = NextCommandIndex - 1, depth = 0; i >= 0; i--)
            {
                var t = _acl.UnderlyingCommands[i].CommandType;
                if (t == ArrayCommandType.EndIf) depth++;
                else if (t == ArrayCommandType.If)
                {
                    if (depth == 0) { ifStart = i; break; }
                    depth--;
                }
            }
            int start = ifStart >= 0 ? ifStart : 0;
            int ns = 0, nd = 0;
            for (int i = start; i < NextCommandIndex; i++)
            {
                var t = _acl.UnderlyingCommands[i].CommandType;
                if (t == ArrayCommandType.NextSource) ns++;
                else if (t == ArrayCommandType.NextDestination) nd++;
            }
            sb.AppendLine("Recorded-side (since innermost IF):");
            sb.AppendLine($"  Rec NextSource: {ns}, Rec NextDestination: {nd}");
            sb.AppendLine();
        }

        private string RenderLineageChain(int idx)
        {
            if (idx < 0) return "(n/a)";
            var chain = new System.Collections.Generic.List<string>();
            int guard = 0;
            int cur = idx;
            while (cur >= 0 && guard++ < 16)
            {
                if (!_slotMeta.TryGetValue(cur, out var m))
                {
                    chain.Add($"slot {cur}");
                    break;
                }
                string tag = string.IsNullOrEmpty(m.Tag) ? "" : $" tag=\"{m.Tag}\"";
                chain.Add($"slot {cur} (first@{m.FirstCmd} {m.FirstOp}, last@{m.LastCmd} {m.LastOp}{tag})");
                cur = m.AliasedFrom;
                if (cur < 0) break;
            }
            return string.Join("  <=  ", chain);
        }

        private void DumpRecordedWindow(System.Text.StringBuilder sb, int windowRadius)
        {
            int s = System.Math.Max(0, NextCommandIndex - windowRadius);
            int eExclusive = System.Math.Min(_acl.MaxCommandIndex + 1, NextCommandIndex + windowRadius + 1);

            sb.AppendLine($"Recorded window [{s},{eExclusive}):");
            int depth = 0;

            for (int i = s; i < eExclusive; i++)
            {
                var c = _acl.UnderlyingCommands[i];

                if (c.CommandType == ArrayCommandType.DecrementDepth)
                    depth = System.Math.Max(0, depth - 1);

                string mark = i == NextCommandIndex ? ">>" : "  ";
                sb.AppendLine($"{mark} {i,6}: {c.CommandType,-16} idx={c.Index,5} src={c.SourceIndex,5} depth~{depth}");

                if (c.CommandType == ArrayCommandType.IncrementDepth)
                    depth++;
            }

            sb.AppendLine();
        }

        private void DumpRecentNewAttempts(System.Text.StringBuilder sb, int maxEntries)
        {
            sb.AppendLine("Recent new attempts (ring buffer):");
            int count = System.Math.Min(_recentNewCount, maxEntries);
            for (int k = count - 1; k >= 0; k--)
            {
                int pos = (_recentNewHead - 1 - k) % _recentNewRing.Length;
                if (pos < 0) pos += _recentNewRing.Length;
                var (attemptedIndex, cmd) = _recentNewRing[pos];
                sb.AppendLine($"  new@{attemptedIndex}: {RenderCommand(cmd)}");
            }
            sb.AppendLine();
        }

        // Enables conditional per-command logging via the existing OnEmit hook.
        // By default logs only structural and ordered I/O commands.
        // Use extraPredicate to further filter (e.g., by command index).
        public void EnableConditionalEmitLogging(
            Func<ArrayCommandType, bool> includeCommandType = null,
            Func<int, ArrayCommand, bool> extraPredicate = null,
            bool includeReplayFlag = true,
            int logEveryNth = 1)
        {
            if (logEveryNth <= 0) logEveryNth = 1;

            OnEmit = (ci, cmd, isReplay) =>
            {
                if ((++_emitLogCounter % logEveryNth) != 0)
                    return;

                bool include = (includeCommandType ?? DefaultEmitFilter).Invoke(cmd.CommandType);
                if (!include)
                    return;

                if (extraPredicate != null && !extraPredicate(ci, cmd))
                    return;

                string replayText = includeReplayFlag ? (isReplay ? "replay " : "record ") : string.Empty;

                string originText = "";
                if (cmd.CommandType == ArrayCommandType.IncrementDepth)
                {
                    string openMark = _openDepthMarks.Count > 0 ? _openDepthMarks.Peek() : "Unmarked";
                    originText = $" openMark={openMark}";
                }
                else if (cmd.CommandType == ArrayCommandType.DecrementDepth)
                {
                    string closeMark = _lastClosedDepthMark ?? "Unmarked";
                    originText = $" closeMark={closeMark}";
                }

                // TabbedText.WriteLine(
                //    $"[EMIT] ci={ci} {replayText}{cmd.CommandType} idx={cmd.Index} src={cmd.SourceIndex} " +
                //    $"depthFrames={_depthStartSlots.Count} repeatFrames={_repeatRangeStack.Count} ifFrames={_ifStack.Count}{originText}");
            };
        }


        // Resets the conditional logger.
        public void DisableEmitLogging() => OnEmit = null;

        // Default filter: structural and ordered I/O are often sufficient for tracing structure.
        private static bool DefaultEmitFilter(ArrayCommandType t) =>
            t == ArrayCommandType.If ||
            t == ArrayCommandType.EndIf ||
            t == ArrayCommandType.IncrementDepth ||
            t == ArrayCommandType.DecrementDepth ||
            t == ArrayCommandType.NextSource ||
            t == ArrayCommandType.NextDestination ||
            t == ArrayCommandType.Checkpoint ||
            t == ArrayCommandType.Comment;

        // Internal counter for throttling logs (logEveryNth).
        private int _emitLogCounter;

        private static string RenderCommand(in ArrayCommand c) =>
            $"{c.CommandType} (idx={c.Index}, src={c.SourceIndex})";

        private const bool TraceProvenance = true; 

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

        private void SlotMetaWrite(int idx, ArrayCommandType op, int sourceIdx)
        {
            if (!TraceProvenance || idx < 0) return;

            ref SlotMeta m = ref CollectionsMarshal.GetValueRefOrAddDefault(_slotMeta, idx, out bool existed);
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

        // Depth-origin tagging (for logging only; no effect on emitted commands)
        private string _pendingDepthMark;
        private readonly Stack<string> _openDepthMarks = new();
        private string _lastClosedDepthMark;

        /// <summary>
        /// Tag the next IncrementDepth with a human-readable origin (e.g., "DecisionEVBody").
        /// The tag is consumed by the next call to IncrementDepth.
        /// </summary>
        public void MarkNextDepth(string label)
        {
            _pendingDepthMark = string.IsNullOrWhiteSpace(label) ? "Unmarked" : label;
        }

        // Add this small policy to control replay-time structural emissions.
        public enum ReplayStructuralPolicy
        {
            Legacy,                    // current behavior (unchanged)
            GuardStructuralOps,        // during replay, emit structural ops only if the recorded stream expects them
            AssertStructuralOpsMatch   // during replay, throw immediately if the recorded stream differs
        }

        // Opt-in; default preserves current behavior.
        public ReplayStructuralPolicy StructuralPolicy { get; set; } = ReplayStructuralPolicy.Legacy;

        // Lightweight, in-memory tracing (does NOT touch the command stream).
        public bool StructuralReplayTracing { get; set; } = false;
        public readonly System.Collections.Generic.List<string> StructuralLog = new();
        public string StructuralLogContents => String.Join("\n", StructuralLog);

        private void TraceStructural(string message)
        {
            if (StructuralReplayTracing)
                StructuralLog.Add($"ci={NextCommandIndex} depth={_depthStartSlots.Count} :: {message}");
        }
        // When > 0, we are in a region where a new-side If was suppressed during replay;
        // every emitted command is force-mapped to the recorded stream until the region ends.
        private int _forcedReplayDepth = 0;


        #endregion

        #region ScratchAllocation

        /// <summary>Create one scratch slot initialised to 0.</summary>
        public int NewZero()
        {
            bool isReplay = _acl.RepeatingExistingCommandRange;

            if (isReplay)
            {
                var recorded = _acl.UnderlyingCommands[NextCommandIndex];

                // Fast path: recorded also has Zero here — follow strictly.
                if (recorded.CommandType == ArrayCommandType.Zero)
                {
                    // Ensure our NextArrayIndex keeps up with the recorded target.
                    if (recorded.Index >= 0 && recorded.Index + 1 > NextArrayIndex)
                        NextArrayIndex = recorded.Index + 1;

                    // Consume via emitter so replay depth/stack mirroring remains centralized.
                    _emitter.Emit(recorded);
                    return recorded.Index;
                }

                // Guarded tolerance: if policy is GuardStructuralOps, map a non-struct mismatch too.
                // This is necessary when replaying after a suppressed If where the generator attempts
                // to "allocate a zero" but the recorded stream proceeds with a data op instead.
                if (StructuralPolicy == ReplayStructuralPolicy.GuardStructuralOps)
                {
                    // If the recorded command writes to VS, align our high-water to its target.
                    if (recorded.Index >= 0 && recorded.Index + 1 > NextArrayIndex)
                        NextArrayIndex = recorded.Index + 1;

                    // Optional provenance (diagnostic).
                    if (recorded.Index >= 0)
                        SlotMetaWrite(recorded.Index, recorded.CommandType, recorded.SourceIndex);

                    // Consume the recorded command verbatim so streams stay aligned.
                    _emitter.Emit(recorded);

                    // Structural trace is optional; enable via StructuralReplayTracing.
                    TraceStructural($"NEWZERO-GUARD: mapped new Zero \u2192 recorded {recorded.CommandType}");

                    // Return the recorded target index so callers use the same slot the recorded stream used.
                    return recorded.Index;
                }

                // Strict/Assert policies: enforce exact type equality.
                throw new InvalidOperationException(
                    $"Repeat-range mismatch at cmd {NextCommandIndex}: recorded {recorded.CommandType} vs new Zero.");
            }

            // Recording path (unchanged).
            int fresh = NextArrayIndex++;
            _emitter.Emit(new ArrayCommand(ArrayCommandType.Zero, fresh, -1));
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

        // Copy/Move
        public int CopyToNew(VsIndex source) => CopyToNew(source.Value, fromOriginalSources: false);
        public int CopyToNew(OsIndex source) => CopyToNew(source.Value, fromOriginalSources: true);

        public void CopyToExisting(VsIndex index, VsIndex source) => CopyToExisting(index.Value, source.Value);

        // Arithmetic
        public void MultiplyBy(VsIndex index, VsIndex multiplier) => MultiplyBy(index.Value, multiplier.Value);

        public void Increment(VsIndex index, bool targetOriginal, VsIndex inc)
            => Increment(index.Value, targetOriginal, inc.Value);

        public void Decrement(VsIndex index, VsIndex dec)
            => Decrement(index.Value, dec.Value);

        // Comparisons (VS indices)
        public void InsertEqualsOtherArrayIndexCommand(VsIndex i1, VsIndex i2)
            => InsertEqualsOtherArrayIndexCommand(i1.Value, i2.Value);

        public void InsertNotEqualsOtherArrayIndexCommand(VsIndex i1, VsIndex i2)
            => InsertNotEqualsOtherArrayIndexCommand(i1.Value, i2.Value);

        public void InsertGreaterThanOtherArrayIndexCommand(VsIndex i1, VsIndex i2)
            => InsertGreaterThanOtherArrayIndexCommand(i1.Value, i2.Value);

        public void InsertLessThanOtherArrayIndexCommand(VsIndex i1, VsIndex i2)
            => InsertLessThanOtherArrayIndexCommand(i1.Value, i2.Value);

        // Comparisons vs. immediate
        public void InsertEqualsValueCommand(VsIndex idx, int value)
            => InsertEqualsValueCommand(idx.Value, value);

        public void InsertNotEqualsValueCommand(VsIndex idx, int value)
            => InsertNotEqualsValueCommand(idx.Value, value);


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
        public void MultiplyBy(int idx, int multIdx)
        {
            Mode.EmitMultiplyBy(idx, multIdx, this);
        }



        /// <summary>Increment or stage-increment slot <paramref name="idx"/> by value from <paramref name="incIdx"/>.</summary>
        public void Increment(int idx, bool targetOriginal, int incIdx)
        {
            InsertComment($"[ROUTE] Increment targetOriginal={targetOriginal} idx={idx} src={incIdx} inRepeat={_acl.RepeatingExistingCommandRange} useOSOD={_acl.UseOrderedSourcesAndDestinations}");
            Mode.EmitIncrement(idx, targetOriginal, incIdx, this, IO);
        }

        public void Decrement(int idx, int decIdx)
        {
            Mode.EmitDecrement(idx, decIdx, this);
        }


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
            using var scratch = OpenScratchScope(reclaimOnDispose: reuseTmp);

            int tmp = CopyToNew(factor1Idx, fromOriginalSources: false);
            MultiplyBy(tmp, factor2Idx);
            Increment(targetIdx, targetOriginal, tmp);

            // Returning the temporary index preserves existing behavior and is used
            // only for capacity determination elsewhere.
            return tmp;
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

        public int DecrementByProduct(int targetIdx, bool targetOriginal,
                                      int factor1Idx, int factor2Idx,
                                      bool reuseTmp = true)
        {
            using var scratch = OpenScratchScope(reclaimOnDispose: reuseTmp);

            int tmp = CopyToNew(factor1Idx, fromOriginalSources: false);
            MultiplyBy(tmp, factor2Idx);

            if (targetOriginal)
                throw new NotSupportedException(
                    "Ordered-destination decrement is not supported. " +
                    "If you truly need OD subtraction, use Increment(targetOriginal: true, <negated value>).");

            Decrement(targetIdx, tmp);
            return tmp;
        }

        #endregion

        #region ComparisonFlowControl
        private struct IfFrame { public int IfCmdIndex; public int NewNextSrc; public int NewNextDst; }
        private readonly RefStack<IfFrame> _ifStack = new();

        public void InsertIf()
        {
            int ifCmdIndex = NextCommandIndex;

            if (_acl.RepeatingExistingCommandRange)
            {
                // Auto-consume closers if the recorded stream has them here (existing behavior).
                while (NextCommandIndex < _acl.UnderlyingCommands.Length &&
                       _acl.UnderlyingCommands[NextCommandIndex].CommandType == ArrayCommandType.DecrementDepth)
                {
                    TraceStructural("InsertIf(): auto-consuming recorded DecrementDepth prior to If");
                    DecrementDepth();
                }

                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                TraceStructural($"InsertIf(): expected next recorded={expected.CommandType}");

                if (StructuralPolicy == ReplayStructuralPolicy.GuardStructuralOps &&
                    expected.CommandType != ArrayCommandType.If)
                {
                    // Do not emit If; instead enter a forced-replay region so any body emissions
                    // are mapped 1:1 to the recorded stream and won't mismatch.
                    _forcedReplayDepth++;
                    TraceStructural($"InsertIf(): GUARD suppressed If; entering forced-replay region (depth={_forcedReplayDepth})");
                    return; // no push
                }

                if (StructuralPolicy == ReplayStructuralPolicy.AssertStructuralOpsMatch &&
                    expected.CommandType != ArrayCommandType.If)
                {
                    ThrowRepeatMismatch(new ArrayCommand(ArrayCommandType.If, -1, -1), expected);
                }
            }

            // During replay, only push if recorded stream truly has an If here (existing behavior).
            bool shouldPush =
                !_acl.RepeatingExistingCommandRange
                || _acl.UnderlyingCommands[NextCommandIndex].CommandType == ArrayCommandType.If;

            _emitter.Emit(ArrayCommandType.If, -1, -1, alignVsFromRecordedIndex: false);

            if (shouldPush)
                _ifStack.Push(new IfFrame { IfCmdIndex = ifCmdIndex, NewNextSrc = 0, NewNextDst = 0 });

            TraceStructural($"InsertIf(): EMIT If; pushed={shouldPush}");
        }

        public void InsertEndIf()
        {
            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                TraceStructural($"InsertEndIf(): expected next recorded={expected.CommandType}");

                // If we previously suppressed an If, we must also suppress the matching EndIf
                // AND consume the recorded EndIf here so the streams stay aligned.
                if (_forcedReplayDepth > 0)
                {
                    if (expected.CommandType == ArrayCommandType.EndIf)
                    {
                        // Advance the recorded stream without emitting anything.
                        NextCommandIndex++;
                        TraceStructural($"InsertEndIf(): forced-replay region exit (depth={_forcedReplayDepth - 1}); "
                                        + "consumed recorded EndIf; suppressing EndIf");
                    }
                    else
                    {
                        // Defensive: if for some reason the recorded isn't EndIf here, just log and suppress.
                        TraceStructural($"InsertEndIf(): forced-replay region exit (depth={_forcedReplayDepth - 1}); "
                                        + "recorded not EndIf; suppressing EndIf");
                    }

                    // Do NOT emit; do NOT pop IF stack (we never pushed when we suppressed the If).
                    _forcedReplayDepth--;
                    return;
                }

                // Guarded mode: if the recorded stream doesn't expect EndIf here, suppress it.
                if (StructuralPolicy == ReplayStructuralPolicy.GuardStructuralOps &&
                    expected.CommandType != ArrayCommandType.EndIf)
                {
                    TraceStructural("InsertEndIf(): GUARD suppressed EndIf (recorded is not EndIf)");
                    return; // do not pop
                }

                // Assert mode: fail fast if structural shape differs.
                if (StructuralPolicy == ReplayStructuralPolicy.AssertStructuralOpsMatch &&
                    expected.CommandType != ArrayCommandType.EndIf)
                {
                    ThrowRepeatMismatch(new ArrayCommand(ArrayCommandType.EndIf, -1, -1), expected);
                }
            }

            // During replay, only pop if the recorded stream has EndIf here (existing behavior).
            bool popOnReplay =
                _acl.RepeatingExistingCommandRange
                    ? _acl.UnderlyingCommands[NextCommandIndex].CommandType == ArrayCommandType.EndIf
                    : true;

            _emitter.Emit(ArrayCommandType.EndIf, -1, -1, alignVsFromRecordedIndex: false);

            if (popOnReplay && _ifStack.Count > 0)
                _ifStack.Pop();

            TraceStructural($"InsertEndIf(): EMIT EndIf; popped={popOnReplay}");
        }


        public void InsertEqualsOtherArrayIndexCommand(int i1, int i2)
        {
            Mode.EmitEqualsOtherArrayIndex(i1, i2, this);
        }

        public void InsertNotEqualsOtherArrayIndexCommand(int i1, int i2)
        {
            Mode.EmitNotEqualsOtherArrayIndex(i1, i2, this);
        }

        public void InsertGreaterThanOtherArrayIndexCommand(int i1, int i2)
        {
            Mode.EmitGreaterThanOtherArrayIndex(i1, i2, this);
        }

        public void InsertLessThanOtherArrayIndexCommand(int i1, int i2)
        {
            Mode.EmitLessThanOtherArrayIndex(i1, i2, this);
        }

        public void InsertEqualsValueCommand(int idx, int v)
        {
            Mode.EmitEqualsValue(idx, v, this);
        }

        public void InsertNotEqualsValueCommand(int idx, int v)
        {
            Mode.EmitNotEqualsValue(idx, v, this);
        }

        #endregion

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
            // During replay (including forced-replay), do not mutate depth stacks/marks.
            // Simply consume the recorded stream via the emitter to keep alignment.
            if (_acl.RepeatingExistingCommandRange)
            {
                _emitter.Emit(ArrayCommandType.IncrementDepth, -1, -1, alignVsFromRecordedIndex: false);
                return;
            }

            // Recording path (unchanged).
            string mark = _pendingDepthMark ?? "Unmarked";
            _pendingDepthMark = null;
            _openDepthMarks.Push(mark);

            _depthStartSlots.Push(NextArrayIndex);
            InsertIncrementDepthCommand();
            // InsertComment($"[DEPTH] op=INC mark={mark} nextAI={NextArrayIndex} frames={_depthStartSlots.Count} nextCI={NextCommandIndex}");
        }

        internal void DecrementDepth(bool completeCommandList = false)
        {
            // During replay (including forced-replay), do not mutate depth stacks/marks,
            // do not rewind, and do not write comments. Just consume the recorded stream.
            if (_acl.RepeatingExistingCommandRange)
            {
                _emitter.Emit(ArrayCommandType.DecrementDepth, -1, -1, alignVsFromRecordedIndex: false);
                return;
            }

            // Recording path (unchanged).
            _lastClosedDepthMark = _openDepthMarks.Count > 0 ? _openDepthMarks.Pop() : "Unmarked";

            InsertDecrementDepthCommand();

            int rewind = _depthStartSlots.Pop(); // balanced in recording mode
            int before = NextArrayIndex;

            if (_acl.RepeatIdenticalRanges && _acl.ReuseScratchSlots)
                NextArrayIndex = rewind;

            InsertComment($"[DEPTH] op=DEC closeMark={_lastClosedDepthMark} after nextAI={NextArrayIndex} " +
                          $"rewind={rewind} before={before} frames={_depthStartSlots.Count} nextCI={NextCommandIndex}");

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
            _decoration.EmitComment(text);
        }

        /// <summary>
        /// Insert a placeholder “Blank” command and return its position in the command buffer.
        /// (This returns a command index, not an array index.)
        /// </summary>
        public int InsertBlankCommand()
        {
            int cmdIndex = NextCommandIndex;
            _emitter.Emit(ArrayCommandType.Blank, -1, -1, alignVsFromRecordedIndex: false);
            return cmdIndex;
        }

        public int InsertIncrementDepthCommand()
        {
            int cmdIndex = NextCommandIndex;
            _emitter.Emit(ArrayCommandType.IncrementDepth, -1, -1, alignVsFromRecordedIndex: false);
            return cmdIndex;
        }

        public int InsertDecrementDepthCommand()
        {
            int cmdIndex = NextCommandIndex;
            _emitter.Emit(ArrayCommandType.DecrementDepth, -1, -1, alignVsFromRecordedIndex: false);
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

        #region Helper types

        /// <summary>
        /// Centralized command emitter. This routes all writes and preserves existing behavior
        /// for both recording and replay.
        /// </summary>
        private sealed class CommandEmitter
        {
            private readonly CommandRecorder _recorder;

            internal CommandEmitter(CommandRecorder recorder)
            {
                _recorder = recorder;
            }

            /// <summary>
            /// Emits a command of the given type and operands, with optional replay-time
            /// alignment of the virtual stack based on the recorded command's target index.
            /// </summary>
            internal void Emit(ArrayCommandType type, int index, int source, bool alignVsFromRecordedIndex)
            {
                // If replaying and alignment is requested, align VS from the recorded command.
                if (_recorder._acl.RepeatingExistingCommandRange && alignVsFromRecordedIndex)
                {
                    var expected = _recorder._acl.UnderlyingCommands[_recorder.NextCommandIndex];
                    if (expected.Index >= 0)
                        _recorder.VS.AlignToAtLeast(expected.Index + 1);
                }

                // Delegate to the canonical path (handles validation and writing).
                Emit(new ArrayCommand(type, index, source));
            }

            internal void Emit(in ArrayCommand cmd)
            {
                // Remember attempted emission (existing behavior).
                _recorder.RememberNewCandidate(cmd);

                // Optional breakpoint hook.
                if (_recorder.BreakOnPredicate != null && _recorder.BreakOnPredicate(_recorder.NextCommandIndex, cmd))
                    System.Diagnostics.Debugger.Break();

                // Optional callback.
                _recorder.OnEmit?.Invoke(_recorder.NextCommandIndex, cmd, _recorder._acl.RepeatingExistingCommandRange);

                bool isReplay = _recorder._acl.RepeatingExistingCommandRange;

                // ─────────────────────────────────────────────────────────────────────
                // REPLAY: forced-mapping active (e.g., suppressed If region).
                // Always map to the recorded command and mirror depth effects.
                // ─────────────────────────────────────────────────────────────────────
                if (isReplay && _recorder._forcedReplayDepth > 0)
                {
                    var recorded = _recorder._acl.UnderlyingCommands[_recorder.NextCommandIndex];
                    MirrorWriteAlignment(recorded);
                    MirrorDepthEffectsFromRecorded(recorded); // <— balances depth stacks + rewind when recorded is depth
                    MirrorProvenance(recorded);

                    _recorder.NextCommandIndex++;
                    _recorder.TraceStructural($"FORCED-REPLAY: mapped new={cmd.CommandType} \u2192 recorded={recorded.CommandType}");
                    return;
                }

                // ─────────────────────────────────────────────────────────────────────
                // REPLAY: normal path — validate equality; but if structural mismatch
                // and policy == GuardStructuralOps, soft-map to recorded and mirror depth.
                // ─────────────────────────────────────────────────────────────────────
                if (isReplay)
                {
                    var recorded = _recorder._acl.UnderlyingCommands[_recorder.NextCommandIndex];

                    if (!cmd.Equals(recorded))
                    {
                        // Soft-map ANY structural mismatch (either side structural) under Guard policy.
                        if (_recorder.StructuralPolicy == CommandRecorder.ReplayStructuralPolicy.GuardStructuralOps
                            && (IsStructural(cmd.CommandType) || IsStructural(recorded.CommandType)))
                        {
                            MirrorWriteAlignment(recorded);
                            MirrorDepthEffectsFromRecorded(recorded); // keep stacks/rewinds consistent
                            MirrorProvenance(recorded);

                            _recorder.NextCommandIndex++;
                            _recorder.TraceStructural($"REPLAY-MAP: new={cmd.CommandType} \u2192 recorded={recorded.CommandType}");
                            return;
                        }

                        // Otherwise, strict mismatch.
                        _recorder.ThrowRepeatMismatch(cmd, recorded);
                    }

                    // Equal -> still mirror depth effects so stacks/rewinds match recorded.
                    MirrorDepthEffectsFromRecorded(recorded);
                    _recorder.NextCommandIndex++;
                    return;
                }

                // ─────────────────────────────────────────────────────────────────────
                // RECORDING: unchanged — capacity, IF-stack counters, provenance, write.
                // ─────────────────────────────────────────────────────────────────────
                const int HardLimit = 1_000_000_000;
                if (_recorder.NextCommandIndex >= _recorder._acl.UnderlyingCommands.Length)
                {
                    if (_recorder._acl.UnderlyingCommands.Length >= HardLimit)
                        throw new InvalidOperationException("Command buffer exceeded hard limit.");

                    Array.Resize(ref _recorder._acl.UnderlyingCommands,
                                 _recorder._acl.UnderlyingCommands.Length * 2);
                }

                // IF-frame ordered I/O counters (existing behavior).
                if (_recorder._ifStack.Count > 0)
                {
                    ref var top = ref _recorder._ifStack.PeekRef();
                    if (cmd.CommandType == ArrayCommandType.NextSource)
                        top.NewNextSrc++;
                    else if (cmd.CommandType == ArrayCommandType.NextDestination)
                        top.NewNextDst++;
                }

                // Provenance for VS writes (diagnostic).
                if (WritesToVS(cmd.CommandType))
                    _recorder.SlotMetaWrite(cmd.Index, cmd.CommandType, cmd.SourceIndex);

                // Write and advance.
                _recorder._acl.UnderlyingCommands[_recorder.NextCommandIndex++] = cmd;

                // Maintain VS high-water mark.
                _recorder.VS.TouchHighWater();
            }

            // ─────────────────────────────────────────────────────────────────────────
            // Helpers (kept private to CommandEmitter for encapsulation).
            // ─────────────────────────────────────────────────────────────────────────

            private static bool IsStructural(ArrayCommandType t)
            {
                return t == ArrayCommandType.If
                    || t == ArrayCommandType.EndIf
                    || t == ArrayCommandType.IncrementDepth
                    || t == ArrayCommandType.DecrementDepth;
            }

            private void MirrorWriteAlignment(in ArrayCommand recorded)
            {
                if (WritesToVS(recorded.CommandType) && recorded.Index >= 0)
                    _recorder.VS.AlignToAtLeast(recorded.Index + 1);
            }

            private void MirrorProvenance(in ArrayCommand recorded)
            {
                if (WritesToVS(recorded.CommandType))
                    _recorder.SlotMetaWrite(recorded.Index, recorded.CommandType, recorded.SourceIndex);
            }

            private void MirrorDepthEffectsFromRecorded(in ArrayCommand recorded)
            {
                // We mirror depth frames so the recorder's stacks/rewinds remain balanced
                // even when we soft-map structural mismatches.
                switch (recorded.CommandType)
                {
                    case ArrayCommandType.IncrementDepth:
                    {
                        // Use any pending mark if a caller set one; otherwise a neutral placeholder.
                        string mark = _recorder._pendingDepthMark ?? "replay";
                        _recorder._pendingDepthMark = null;

                        _recorder._openDepthMarks.Push(mark);
                        _recorder._depthStartSlots.Push(_recorder.NextArrayIndex);
                        break;
                    }

                    case ArrayCommandType.DecrementDepth:
                    {
                        // Pop mark if present to mirror recording path’s comment bookkeeping.
                        _recorder._lastClosedDepthMark = _recorder._openDepthMarks.Count > 0
                            ? _recorder._openDepthMarks.Pop()
                            : "replay";

                        if (_recorder._depthStartSlots.Count > 0)
                        {
                            int rewind = _recorder._depthStartSlots.Pop();
                            if (_recorder._acl.RepeatIdenticalRanges && _recorder._acl.ReuseScratchSlots)
                                _recorder.NextArrayIndex = rewind;
                        }
                        else
                        {
                            // Defensive only: don't throw; just trace the anomaly once.
                            _recorder.TraceStructural("MirrorDepthEffectsFromRecorded(): underflow on _depthStartSlots during replay; ignored.");
                        }
                        break;
                    }

                    // Structural IF/EndIF are intentionally *not* mirrored into _ifStack here.
                    // _ifStack is managed by InsertIf/InsertEndIf when the recorded stream actually has IF/EndIF.
                    // That keeps ordered I/O counters aligned without double-accounting.
                    default:
                        break;
                }
            }


            private static bool WritesToVS(ArrayCommandType t) =>
                t == ArrayCommandType.Zero ||
                t == ArrayCommandType.CopyTo ||
                t == ArrayCommandType.MultiplyBy ||
                t == ArrayCommandType.IncrementBy ||
                t == ArrayCommandType.DecrementBy ||
                t == ArrayCommandType.NextSource; // NextDestination writes to the ordered buffer, not VS
        }

        /// <summary>
        /// Centralizes emission of non-semantic decorations (e.g., comments)
        /// and preserves existing replay-time gating and comment table behavior.
        /// </summary>
        private sealed class DecorationEmitter
        {
            private readonly CommandRecorder _recorder;

            internal DecorationEmitter(CommandRecorder recorder)
            {
                _recorder = recorder;
            }

            internal void EmitComment(string text)
            {
                // If not replaying and comments are disabled, skip emission (preserves behavior).
                if (!_recorder._acl.RepeatingExistingCommandRange && !_recorder._acl.EmitComments)
                    return;

                if (_recorder._acl.RepeatingExistingCommandRange)
                {
                    // During replay: only emit if the recorded stream expects a comment here.
                    var expected = _recorder._acl.UnderlyingCommands[_recorder.NextCommandIndex];

                    if (expected.CommandType == ArrayCommandType.Comment)
                    {
                        int expectedId = expected.SourceIndex;

                        // Ensure local table can resolve the recorded id for diagnostics.
                        _recorder.EnsureCommentSlotWithText(expectedId, text);

                        // Emit the recorded comment id verbatim (validation happens in the emitter).
                        _recorder._emitter.Emit(ArrayCommandType.Comment, -1, expectedId, alignVsFromRecordedIndex: false);
                    }

                    // If the recorded stream does not expect a comment here, do nothing.
                    return;
                }

                // Recording: assign a new comment id and emit.
                int id = _recorder.CommentTable.Count;
                _recorder.CommentTable.Add(text);
                _recorder._emitter.Emit(ArrayCommandType.Comment, -1, id, alignVsFromRecordedIndex: false);
            }
        }

        /// <summary>
        /// Scope for temporary virtual-stack allocations. When reclamation is enabled,
        /// disposal restores the virtual stack top to its saved value.
        /// </summary>
        public readonly struct ScratchScope : IDisposable
        {
            private readonly CommandRecorder _recorder;
            private readonly int _savedNext;
            private readonly bool _shouldReclaim;

            public ScratchScope(CommandRecorder recorder, bool reclaimOnDispose)
            {
                _recorder = recorder;
                _savedNext = recorder.NextArrayIndex;
                _shouldReclaim = reclaimOnDispose && recorder._acl.ReuseScratchSlots;
            }

            public void Dispose()
            {
                if (_shouldReclaim)
                    _recorder.NextArrayIndex = _savedNext;
            }
        }

        /// <summary>
        /// Opens a scope for temporary allocations. If <paramref name="reclaimOnDispose"/> is true
        /// and scratch-slot reuse is enabled, the virtual stack top is restored on dispose.
        /// </summary>
        public ScratchScope OpenScratchScope(bool reclaimOnDispose = true) => new ScratchScope(this, reclaimOnDispose);


        #endregion
    }
}
