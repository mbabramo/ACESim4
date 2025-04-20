using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
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

        private static bool RunFuzzTest => false; // DEBUG

        [TestMethod]
        public void InterpreterVsCompiled_AllThresholds()
        {
            if (!RunFuzzTest) return;

            for (int stage = 0; stage < Stages.Length; stage++)
            {
                var (iters, maxDepth, maxBody) = Stages[stage];
                int seedBase = (int)(0xACE0_0000 + stage * 10_000);

                for (int iter = 0; iter < iters; iter++)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine(@$"--- Iteration {iter} ---");
                    int seed = seedBase + iter;
                    var rnd = new Random(seed);

                    var fuzzer = new CommandFuzzer(rnd);
                    fuzzer.BuildRandomAcl(maxDepth, maxBody);

                    Debug.WriteLine(@$"=== Command Tree ===
{fuzzer.Acl.CommandTree.ToString()}
----------------------");
                    for (int i = 0; i < fuzzer.Acl.NextCommandIndex; i++)
                        Debug.WriteLine($"{i,3}: {fuzzer.Acl.UnderlyingCommands[i]}");

                    double[] expected = fuzzer.MakeInitialData();
                    fuzzer.Acl.ExecuteAll(expected, tracing: false);

                    int totalCmds = fuzzer.Acl.NextCommandIndex;

                    // We want to try different threshold numbers, but no more than three per iteration.
                    // Choose at random from the range [1, totalCmds + 1] (inclusive).
                    const int numThresholdsToTry = 3;
                    var thresholds = new HashSet<int>();
                    while (thresholds.Count < Math.Min(numThresholdsToTry, totalCmds + 1))
                    {
                        int threshold = rnd.Next(1, totalCmds + 2);
                        thresholds.Add(threshold);
                    }

                    foreach (int th in thresholds)
                    {
                        Debug.WriteLine($"Iteration {iter}; Threshold {th}");
                        Debug.WriteLine($"------------------");
                        foreach (bool useRoslyn in new[] { true, false })
                        {
                            var acl = fuzzer.CloneFor(th, useRoslyn);
                            double[] actual = fuzzer.MakeInitialData();
                            acl.ExecuteAll(actual, tracing: false);

                            for (int idx = 0; idx < expected.Length; idx++)
                            {
                                double tol = Math.Max(1e-9, Math.Abs(expected[idx]) / 1000.0);
                                if (Math.Abs(expected[idx] - actual[idx]) > tol)
                                {
                                    Dump(seed, stage, th, useRoslyn, idx, expected[idx], actual[idx]);
                                    Assert.Fail("Value mismatch exceeds tolerance");
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void Dump(int seed, int stage, int th, bool roslyn, int idx, double exp, double act)
        {
            Debug.WriteLine($"FAIL seed={seed} stage={stage} th={th} roslyn={roslyn} idx={idx} exp={exp} act={act}");
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
                MinNumCommandsToCompile = minCompile,
                DisableAdvancedFeatures = false,
                MaxCommandsPerChunk = 1_000_000 // large but keeps ReuseScratchSlots=false  // will be reset per CloneFor
            };

            EmitChunk(0, maxDepth, maxBody);
            Acl.CompleteCommandList();
            // basic structural assertions are disabled for now; ACL itself will throw if bad

        }

        public ArrayCommandList CloneFor(int threshold, bool useRoslyn)
        {
            var clone = new ArrayCommandList(Acl.UnderlyingCommands.Length,
                                             DstStart + OrigCt,
                                             parallelize: false)
            {
                DisableAdvancedFeatures = false,
                MinNumCommandsToCompile = Acl.MinNumCommandsToCompile,
                MaxCommandsPerChunk = threshold,
                UseRoslyn = useRoslyn
            };

            /* 1️⃣  Copy the raw command stream */
            Array.Copy(Acl.UnderlyingCommands,
                       clone.UnderlyingCommands,
                       Acl.NextCommandIndex);
            clone.NextCommandIndex = Acl.NextCommandIndex;
            clone.NextArrayIndex = Acl.NextArrayIndex;
            clone.MaxArrayIndex = Acl.MaxArrayIndex;

            /* 2️⃣  Copy ordered‑index metadata so NextSource / NextDestination work */
            clone.OrderedSourceIndices = new List<int>(Acl.OrderedSourceIndices);
            clone.OrderedDestinationIndices = new List<int>(Acl.OrderedDestinationIndices);
            clone.ReusableOrderedDestinationIndices =
                new Dictionary<int, int>(Acl.ReusableOrderedDestinationIndices);

            /* 3️⃣  Rebuild CommandTree using the cloned lists */
            clone.RebuildCommandTree();
            return clone;
        }

        public double[] MakeInitialData()
        {
            double[] data = new double[DstStart + OrigCt + 400];
            for (int i = 0; i < DstStart + OrigCt; i++) data[i] = (i % 17) - 8;
            return data;
        }

        /*----------------- emit program -----------------*/
        private void EmitChunk(int depth, int maxDepth, int maxBody)
        {
            Acl.StartCommandChunk(false, null);

            EmitLinear(_rnd.Next(4, maxBody + 1));

            if (depth < maxDepth)
            {
                int guard = EnsureScratch();
                Acl.InsertNotEqualsValueCommand(guard, 0);
                Acl.InsertIfCommand();
                EmitChunk(depth + 1, maxDepth, maxBody);
                Acl.InsertEndIfCommand();
            }

            Acl.EndCommandChunk();
        }

        private void EmitLinear(int count)
        {
            for (int i = 0; i < count; i++) EmitRandomCommand();
        }

        private void EmitRandomCommand()
        {
            switch (_rnd.Next(0, 12))
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
                default: IncrementByProduct(); break;
            }
        }

        /*----------------- command emit helpers -----------------*/
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
                case 0:
                    Acl.Increment(idx, targetOriginal: false, indexOfIncrement: idx);
                    break;
                case 1:
                    Acl.Decrement(idx, indexOfDecrement: idx);
                    break;
                default:
                    Acl.MultiplyBy(idx, idx);
                    break;
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
            // SourceIndex must be ≥‑1 (constructor restriction). Choose small non‑negative constants.
            do { val = _rnd.Next(0, 5); } while (val == -2);

            if (_rnd.NextDouble() < 0.5)
                Acl.InsertEqualsValueCommand(idx, val);
            else
                Acl.InsertNotEqualsValueCommand(idx, val);
        }
        void ComparisonIndices()
        {
            if (_scratch.Count < 2) { CopyScratchToNew(); return; }
            int a = _scratch[_rnd.Next(_scratch.Count)];
            int b = _scratch[_rnd.Next(_scratch.Count)];
            switch (_rnd.Next(4))
            {
                case 0: Acl.InsertEqualsOtherArrayIndexCommand(a, b); break;
                case 1: Acl.InsertNotEqualsOtherArrayIndexCommand(a, b); break;
                case 2: Acl.InsertGreaterThanOtherArrayIndexCommand(a, b); break;
                default: Acl.InsertLessThanOtherArrayIndexCommand(a, b); break;
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
            // With MaxCommandsPerChunk != Int32.MaxValue, ReuseScratchSlots is false,
            // so ArrayCommandList will NOT decrement NextArrayIndex internally.
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
