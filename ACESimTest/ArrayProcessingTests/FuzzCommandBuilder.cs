using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESimBase.Util.ArrayProcessing;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Builds exact-length, well-nested command buffers for fuzz tests and can
    /// pretty-print them with tab indentation that reflects chunk, If/EndIf and
    /// IncrementDepth/DecrementDepth structure.
    /// </summary>
    public class FuzzCommandBuilder
    {
        private readonly Random _rnd;
        private readonly int _originalSourcesCount;

        private readonly List<int> _virtualStack = new(); // the virtualStack area, corresponding to local variables. Initially, the original sources are copied into the virtualStack area. Later, new virtualStack variables can be added, either from original sources or in other ways. The chunk executor will use the concept of the "virtual stack" to hold every virtualStack variable. But it may use local variables to hold the values of the virtual stack, either one per virtual stack slot that is used in the chunk (the basic Roslyn executor, for example) or in a more complex way (same with local variable reuse). 
        private readonly Stack<int> _virtualStackCheckpoint = new(); // keeps track of the # of virtualStack items that existed at each successive if/increment depth, so that when we exit these loops, we can remove extra virtualStack items, thus preventing us from using a variable out of scope.
        public List<int> OrderedSourceIndices = new(); // whenever an original source is copied to the virtualStack, we record its original index here. This is not necessary when just executing within a single command chunk, but is needed when setting up an ArrayCommandList.
        public int MaxVirtualStackSize { get; private set; }

        /* ── state captured at the last Build() so ToFormattedString() can work ── */
        private ArrayCommand[] _lastResult = Array.Empty<ArrayCommand>();
        private int[] _lastChunkSizes = Array.Empty<int>();

        public FuzzCommandBuilder(int seed, int originalSourcesCount)
        {
            _rnd = new Random(seed);
            _originalSourcesCount = originalSourcesCount;
        }

        /*────────────────────────────────────────────────────────────────────────*/
        public ArrayCommandList Build(int targetSize = 40,
                                    int maxDepth = 2,
                                    double pSingleChunk = .75)
        {
            var acl = new ArrayCommandList(targetSize, _originalSourcesCount);
            _virtualStack.Clear();
            MaxVirtualStackSize = 0;

            int chunkCount = targetSize < 10 || _rnd.NextDouble() < pSingleChunk ? 1 : 2;
            _lastChunkSizes = Split(targetSize, chunkCount);

            foreach (int sz in _lastChunkSizes)
            {
                acl.StartCommandChunk(false, null);

                CopyOriginalToNew(acl);                // seed virtualStack
                EmitChunkBody(acl, sz - 1, maxDepth);  // remaining slots

                acl.EndCommandChunk();
            }
            acl.OrderedSourceIndices = OrderedSourceIndices.ToList();

            _lastResult = acl.UnderlyingCommands;      // remember for ToString()
            return acl;
        }

        /*──────────────────────────── PRETTY-PRINTER ───────────────────────────*/
        public string ToFormattedString()
        {
            if (_lastResult.Length == 0)
                return "(no Build() has been run yet)";

            // ── pre-compute chunk boundaries ─────────────────────────────
            var starts = new HashSet<int>();
            var ends = new HashSet<int>();
            int cum = 0;
            for (int c = 0; c < _lastChunkSizes.Length; c++)
            {
                starts.Add(cum);                       // first cmd of chunk
                ends.Add(cum + _lastChunkSizes[c] - 1); // last cmd of chunk
                cum += _lastChunkSizes[c];
            }

            var sb = new StringBuilder();
            int indent = 0;
            int chunkNo = 0;

            void WriteLine(string txt) => sb.Append('\t', indent).AppendLine(txt);

            for (int i = 0; i < _lastResult.Length; i++)
            {
                // ---- banner BEFORE command ----
                if (starts.Contains(i))
                {
                    WriteLine($"--- Start Chunk {chunkNo} ---");
                    indent++;                  // inner level for commands
                }

                // ---- pre-indent for closing tokens (EndIf / DecDepth) ----
                if (_lastResult[i].CommandType == ArrayCommandType.EndIf ||
                    _lastResult[i].CommandType == ArrayCommandType.DecrementDepth)
                    indent = Math.Max(0, indent - 1);

                // ---- emit the command ----
                WriteLine($"{i}: {_lastResult[i]}");

                // ---- post-indent for opening tokens (If / IncDepth) ----
                if (_lastResult[i].CommandType == ArrayCommandType.If ||
                    _lastResult[i].CommandType == ArrayCommandType.IncrementDepth)
                    indent++;

                // ---- banner AFTER command ----
                if (ends.Contains(i))
                {
                    indent--;                  // close chunk indentation
                    WriteLine($"--- End Chunk {chunkNo} ---");
                    chunkNo++;
                }
            }
            return sb.ToString();
        }




        /*──────────────────── internal: emitters & plan helpers ─────────────────*/

        private enum D { IfCond, If, EndIf, Inc, Dec }

        private void EmitChunkBody(ArrayCommandList acl, int len, int maxDepth)
        {
            if (len <= 0) return;

            var plan = new D?[len];
            BuildPlan(plan, 0, len, 0, maxDepth);

            var stack = new Stack<D>();

            for (int i = 0; i < len; i++)
            {
                switch (plan[i])
                {
                    case D.IfCond:
                        EmitComparison(acl);
                        break;

                    case D.If:
                        acl.InsertIfCommand();
                        _virtualStackCheckpoint.Push(_virtualStack.Count);
                        stack.Push(D.If);
                        break;

                    case D.EndIf:
                        if (stack.Count == 0 || stack.Pop() != D.If)
                            throw new InvalidOperationException($"Mismatched EndIf at plan index {i}.");
                        acl.InsertEndIfCommand();
                        TruncateVirtualStack();
                        break;

                    case D.Inc:
                        acl.IncrementDepth();
                        _virtualStackCheckpoint.Push(_virtualStack.Count);
                        stack.Push(D.Inc);
                        break;

                    case D.Dec:
                        if (stack.Count == 0 || stack.Pop() != D.Inc)
                            throw new InvalidOperationException($"Mismatched DecrementDepth at plan index {i}.");
                        acl.DecrementDepth();
                        TruncateVirtualStack();
                        break;

                    default:
                        // how many consecutive empty slots remain (including i)
                        int avail = 1;
                        while (avail < 4 && i + avail < len && plan[i + avail] is null) avail++;

                        int used = EmitPrimitive(acl, avail);   // may be 1-3
                        i += used - 1;                          // account for extra slots
                        break;
                }
            }

            if (acl.UnderlyingCommands.Any(x => x.CommandType == ArrayCommandType.CopyTo && x.Index >= MaxVirtualStackSize))
                throw new Exception("DEBUG");

            if (stack.Count != 0)
                throw new InvalidOperationException($"Planner produced unbalanced delimiters; {stack.Count} still open.");
        }



        private void BuildPlan(D?[] buf, int s, int len, int depth, int maxDepth)
        {
            if (len < 2 || depth >= maxDepth) return;

            // ----- try to place an If-block(needs ≥ 4 total slots)---- -
            bool placeIf = len >= 4 && _rnd.NextDouble() < 0.6;
            if (placeIf)
            {
                // choose any pre-span so at least 1 slot remains for body
                int preMax = len - 4;               // Guard,If,EndIf,body(1)
                int pre = _rnd.Next(0, preMax + 1);

                int remAfterPre = len - pre - 3;      // remaining for body+post
                                                      // ensure the body never swallows the whole remainder
                int body = remAfterPre > 1
                         ? _rnd.Next(1, remAfterPre)   // upper bound *exclusive*
                         : remAfterPre;                // only one slot left → body gets it
                int post = remAfterPre - body;

                int cond = s + pre;
                int ifPos = cond + 1;
                int end = ifPos + body + 1;

                buf[cond] = D.IfCond;
                buf[ifPos] = D.If;
                buf[end] = D.EndIf;

                BuildPlan(buf, s, pre, depth, maxDepth);
                BuildPlan(buf, ifPos + 1, body, depth + 1, maxDepth);
                BuildPlan(buf, end + 1, post, depth, maxDepth);
                return;
            }

            // ----- try to place an IncrementDepth-block (needs ≥ 2 slots) -----
            bool placeDepth = len >= 2 && _rnd.NextDouble() < 0;
            if (placeDepth)
            {
                int preMax = len - 2;           // Inc,Dec
                int pre = _rnd.Next(0, preMax + 1);

                int remAfterPre = len - pre - 2;     // body+post
                int body = _rnd.Next(0, remAfterPre + 1); // body may be 0
                int post = remAfterPre - body;

                int inc = s + pre;
                int dec = inc + body + 1;

                buf[inc] = D.Inc;
                buf[dec] = D.Dec;

                BuildPlan(buf, s, pre, depth, maxDepth);
                BuildPlan(buf, inc + 1, body, depth + 1, maxDepth);
                BuildPlan(buf, dec + 1, post, depth, maxDepth);
            }

        }

        private static int[] Split(int total, int parts)
        {
            if (parts == 1) return new[] { total };
            int first = new Random().Next(1, total);
            return new[] { first, total - first };
        }

        // ───────────────── emitters ─────────────────

        private void EmitComparison(ArrayCommandList acl)
        {
            int idx = RandomVirtualStackSlot();

            if (_rnd.NextDouble() < .5)
            {
                int v = _rnd.Next(0, 5);
                if (_rnd.NextDouble() < .5)
                    acl.InsertEqualsValueCommand(idx, v);
                else
                    acl.InsertNotEqualsValueCommand(idx, v);
            }
            else if (_virtualStack.Count >= 2)
            {
                int a = RandomVirtualStackSlot();
                int b = RandomVirtualStackSlot();
                switch (_rnd.Next(4))
                {
                    case 0: acl.InsertEqualsOtherArrayIndexCommand(a, b); break;
                    case 1: acl.InsertNotEqualsOtherArrayIndexCommand(a, b); break;
                    case 2: acl.InsertGreaterThanOtherArrayIndexCommand(a, b); break;
                    default: acl.InsertLessThanOtherArrayIndexCommand(a, b); break;
                }
            }
            else
            {
                acl.InsertEqualsValueCommand(idx, 0);
            }
        }

        private int EmitPrimitive(ArrayCommandList acl, int slotsRemaining)
        {
            while (true)
            {
                int pick = _rnd.Next(9);
                int cost = pick switch
                {
                    1 => 2,           // MultiplyToNew ⇒ CopyToNew + MultiplyBy 
                    5 => 3,           // IncrementByProduct ⇒ CopyToNew + MultiplyBy + Increment 
                    _ => 1            // all others emit a single command
                };

                if (cost > slotsRemaining || (pick == 7 && _virtualStack.Count < 2)) continue;   // pick again until it fits

                switch (pick)
                {
                    case 0: CopyVirtualStackToNew(acl); break;
                    case 1: MultiplyToNew(acl); break;
                    case 2: IncrementVirtualStack(acl); break;
                    case 3: DecrementVirtualStack(acl); break;
                    case 4: InplaceMath(acl); break;
                    case 5: IncrementByProduct(acl); break;
                    case 6: ZeroVirtualStack(acl); break;
                    default: IncrementOriginal(acl); break; // we make this more likely because we only find an error if there is a change to the originalsa
                }
                return cost;
            }
        }

        // ───────────────── virtualStack helpers ─────────────────

        void TruncateVirtualStack()
        {
            int keep = _virtualStackCheckpoint.Pop();
            if (_virtualStack.Count > keep)
                _virtualStack.RemoveRange(keep, _virtualStack.Count - keep);
        }

        private void VirtualStackAdd(int i)
        {
            _virtualStack.Add(i);
            if (i + 1 > MaxVirtualStackSize)
                MaxVirtualStackSize = i + 1;
        }

        private int RandomVirtualStackSlot() => _virtualStack[_rnd.Next(_virtualStack.Count)];

        private void CopyOriginalToNew(ArrayCommandList acl)
        {
            int src = _rnd.Next(_originalSourcesCount);
            VirtualStackAdd(acl.CopyToNew(src, true));
            OrderedSourceIndices.Add(src);
        }

        private void CopyVirtualStackToNew(ArrayCommandList acl)
        {
            int s = RandomVirtualStackSlot();
            VirtualStackAdd(acl.CopyToNew(s, false));
        }

        private void MultiplyToNew(ArrayCommandList acl)
        {
            int s = RandomVirtualStackSlot();
            VirtualStackAdd(acl.MultiplyToNew(s, false, s));
        }

        private void IncrementVirtualStack(ArrayCommandList acl)
        {
            int t = RandomVirtualStackSlot();
            int v = RandomVirtualStackSlot();
            acl.Increment(t, false, v);
        }

        private void DecrementVirtualStack(ArrayCommandList acl)
        {
            int t = RandomVirtualStackSlot();
            int v = RandomVirtualStackSlot();
            acl.Decrement(t, v);
        }

        private void InplaceMath(ArrayCommandList acl)
        {
            int x = RandomVirtualStackSlot();
            switch (_rnd.Next(3))
            {
                case 0: acl.Increment(x, false, x); break;
                case 1: acl.Decrement(x, x); break;
                default: acl.MultiplyBy(x, x); break;
            }
        }

        private void IncrementOriginal(ArrayCommandList acl)
        {
            int val = RandomVirtualStackSlot();
            int dst = _rnd.Next(_originalSourcesCount);
            acl.Increment(dst, true, val);
        }

        private void ZeroVirtualStack(ArrayCommandList acl)
        {
            int idx = RandomVirtualStackSlot();
            acl.ZeroExisting(idx);
        }

        private void IncrementByProduct(ArrayCommandList acl)
        {
            int tgt = RandomVirtualStackSlot();
            int a = RandomVirtualStackSlot();
            int b = RandomVirtualStackSlot();
            int stackSlotForTemporaryVariable = acl.IncrementByProduct(tgt, false, a, b);
            if (stackSlotForTemporaryVariable + 1 > MaxVirtualStackSize)
                MaxVirtualStackSize = stackSlotForTemporaryVariable + 1;
        }
    }
}
