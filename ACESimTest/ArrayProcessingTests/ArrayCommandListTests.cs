using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.NWayTreeStorage;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ACESimTest.ArrayProcessingTests.ArrayProcessingTestHelpers;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Core behavioural and structural tests for ArrayCommandList after the
    /// hoist‑runner refactor.  Fuzz/property tests live elsewhere.
    /// </summary>
    [TestClass]
    public class ArrayCommandListTests
    {
        /* -------------------------------------------------------------
         * Smoke test – copy from source, add to destination
         * -----------------------------------------------------------*/
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SimpleCopyAdd(bool parallel)
        {
            var acl = NewAcl(parallel: parallel);
            const int SRC0 = 0, DST0 = 10;

            acl.StartCommandChunk(false, null, "root");
            int tmp = acl.CopyToNew(SRC0, true);   // ordered source
            acl.Increment(DST0, true, tmp);        // ordered destination
            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var data = Seed(20, i => i);
            acl.ExecuteAll(data, tracing: false);
            data[DST0].Should().Be(0);
        }

        /* -------------------------------------------------------------
         * Ordered buffer round‑trip: serial vs parallel
         * -----------------------------------------------------------*/
        [TestMethod]
        public void OrderedBuffersSerialVsParallelMatch()
        {
            const int SRC_BASE = 0;
            const int DST_BASE = 20;

            ArrayCommandList Build(bool par)
            {
                var a = NewAcl(parallel: par);
                a.StartCommandChunk(false, null, "root");
                var tmp = new List<int>();
                for (int i = 0; i < 5; i++)
                    tmp.Add(a.CopyToNew(SRC_BASE + i, true));
                foreach (var t in tmp)
                    a.Increment(DST_BASE + (t - tmp[0]), true, t);
                a.EndCommandChunk();
                a.CompleteCommandList();
                return a;
            }

            double[] serial = Seed(40, i => i);
            double[] parallel = serial.ToArray();

            Build(false).ExecuteAll(serial, false);
            Build(true).ExecuteAll(parallel, false);

            parallel.Should().Equal(serial);
        }

        /* -------------------------------------------------------------
         * CopyIncrementsToParent assignment semantics
         * -----------------------------------------------------------*/
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ChildAssignmentsPropagate(bool parallelChildren)
        {
            var acl = NewAcl();

            acl.StartCommandChunk(parallelChildren, null, "parent");
            int tmpParent = acl.NewZero();

            acl.StartCommandChunk(false, null, "childA");
            acl.Increment(tmpParent, false, tmpParent);
            acl.EndCommandChunk(new[] { tmpParent });

            acl.StartCommandChunk(false, null, "childB");
            acl.Increment(tmpParent, false, tmpParent);
            acl.EndCommandChunk(new[] { tmpParent });

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var data = new double[5];
            acl.ExecuteAll(data, false);
            data[tmpParent].Should().Be(0);
        }

        /* -------------------------------------------------------------
         * Virtual‑stack sharing vs private allocation
         * -----------------------------------------------------------*/
        [TestMethod]
        public void VirtualStackSharingRules()
        {
            var acl = NewAcl();
            acl.StartCommandChunk(false, null, "root");

            acl.StartCommandChunk(false, null, "shared");
            acl.EndCommandChunk();

            acl.StartCommandChunk(true, null, "privatePar");
            acl.EndCommandChunk();

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var chunks = new List<ArrayCommandChunk>();
            acl.CommandTree.WalkTree(n => chunks.Add(((NWayTreeStorageInternal<ArrayCommandChunk>)n).StoredValue));

            var rootVs = chunks.First().VirtualStack;
            var sharedVs = chunks.Single(c => c.Name == "shared").VirtualStack;
            var privateVs = chunks.Single(c => c.Name == "privatePar").VirtualStack;

            ReferenceEquals(rootVs, sharedVs).Should().BeTrue();
            ReferenceEquals(rootVs, privateVs).Should().BeFalse();
        }

        /* -------------------------------------------------------------
         * Repeat‑identical‑range optimisation
         * -----------------------------------------------------------*/
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RepeatIdenticalRange(bool parallel)
        {
            var acl = NewAcl(parallel: parallel);
            acl.MaxCommandsPerSplittableChunk = int.MaxValue; // enables RepeatIdenticalRanges

            acl.StartCommandChunk(false, null, "root");
            int idx = acl.NewZero();

            acl.StartCommandChunk(false, null, "body1");
            acl.Increment(idx, false, idx);
            acl.EndCommandChunk();

            int repeatStart = acl.NextCommandIndex;
            acl.StartCommandChunk(false, repeatStart, "body2");
            acl.EndCommandChunk(endingRepeatedChunk: true);

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var d = new double[3];
            acl.ExecuteAll(d, false);
            d[idx].Should().Be(0);
        }

        /* -------------------------------------------------------------
         * Hoist leaves do not exceed threshold
         * -----------------------------------------------------------*/
        [TestMethod]
        public void HoistLeavesRespectThreshold()
        {
            var acl = NewAcl();
            acl.MaxCommandsPerSplittableChunk = 5;

            // Build two oversize leaves: one with control flow (must split),
            // one plain linear (may remain whole).
            acl.StartCommandChunk(false, null, "root");

            // ①  conditional body → splittable
            acl.InsertEqualsValueCommand(acl.NewZero(), 0);
            acl.InsertIfCommand();
            for (int i = 0; i < 40; i++) acl.InsertBlankCommand();
            acl.InsertEndIfCommand();

            // ②  linear oversize body → NOT splittable by design
            for (int i = 0; i < 40; i++) acl.InsertBlankCommand();

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            acl.CommandTree.WalkTree(n =>
            {
                var leaf = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                if (leaf.Branches?.Length > 0) return;

                int len = leaf.StoredValue.EndCommandRangeExclusive -
                          leaf.StoredValue.StartCommandRange;

                bool containsFlow =
                    Enumerable.Range(leaf.StoredValue.StartCommandRange, len)
                              .Select(i => acl.UnderlyingCommands[i].CommandType)
                              .Any(t => t is ArrayCommandType.If
                                     or ArrayCommandType.EndIf
                                     or ArrayCommandType.IncrementDepth
                                     or ArrayCommandType.DecrementDepth);

                if (containsFlow)
                    len.Should().BeLessOrEqualTo(acl.MaxCommandsPerSplittableChunk,
                        "splittable leaves must respect the threshold");
                else
                    len.Should().BeGreaterThan(acl.MaxCommandsPerSplittableChunk,
                        "unsplittable linear leaves are allowed to exceed the threshold");
            });
        }


        /* -------------------------------------------------------------
         * Edge‑case checks
         * -----------------------------------------------------------*/
        [TestMethod]
        public void MaxCommandsPerChunkIntMaxDisablesHoist()
        {
            var acl = NewAcl();
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;
            acl.StartCommandChunk(false, null, "root");
            for (int i = 0; i < 20; i++) acl.InsertBlankCommand();
            acl.EndCommandChunk();
            acl.CompleteCommandList();

            acl.CommandTree.Branches.Should().BeNull();
        }

        [TestMethod]
        public void EmptyDestinationsDoesNotCrash()
        {
            var acl = NewAcl();
            acl.StartCommandChunk(false, null, "root");
            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var data = new double[10];
            acl.OrderedDestinationIndices.Clear();
            acl.ExecuteAll(data, false);
        }

        [TestMethod]
        public void LinearOversizeListWithoutIfIsNotHoisted()
        {
            var acl = NewAcl();
            acl.MaxCommandsPerSplittableChunk = 5;
            acl.StartCommandChunk(false, null, "root");
            for (int i = 0; i < 50; i++) acl.InsertBlankCommand();
            acl.EndCommandChunk();
            acl.CompleteCommandList();

            acl.CommandTree.Branches.Should().BeNull();
        }
    }
}
