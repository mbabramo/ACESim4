using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Builds a random ArrayCommand[] using your EmitChunk/EmitRandomCommand logic.
    /// Adds a tiny post‑pass that removes immediately‑repeated Zero‑to‑same‑slot
    /// commands so the generated code no longer ends with hundreds of
    /// `l0 = 0;` lines.
    /// </summary>
    public class FuzzCommandBuilder
    {
        private readonly Random _rnd;
        private readonly List<int> _scratch = new();
        private readonly ArrayCommandList _acl;
        private readonly int _origCount;

        public FuzzCommandBuilder(int seed, int origCount)
        {
            _rnd = new Random(seed);
            _origCount = origCount;
            _acl = new ArrayCommandList(maxNumCommands: 1000, initialArrayIndex: origCount, parallelize: false);
        }

        /// <summary>
        /// Build a single chunk, then (if maxCommands!=null) truncate to at most that
        /// many commands, rebalance any unmatched If/EndIf, collapse redundant
        /// Zero‑Existing streaks, and return the resulting ArrayCommand[].
        /// </summary>
        public ArrayCommand[] Build(int maxDepth = 2, int maxBody = 8, int? maxCommands = null)
        {
            // 1) generate the full chunk
            _scratch.Clear();
            _acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: null);
            int localDepth = 0;
            EmitChunk(0, maxDepth, maxBody, ref localDepth);
            while (localDepth-- > 0) _acl.DecrementDepth();
            _acl.EndCommandChunk();

            var full = _acl.UnderlyingCommands;

            // 2) optionally truncate
            if (maxCommands.HasValue && full.Length > maxCommands.Value)
            {
                full = full.Take(maxCommands.Value).ToArray();

                // rebalance If/EndIf so the chunk remains well‑formed
                int opens = full.Count(c => c.CommandType == ArrayCommandType.If) -
                            full.Count(c => c.CommandType == ArrayCommandType.EndIf);
                if (opens > 0)
                {
                    Array.Resize(ref full, full.Length + opens);
                    for (int i = full.Length - opens; i < full.Length; i++)
                        full[i] = new ArrayCommand(ArrayCommandType.EndIf, -1, -1);
                }
            }

            // 3) remove redundant consecutive Zero‑Existing commands
            var cleaned = new List<ArrayCommand>(full.Length);
            foreach (var cmd in full)
            {
                if (cmd.CommandType == ArrayCommandType.Zero &&
                    cleaned.Count > 0 &&
                    cleaned[^1].CommandType == ArrayCommandType.Zero &&
                    cleaned[^1].Index == cmd.Index)
                {
                    // drop duplicate
                    continue;
                }
                cleaned.Add(cmd);
            }

            return cleaned.ToArray();
        }

        // ---------------- existing generation helpers below ----------------

        private void EmitChunk(int depth, int maxDepth, int maxBody, ref int localDepth)
        {
            // 30% chance: read from a scratch slot before any write
            if (_scratch.Count > 0 && _rnd.NextDouble() < 0.30)
            {
                int p = _scratch[_rnd.Next(_scratch.Count)];
                _acl.CopyToNew(p, fromOriginalSources: false);
            }

            var copyUp = new List<int>();
            EmitLinear(_rnd.Next(4, maxBody + 1), copyUp, ref localDepth);

            // optional nested child
            if (depth < maxDepth)
            {
                int guard = EnsureScratch();
                _acl.InsertNotEqualsValueCommand(guard, 0);
                _acl.InsertIfCommand();
                EmitChunk(depth + 1, maxDepth, maxBody, ref localDepth);
                _acl.InsertEndIfCommand();
            }

            // close depths introduced inside this chunk
            while (localDepth-- > 0)
                _acl.DecrementDepth();

            // end this chunk, maybe bubbling up scratch slots
            if (copyUp.Count == 0 || _rnd.NextDouble() < 0.50)
                _acl.EndCommandChunk();
            else
                _acl.EndCommandChunk(copyUp.ToArray(), endingRepeatedChunk: false);
        }

        private void EmitLinear(int count, List<int> copyUp, ref int localDepth)
        {
            for (int i = 0; i < count; i++)
                EmitRandomCommand(copyUp, ref localDepth);
        }

        private void EmitRandomCommand(List<int> copyUp, ref int localDepth)
        {
            MaybeToggleDepth(ref localDepth, copyUp);
            if (_rnd.NextDouble() < 0.20) return;

            if (_scratch.Count > 0 && _rnd.NextDouble() < 0.15)
            {
                int tgt = _scratch[_rnd.Next(_scratch.Count)];
                int src = _scratch[_rnd.Next(_scratch.Count)];
                _acl.Increment(tgt, targetOriginal: false, indexOfIncrement: src);
                return;
            }

            switch (_rnd.Next(0, 13))
            {
                case 0: CopyOriginalToNew(); break;
                case 1: CopyScratchToNew(); break;
                case 2: MultiplyToNew(); break;
                case 3: IncrementScratch(); break;
                case 4: DecrementScratch(); break;
                case 5: InplaceMath(); break;
                case 6: IncrementOriginal(); break;
                case 7: ZeroScratch(); break;
                case 8: ComparisonValue(); break;
                case 9: ComparisonIndices(); break;
                case 10: NextSourceDrain(); break;
                case 11: ReadParentScratch(); break;
                default: IncrementByProduct(); break;
            }
        }

        private void MaybeToggleDepth(ref int localDepth, List<int> copyUp)
        {
            if (_rnd.NextDouble() >= 0.20) return;
            if (localDepth == 0 || _rnd.NextDouble() < 0.50)
            {
                _acl.IncrementDepth();
                localDepth++;
                int s = EnsureScratch();
                copyUp.Add(s);
            }
            else
            {
                _acl.DecrementDepth();
                localDepth--;
            }
        }

        private int EnsureScratch()
        {
            if (_scratch.Count == 0)
                CopyOriginalToNew();
            return _scratch[_rnd.Next(_scratch.Count)];
        }

        private void CopyOriginalToNew()
        {
            int src = _rnd.Next(_origCount);
            int idx = _acl.CopyToNew(src, fromOriginalSources: true);
            _scratch.Add(idx);
        }

        private void CopyScratchToNew()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int s = _scratch[_rnd.Next(_scratch.Count)];
            int idx = _acl.CopyToNew(s, fromOriginalSources: false);
            _scratch.Add(idx);
        }

        private void MultiplyToNew()
        {
            int s = EnsureScratch();
            int idx = _acl.MultiplyToNew(s, fromOriginalSources: false, idx2: s);
            _scratch.Add(idx);
        }

        private void IncrementScratch()
        {
            if (_scratch.Count < 1) { CopyOriginalToNew(); return; }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int inc = _scratch[_rnd.Next(_scratch.Count)];
            _acl.Increment(tgt, false, inc);
        }

        private void DecrementScratch()
        {
            if (_scratch.Count < 1) { CopyOriginalToNew(); return; }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int dec = _scratch[_rnd.Next(_scratch.Count)];
            _acl.Decrement(tgt, dec);
        }

        private void InplaceMath()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int idx = _scratch[_rnd.Next(_scratch.Count)];
            switch (_rnd.Next(3))
            {
                case 0: _acl.Increment(idx, false, idx); break;
                case 1: _acl.Decrement(idx, idx); break;
                default: _acl.MultiplyBy(idx, idx); break;
            }
        }

        private void IncrementOriginal()
        {
            int inc = EnsureScratch();
            int dest = _rnd.Next(_origCount);
            _acl.Increment(dest, targetOriginal: true, indexOfIncrement: inc);
        }

        private void ZeroScratch()
        {
            int idx = EnsureScratch();

            // guard: if the previous *emitted* command already zeroed this same
            // slot, skip emitting another redundant zero
            var cmds = _acl.UnderlyingCommands;
            if (cmds.Length > 0)
            {
                var prev = cmds[^1];
                if (prev.CommandType == ArrayCommandType.Zero && prev.Index == idx)
                    return; // redundant
            }

            _acl.ZeroExisting(idx);
        }

        private void ComparisonValue()
        {
            int idx = EnsureScratch();
            int val = _rnd.Next(0, 5);
            if (_rnd.NextDouble() < 0.5)
                _acl.InsertEqualsValueCommand(idx, val);
            else
                _acl.InsertNotEqualsValueCommand(idx, val);
        }

        private void ComparisonIndices()
        {
            if (_scratch.Count < 1) { CopyScratchToNew(); return; }
            if (_rnd.NextDouble() < 0.5)
            {
                int a = _scratch[_rnd.Next(_scratch.Count)];
                _acl.InsertEqualsOtherArrayIndexCommand(a, a);
            }
            else
            {
                if (_scratch.Count < 2) { CopyScratchToNew(); return; }
                int i1 = _scratch[_rnd.Next(_scratch.Count)];
                int i2 = _scratch[_rnd.Next(_scratch.Count)];
                switch (_rnd.Next(4))
                {
                    case 0: _acl.InsertEqualsOtherArrayIndexCommand(i1, i2); break;
                    case 1: _acl.InsertNotEqualsOtherArrayIndexCommand(i1, i2); break;
                    case 2: _acl.InsertGreaterThanOtherArrayIndexCommand(i1, i2); break;
                    default: _acl.InsertLessThanOtherArrayIndexCommand(i1, i2); break;
                }
            }
        }

        private void NextSourceDrain()
        {
            int src = _rnd.Next(_origCount);
            _acl.CopyToNew(src, fromOriginalSources: true);
        }

        private void ReadParentScratch()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int s = _scratch[_rnd.Next(_scratch.Count)];
            _acl.CopyToNew(s, fromOriginalSources: false);
        }

        private void IncrementByProduct()
        {
            if (_scratch.Count < 2) { CopyScratchToNew(); return; }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int a = _scratch[_rnd.Next(_scratch.Count)];
            int b = _scratch[_rnd.Next(_scratch.Count)];
            _acl.IncrementByProduct(tgt, false, a, b);
        }
    }
}
