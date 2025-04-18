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

        // Paste inside your test class ------------------------------------------------
        [TestMethod]
        public void HoistCreatesConditionalNode()
        {
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);  // > threshold (10)
            acl.CompleteCommandList();                         // triggers hoist

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
                    Assert.IsTrue(hasIf && hasEndIf,
                        "Leaf contains If without matching EndIf (or vice‑versa)");
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
        private static ArrayCommandList BuildSimpleACLWithHugeIf(int bodySize)
        {
            var acl = new ArrayCommandList(maxNumCommands: 10_000,
                                           initialArrayIndex: 0,
                                           parallelize: false)
            {
                MaxCommandsPerChunk = 10,      // tiny threshold for tests
                UseCheckpoints = true
            };

            /* ---------- OPEN CHUNK --------------------------------------- */
            acl.StartCommandChunk(runChildrenInParallel: false,
                                  identicalStartCommandRange: null,
                                  name: "test");

            int idx0 = acl.NewZero();
            int idx1 = acl.NewZero();
            int idx2 = acl.NewZero();

            acl.InsertNotEqualsOtherArrayIndexCommand(idx0, idx1);  // condition true
            acl.InsertIfCommand();

            for (int i = 0; i < bodySize; i++)
                acl.Increment(idx2, targetOriginal: false, idx0);

            acl.InsertEndIfCommand();

            /* ---------- CLOSE CHUNK -------------------------------------- */
            acl.EndCommandChunk();

            acl.CompleteCommandList();          // now hoist pass will run
            return acl;
        }



        [TestMethod]
        public void TraceSiblingExecutionOrder()
        {
            // build a tiny command list that forces hoisting (threshold = 10)
            var acl = BuildSimpleACLWithHugeIf(bodySize: 25);   // helper from earlier

            // ensure checkpoints infrastructure is active without relying on Roslyn
            acl.Checkpoints = new List<double>();               // fresh empty list

            // run once – during execution each leaf / conditional chunk records its ExecId
            acl.ExecuteAll(new double[200], tracing: false);

            // Sanity check: we should have recorded at least two chunk executions
            Assert.IsTrue(acl.Checkpoints.Count >= 2,
                          "Expected at least two chunks to execute.");

            // Build a readable arrow sequence from the numeric ExecIds
            string seq = string.Join(" → ", acl.Checkpoints.Select(d => d.ToString("0")));
            Console.WriteLine($"EXECUTION ORDER: {seq}");

            // First executed chunk should be a leaf (ExecId >= 0),
            // second executed chunk should be the Conditional node (ExecId < 0).
            Assert.IsTrue(acl.Checkpoints[0] >= 0,
                $"First chunk executed was not a leaf.  Order: {seq}");
            Assert.IsTrue(acl.Checkpoints[1] < 0,
                $"Second chunk executed was not the conditional node.  Order: {seq}");
        }



    }
}
