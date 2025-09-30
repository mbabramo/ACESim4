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

        // Logging
        private const int _recentNewCapacity = 64;
        private (int attemptedCmdIndex, ArrayCommand cmd)[] _recentNewRing = new (int, ArrayCommand)[_recentNewCapacity];
        private int _recentNewHead = 0;
        private int _recentNewCount = 0;

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

            // Recorder / ACL state
            sb.AppendLine("Recorder/ACL state:");
            sb.AppendLine($"  UseOrderedSourcesAndDestinations: {_acl.UseOrderedSourcesAndDestinations}");
            sb.AppendLine($"  RepeatIdenticalRanges           : {_acl.RepeatIdenticalRanges}");
            sb.AppendLine($"  RepeatingExistingCommandRange   : {_acl.RepeatingExistingCommandRange}");
            sb.AppendLine($"  ReuseScratchSlots               : {_acl.ReuseScratchSlots}");
            sb.AppendLine($"  NextArrayIndex / MaxArrayIndex  : {NextArrayIndex} / {MaxArrayIndex}");
            sb.AppendLine($"  Depth frames                    : {_depthStartSlots.Count}");
            sb.AppendLine();

            // Envelope: find nearest [REPEAT-BEGIN] / [IDENTICAL-BEGIN] before, and matching END after
            int envStart = -1, envEnd = -1;
            string envStartText = null, envEndText = null;

            for (int i = NextCommandIndex - 1; i >= 0 && i >= NextCommandIndex - 4000; i--)
            {
                var c = _acl.UnderlyingCommands[i];
                if (c.CommandType != ArrayCommandType.Comment) continue;
                string txt = GetCommentTextOrNull(c.SourceIndex);
                if (txt != null && (txt.StartsWith("[REPEAT-BEGIN]") || txt.StartsWith("[IDENTICAL-BEGIN]")))
                {
                    envStart = i;
                    envStartText = txt;
                    break;
                }
            }
            if (envStart >= 0)
            {
                for (int i = NextCommandIndex; i < System.Math.Min(_acl.UnderlyingCommands.Length, NextCommandIndex + 8000); i++)
                {
                    var c = _acl.UnderlyingCommands[i];
                    if (c.CommandType != ArrayCommandType.Comment) continue;
                    string txt = GetCommentTextOrNull(c.SourceIndex);
                    if (txt != null && (txt.StartsWith("[REPEAT-END") || txt.StartsWith("[IDENTICAL-END")))
                    {
                        envEnd = i;
                        envEndText = txt;
                        break;
                    }
                }
                sb.AppendLine("Repeat/identical envelope:");
                sb.AppendLine($"  start @{envStart}: {envStartText ?? "<unavailable>"}");
                sb.AppendLine($"  end   @{(envEnd >= 0 ? envEnd : -1)}: {(envEndText ?? "<unavailable>")}");
                sb.AppendLine();
            }

            // Nearest prior comment (still useful if no envelope marks)
            int priorCommentIdx = -1;
            for (int i = NextCommandIndex - 1; i >= 0 && i >= NextCommandIndex - 400; i--)
            {
                var c = _acl.UnderlyingCommands[i];
                if (c.CommandType == ArrayCommandType.Comment) { priorCommentIdx = i; break; }
            }
            if (priorCommentIdx >= 0)
            {
                var c = _acl.UnderlyingCommands[priorCommentIdx];
                string note = GetCommentTextOrNull(c.SourceIndex) ?? "<unavailable>";
                sb.AppendLine("Nearest prior comment:");
                sb.AppendLine($"  @{priorCommentIdx}: {note}");
                sb.AppendLine();
            }

            // Control-flow & pointer stats between envelope-start (or a nearby window) and the mismatch
            int scanStart = envStart >= 0 ? envStart + 1 : System.Math.Max(0, NextCommandIndex - 200);
            int ifDepth = 0, lastIfIdx = -1, recNextSrc = 0, recNextDst = 0;

            for (int i = scanStart; i < NextCommandIndex; i++)
            {
                var t = _acl.UnderlyingCommands[i].CommandType;
                switch (t)
                {
                    case ArrayCommandType.If:       ifDepth++; lastIfIdx = i; break;
                    case ArrayCommandType.EndIf:    ifDepth = System.Math.Max(0, ifDepth - 1); break;
                    case ArrayCommandType.NextSource:      recNextSrc++; break;
                    case ArrayCommandType.NextDestination: recNextDst++; break;
                }
            }

            sb.AppendLine("Recorded within window (to mismatch):");
            sb.AppendLine($"  IF-depth at mismatch: {ifDepth} (last IF @{(lastIfIdx >= 0 ? lastIfIdx : -1)})");
            sb.AppendLine($"  Recorded NextSource: {recNextSrc}, NextDestination: {recNextDst}");

            if (lastIfIdx >= 0)
            {
                // Local helper: approximate pointer skips for the *innermost* IF starting at lastIfIdx
                (int src, int dst) CountPointerSkips(int ifIndex, int endExclusive)
                {
                    int src = 0, dst = 0, depth = 1;
                    for (int i = ifIndex + 1; i < endExclusive; i++)
                    {
                        var t = _acl.UnderlyingCommands[i].CommandType;
                        if (t == ArrayCommandType.If) { depth++; continue; }
                        if (t == ArrayCommandType.EndIf)
                        {
                            depth--;
                            if (depth == 0) break;
                            continue;
                        }
                        if (depth == 1)
                        {
                            if (t == ArrayCommandType.NextSource) src++;
                            else if (t == ArrayCommandType.NextDestination) dst++;
                        }
                    }
                    return (src, dst);
                }

                var ps = CountPointerSkips(lastIfIdx, System.Math.Min(_acl.UnderlyingCommands.Length, NextCommandIndex + 4000));
                sb.AppendLine($"  Pointer-skips from last IF to its END (recorded): src+={ps.src}, dst+={ps.dst}");
            }
            sb.AppendLine();

            // Recorded commands around mismatch (existing view)
            int windowBefore = 16, windowAfter = 8;
            int start = System.Math.Max(0, NextCommandIndex - windowBefore);
            int end   = System.Math.Min(_acl.UnderlyingCommands.Length - 1, NextCommandIndex + windowAfter);

            sb.AppendLine("Recorded commands near mismatch:");
            for (int i = start; i <= end; i++)
            {
                var c = _acl.UnderlyingCommands[i];
                if (i > NextCommandIndex && c.CommandType == ArrayCommandType.Blank && c.Index == -1 && c.SourceIndex == -1)
                    break;
                string row = $"{i:D6}: {RenderCommand(c)}";
                if (c.CommandType == ArrayCommandType.Comment)
                {
                    string txt = GetCommentTextOrNull(c.SourceIndex) ?? "<unavailable>";
                    row += $"    // {txt}";
                }
                if (i == NextCommandIndex) row += "    << mismatch here";
                sb.AppendLine(row);
            }
            sb.AppendLine();

            // Ordered OS/OD tails (already helpful)
            static string Tail(System.Collections.Generic.IReadOnlyList<int> xs, int k = 12)
                => xs == null || xs.Count == 0 ? "(empty)" : string.Join(", ", xs.Skip(System.Math.Max(0, xs.Count - k)));
            sb.AppendLine("Ordered indices tails:");
            sb.AppendLine($"  OrderedSourceIndices      : {Tail(_acl.OrderedSourceIndices)}");
            sb.AppendLine($"  OrderedDestinationIndices : {Tail(_acl.OrderedDestinationIndices)}");
            sb.AppendLine();

            // Recent "attempted" commands
            if (_recentNewCount > 0)
            {
                sb.AppendLine("Recent new commands (attempted just before mismatch):");
                int toShow = System.Math.Min(_recentNewCount, 16);
                for (int i = 0; i < toShow; i++)
                {
                    int idx = (_recentNewHead - 1 - i + _recentNewCapacity) % _recentNewCapacity;
                    var rec = _recentNewRing[idx];
                    sb.AppendLine($"  @{rec.attemptedCmdIndex:D6}: {RenderCommand(rec.cmd)}");
                }
                sb.AppendLine();
            }

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
                bool expectNextSource = expected.CommandType == ArrayCommandType.NextSource;
                bool expectCopyTo     = expected.CommandType == ArrayCommandType.CopyTo;

                if (!expectNextSource && !expectCopyTo)
                    throw new InvalidOperationException(
                        $"Repeat-range mismatch at cmd {NextCommandIndex}: recorded {expected.CommandType} vs new {(fromOriginalSources && _acl.UseOrderedSourcesAndDestinations ? "NextSource" : "CopyTo")}.");

                int target = expected.Index;
                if (target + 1 > NextArrayIndex)
                    NextArrayIndex = target + 1;

                // During replay, emit the exact recorded command to stay byte-identical.
                if (expectNextSource)
                {
                    // Maintain ordered-sources side-effect shape for the replayed NextSource.
                    _acl.OrderedSourceIndices.Add(sourceIdx);
                    AddCommand(expected);
                }
                else
                {
                    AddCommand(expected);
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


        public void CopyToExisting(int index, int sourceIndex)
        {
            InsertComment($"[COPY/EXIST] idx={index} src={sourceIndex} replay={_acl.RepeatingExistingCommandRange}");

            bool isCheckpoint = index == ArrayCommandList.CheckpointTrigger;

            if (_acl.RepeatingExistingCommandRange)
            {
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                var expectedKind = isCheckpoint ? ArrayCommandType.Checkpoint : ArrayCommandType.CopyTo;

                if (expected.CommandType != expectedKind)
                {
                    // Keep strict mismatch for opcode/shape differences
                    ThrowRepeatMismatch(new ArrayCommand(expectedKind, index, sourceIndex), expected);
                }

                // Reproduce the recorded command EXACTLY during replay (target + source).
                int recordedTarget = expected.Index;
                if (recordedTarget + 1 > NextArrayIndex)
                    NextArrayIndex = recordedTarget + 1;

                AddCommand(expected); // identical to recorded; advances NextCommandIndex
                return;
            }

            if (isCheckpoint)
                AddCommand(new ArrayCommand(ArrayCommandType.Checkpoint, ArrayCommandList.CheckpointTrigger, sourceIndex));
            else
                AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }



        public int[] CopyToNew(int[] sourceIndices, bool fromOriginalSources) =>
            sourceIndices.Select(idx => CopyToNew(idx, fromOriginalSources)).ToArray();




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
        public void ZeroExisting(int index)
        {
            if (_acl.RepeatingExistingCommandRange)
            {
                // Mirror CopyToExisting replay behavior: align to recorded target.
                var expected = _acl.UnderlyingCommands[NextCommandIndex];
                if (expected.CommandType != ArrayCommandType.Zero)
                {
                    ThrowRepeatMismatch(new ArrayCommand(ArrayCommandType.Zero, index, -1), expected);
                }

                int recordedTarget = expected.Index;
                AddCommand(new ArrayCommand(ArrayCommandType.Zero, recordedTarget, -1));
                return;
            }

            // Normal recording path
            AddCommand(new ArrayCommand(ArrayCommandType.Zero, index, -1));
        }


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
