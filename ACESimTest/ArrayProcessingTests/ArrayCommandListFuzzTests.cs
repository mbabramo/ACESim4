
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using ACESimBase.Util.ArrayProcessing;
//using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
//using ACESimBase.Util.Debugging;
//using ACESimBase.Util.NWayTreeStorage;
//using FluentAssertions;
//using Microsoft.VisualStudio.TestTools.UnitTesting;
//using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

//namespace ACESimTest.ArrayProcessingTests
//{
//    /*───────────────────────────────────────────────────────────────────────────
//     * Robust fuzz test exercising interpreter vs. compiled (IL & Roslyn) with
//     * production defaults (OrderedSources/Destinations ON, ReuseDestinations OFF).
//     *──────────────────────────────────────────────────────────────────────────*/

//    [TestClass]
//    [TestCategory("Fuzz")]
//    public class ArrayCommandListFuzzTests
//    {
//        private static readonly (int Iter, int MaxDepth, int MaxBody)[] Stages =
//        {
//            (150, 1,  8),    // toy
//            (150, 2, 18),    // small
//            (100, 3, 40),    // medium
//            ( 40, 4, 75),    // large
//            ( 10, 5,120)     // stress
//        };

//        private static bool RunFuzzTest => true; // DEBUG

//        [TestMethod]
//        public void InterpreterVsCompiled_AllThresholds()
//        {
//            if (!RunFuzzTest) return;
//            //TabbedText.WriteToConsole = false; // DEBUG
//            //TabbedText.DisableOutput();// DEBUG

//            for (int stage = 0; stage < Stages.Length; stage++)
//            {
//                var (iters, maxDepth, maxBody) = Stages[stage];
//                int seedBase = (int)(0xACE0_0000 + stage * 10_000);

//                for (int iter = 0; iter < iters; iter++)
//                {
//                    int seed = seedBase + iter;
//                    var rnd = new Random(seed);

//                    // 1) Build the *un‑thresholded* ACL (MaxCommandsPerChunk=1_000_000)
//                    var fuzzer = new FuzzCommandBuilder(rnd, );
//                    fuzzer.BuildRandomAcl(maxDepth, maxBody);

//                    // 2) Compute the reference result *once* with the flat interpreter
//                    var interp = fuzzer.CloneFor(threshold: int.MaxValue);
//                    interp.DisableAdvancedFeatures = true;
//                    double[] expected = fuzzer.MakeInitialData();
//                    interp.ExecuteAll(expected, false, ChunkExecutorKind.Interpreted, int.MaxValue);

//                    // 3) Pick random thresholds to exercise the compiled path
//                    int totalCmds = fuzzer.Acl.NextCommandIndex;
//                    const int numThresholds = 3;
//                    var thresholds = new HashSet<int>();
//                    while (thresholds.Count < Math.Min(numThresholds, totalCmds + 1))
//                        thresholds.Add(rnd.Next(1, totalCmds + 2));
//                    thresholds.Add(1); // always test threshold=1

//                    // 4) For each threshold, test Roslyn & IL vs. interpreter
//                    foreach (int th in thresholds)
//                    {
//                        TabbedText.WriteLine($"Stage {stage} of {Stages.Length}; Iteration {iter} of {iters}; Threshold {th}");
//                        TabbedText.WriteLine($"Stage {stage} of {Stages.Length}; Iteration {iter} of {iters}; Threshold {th}");
//                        TabbedText.WriteLine(new string('-', 30));

//                        foreach (ChunkExecutorKind kind in new ChunkExecutorKind[] { ChunkExecutorKind.Roslyn }) // DEBUG SUPERDEBUG, ChunkExecutorKind.IL, ChunkExecutorKind.RoslynWithLocalVariableRecycling, ChunkExecutorKind.ILWithLocalVariableRecycling })
//                        {
//                            var acl = fuzzer.CloneFor(th);

//                            double[] actual = fuzzer.MakeInitialData();
//                            acl.ExecuteAll(actual, false, kind, null);

//                            int outputCount = fuzzer.OutputSize;
//                            for (int idx = 0; idx < outputCount; idx++)
//                            {
//                                double tol = Math.Max(1e-9, Math.Abs(expected[idx]) / 1000.0);
//                                if (Math.Abs(expected[idx] - actual[idx]) > tol)
//                                {
//                                    Dump(seed, stage, th, idx, expected[idx], actual[idx]);
//                                    Assert.Fail("Compiled result mismatch vs. interpreter baseline");
//                                }
//                            }
//                        }
//                    }
//                }
//            }
//        }

//        private static void Dump(int seed, int stage, int th, int idx, double exp, double act)
//        {
//#if OUTPUT_HOISTING_INFO
//            TabbedText.WriteLine($"FAIL seed={seed} stage={stage} th={th} idx={idx} exp={exp} act={act}");
//#endif
//        }
//    }
//}