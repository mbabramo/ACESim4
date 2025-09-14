using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.NWayTreeStorage;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
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
         * Repeat‑identical‑range optimisation
         * -----------------------------------------------------------*/
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RepeatIdenticalRange(bool parallel)
        {
            var acl = NewAcl();
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;

            acl.StartCommandChunk(false, null, "root");
            int idx = acl.NewZero();

            int body1Start = acl.NextCommandIndex;   // <-- start of the range we intend to repeat
            acl.StartCommandChunk(false, null, "body1");
            acl.Increment(idx, false, idx);
            acl.EndCommandChunk();

            acl.StartCommandChunk(false, body1Start, "body2");   // <-- repeat body1
            acl.EndCommandChunk(endingRepeatedChunk: true);

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList();

            var d = new double[3];
            acl.CompileAndRunOnce(d, false);
            d[idx].Should().Be(0);
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

        /* -------------------------------------------------------------
 * Additional structural & behavioural safety-net tests
 * -----------------------------------------------------------*/
        [TestMethod]
        public void GapAndTailBranchInsertion()
        {
            var acl = NewAcl();
            acl.StartCommandChunk(false, null, "root");
            acl.Recorder.InsertBlankCommands(2);                 // gap
            acl.StartCommandChunk(false, null, "inner");
            acl.Recorder.InsertBlankCommands(2);
            acl.EndCommandChunk();
            acl.Recorder.InsertBlankCommands(2);                 // tail
            acl.EndCommandChunk();
            acl.CompleteCommandList();

            var root = acl.CommandTree;
            root.Branches.Should().NotBeNull();
            root.Branches.Length.Should().Be(3);

            root.Branches[0].StoredValue.StartCommandRange.Should().Be(0);
            root.Branches[0].StoredValue.EndCommandRangeExclusive.Should().Be(2);

            root.Branches[1].StoredValue.Name.Should().Be("inner");

            root.Branches[2].StoredValue.StartCommandRange.Should().Be(4);
            root.Branches[2].StoredValue.EndCommandRangeExclusive.Should().Be(6);
        }

        [TestMethod]
        public void KeepTogetherSuppressesNestedChunks()
        {
            var acl = NewAcl();
            acl.KeepCommandsTogether();
            acl.StartCommandChunk(false, null, "solo");
            acl.Recorder.InsertBlankCommands(3);
            acl.EndCommandChunk();
            acl.EndKeepCommandsTogether();
            acl.CompleteCommandList();

            acl.CommandTree.Branches.Should().BeNull();
        }


    }
}
