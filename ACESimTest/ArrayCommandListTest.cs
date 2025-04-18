using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.Util.ArrayProcessing;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
                Debug.WriteLine($"Repeated command index {repeatedCommandIndex}");
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
        public void GateChunkBalancedIfEndIf()
        {
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);
            acl.CompleteCommandList();

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (n.Branches != null && n.Branches.Length > 0) return; // not leaf

                bool hasIf = false;
                bool hasEndIf = false;
                for (int i = n.StoredValue.StartCommandRange;
                         i < n.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) hasIf = true;
                    if (t == ArrayCommandType.EndIf) hasEndIf = true;
                }

                if (hasIf || hasEndIf)
                {
                    if (!(hasIf && hasEndIf))
                        acl.DumpLeafIfUnbalanced(n);          // ← added line

                    Assert.IsTrue(hasIf && hasEndIf,
                        "Leaf contains If without matching EndIf (or vice‑versa)");
                }
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
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);
            acl.CompleteCommandList();

            double[] data1 = new double[200];
            double[] data2 = new double[200];

            // prepare identical initial data (some random numbers)
            for (int i = 0; i < data1.Length; i++) data1[i] = data2[i] = i % 7;

            // path A: interpreter only
            acl.ExecuteAllCommands(data1);

            // path B: hoisted tree (may run compiled chunks)
            acl.ExecuteAll(data2, tracing: false);

            CollectionAssert.AreEqual(data1, data2, "Mismatch after hoist execution");
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

            // dump commands of the first leaf before execution
            var firstLeaf = (NWayTreeStorageInternal<ArrayCommandChunk>)
                            acl.CommandTree.GetBranch(1);
            LogCommandsInRange(acl,
                               firstLeaf.StoredValue.StartCommandRange,
                               firstLeaf.StoredValue.EndCommandRangeExclusive);

            // fresh checkpoint list for execution tracing
            acl.Checkpoints = new List<double>();

            // run once (Debug output shows chunk entry order)
            acl.ExecuteAll(new double[200], tracing: false);

            // after run we expect multiple chunks executed
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

        // 1) structural: every leaf balanced, Conditional nodes created at both depths
        [TestMethod]
        public void NestedHoistStructural()
        {
            var acl = BuildNestedHugeIf(outerBodySize: 40, innerBodySize: 40,
                                        maxPerChunk: 10);

            bool outerGateFound = false, innerGateFound = false;

            acl.CommandTree.WalkTree(nodeObj =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                if (leaf.Branches != null && leaf.Branches.Length > 0) return;

                // count If / EndIf tokens in this leaf
                int ifs = 0, endIfs = 0;
                for (int i = leaf.StoredValue.StartCommandRange;
                         i < leaf.StoredValue.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If) ifs++;
                    if (t == ArrayCommandType.EndIf) endIfs++;
                }

                Assert.IsTrue(ifs == endIfs, "Unbalanced leaf found");

                if (ifs == 1 && endIfs == 1)
                {
                    // this is a gate leaf
                    if (!outerGateFound) outerGateFound = true;
                    else innerGateFound = true;
                }
            });

            Assert.IsTrue(outerGateFound, "Outer Conditional gate not found");
            Assert.IsTrue(innerGateFound, "Inner Conditional gate not found");
        }

        // 2) execution: only the inner‑If body executes; outer body is skipped
        [TestMethod]
        public void NestedHoistExecutionMatches()
        {
            int outerBody = 40, innerBody = 40, maxPerChunk = 10;
            var acl = BuildNestedHugeIf(outerBody, innerBody, maxPerChunk);

            double[] data1 = new double[100];
            acl.ExecuteAll(data1, tracing: false);

            // idx2 is third scratch index => value == innerBody  (outer skipped)
            Assert.AreEqual(innerBody, data1[2], 1e-12);
        }

        /// <summary>
        /// Pure *read‑only* planner: walks the existing tree and records which
        /// leaves need a Conditional gate inserted (because they are oversized
        /// and contain an outer‑level If … EndIf pair).
        ///
        ///   • No tree mutations here.
        ///   • Unit‑test this planner in isolation.
        ///   • Production code will later consume the PlanEntry list and apply the
        ///     mutations in a second pass.
        /// </summary>
        internal sealed class HoistPlanner
        {
            private readonly ArrayCommand[] _cmds;
            private readonly int _max;

            internal record PlanEntry(
                int LeafId,
                int IfIdx,
                int EndIfIdx,
                int BodyLen);

            public HoistPlanner(ArrayCommand[] cmds, int maxCommandsPerChunk)
            {
                _cmds = cmds;
                _max = maxCommandsPerChunk;
            }

            /// <summary>
            /// Return a deterministic list of oversize leaves that need hoisting.
            /// </summary>
            public IList<PlanEntry> BuildPlan(
                NWayTreeStorageInternal<ArrayCommandChunk> root)
            {
                var plan = new List<PlanEntry>();

                root.WalkTree(nodeObj =>
                {
                    var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                    if (leaf.Branches != null && leaf.Branches.Length > 0) return;

                    var info = leaf.StoredValue;
                    int size = info.EndCommandRangeExclusive - info.StartCommandRange;
                    if (size <= _max) return;

                    var (ifIdx, endIfIdx) =
                        FindOutermostIf(info.StartCommandRange, info.EndCommandRangeExclusive);
                    if (ifIdx != -1)
                    {
                        int bodyLen = (endIfIdx - ifIdx) - 1;
                        plan.Add(new PlanEntry(info.ID, ifIdx, endIfIdx, bodyLen));
                    }
                });

                return plan;
            }

            /// <summary>
            /// Scan [_start, _end) and return indices of the first outer‑level
            /// If and its matching EndIf.  Returns (-1,-1) if none.
            /// </summary>
            private (int ifIdx, int endIfIdx) FindOutermostIf(int start, int end)
            {
                int depth = 0;
                for (int i = start; i < end; i++)
                {
                    switch (_cmds[i].CommandType)
                    {
                        case ArrayCommandType.If:
                            if (depth == 0)
                            {
                                // find its matching EndIf
                                int j = i + 1;
                                int d = 1;
                                while (j < end && d > 0)
                                {
                                    if (_cmds[j].CommandType == ArrayCommandType.If) d++;
                                    if (_cmds[j].CommandType == ArrayCommandType.EndIf) d--;
                                    j++;
                                }
                                if (d == 0)
                                    return (i, j - 1); // j stepped past EndIf
                            }
                            depth++;
                            break;

                        case ArrayCommandType.EndIf:
                            depth--;
                            break;
                    }
                }
                return (-1, -1); // none
            }
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

        // --------------------------------------------------------------
        //  HoistMutator  –  pass‑2: mutate the CommandTree according to
        //  the plan produced by HoistPlanner.
        // --------------------------------------------------------------
        internal static class HoistMutator
        {
            /// <summary>
            /// Walk the <paramref name="plan"/> list produced by <see cref="HoistPlanner"/>
            /// and mutate <paramref name="acl.CommandTree"/> in‑place.
            /// </summary>
            public static void ApplyPlan(
                ArrayCommandList acl,
                IList<ArrayCommandListTest.HoistPlanner.PlanEntry> plan)
            {
                var root = (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree;

                foreach (var entry in plan)
                {
                    // 1) locate oversize leaf by ID
                    var leaf = FindLeafById(root, entry.LeafId);
                    if (leaf == null)
                        throw new InvalidOperationException($"Leaf {entry.LeafId} not found");

                    // 2) splice in Conditional gate + child slices
                    ReplaceLeafWithGate(leaf, entry);   // ← acl param removed
                }

                // ⭐ NEW – scrap the old tree and rebuild from the mutated command list
                acl.RebuildCommandTree();
            }

            // ------------------------------------------------------------------
            // helpers
            // ------------------------------------------------------------------

            private static NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>
                FindLeafById(NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> root,
                             int id)
            {
                NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> found = null;

                root.WalkTree(nodeObj =>
                {
                    var n = (NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>)nodeObj;
                    if (n.StoredValue.ID == id)
                        found = n;
                });

                return found;
            }

            // ─────────────────────────────────────────────────────────────────────────────
            //  Replace the oversize LEAF by a Conditional‑gate node, keeping the same
            //  branch‑index it originally occupied in its parent.
            // ─────────────────────────────────────────────────────────────────────────────
            private static void ReplaceLeafWithGate(
                    NWayTreeStorageInternal<ArrayCommandChunk> leaf,
                    HoistPlanner.PlanEntry planEntry)
            {
                var parent = (NWayTreeStorageInternal<ArrayCommandChunk>)leaf.Parent;
                if (parent == null)
                    throw new InvalidOperationException("root cannot be replaced");

                /* ------------------------------------------------------------------
                   1.  Determine which branch‑slot in the parent the old leaf used.
                       We keep that slot number for the new gate to avoid gaps.
                ------------------------------------------------------------------ */
                byte leafBranchId = 0;
                for (byte i = 1; i <= parent.StoredValue.LastChild; i++)
                {
                    if (ReferenceEquals(parent.GetBranch(i), leaf))
                    {
                        leafBranchId = i;
                        break;
                    }
                }
                if (leafBranchId == 0)
                    throw new InvalidOperationException("leaf not found in parent.Branches");

                /* ------------------------------------------------------------------
                   2.  Create the Conditional gate node that will *replace* the leaf
                ------------------------------------------------------------------ */
                var gateNode = new NWayTreeStorageInternal<ArrayCommandChunk>(parent);
                gateNode.StoredValue = new ArrayCommandChunk
                {
                    Name = "Conditional",
                    StartCommandRange = leaf.StoredValue.StartCommandRange,
                    EndCommandRangeExclusive = leaf.StoredValue.EndCommandRangeExclusive,
                    StartSourceIndices = leaf.StoredValue.StartSourceIndices,
                    EndSourceIndicesExclusive = leaf.StoredValue.EndSourceIndicesExclusive,
                    StartDestinationIndices = leaf.StoredValue.StartDestinationIndices,
                    EndDestinationIndicesExclusive = leaf.StoredValue.EndDestinationIndicesExclusive,
                    ChildrenParallelizable = false
                };

                /* ------------------------------------------------------------------
                   3.  Slice the If‑body into ≤MaxCommandsPerChunk children.
                       Each slice becomes a child‑leaf of the new gate.
                ------------------------------------------------------------------ */
                int max = leaf.StoredValue.VirtualStack?.Length > 0
                          ? leaf.StoredValue.VirtualStack.Length   // keep same stack size
                          : int.MaxValue;

                int bodyStart = planEntry.IfIdx + 1;
                int bodyEnd = planEntry.EndIfIdx;      // EndIf index *not* included

                byte childId = 1;
                for (int sliceStart = bodyStart; sliceStart < bodyEnd;)
                {
                    int sliceEnd = Math.Min(sliceStart + max, bodyEnd);

                    var childLeaf = new NWayTreeStorageInternal<ArrayCommandChunk>(gateNode);
                    childLeaf.StoredValue = new ArrayCommandChunk
                    {
                        StartCommandRange = sliceStart,
                        EndCommandRangeExclusive = sliceEnd,
                        ChildrenParallelizable = false
                    };

                    gateNode.SetBranch(childId++, childLeaf);
                    sliceStart = sliceEnd;
                }
                gateNode.StoredValue.LastChild = (byte)(childId - 1);

                /* ------------------------------------------------------------------
                   4.  Replace the old leaf in its parent *at the very same slot*.
                       No other parent metadata needs changing.
                ------------------------------------------------------------------ */
                parent.SetBranch(leafBranchId, gateNode);
            }




            /// Helper to create a new node with minimal metadata
            private static NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>
                NewNode(NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> parent,
                        string name, int startCmd, int endCmd)
            {
                var node = new NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk>(parent);
                node.StoredValue = new ArrayCommandList.ArrayCommandChunk
                {
                    ID = ArrayCommandList.ArrayCommandChunk.NextID++,
                    Name = name,
                    StartCommandRange = startCmd,
                    EndCommandRangeExclusive = endCmd
                };
                return node;
            }

            /// Locate which branch index in <parent> points to <child>
            private static byte FindSlotInParent(
                NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> parent,
                NWayTreeStorageInternal<ArrayCommandList.ArrayCommandChunk> child)
            {
                for (byte i = 0; i <= parent.StoredValue.LastChild; i++)
                    if (parent.GetBranch(i) == child)
                        return i;

                throw new InvalidOperationException("child not found in parent branches");
            }
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



        #endregion


    }
}
