
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.NWayTreeStorage;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimTest.ArrayProcessingTests
{
    /*───────────────────────────────────────────────────────────────────────────
     * Robust fuzz test exercising interpreter vs. compiled (IL & Roslyn) with
     * production defaults (OrderedSources/Destinations ON, ReuseDestinations OFF).
     *──────────────────────────────────────────────────────────────────────────*/

    [TestClass]
    [TestCategory("Fuzz")]
    public class ArrayCommandListFuzzTests
    {
        private static readonly (int Iter, int MaxDepth, int MaxBody)[] Stages =
        {
            (150, 1,  8),    // toy
            (150, 2, 18),    // small
            (100, 3, 40),    // medium
            ( 40, 4, 75),    // large
            ( 10, 5,120)     // stress
        };

        private static bool RunFuzzTest => true; // DEBUG

        [TestMethod]
        public void InterpreterVsCompiled_AllThresholds()
        {
            if (!RunFuzzTest) return;
            //TabbedText.WriteToConsole = false; // DEBUG
            //TabbedText.DisableOutput();// DEBUG

            for (int stage = 0; stage < Stages.Length; stage++)
            {
                var (iters, maxDepth, maxBody) = Stages[stage];
                int seedBase = (int)(0xACE0_0000 + stage * 10_000);

                for (int iter = 0; iter < iters; iter++)
                {
                    int seed = seedBase + iter;
                    var rnd = new Random(seed);

                    // 1) Build the *un‑thresholded* ACL (MaxCommandsPerChunk=1_000_000)
                    var fuzzer = new CommandFuzzer(rnd);
                    fuzzer.BuildRandomAcl(maxDepth, maxBody);

                    // 2) Compute the reference result *once* with the flat interpreter
                    var interp = fuzzer.CloneFor(threshold: int.MaxValue);
                    interp.DisableAdvancedFeatures = true;
                    double[] expected = fuzzer.MakeInitialData();
                    interp.ExecuteAll(expected, false, ChunkExecutorKind.Interpreted, int.MaxValue);

                    // 3) Pick random thresholds to exercise the compiled path
                    int totalCmds = fuzzer.Acl.NextCommandIndex;
                    const int numThresholds = 3;
                    var thresholds = new HashSet<int>();
                    while (thresholds.Count < Math.Min(numThresholds, totalCmds + 1))
                        thresholds.Add(rnd.Next(1, totalCmds + 2));
                    thresholds.Add(1); // always test threshold=1

                    // 4) For each threshold, test Roslyn & IL vs. interpreter
                    foreach (int th in thresholds)
                    {
                        TabbedText.WriteLine($"Stage {stage} of {Stages.Length}; Iteration {iter} of {iters}; Threshold {th}");
                        TabbedText.WriteLine($"Stage {stage} of {Stages.Length}; Iteration {iter} of {iters}; Threshold {th}");
                        TabbedText.WriteLine(new string('-', 30));

                        foreach (ChunkExecutorKind kind in new ChunkExecutorKind[] { ChunkExecutorKind.Roslyn }) // DEBUG SUPERDEBUG, ChunkExecutorKind.IL, ChunkExecutorKind.RoslynWithLocalVariableRecycling, ChunkExecutorKind.ILWithLocalVariableRecycling })
                        {
                            var acl = fuzzer.CloneFor(th);

                            double[] actual = fuzzer.MakeInitialData();
                            acl.ExecuteAll(actual, false, kind, null);

                            int outputCount = fuzzer.OutputSize;
                            for (int idx = 0; idx < outputCount; idx++)
                            {
                                double tol = Math.Max(1e-9, Math.Abs(expected[idx]) / 1000.0);
                                if (Math.Abs(expected[idx] - actual[idx]) > tol)
                                {
                                    Dump(seed, stage, th, idx, expected[idx], actual[idx]);
                                    Assert.Fail("Compiled result mismatch vs. interpreter baseline");
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void Dump(int seed, int stage, int th, int idx, double exp, double act)
        {
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine($"FAIL seed={seed} stage={stage} th={th} idx={idx} exp={exp} act={act}");
#endif
        }
    }

    /*───────────────────────────────────────────────────────────────────────────
     * CommandFuzzer – emits legal random programs under production defaults
     *──────────────────────────────────────────────────────────────────────────*/
    internal sealed class CommandFuzzer
    {
        private readonly Random _rnd;
        private const int SrcStart = 0;   // originals 0‑9
        private const int DstStart = 10;  // originals 10‑19
        private const int OrigCt = 10;
        private const int ScratchSize = 400;

        public int OutputSize => DstStart + OrigCt;

        private readonly List<int> _scratch = new();
        public ArrayCommandList Acl { get; private set; }

        public CommandFuzzer(Random rnd) => _rnd = rnd;

        /*----------------- public helpers -----------------*/
        public void BuildRandomAcl(int maxDepth, int maxBody)
        {
            int maxAlloc = 60_000;
            int minCompile = _rnd.Next(0, 3);

            Acl = new ArrayCommandList(maxAlloc, DstStart + OrigCt, parallelize: false)
            {
                DisableAdvancedFeatures = false,
                MaxCommandsPerSplittableChunk = 1_000_000 // keeps ReuseScratchSlots = false
            };

            EmitChunk(0, maxDepth, maxBody);
            Acl.CompleteCommandList();
        }

        public ArrayCommandList CloneFor(int threshold)
        {
            var clone = new ArrayCommandList(Acl.UnderlyingCommands.Length,
                                             DstStart + OrigCt,
                                             parallelize: false)
            {
                DisableAdvancedFeatures = false,
                MaxCommandsPerSplittableChunk = threshold
            };

            /* 1️⃣  copy raw commands */
            Array.Copy(Acl.UnderlyingCommands,
                       clone.UnderlyingCommands,
                       Acl.NextCommandIndex);
            clone.NextCommandIndex = Acl.NextCommandIndex;
            clone.NextArrayIndex = Acl.NextArrayIndex;
            clone.MaxArrayIndex = Acl.MaxArrayIndex;

            /* 2️⃣  copy ordered‑index metadata */
            clone.OrderedSourceIndices = new List<int>(Acl.OrderedSourceIndices);
            clone.OrderedDestinationIndices = new List<int>(Acl.OrderedDestinationIndices);
            clone.ReusableOrderedDestinationIndices =
                new Dictionary<int, int>(Acl.ReusableOrderedDestinationIndices);

            /* 3️⃣  rebuild the tree for THIS chunk limit */
            clone.CommandTree = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            clone.CommandTree.StoredValue = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = Acl.NextCommandIndex,
                StartSourceIndices = 0,
                StartDestinationIndices = 0
            };

            clone.FinaliseCommandTree();

            return clone;
        }


        public double[] MakeInitialData()
        {
            double[] data = new double[OutputSize + ScratchSize];
            for (int i = 0; i < DstStart + OrigCt; i++) data[i] = i % 17 - 8;
            return data;
        }

        /*----------------- emit program -----------------*/
        private void EmitChunk(int depth, int maxDepth, int maxBody)
        {
            Acl.StartCommandChunk(false, null);

            /* ── 30 % chance: child reads parent‑written scratch before any write ── */
            if (_scratch.Count > 0 && _rnd.NextDouble() < 0.30)
            {
                int p = _scratch[_rnd.Next(_scratch.Count)];
                Acl.CopyToNew(p, false);   // read‑only → primes IndicesReadFromStack
            }

            int localDepth = 0;
            var copyUp = new List<int>();

            EmitLinear(_rnd.Next(4, maxBody + 1), copyUp, ref localDepth);

            /* optional nested child */
            if (depth < maxDepth)
            {
                int guard = EnsureScratch();
                Acl.InsertNotEqualsValueCommand(guard, 0);
                Acl.InsertIfCommand();
                EmitChunk(depth + 1, maxDepth, maxBody);
                Acl.InsertEndIfCommand();
            }

            /* close any open depth */
            CloseDepth(ref localDepth);

            /* 50 % chance to pass increment‑to‑parent list */
            if (copyUp.Count == 0 || _rnd.NextDouble() < 0.50)
                Acl.EndCommandChunk();
            else
                Acl.EndCommandChunk(copyUp.ToArray(), false);
        }

        private void EmitLinear(int count, List<int> copyUp, ref int localDepth)
        {
            for (int i = 0; i < count; i++)
                EmitRandomCommand(copyUp, ref localDepth, i == count - 1);
        }

        /* ------------------------------------------------------------------ */
        /* EmitRandomCommand – random op plus depth bookkeeping               */
        /*   - guarantees                                                     */
        /*     (a) comparison → immediately followed by If/EndIf              */
        /*     (b) comparisons are never the last command in a segment        */
        /*     (c) comparisons only when we can IncrementDepth                */
        /* ------------------------------------------------------------------ */
        private void EmitRandomCommand(List<int> copyUp,
                                       ref int localDepth,
                                       bool isLastCmd)        // ← NEW ARG
        {
            // ❶ first handle any mandatory If after a comparison we just emitted
            if (_lastWasComparison)
            {
                Acl.InsertIfCommand();
                Acl.IncrementDepth();               // one-line body
                int tmp = EnsureScratch();
                Acl.Increment(tmp, false, tmp);     // cheap side-effect
                Acl.DecrementDepth();
                Acl.InsertEndIfCommand();
                _lastWasComparison = false;
                return;                             // this slot consumed
            }

            // ❷ optional push/pop of bookkeeping depth
            MaybeToggleDepth(ref localDepth, copyUp);
            if (_rnd.NextDouble() < 0.20)
                return;                             // depth op took the slot

            /* ------------------------------------------------------------------
             * choose opcode
             *   range 0-12  (original 13 cases)
             *   – but exclude comparison ops      when `isLastCmd`
             *   – and exclude comparison          if we cannot push a depth level
             * ------------------------------------------------------------------ */
            bool comparisonAllowed = !isLastCmd && localDepth < _maxLegalDepth;

            int choice = _rnd.Next(comparisonAllowed ? 13 : 10); // 0-9 non-cmp, 10-12 cmp
            switch (choice)
            {
                case 0: CopyOriginalToNew(); break;
                case 1: CopyScratchToNew(); break;
                case 2: MultiplyToNew(); break;
                case 3: IncrementScratch(); break;
                case 4: DecrementScratch(); break;
                case 5: InplaceMath(); break;
                case 6: IncrementOriginal(); break;
                case 7: ZeroScratch(); break;
                case 8: NextSourceDrain(); break;
                case 9: ReadParentScratch(); break;

                /* ---- comparison ops (only if allowed) ---- */
                case 10:
                case 11:
                case 12:
                    if (!comparisonAllowed) { CopyOriginalToNew(); break; }

                    if (_rnd.NextDouble() < 0.50)
                        ComparisonValue();
                    else
                        ComparisonIndices();

                    _lastWasComparison = true;      // flag so next slot injects If/EndIf
                    break;
            }
        }

        /*--------------- supporting state ---------------*/
        private bool _lastWasComparison = false;             // add near other fields
        private readonly int _maxLegalDepth = 8;             // same as BuildRandomAcl()



        /*----------------- command emit helpers -----------------*/
        private void MaybeToggleDepth(ref int localDepth, List<int> copyUp)
        {
            if (_rnd.NextDouble() >= 0.20) return;  // 80 % do nothing

            if (localDepth == 0 || _rnd.NextDouble() < 0.50)
            {   // push
                Acl.IncrementDepth();
                localDepth++;
                int s = EnsureScratch();
                copyUp.Add(s);                      // bubble this index up
            }
            else
            {   // pop
                Acl.DecrementDepth();
                localDepth--;
            }
        }

        private void CloseDepth(ref int localDepth)
        {
            while (localDepth-- > 0) Acl.DecrementDepth();
        }

        /* parent‑value read without prior local write */
        private void ReadParentScratch()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int s = _scratch[_rnd.Next(_scratch.Count)];
            Acl.CopyToNew(s, false);   // read only
        }

        void CopyOriginalToNew()
        {
            int src = SrcStart + _rnd.Next(OrigCt);
            _scratch.Add(Acl.CopyToNew(src, true));
        }
        void CopyScratchToNew()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int src = _scratch[_rnd.Next(_scratch.Count)];
            _scratch.Add(Acl.CopyToNew(src, false));
        }
        void MultiplyToNew()
        {
            int s = EnsureScratch();
            _scratch.Add(Acl.MultiplyToNew(s, false, s));
        }
        void IncrementScratch()
        {
            if (_scratch.Count < 1) { CopyOriginalToNew(); return; }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int inc = _scratch[_rnd.Next(_scratch.Count)];
            Acl.Increment(tgt, false, inc);
        }
        void DecrementScratch()
        {
            if (_scratch.Count < 1) { CopyOriginalToNew(); return; }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int dec = _scratch[_rnd.Next(_scratch.Count)];
            Acl.Decrement(tgt, dec);
        }
        void InplaceMath()
        {
            if (_scratch.Count == 0) { CopyOriginalToNew(); return; }
            int idx = _scratch[_rnd.Next(_scratch.Count)];
            switch (_rnd.Next(3))
            {
                case 0: Acl.Increment(idx, false, idx); break;
                case 1: Acl.Decrement(idx, idx); break;
                default: Acl.MultiplyBy(idx, idx); break;
            }
        }
        void IncrementOriginal()
        {
            int inc = EnsureScratch();
            int dest = DstStart + _rnd.Next(OrigCt);
            Acl.Increment(dest, true, inc);
        }
        void ZeroScratch()
        {
            int idx = EnsureScratch();
            Acl.ZeroExisting(idx);
        }
        void ComparisonValue()
        {
            int idx = EnsureScratch();
            int val;
            do { val = _rnd.Next(0, 5); } while (val == -2);

            if (_rnd.NextDouble() < 0.5)
                Acl.InsertEqualsValueCommand(idx, val);
            else
                Acl.InsertNotEqualsValueCommand(idx, val);
        }
        /* ------------------------------------------------------------------ */
        /* ComparisonIndices – 4‑way random comparison between two stack idx  */
        /*           (now with 50 % chance to do  a == a)                     */
        /* ------------------------------------------------------------------ */
        /* --------------------------------------------------------------- */
        /*  ComparisonIndices                                              */
        /*  – 4 original cases (==, !=, >, <)                               */
        /*  – NEW: 50 % of the time emit   EqualsOtherArrayIndex(a, a)      */
        /*         so the condition is guaranteed TRUE and the If‑body      */
        /*         always executes.                                         */
        /* --------------------------------------------------------------- */
        void ComparisonIndices()
        {
            // If we don’t yet have two scratch slots, fall back to a copy
            if (_scratch.Count < 1)
            { CopyScratchToNew(); return; }

            /* 50 % chance to compare a slot with itself (a == a) */
            if (_rnd.NextDouble() < 0.50)
            {
                int a = _scratch[_rnd.Next(_scratch.Count)];
                Acl.InsertEqualsOtherArrayIndexCommand(a, a);   // always true
                return;
            }

            /* Original behaviour (needs two distinct indices) */
            if (_scratch.Count < 2)
            { CopyScratchToNew(); return; }

            int i1 = _scratch[_rnd.Next(_scratch.Count)];
            int i2 = _scratch[_rnd.Next(_scratch.Count)];

            switch (_rnd.Next(4))
            {
                case 0: Acl.InsertEqualsOtherArrayIndexCommand(i1, i2); break;
                case 1: Acl.InsertNotEqualsOtherArrayIndexCommand(i1, i2); break;
                case 2: Acl.InsertGreaterThanOtherArrayIndexCommand(i1, i2); break;
                default: Acl.InsertLessThanOtherArrayIndexCommand(i1, i2); break;
            }
        }


        void NextSourceDrain()
        {
            int src = SrcStart + _rnd.Next(OrigCt);
            Acl.CopyToNew(src, true);
        }
        void IncrementByProduct()
        {
            if (_scratch.Count < 2) { CopyScratchToNew(); }
            int tgt = _scratch[_rnd.Next(_scratch.Count)];
            int a = _scratch[_rnd.Next(_scratch.Count)];
            int b = _scratch[_rnd.Next(_scratch.Count)];
            Acl.IncrementByProduct(tgt, false, a, b);
        }

        /*----------------- utils -----------------*/
        int EnsureScratch()
        {
            if (_scratch.Count == 0) CopyOriginalToNew();
            return _scratch[_rnd.Next(_scratch.Count)];
        }
    }
}