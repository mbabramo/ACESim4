using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.Util.ArrayProcessing;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TorchSharp.Modules;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

namespace ACESimTest
{
    [TestClass]
    public class ArrayCommandListTest
    {
        #region Array command list basics
        [TestMethod]
        public void ArrayCommandList_CopyFromSourceAndToDestination()
        {
            bool parallel = true;
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] values = new double[20] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallel);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(parallel, null, "Chunk");

            int source = sourceIndicesStart + 2;
            int result = cl.CopyToNew(source, true);
            cl.Increment(destinationIndicesStart + 1, true, result);

            cl.EndCommandChunk();


            cl.CompleteCommandList();
            cl.ExecuteAll(values, false);
            values[destinationIndicesStart + 1].Should().BeApproximately(20, 0.001);
        }

        [TestMethod]
        public void ArrayCommandListBasic()
        {
            bool parallel = true;
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;
            
            double[] values = new double[20] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallel);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(false, null, "Chunk");

            int v0_10 = cl.CopyToNew(sourceIndicesStart + 1 /* 10 */, fromOriginalSources: true); // example of copying from source
            int v1_10 = cl.CopyToNew(v0_10, fromOriginalSources: false);
            int v2_50 = cl.CopyToNew(sourceIndicesStart + 5 /* 50 */, fromOriginalSources: true);
            int v3_60 = cl.AddToNew(v0_10, false, v2_50);
            int v4_80 = cl.AddToNew(sourceIndicesStart + 2 /* 20 */, true, v3_60);
            int v5_4800 = cl.MultiplyToNew(v4_80, false, v3_60);
            cl.Increment(destinationIndicesStart + 0, true, v5_4800); // example of incrementing to destination --> destination 0 is 4800

            int[] sources = new int[] { sourceIndicesStart + 2 /* 20 */, sourceIndicesStart + 3 /* 30 */, sourceIndicesStart + 4 /* 40 */};
            int[] sourcesCopied = cl.CopyToNew(sources, true);
            cl.MultiplyArrayBy(sourcesCopied, sourcesCopied); // 40, 900, 1600
            cl.MultiplyBy(sourcesCopied[1] /* 900 */, sourcesCopied[2] /* 1600 */); // 900 * 1600 = 1_440_000 in sourcesCopied[1]
            int v6 = cl.CopyToNew(sources[0], true); // 20
            cl.DecrementArrayBy(sourcesCopied, v6); // 1_439_980 in sourcesCopied[1]
            cl.IncrementByProduct(sourcesCopied[1], false, v6, v6); // + 400 = 1_440_380
            cl.Increment(destinationIndicesStart + 1, true, sourcesCopied[1]); // copied to target

            cl.EndCommandChunk();
            cl.CompleteCommandList();
            cl.ExecuteAll(values, false);

            values[destinationIndicesStart + 0].Should().BeApproximately(4800, 0.001);
            values[destinationIndicesStart + 1].Should().BeApproximately(1_440_380, 0.001);
        }

        [TestMethod]
        public void ArrayCommandList_Conditional()
        {
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] sourceValues = new double[20] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; 
            int[] sourceIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, false);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(false, null, "Chunk");

            int[] copiedValues = cl.CopyToNew(sourceIndices, true);
            int v1 = cl.NewZero();

            cl.InsertEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[0]); // since true, add 1 => 1
            cl.InsertEndIfCommand();

            cl.InsertEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[1]); // since false => 1
            cl.InsertEndIfCommand();

            cl.InsertNotEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[2]); // since false => 1
            cl.InsertEndIfCommand();

            cl.InsertNotEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[3]); // since false => 1 + 8 => 9
            cl.InsertEndIfCommand();

            cl.InsertGreaterThanOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[4]); // since false => 9
            cl.InsertEndIfCommand();

            cl.InsertGreaterThanOtherArrayIndexCommand(copiedValues[4], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[5]); // since true => 9 + 32 => 41
            cl.InsertEndIfCommand();

            cl.InsertLessThanOtherArrayIndexCommand(copiedValues[3], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[6]); // since false => 41
            cl.InsertEndIfCommand();

            cl.InsertLessThanOtherArrayIndexCommand(copiedValues[4], copiedValues[5]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[7]); // since true => 41 + 128 = 169
            cl.InsertEndIfCommand();

            cl.InsertEqualsValueCommand(copiedValues[4], 999);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[8]); // since false => 169
            cl.InsertEndIfCommand();

            cl.InsertEqualsValueCommand(copiedValues[5], 32);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[9]); // since true => 169 + 512 = 681
            cl.InsertEndIfCommand();

            cl.Increment(destinationIndicesStart + 1, true, v1);

            cl.EndCommandChunk();


            cl.CompleteCommandList();
            cl.ExecuteAll(sourceValues, false);
            sourceValues[destinationIndicesStart + 1].Should().BeApproximately(681, 0.001);
        }

        [TestMethod]
        public void ArrayCommandList_ChildIncrements_NotParallel_NotRepeated() => ArrayCommandList_ChildIncrements(false, false);
        [TestMethod]
        public void ArrayCommandList_ChildIncrements_NotParallel_Repeated() => ArrayCommandList_ChildIncrements(false, true);
        [TestMethod]
        public void ArrayCommandList_ChildIncrements_Parallel_NotRepeated() => ArrayCommandList_ChildIncrements(true, false);
        [TestMethod]
        public void ArrayCommandList_ChildIncrements_Parallel_Repeated() => ArrayCommandList_ChildIncrements(true, true);

        private void ArrayCommandList_ChildIncrements(bool parallelize, bool repeatIdenticalChunk)
        {
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] sourceValues = new double[20] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int[] sourceIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] destinationIndices = new int[] { 10, 11 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 5000;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallelize);

            cl.MinNumCommandsToCompile = 1;
            //cl.IncrementDepth(); // NOTE: This is optional here (along with decrement below)
            cl.StartCommandChunk(false, null, "Chunk");
            int[] copiedValues = cl.CopyToNew(sourceIndices, true);
            int anotherValue = cl.CopyToNew(sourceIndices[5], true);
            int yetAnother = cl.CopyToNew(anotherValue, false);

            const int numParallelChunks = 50; 
            const int numPotentialIncrementsWithin = 10;
            const int excludeIndexFromIncrement = 3;

            int intermediateVariableIndex = cl.CopyToNew(copiedValues[0], false);
            int[] incrementToParentIndices = new int[] { intermediateVariableIndex }; // that is, we want increments to this intermediate variable to be copied from the child virtual stacks back into this virtual stack. 

            cl.StartCommandChunk(true, null); // parallel within this chunk
            int repeatedCommandIndex = -1;

            int inParallelWithParallelChunks = cl.CopyToNew(copiedValues[4], false); // this is in parallel with the parallel chunks below, because it is before the call to StartCommandChunk in the for  loop below. 

            for (int i = 0; i < numParallelChunks; i++)
            {
                if (i == 0 && repeatIdenticalChunk)
                    repeatedCommandIndex = cl.NextCommandIndex;
                // DEBUG Debug.WriteLine($"Repeated command index {repeatedCommandIndex}");
                cl.StartCommandChunk(false, repeatIdenticalChunk && i != 0 ? (int?) repeatedCommandIndex : null);
                cl.IncrementDepth(); // NOTE -- this is critical. We must increment depth here (and decrement below) so that the increments are copied from the child virtual stack back to the parent.
                for (int j = numPotentialIncrementsWithin - 1; j >= 0; j--) // go backward to make it easier to follow algorithm
                    if (j != excludeIndexFromIncrement)
                    {
                        // do a bunch of operations that amount to incrementing destinationIndices[0] once. Here, we are targeting the ORIGINAL values array.
                        int copiedValueIndex = cl.CopyToNew(copiedValues[j], false);
                        int negativeCopiedValueIndex = cl.NewZero();
                        cl.Decrement(negativeCopiedValueIndex, copiedValueIndex);
                        cl.Increment(destinationIndices[0], true, copiedValues[j]);
                        cl.Increment(destinationIndices[0], true, negativeCopiedValueIndex);
                        cl.Increment(destinationIndices[0], true, copiedValues[j]);
                        // and some irrelevant stuff
                        for (int k = 0; k < 2; k++)
                        {
                            int ignored = cl.NewZero();
                            cl.Increment(ignored, false, copiedValueIndex);
                        }
                    }
                // And now let's increment an intermediate value from before this command chunk.
                cl.Increment(intermediateVariableIndex, false, copiedValues[1]);

                cl.DecrementDepth();
                cl.EndCommandChunk(incrementToParentIndices, i == 0 ? false : repeatIdenticalChunk); // two key things here: (1) We specify what is to be incremented to parent indices. Note that this doesn't include changes directly to the destination, only changes to the parent virtual stack's indices. (2) We need to specify on each iteration after the initial one being repeated that this was a repeat, so that we can record that this repeat is done.
            }

            int inParallelWithParallelChunks2 = cl.CopyToNew(copiedValues[5], false); // this is also in parallel with the parallel chunks below, because it is before the call to StartCommandChunk in the for  loop below. 
            cl.EndCommandChunk();    // end parallel within chunk

            int[] ignoredArray = cl.NewZeroArray(5);
            cl.Increment(destinationIndices[1], true, intermediateVariableIndex);

            cl.EndCommandChunk(); // sequential within this chunk (but child chunk contains parallel).
            //cl.DecrementDepth(); // optional -- but must match
            cl.CompleteCommandList();
            //Debug.WriteLine($"{cl.CommandTree}");
            cl.ExecuteAll(sourceValues, false);
            sourceValues[destinationIndicesStart].Should().BeApproximately(numParallelChunks * (1023 - 2 * 2 * 2), 0.001);
            sourceValues[destinationIndicesStart + 1].Should().BeApproximately(2 * numParallelChunks + 1, 0.001);
            sourceValues[destinationIndicesStart] = 0;
            sourceValues[destinationIndicesStart + 1] = 0;
        }

        #endregion

        #region Hoisting to split long sections of commands

        [TestMethod]
        public void HoistCreatesConditionalNode()
        {
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);  // > threshold (10)
            acl.CompleteCommandList();                         // triggers hoist
            acl.DumpLeafSummary("after hoist");

            int threshold = acl.MaxCommandsPerChunk;           // 10
            bool conditionalSeen = false;
            bool oversizeLeaf = false;

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (n.StoredValue?.Name == "Conditional")
                    conditionalSeen = true;

                if (n.Branches == null || n.Branches.Length == 0) // leaf
                {
                    int len = n.StoredValue.EndCommandRangeExclusive -
                              n.StoredValue.StartCommandRange;
                    if (len > threshold)
                        oversizeLeaf = true;
                }
            });

            Assert.IsTrue(conditionalSeen, "No Conditional node created");
            Assert.IsFalse(oversizeLeaf, "Leaf exceeds threshold after hoist");
        }

        [TestMethod]
        public void GateChunkLeavesContainNoControlTokens()
        {
            // Build flat ACL, then hoist exactly once
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25, finalize: false);
            acl.CompleteCommandList();                   // planner + mutator + IL

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return; // skip non‑leaf

                bool hasIf = false, hasEndIf = false;

                for (int i = leaf.StoredValue.StartCommandRange;
                         i < leaf.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) hasIf = true;
                    if (t == ArrayCommandType.EndIf) hasEndIf = true;
                }

                if (hasIf || hasEndIf)
                    acl.DumpLeafIfUnbalanced(leaf);   // diagnostic helper

                Assert.IsFalse(hasIf,
                    "Leaf unexpectedly contains an If token");
                Assert.IsFalse(hasEndIf,
                    "Leaf unexpectedly contains an EndIf token");
            });
        }


        [TestMethod]
        public void EmitILAfterHoistNoException()
        {
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);
            acl.CompleteCommandList();  // includes hoist

            // Should *not* throw InvalidOperationException
            acl.CompileCode();
        }

        [TestMethod]
        public void ExecuteAfterHoistMatchesInterpreter()
        {
            /* 1️⃣  Build a flat ACL (no hoisting yet) */
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25, finalize: false);

            double[] dataInterp = new double[200];
            double[] dataHoist = new double[200];

            for (int i = 0; i < dataInterp.Length; i++)
                dataInterp[i] = dataHoist[i] = i % 7;   // identical seed

            /* 2️⃣  PATH A – interpreter over the flat command list */
            acl.ExecuteAllCommands(dataInterp);

            /* 3️⃣  Hoist + compile ONCE */
            acl.CompleteCommandList();      // mutator + code‑gen

            /* 4️⃣  PATH B – run the hoisted / compiled tree */
            acl.ExecuteAll(dataHoist, tracing: false);

            // after your existing calls to ExecuteAllCommands and ExecuteAll…
            for (int i = 0; i < dataInterp.Length; i++)
            {
                if (dataInterp[i] != dataHoist[i])
                {
                    Debug.WriteLine($"First mismatch at index {i}: interp={dataInterp[i]}, hoist={dataHoist[i]}");
                    break;
                }
            }

            /* 5️⃣  Compare results */
            CollectionAssert.AreEqual(dataInterp, dataHoist,
                "Mismatch after hoist execution");
        }

        [TestMethod]
        public void TwoIndependentACLsProduceSameResult()
        {
            /* Build program *text* once, then make TWO separate ACLs from it */
            var bodySize = 25;
            var cmdsFlat = BuildCustomFlat(prefixLen: 2, bodyLen: bodySize, postfixLen: 2);

            ArrayCommandList MakeACL()
            {
                var acl = CreateStubACL(cmdsFlat, maxPerChunk: 10);
                acl.CompleteCommandList();          // planner + mutator + IL
                return acl;
            }

            var acl1 = MakeACL();
            var acl2 = MakeACL();

            double[] data1 = new double[200];
            double[] data2 = new double[200];
            for (int i = 0; i < 200; i++) data1[i] = data2[i] = i % 11;  // identical seed

            acl1.ExecuteAll(data1, tracing: false);
            acl2.ExecuteAll(data2, tracing: false);

            CollectionAssert.AreEqual(data1, data2,
                "Two independent ACL instances produced different results.");
        }



        /* ---------------------------------------------------------------------------
           Helper: build a minimal ArrayCommandList with one "huge" if‑body
        --------------------------------------------------------------------------- */
        /// Builds an ArrayCommandList containing one “huge” If body of
        /// <paramref name="bodySize" /> IncrementBy commands.
        ///
        /// • By default (<paramref name="finalize" /> = true) the helper calls
        ///   <c>CompleteCommandList()</c>, so the returned ACL is *ready to run*
        ///   with its tree already built.
        ///
        /// • Pass <c>false</c> when you want a *flat* command list (no hoisting
        ///   performed yet) — e.g. when a unit‑test is about to run its own
        ///   planner / mutator first.
        private static ArrayCommandList BuildSimpleACLWithHugeIf(int bodySize,
                                                                 bool finalize = true)
        {
            var acl = new ArrayCommandList(maxNumCommands: 10_000,
                                           initialArrayIndex: 0,
                                           parallelize: false)
            {
                MaxCommandsPerChunk = 10,      // small threshold for tests
                DisableAdvancedFeatures = false,
                UseCheckpoints = false
            };

            // explicit root chunk
            acl.StartCommandChunk(runChildrenInParallel: false,
                                  identicalStartCommandRange: null,
                                  name: "root");

            int idx0 = acl.NewZero();
            int idx1 = acl.NewZero();
            int idx2 = acl.NewZero();

            acl.InsertNotEqualsOtherArrayIndexCommand(idx0, idx1); // condition TRUE
            acl.InsertIfCommand();

            for (int i = 0; i < bodySize; i++)
                acl.Increment(idx2, targetOriginal: false, idx0);

            acl.InsertEndIfCommand();
            acl.EndCommandChunk();        // close root

            if (finalize)
                acl.CompleteCommandList(); // build tree & code‑gen

            return acl;
        }

        [TestMethod]
        public void TraceSiblingExecutionOrder()
        {
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);

            // Log commands of the first leaf (optional debug)
            var firstLeaf = (NWayTreeStorageInternal<ArrayCommandChunk>)
                            acl.CommandTree.GetBranch(1);
            LogCommandsInRange(acl,
                               firstLeaf.StoredValue.StartCommandRange,
                               firstLeaf.StoredValue.EndCommandRangeExclusive);

            /* ─── enable checkpoints so ExecuteAll records every leaf ─── */
            acl.UseCheckpoints = true;
            acl.Checkpoints = new List<double>();

            // Run once (debug output shows chunk entry order)
            acl.ExecuteAll(new double[200], tracing: false);

            // Expect multiple chunks executed
            Assert.IsTrue(acl.Checkpoints.Count >= 2,
                $"Only {acl.Checkpoints.Count} chunk executed; splitter may have failed.");
        }


        private void LogCommandsInRange(ArrayCommandList acl, int startCmd, int endCmd)
        {
            int depth = 0;
            for (int i = startCmd; i < endCmd; i++)
            {
                var cmd = acl.UnderlyingCommands[i];
                if (cmd.CommandType == ArrayCommandType.If) depth++;
                if (cmd.CommandType == ArrayCommandType.EndIf) depth--;

                System.Diagnostics.Debug.WriteLine(
                    $"   {i,6}  d={depth,2}  {cmd}");
            }
        }
        private static ArrayCommandList BuildNestedHugeIf(
                int outerBodySize, int innerBodySize, int maxPerChunk)
        {
            var acl = new ArrayCommandList(maxNumCommands: 50_000,
                                           initialArrayIndex: 0,
                                           parallelize: false)
            {
                MaxCommandsPerChunk = maxPerChunk,
                DisableAdvancedFeatures = false,
                UseCheckpoints = false
            };

            // scratch indices
            int idx0 = acl.NewZero();   // always 0
            int idx1 = acl.NewZero();
            int idx2 = acl.NewZero();

            // OUTER If (condition false)
            acl.InsertNotEqualsOtherArrayIndexCommand(idx0, idx1); // 0 != 0  → false
            acl.InsertIfCommand();

            // huge body at depth‑1
            for (int i = 0; i < outerBodySize; i++)
                acl.Increment(idx2, targetOriginal: false, idx0);

            // INNER If (condition true)
            acl.InsertEqualsOtherArrayIndexCommand(idx0, idx0); // 0 == 0 → true
            acl.InsertIfCommand();

            for (int i = 0; i < innerBodySize; i++)
                acl.Increment(idx2, targetOriginal: false, idx0);

            acl.InsertEndIfCommand();  // close INNER
            acl.InsertEndIfCommand();  // close OUTER

            acl.CompleteCommandList();
            return acl;
        }

        /*───────────────────────────────────────────────────────────────
         * STRUCTURAL  – after hoisting, every executable leaf contains
         *               zero If / EndIf tokens (balanced at parent level).
         *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void NestedHoistLeavesContainNoControlTokens()
        {
            var acl = BuildNestedHugeIf(outerBodySize: 40,
                                        innerBodySize: 40,
                                        maxPerChunk: 10);

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return; // skip non‑leaf

                for (int i = leaf.StoredValue.StartCommandRange;
                         i < leaf.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    Assert.IsFalse(t == ArrayCommandType.If ||
                                   t == ArrayCommandType.EndIf,
                        "Leaf unexpectedly contains control‑flow tokens");
                }
            });
        }




        /*───────────────────────────────────────────────────────────────
         * EXECUTION  –  outer body skipped (condition false),
         *               inner body never reached, counter stays 0.
         *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void NestedHoistExecutionMatches()
        {
            int outerBody = 40, innerBody = 40, maxPerChunk = 10;
            var acl = BuildNestedHugeIf(outerBody, innerBody, maxPerChunk);

            double[] data = new double[100];
            acl.ExecuteAll(data, tracing: false);

            /* idx2 is incremented inside both bodies; with the outer If FALSE
               neither body runs, so idx2 remains 0. */
            Assert.AreEqual(0.0, data[2], 1e-12);
        }



        [TestMethod]
        public void PlannerDetectsOversizeIf()
        {
            // ── 1. Build the flat command list (32 cmds with 25‑command If‑body) ──
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);   // threshold = 10
            var cmds = acl.UnderlyingCommands;                  // convenience alias

            // ── 2.  Create a *synthetic* one‑leaf tree so the planner sees it ──
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            root.StoredValue = new ArrayCommandChunk
            {
                ID = 0,
                StartCommandRange = 0,
                EndCommandRangeExclusive = acl.NextCommandIndex,   // entire list
                StartSourceIndices = 0,
                EndSourceIndicesExclusive = 0,
                StartDestinationIndices = 0,
                EndDestinationIndicesExclusive = 0
            };

            // ── 3.  Run the planner (pure read‑only) ─────────────────────────────
            var planner = new HoistPlanner(cmds, maxCommandsPerChunk: 10);
            var plan = planner.BuildPlan(root);

            // ── 4.  Assertions ───────────────────────────────────────────────────
            Assert.AreEqual(1, plan.Count, "expected one oversize leaf");
            Assert.AreEqual(25, plan[0].BodyLen, "body length should be 25");
        }

        /// Helper: make a synthetic root leaf that spans the full command list
        private static NWayTreeStorageInternal<ArrayCommandChunk>
            MakeRootLeaf(ArrayCommand[] cmds)
        {
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            root.StoredValue = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length
            };
            return root;
        }

        /// <summary>
        /// Flat command list used by Split/Insert unit‑tests:
        ///   • cmds 0‑1 prefix  (two <see cref="ArrayCommandType.IncrementBy"/>)
        ///   • cmd  2   “If”
        ///   • cmds 3‑17 (15 IncrementBy) ► oversize body
        ///   • cmd  18  “EndIf”
        ///   • cmds 19‑20 postfix (two IncrementBy)
        /// Total = 21 commands.
        /// </summary>
        private static List<ArrayCommand> BuildFlatSample()
        {
            var cmds = new List<ArrayCommand>();

            // prefix (0‑1)
            cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));

            // If token (2)
            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));

            // body (3‑17) – 15 increment commands
            for (int i = 0; i < 15; i++)
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));

            // EndIf token (18)
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

            // postfix (19‑20)
            cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));

            return cmds;
        }

        /// <summary>
        /// Build a flat command list with
        ///   • <paramref name="prefixLen"/>  IncrementBy commands before the If
        ///   • one If token
        ///   • <paramref name="bodyLen"/>    IncrementBy commands in the body
        ///   • EndIf token
        ///   • <paramref name="postfixLen"/> IncrementBy commands after EndIf
        /// </summary>
        private static List<ArrayCommand> BuildCustomFlat(
            int prefixLen, int bodyLen, int postfixLen)
        {
            var cmds = new List<ArrayCommand>();

            for (int i = 0; i < prefixLen; i++) cmds.Add(new(ArrayCommandType.IncrementBy, 0, 0));
            int ifIdx = cmds.Count;
            cmds.Add(new(ArrayCommandType.If, -1, -1));

            for (int i = 0; i < bodyLen; i++) cmds.Add(new(ArrayCommandType.IncrementBy, 0, 0));
            int endIfIdx = cmds.Count;
            cmds.Add(new(ArrayCommandType.EndIf, -1, -1));

            for (int i = 0; i < postfixLen; i++) cmds.Add(new(ArrayCommandType.IncrementBy, 0, 0));

            return cmds;
        }

        /// Build an ACL whose *entire* command list is a single oversized If‑body
        /// (size = bodyLen) so that HoistPlanner emits exactly one PlanEntry.
        private static ArrayCommandList BuildFlatOversizeACL(
            int bodyLen, int threshold)
        {
            var cmds = BuildCustomFlat(prefixLen: 0, bodyLen, postfixLen: 0);

            var acl = CreateStubACL(cmds, threshold);
            // Give every command a distinct ID so the mutator re‑generates stacks
            acl.MaxArrayIndex = 0;           // minimal virtual stack
            return acl;
        }

        /// Build an ACL containing <paramref name="depth"/> nested If blocks.
        /// Each body just increments idx2 by 1. All conditions are TRUE so the
        /// innermost body executes exactly once.
        private static ArrayCommandList BuildDeepNestedIf(int depth)
        {
            var acl = new ArrayCommandList(maxNumCommands: 5_000,
                                           initialArrayIndex: 0,
                                           parallelize: false)
            {
                MaxCommandsPerChunk = 1000,          // large; avoid hoist here
                DisableAdvancedFeatures = false
            };

            int idx0 = acl.NewZero();
            int idx2 = acl.NewZero();

            // chain of TRUE conditions
            for (int d = 0; d < depth; d++)
            {
                acl.InsertEqualsOtherArrayIndexCommand(idx0, idx0); // 0 == 0 → true
                acl.InsertIfCommand();
            }

            acl.Increment(idx2, targetOriginal: false, idx0);       // body

            // close nest
            for (int d = 0; d < depth; d++)
                acl.InsertEndIfCommand();

            acl.CompleteCommandList();          // compile tree & IL
            return acl;
        }


        /// <summary>
        /// Build an <see cref="ArrayCommandList"/> from an existing command array
        /// without going through the normal AddCommand pipeline.  
        /// Intended for **unit‑tests only**.
        /// </summary>
        /// <param name="cmds">The complete list of commands that will become the ACL’s program.</param>
        /// <param name="maxPerChunk">Value to assign to <c>MaxCommandsPerChunk</c>
        private static ArrayCommandList CreateStubACL(
    IList<ArrayCommand> cmds,
    int maxPerChunk)
        {
            // 1) Allocate with a generous command capacity.
            var acl = new ArrayCommandList(
                maxNumCommands: cmds.Count + 10,
                initialArrayIndex: 0,
                parallelize: false)
            {
                MaxCommandsPerChunk = maxPerChunk
            };

            // 2) Drop the finished command list straight in.
            acl.UnderlyingCommands = cmds.ToArray();

            // 3) Bring the bookkeeping counters in‑sync.
            acl.NextCommandIndex = cmds.Count;
            acl.MaxCommandIndex = cmds.Count;

            // 4) Derive MaxArrayIndex from the commands we have.
            int maxIdx = -1;
            foreach (var c in cmds)
            {
                int s = c.GetSourceIndexIfUsed();
                int t = c.GetTargetIndexIfUsed();
                if (s > maxIdx) maxIdx = s;
                if (t > maxIdx) maxIdx = t;
            }
            acl.MaxArrayIndex = maxIdx;

            // 5) Provide a minimal one‑leaf command tree that spans the whole list.
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            root.StoredValue = new ArrayCommandChunk
            {
                ID = 0,
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Count
            };
            acl.CommandTree = root;

            return acl;
        }


        /// 1) Nested If inside an oversize body – planner should still
        ///    report exactly **one** outer‑level If/EndIf pair.
        [TestMethod]
        public void PlannerDetectsOversizeWithNestedIf()
        {
            /* layout:
                 0  If
                 1‑5  IncrementBy …
                 6  If          (nested)
                 7‑11 IncrementBy …
                12  EndIf       (nested)
                13‑17 IncrementBy …
                18 EndIf
            • body length (17) > threshold (10)
            */
            var cmds = new List<ArrayCommand>();

            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));     // 0
            for (int i = 0; i < 5; i++)                                  // 1‑5
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));

            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));     // 6 nested
            for (int i = 0; i < 5; i++)                                  // 7‑11
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));  // 12

            for (int i = 0; i < 5; i++)                                  // 13‑17
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));  // 18

            var root = MakeRootLeaf(cmds.ToArray());
            var planner = new HoistPlanner(cmds.ToArray(), maxCommandsPerChunk: 10);
            var plan = planner.BuildPlan(root);

            Assert.AreEqual(1, plan.Count, "planner should hoist only the outer If");
            Assert.AreEqual(17, plan[0].BodyLen);
        }

        /// 2) Oversize leaf **without** any If – planner should skip it.
        [TestMethod]
        public void PlannerIgnoresOversizeWithoutIf()
        {
            // 15 Zero commands (no flow control), threshold 10
            var cmds = Enumerable.Range(0, 15)
                .Select(_ => new ArrayCommand(ArrayCommandType.Zero, 0, -1))
                .ToArray();

            var root = MakeRootLeaf(cmds);
            var planner = new HoistPlanner(cmds, 10);
            var plan = planner.BuildPlan(root);

            Assert.AreEqual(0, plan.Count, "planner should ignore oversize w/out If");
        }

        /// 3) Leaf size already ≤ threshold, even with If – no action expected.
        [TestMethod]
        public void PlannerSkipsBalancedLeaf()
        {
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.If, -1, -1),
        new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0),
        new ArrayCommand(ArrayCommandType.EndIf, -1, -1)
    };

            var root = MakeRootLeaf(cmds);
            var planner = new HoistPlanner(cmds, maxCommandsPerChunk: 10);
            var plan = planner.BuildPlan(root);

            Assert.AreEqual(0, plan.Count, "small leaf should not be hoisted");
        }

        /// 4) Two separate oversize If‑bodies in two leaves – planner finds both.
        [TestMethod]
        public void PlannerDetectsMultipleOversizeLeaves()
        {
            // Build first oversize leaf (20 cmds with If)
            var cmds = new List<ArrayCommand>();
            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));
            for (int i = 0; i < 18; i++)
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

            int secondLeafStart = cmds.Count;

            // Build second oversize leaf (another 20 cmds with If)
            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));
            for (int i = 0; i < 18; i++)
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

            // Split into two root‑children leaves to simulate tree structure
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            var left = MakeRootLeaf(cmds.Take(secondLeafStart).ToArray());
            var right = MakeRootLeaf(cmds.Skip(secondLeafStart).ToArray());

            root.SetBranch(1, left);
            root.SetBranch(2, right);

            var planner = new HoistPlanner(cmds.ToArray(), maxCommandsPerChunk: 10);
            var plan = planner.BuildPlan(root);

            Assert.AreEqual(2, plan.Count, "planner should detect both oversize leaves");
            Assert.IsTrue(plan.All(p => p.BodyLen == 18), "each body should be 18 cmds");
        }

        /*────────────────────────────────────────────────────────────
 * 1) EnsureTreeExists creates a one‑leaf root when missing
 *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void EnsureTreeExists_CreatesSyntheticRoot()
        {
            var cmds = BuildCustomFlat(1, 2, 1);
            var acl = CreateStubACL(cmds, 10);
            acl.CommandTree = null;                 // simulate “no tree yet”

            var root = HoistMutator.EnsureTreeExists(acl);

            Assert.IsNotNull(root);
            Assert.AreEqual(0, root.StoredValue.StartCommandRange);
            Assert.AreEqual(cmds.Count, root.StoredValue.EndCommandRangeExclusive);
            Assert.IsTrue(root.Branches == null || root.Branches.Length == 0,
                "synthetic root should be a single leaf");
        }

        /*────────────────────────────────────────────────────────────
 *  ApplyPlan correctly hoists one oversized leaf
 *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void ApplyPlan_SingleOversizeLeaf()
        {
            const int THRESH = 6;
            var acl = BuildFlatOversizeACL(bodyLen: 20, threshold: THRESH);

            // Planner
            var planner = new HoistPlanner(acl.UnderlyingCommands, THRESH);
            var plan = planner.BuildPlan(
                (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            // Mutator
            HoistMutator.ApplyPlan(acl, plan);

            // Assertions
            acl.CommandTree.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches?.Length > 0) return;   // skip non‑leaf nodes

                int len = leaf.StoredValue.EndCommandRangeExclusive -
                          leaf.StoredValue.StartCommandRange;
                Assert.IsTrue(len <= THRESH, "leaf exceeds threshold");

                // balanced If / EndIf
                int ifs = 0, ends = 0;
                for (int i = leaf.StoredValue.StartCommandRange;
                         i < leaf.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) ifs++;
                    if (t == ArrayCommandType.EndIf) ends++;
                }
                Assert.AreEqual(ifs, ends, "unbalanced leaf");

                // every leaf must have a virtual stack
                Assert.IsNotNull(leaf.StoredValue.VirtualStack,
                    "leaf missing VirtualStack");
            });
        }


        /*────────────────────────────────────────────────────────────
         * 3) ApplyPlan hoists two leaves and updates parent.LastChild
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void ApplyPlan_MultipleLeaves()
        {
            // build two oversize leaves back‑to‑back
            const int THRESH = 5;
            var cmds = new List<ArrayCommand>();
            cmds.AddRange(BuildCustomFlat(0, 12, 0));   // first leaf  (0‑13)
            int secondStart = cmds.Count;
            cmds.AddRange(BuildCustomFlat(0, 12, 0));   // second leaf (14‑27)

            // create root with two branches to mimic separate leaves
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            var left = new NWayTreeStorageInternal<ArrayCommandChunk>(root);
            var right = new NWayTreeStorageInternal<ArrayCommandChunk>(root);

            left.StoredValue = new ArrayCommandChunk
            {
                ID = 1,
                StartCommandRange = 0,
                EndCommandRangeExclusive = secondStart
            };
            right.StoredValue = new ArrayCommandChunk
            {
                ID = 2,
                StartCommandRange = secondStart,
                EndCommandRangeExclusive = cmds.Count
            };
            root.SetBranch(1, left);
            root.SetBranch(2, right);
            root.StoredValue = new ArrayCommandChunk { LastChild = 2 };

            var acl = CreateStubACL(cmds, THRESH);
            acl.CommandTree = root;

            // planner + mutator
            var plan = new HoistPlanner(acl.UnderlyingCommands, THRESH).BuildPlan(root);
            HoistMutator.ApplyPlan(acl, plan);

            // root should still have two children (left = Prefix slice, right same)
            Assert.AreEqual(2, root.StoredValue.LastChild);
        }


        [TestMethod]
        public void SplitCreatesAllThreeSlices()
        {
            // flat 0‑20 command list with If at 2 and EndIf at 18
            var cmds = BuildFlatSample();            // helper from previous test
            var acl = CreateStubACL(cmds, 6);       // helper we added earlier

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 2, 18);

            Assert.AreEqual((0, 2), (split.Prefix.StoredValue.StartCommandRange,
                                     split.Prefix.StoredValue.EndCommandRangeExclusive));
            Assert.AreEqual((2, 19), (split.Gate.StoredValue.StartCommandRange,
                                     split.Gate.StoredValue.EndCommandRangeExclusive));
            Assert.AreEqual((19, 21), (split.Postfix!.StoredValue.StartCommandRange,
                                     split.Postfix.StoredValue.EndCommandRangeExclusive));
        }

        [TestMethod]
        public void InsertSplitAddsBranchesInOrder()
        {
            var cmds = BuildFlatSample();
            var acl = CreateStubACL(cmds, 6);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 2, 18);

            HoistMutator.InsertSplitIntoTree(split);

            Assert.AreEqual(2, leaf.StoredValue.LastChild);         // 0=prefix,1=gate,2=postfix
            Assert.AreSame(split.Gate, leaf.GetBranch(1));
            Assert.AreSame(split.Postfix, leaf.GetBranch(2));
        }

        /*───────────────────────────────────────────────────────────────
 * 1)  If … EndIf is the *final* thing in the leaf ⇒ no postfix
 *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void SplitWithoutPostfix()
        {
            // Build commands identical to BuildFlatSample BUT drop the postfix.
            var cmds = BuildFlatSample();
            cmds.RemoveRange(19, 2);           // remove cmds 19‑20

            var acl = CreateStubACL(cmds, maxPerChunk: 6);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;

            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, ifIdx: 2, endIfIdx: 18);
            HoistMutator.InsertSplitIntoTree(split);

            Assert.IsNull(split.Postfix, "postfix slice should be null");
            Assert.AreEqual(1, leaf.StoredValue.LastChild, "LastChild should be 1");
        }

        /*───────────────────────────────────────────────────────────────
         * 2)  Every child under Conditional gate ≤ threshold
         *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void ChildrenRespectThreshold()
        {
            const int THRESH = 4;

            // oversize body (25 cmds) so we know we’ll get several children
            var cmds = new List<ArrayCommand>();
            cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));      // 0
            for (int i = 0; i < 25; i++)                                 // 1‑25
                cmds.Add(new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0));
            cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));   // 26

            var acl = CreateStubACL(cmds, THRESH);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 0, 26);
            HoistMutator.InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);   // same call ReplaceLeafWithGate does

            foreach (var child in split.Gate.Branches.Where(b => b != null))
            {
                int len = child.StoredValue.EndCommandRangeExclusive -
                          child.StoredValue.StartCommandRange;
                Assert.IsTrue(len <= THRESH, $"child length {len} exceeds threshold");
            }
        }

        /*───────────────────────────────────────────────────────────────
         * Gate children cover the body exactly – no gaps / no overlap
         *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void NoOverlapOrGapsInGateChildren()
        {
            const int THRESH = 6;

            var cmds = BuildFlatSample();                 // 21 commands (0‑20)
            var acl = CreateStubACL(cmds, THRESH);

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 2, 18);
            HoistMutator.InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);

            var seen = new HashSet<int>();

            foreach (var child in split.Gate.Branches.Where(b => b != null))
                for (int i = child.StoredValue.StartCommandRange;
                         i < child.StoredValue.EndCommandRangeExclusive; i++)
                    Assert.IsTrue(seen.Add(i),
                        $"command {i} duplicated across children");

            /* children should cover exactly indices 3‑17 (15 commands) */
            var expected = Enumerable.Range(3, 15);   // 3,4,…,17
            CollectionAssert.AreEquivalent(expected.ToList(), seen.ToList(),
                "children range mismatch (gap or extra)");
        }


        /*────────────────────────────────────────────────────────────
         * 1) If at index 0  → no prefix slice
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void Split_NoPrefix_HasPostfix()
        {
            const int THRESH = 4;

            var cmds = BuildCustomFlat(prefixLen: 0, bodyLen: 10, postfixLen: 2);
            var acl = CreateStubACL(cmds, THRESH);

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 0, 11); // If at 0, EndIf at 11
            HoistMutator.InsertSplitIntoTree(split);

            /* prefix slice must be empty (0‑0) */
            Assert.AreEqual(0, leaf.StoredValue.StartCommandRange);
            Assert.AreEqual(0, leaf.StoredValue.EndCommandRangeExclusive);

            /* gate + postfix must be present and correctly wired */
            Assert.IsNotNull(split.Gate);
            Assert.IsNotNull(split.Postfix);
            Assert.AreEqual(2, leaf.StoredValue.LastChild);   // 0=prefix,1=gate,2=postfix
        }


        /*────────────────────────────────────────────────────────────
         * 2) list is exactly  If body EndIf  (no prefix, no postfix)
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void Split_NoPrefix_NoPostfix()
        {
            const int THRESH = 5;
            var cmds = BuildCustomFlat(0, 12, 0);
            var acl = CreateStubACL(cmds, THRESH);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 0, 13);
            HoistMutator.InsertSplitIntoTree(split);

            Assert.IsNull(split.Postfix);
            Assert.AreEqual(1, leaf.StoredValue.LastChild);
        }

        /*────────────────────────────────────────────────────────────
         * 3) body size == threshold ⇒ gate gets ONE child
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void Split_BodyEqualsThreshold_OneChild()
        {
            const int THRESH = 6;
            var cmds = BuildCustomFlat(2, THRESH, 1);         // body 6 == threshold
            var acl = CreateStubACL(cmds, THRESH);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 2, 9);
            HoistMutator.InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);

            Assert.AreEqual(1, split.Gate.StoredValue.LastChild,
                "body == threshold should produce exactly one child");
        }

        /*────────────────────────────────────────────────────────────
         * 4) tiny threshold (2)  → many small children
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void Split_TinyThreshold_ManyChildren()
        {
            const int THRESH = 2;
            var cmds = BuildCustomFlat(1, 11, 1);
            var acl = CreateStubACL(cmds, THRESH);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 1, 13);
            HoistMutator.InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);

            int childCount = split.Gate.StoredValue.LastChild;
            Assert.IsTrue(childCount >= 6, "tiny threshold should yield many children");

            foreach (var ch in split.Gate.Branches.Where(b => b != null))
            {
                int len = ch.StoredValue.EndCommandRangeExclusive - ch.StoredValue.StartCommandRange;
                Assert.IsTrue(len <= THRESH, $"child length {len} > threshold");
            }
        }

        /*────────────────────────────────────────────────────────────
         * 5) huge body tests LastChild byte overflow guard
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void Split_HugeBody_ChildCountUnder256()
        {
            const int THRESH = 3;
            const int BODY = 800;       // 800 / 3  ≈ 267  (> 255 without gate+postfix)
            var cmds = BuildCustomFlat(2, BODY, 2);
            var acl = CreateStubACL(cmds, THRESH);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 2, BODY + 3);
            HoistMutator.InsertSplitIntoTree(split);
            acl.SliceBodyIntoChildren(split.Gate);

            byte last = split.Gate.StoredValue.LastChild;
            Assert.IsTrue(last < 255,
                $"LastChild overflow: {last} children (max 254 allowed in byte)");
        }

        /*────────────────────────────────────────────────────────────
 * SplitOversizeLeaf produces correct ranges
 *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void SplitHelper_RangesCorrect()
        {
            var cmds = BuildCustomFlat(3, 10, 3);   // prefix 3, body 10, postfix 3
            var acl = CreateStubACL(cmds, 6);

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 3, 14);

            // prefix: cmds 0‑2
            Assert.AreEqual((0, 3), (split.Prefix.StoredValue.StartCommandRange,
                                     split.Prefix.StoredValue.EndCommandRangeExclusive));

            // gate: If at 3 … EndIf at 14  (inclusive → 15)
            Assert.AreEqual((3, 15), (split.Gate.StoredValue.StartCommandRange,
                                     split.Gate.StoredValue.EndCommandRangeExclusive));

            // postfix: cmds 15‑17
            Assert.IsNotNull(split.Postfix);
            Assert.AreEqual((15, 18), (split.Postfix!.StoredValue.StartCommandRange,
                                     split.Postfix.StoredValue.EndCommandRangeExclusive));
        }


        /*────────────────────────────────────────────────────────────
         * InsertSplitIntoTree wires branch IDs 1 & 2 (no postfix → only 1)
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void InsertSplitHelper_WiresBranches()
        {
            var cmds = BuildCustomFlat(0, 12, 0);     // no postfix
            var acl = CreateStubACL(cmds, 6);

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 0, 13);
            HoistMutator.InsertSplitIntoTree(split);

            Assert.AreSame(split.Gate, leaf.GetBranch(1));
            Assert.AreEqual(1, leaf.StoredValue.LastChild);
        }

        /*────────────────────────────────────────────────────────────
         * Gate children count == ceil(body/threshold)
         *───────────────────────────────────────────────────────────*/
        [TestMethod]
        public void SliceBodyCreatesExpectedChildCount()
        {
            const int THRESH = 4;
            var cmds = BuildCustomFlat(1, 17, 1);     // body 17
            var acl = CreateStubACL(cmds, THRESH);

            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 1, 19);
            HoistMutator.InsertSplitIntoTree(split);

            acl.SliceBodyIntoChildren(split.Gate);

            int expected = (int)Math.Ceiling(17 / (double)THRESH); // 5 slices
            Assert.AreEqual(expected, split.Gate.StoredValue.LastChild,
                "unexpected number of body slices");
        }


        [TestMethod]
        public void MutatorAppliesPlanEntries()
        {
            var acl = BuildSimpleACLWithHugeIf(25, finalize: false);
            var plan = new HoistPlanner(acl.UnderlyingCommands, 10)
                           .BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            HoistMutator.ApplyPlan(acl, plan); 

        }

        [TestMethod]
        public void MutatorProducesBalancedLeaves()
        {
            // build ACL with an oversize If body (25 > threshold 10)
            var acl = BuildSimpleACLWithHugeIf(25, finalize: false);
            var plan = new HoistPlanner(acl.UnderlyingCommands, 10)
                           .BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            HoistMutator.ApplyPlan(acl, plan); 


            // every leaf must now have matching If / EndIf counts
            acl.CommandTree.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return;

                int ifs = 0, endIfs = 0;
                for (int i = leaf.StoredValue.StartCommandRange;
                         i < leaf.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) ifs++;
                    if (t == ArrayCommandType.EndIf) endIfs++;
                }
                Assert.AreEqual(ifs, endIfs,
                    $"Leaf ID={leaf.StoredValue.ID} unbalanced (If={ifs},EndIf={endIfs})");
            });
        }

        [TestMethod]
        public void MutatorRespectsChunkSizeThreshold()
        {
            var acl = BuildSimpleACLWithHugeIf(25, finalize: false);
            int max = acl.MaxCommandsPerChunk;              // 10

            var plan = new HoistPlanner(acl.UnderlyingCommands, max)
                           .BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);
            HoistMutator.ApplyPlan(acl, plan);


            bool oversizeFound = false;
            acl.CommandTree.WalkTree(n =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return;
                int len = leaf.StoredValue.EndCommandRangeExclusive -
                          leaf.StoredValue.StartCommandRange;
                if (len > max) oversizeFound = true;
            });

            Assert.IsFalse(oversizeFound, "Some leaf still exceeds threshold");
        }

        [TestMethod]
        public void MutatorExecutionMatchesInterpreter()
        {
            var acl = BuildSimpleACLWithHugeIf(25, finalize: false);
            var cmds = acl.UnderlyingCommands;
            var plan = new HoistPlanner(cmds, 10)
                           .BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);
            HoistMutator.ApplyPlan(acl, plan); 


            double[] dataInterp = new double[200];
            double[] dataHoist = new double[200];

            // reference path: plain interpreter
            acl.ExecuteAllCommands(dataInterp);

            // hoisted path (may run compiled chunks, but tree is mutated)
            acl.ExecuteAll(dataHoist, tracing: false);

            CollectionAssert.AreEqual(dataInterp, dataHoist,
                "State mismatch after hoisted execution");
        }


        /* ---------------------------------------------------------------------------
   Mutator unit test – verifies slicing without duplication
   Works with the current design where the prefix node is the *parent*
   of the “Conditional” gate.
--------------------------------------------------------------------------- */
        [TestMethod]
        public void MutatorSlicesBodyWithoutDuplication()
        {
            const int THRESHOLD = 6;

            // 1) flat command list: 2‑prefix, 15‑body, 2‑postfix
            var cmds = BuildCustomFlat(prefixLen: 2, bodyLen: 15, postfixLen: 2);

            // 2) one‑leaf tree → plan
            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            root.StoredValue = new ArrayCommandChunk
            {
                ID = 0,
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Count
            };
            var planner = new HoistPlanner(cmds.ToArray(), THRESHOLD);
            var plan = planner.BuildPlan(root);

            var acl = CreateStubACL(cmds, THRESHOLD);
            HoistMutator.ApplyPlan(acl, plan);

            // 3) verify
            var seen = new HashSet<int>();
            bool prefixOK = false,
                 postfixOK = false;

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                int s = n.StoredValue.StartCommandRange;
                int e = n.StoredValue.EndCommandRangeExclusive;

                bool isGate = n.StoredValue.Name == "Conditional";
                if (!isGate)                                   // ignore gate itself
                {
                    Assert.IsTrue(e - s <= THRESHOLD, $"segment [{s},{e}) too large");
                    for (int i = s; i < e; i++)
                        Assert.IsTrue(seen.Add(i), $"cmd {i} duplicated");
                }

                if (s == 0 && e == 2) prefixOK = true;     // cmds 0‑1
                if (s == 19 && e == 21) postfixOK = true;     // cmds 19‑20
            });

            Assert.IsTrue(prefixOK, "prefix segment missing");
            Assert.IsTrue(postfixOK, "postfix segment missing");
            Assert.AreEqual(cmds.Count - 2, seen.Count, "lost cmds");   // 21 – 2 = 19

        }

        /*───────────────────────────────────────────────────────────────
 * IL emitter handles 12‑level nesting without stack/branch errors
 *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void DeepNestedIf_ILEmitterHandlesDeepNesting()
        {
            const int NEST_DEPTH = 12;
            var acl = BuildDeepNestedIf(NEST_DEPTH);

            double[] dataFlat = new double[10];
            double[] dataIL = new double[10];

            /* run via interpreter (flat list) */
            acl.ExecuteAllCommands(dataFlat);

            /* run compiled IL path */
            acl.ExecuteAll(dataIL, tracing: false);

            CollectionAssert.AreEqual(dataFlat, dataIL,
                "Compiled execution diverged on deep nesting");
        }

        /*───────────────────────────────────────────────────────────────
 * Interpreter → lower threshold → compiled IL  (results identical)
 *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void InterpreterVsCompiled_HotSwapThreshold()
        {
            /* Build once, but DON’T finalise yet */
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25, finalize: false);

            /* Phase A – interpreter only (high threshold) */
            acl.MinNumCommandsToCompile = 1_000;   // higher than program length
            acl.CompleteCommandList();             // builds tree, no IL emitted

            double[] interp = new double[100];
            acl.ExecuteAll(interp, tracing: false);

            /* Phase B – drop threshold and compile IL */
            acl.MinNumCommandsToCompile = 1;       // force IL emission
            acl.CompileCode();                     // just recompiles the chunks

            double[] ilrun = new double[100];
            acl.ExecuteAll(ilrun, tracing: false);

            CollectionAssert.AreEqual(interp, ilrun,
                "Interpreter and compiled paths produced different results after hot‑swap");
        }

        /*───────────────────────────────────────────────────────────────
 * Slicer never splits between an If and its matching EndIf
 *──────────────────────────────────────────────────────────────*/
        [TestMethod]
        public void ChildrenNeverSplitNestedBlocks()
        {
            /* body with a nested If that would cross a naïve 6‑cmd boundary
               0   If                     (outer – depth 1)
               1   Inc
               2   If                     (nested – depth 2)
               3   Inc
               4   EndIf                  (nested)
               5   Inc
               6   Inc
               7   EndIf                  (outer)
            */
            var cmds = new List<ArrayCommand>
    {
        new(ArrayCommandType.If, -1,-1),
        new(ArrayCommandType.IncrementBy,0,0),
        new(ArrayCommandType.If,-1,-1),
        new(ArrayCommandType.IncrementBy,0,0),
        new(ArrayCommandType.EndIf,-1,-1),
        new(ArrayCommandType.IncrementBy,0,0),
        new(ArrayCommandType.IncrementBy,0,0),
        new(ArrayCommandType.EndIf,-1,-1)
    };

            var acl = CreateStubACL(cmds, maxPerChunk: 6);
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, 0, 7);
            HoistMutator.InsertSplitIntoTree(split);

            // <gate> holds If/EndIf; slice the body
            acl.SliceBodyIntoChildren(split.Gate);

            foreach (var child in split.Gate.Branches.Where(b => b != null))
            {
                int depth = 0;
                for (int i = child.StoredValue.StartCommandRange;
                         i < child.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = cmds[i].CommandType;
                    if (t == ArrayCommandType.If) depth++;
                    if (t == ArrayCommandType.EndIf) depth--;
                }
                Assert.AreEqual(0, depth,
                    $"child [{child.StoredValue.StartCommandRange},{child.StoredValue.EndCommandRangeExclusive}) ends with open If");
            }
        }

        [TestMethod]
        public void ChildrenMaySplitInsideNestedBlocks_WhenThresholdTiny()
        {
            /* Layout (indices):
                 0  If          ← outer (depth 1)
                 1  Inc
                 2  If          ← nested (depth 2)
                 3  Inc
                 4  Inc
                 5  EndIf       ← closes nested
                 6  Inc
                 7  EndIf       ← closes outer
               Body length = 7   Threshold = 3   ⇒ naïve slicer will cut after idx 3.
            */
            var cmds = new List<ArrayCommand>
        {
            new(ArrayCommandType.If, -1,-1),      // 0  (not in body slice)
            new(ArrayCommandType.IncrementBy,0,0),// 1
            new(ArrayCommandType.If,-1,-1),       // 2
            new(ArrayCommandType.IncrementBy,0,0),// 3
            new(ArrayCommandType.IncrementBy,0,0),// 4
            new(ArrayCommandType.EndIf,-1,-1),    // 5
            new(ArrayCommandType.IncrementBy,0,0),// 6
            new(ArrayCommandType.EndIf,-1,-1)     // 7
        };

            var acl = CreateStubACL(cmds, maxPerChunk: 3); // tiny threshold
            var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;
            var split = HoistMutator.SplitOversizeLeaf(acl, leaf, ifIdx: 0, endIfIdx: 7);
            HoistMutator.InsertSplitIntoTree(split);

            acl.SliceBodyIntoChildren(split.Gate);   // <-- current implementation

            /* Assert every child ends with depth 0. One slice will fail today. */
            foreach (var child in split.Gate.Branches.Where(b => b != null))
            {
                int depth = 0;
                for (int i = child.StoredValue.StartCommandRange;
                            i < child.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = cmds[i].CommandType;
                    if (t == ArrayCommandType.If) depth++;
                    if (t == ArrayCommandType.EndIf) depth--;
                }
                Assert.AreEqual(0, depth,
                    $"child [{child.StoredValue.StartCommandRange},{child.StoredValue.EndCommandRangeExclusive}) ends with open If (depth={depth})");
            }
        }



        [TestMethod]
        public void HoistedChildIncrementsAreMerged()
        {
            // — build a tiny ACL that is guaranteed to be hoisted/sliced —
            var acl = new ArrayCommandList(100, /*firstScratchIdx*/ 1, parallelize: false)
            {
                MaxCommandsPerChunk = 1,      // force 1‑command slices
                DisableAdvancedFeatures = false,
                MinNumCommandsToCompile = 0, // DEBUG
                UseRoslyn = true // DEBUG
            };

            /* We *want* the hoisted children of this root chunk to be designated as able to be run in parallel
               so that each slice receives its own private virtual stack.           */
            bool requiresPrivateStack = true;
            Debug.WriteLine($"[TEST] Creating root chunk with requiresPrivateStack={requiresPrivateStack}");
            acl.StartCommandChunk(requiresPrivateStack, identicalStartCommandRange: null);

            int scratch = acl.CopyToNew(0, fromOriginalSources: true);   // vs[1] = 1
            acl.InsertNotEqualsValueCommand(scratch, 0);                 // 1 != 0  → always true
            acl.InsertIfCommand();
            acl.Increment(scratch, targetOriginal: false, indexOfIncrement: scratch); // *=2
            acl.Increment(scratch, targetOriginal: false, indexOfIncrement: scratch); // *=2 again
            acl.InsertEndIfCommand();
            acl.Increment(0, targetOriginal: true, indexOfIncrement: scratch);        // write back
            acl.EndCommandChunk();

            acl.CompleteCommandList();

            double[] data = { 1 };
            acl.ExecuteAll(data, tracing: false);

            Assert.AreEqual(4, data[0]);  // should now pass
        }


        [TestMethod]
        public void HoistedChildIncrementTransfersScratchToParent_WithStubHelpers()
        {
            // 1️⃣  Build a flat command list:
            //     • 0 prefix
            //     • 2 body increments of vs[0] by vs[0], so equivalent to vs[0] = 3*vs[0] (so bodyLen>threshold forces hoist)
            //     • 0 postfix
            var cmds = BuildCustomFlat(prefixLen: 0, bodyLen: 2, postfixLen: 0);

            // 2️⃣  Create a stub ACL with threshold=1 to force hoisting on that 2‑cmd body
            var acl = CreateStubACL(cmds, maxPerChunk: 1);

            // 3️⃣  Now, append one more IncrementBy to copy our scratch slot (idx=0) into original dest (dst=0)
            //     This mirrors how Increment(targetOriginal: true) would work.
            //     (Because CreateStubACL leaves NextSource/NextDestination wiring up to you,
            //      we’re just reusing the same pattern of “make cmd” + “wire tree.”)
            var extend = acl.UnderlyingCommands.ToList();
            extend.Add(new ArrayCommand(ArrayCommandType.IncrementBy, index: 0, sourceIndex: 0)); // equivalent to vs[0] = 2*vs[0].
            acl.UnderlyingCommands = extend.ToArray();
            acl.NextCommandIndex = acl.UnderlyingCommands.Length;
            acl.MaxCommandIndex = acl.NextCommandIndex;
            // bump MaxArrayIndex so that scratch slot 0 is visible as used
            acl.MaxArrayIndex = 0;

            // 4️⃣  Completes the tree and does the planner+mutator+IL gen
            acl.CompleteCommandList();

            // 5️⃣  Execute — start with values[0]=1; expect scratch doubled twice → 4, written back
            double[] data = new double[] { 1.0 };
            acl.ExecuteAll(data, tracing: false);

            // 6️⃣  If the child’s two increments weren’t merged via CopyIncrementsToParent,
            //     we’d only see 1, not 4.
            Assert.AreEqual(4.0, data[0], 1e-12);
        }

        [TestMethod]
        public void RandomizedInterpreterVsCompiledAcrossThresholds()
        {
            var rnd = new Random(123456);
            const int iterations = 10;

            for (int iter = 0; iter < iterations; iter++)
            {
                // random initial value in original[0]
                double initialValue = rnd.NextDouble() * 10.0;

                // random body length between 3 and 7
                int bodyLen = rnd.Next(3, 8);
                int[] ops = new int[bodyLen];
                for (int i = 0; i < bodyLen; i++)
                    ops[i] = rnd.Next(3); // 0=copy,1=mul,2=inc

                // test thresholds from 1 (always hoist) up to bodyLen+1 (never hoist)
                for (int threshold = 1; threshold <= bodyLen + 1; threshold++)
                {
                    var acl = new ArrayCommandList(
                        maxNumCommands: 1000,
                        initialArrayIndex: 1,
                        parallelize: false)
                    {
                        MaxCommandsPerChunk = threshold
                    };

                    // root chunk
                    acl.StartCommandChunk(runChildrenInParallel: false,
                                          identicalStartCommandRange: null,
                                          name: "root");

                    // copy original[0] → scratch
                    int scratch = acl.CopyToNew(0, fromOriginalSources: true);

                    // always‐true guard
                    acl.InsertEqualsOtherArrayIndexCommand(scratch, scratch);
                    acl.InsertIfCommand();

                    // random scratch operations
                    foreach (int op in ops)
                    {
                        switch (op)
                        {
                            case 0:
                                scratch = acl.CopyToNew(scratch, fromOriginalSources: false);
                                break;
                            case 1:
                                scratch = acl.MultiplyToNew(scratch, fromOriginalSources: false, scratch);
                                break;
                            default:
                                acl.Increment(scratch, targetOriginal: false, indexOfIncrement: scratch);
                                break;
                        }
                    }

                    acl.InsertEndIfCommand();

                    // write back into original[0]
                    acl.Increment(0, targetOriginal: true, indexOfIncrement: scratch);

                    acl.EndCommandChunk();
                    acl.CompleteCommandList();

                    // execute both paths
                    double[] flatResult = { initialValue };
                    double[] hoistResult = { initialValue };

                    acl.ExecuteAllCommands(flatResult);
                    acl.ExecuteAll(hoistResult, tracing: false);

                    Assert.AreEqual(
                        flatResult[0],
                        hoistResult[0],
                        $"Iteration {iter}, threshold {threshold}: expected {flatResult[0]}, got {hoistResult[0]}");
                }
            }
        }

        #endregion


    }
}
