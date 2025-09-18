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

            // Each repeated identical range must run at its own depth so scratch indices match.
            acl.IncrementDepth();
            int body1Start = acl.NextCommandIndex; // start of the range to repeat
            acl.StartCommandChunk(false, null, "body1");
            acl.Increment(idx, false, idx);
            acl.EndCommandChunk();
            acl.DecrementDepth();

            acl.IncrementDepth();
            acl.StartCommandChunk(false, body1Start, "body2"); // repeat body1
            acl.EndCommandChunk(endingRepeatedChunk: true);
            acl.DecrementDepth();

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList();

            // Only need to seed the "original" data slots, not the full stack
            var d = new double[acl.SizeOfMainData];

            acl.CompileAndRunOnce(d, parallel);

            // Inspect results in VirtualStack, which is where execution occurred
            acl.VirtualStack[idx].Should().Be(0);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RepeatIdenticalRange_AddsValues(bool parallel)
        {
            var acl = NewAcl();
            acl.UseOrderedSourcesAndDestinations = true; // ensure CopyToNew(..., true) emits NextSource for identical-range repeats
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;

            acl.StartCommandChunk(false, null, "root");

            int total = acl.NewZero();

            // Original body: NextSource → add into 'total'
            acl.IncrementDepth();
            int bodyStart = acl.NextCommandIndex;
            acl.StartCommandChunk(false, null, "body1");
            int tmp1 = acl.CopyToNew(0, true);   // queues source index 0; emits NextSource
            acl.Increment(total, false, tmp1);   // emits IncrementBy
            acl.EndCommandChunk();
            acl.DecrementDepth();

            // Repeat body: must re-emit the SAME commands so the recorder can verify/advance.
            // Only the ordered *source index* changes (1 instead of 0); the commands remain identical.
            acl.IncrementDepth();
            acl.StartCommandChunk(false, bodyStart, "body2");
            int tmp2 = acl.CopyToNew(1, true);   // queues source index 1; emits the same NextSource command
            acl.Increment(total, false, tmp2);   // emits the same IncrementBy command
            acl.EndCommandChunk(endingRepeatedChunk: true);
            acl.DecrementDepth();

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList();

            // Seed original data with two values that should be summed: 3 + 7 = 10
            var data = new double[] { 3, 7 };
            acl.CompileAndRunOnce(data, parallel);

            // Execution writes to acl.VirtualStack (not 'data'), so assert there.
            acl.VirtualStack[total].Should().Be(10);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RepeatIdenticalRange_MultipleIterations_SumsSources(bool parallel)
        {
            var acl = NewAcl();
            acl.UseOrderedSourcesAndDestinations = true; // identical-range repeats rely on ordered-source mode
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;
            acl.StartCommandChunk(false, null, "root");

            int total = acl.NewZero();

            // Build the body once (will be repeated)
            acl.IncrementDepth();
            int bodyStart = acl.NextCommandIndex;
            acl.StartCommandChunk(false, null, "body1");
            int tmp = acl.CopyToNew(0, true);     // queues source index; emits NextSource
            acl.Increment(total, false, tmp);     // emits IncrementBy
            acl.EndCommandChunk();
            acl.DecrementDepth();

            // Repeat the same body for remaining sources (re-emitting the SAME commands each time)
            int repeats = 3; // total iterations = 1 original + 3 repeats = 4 sources
            for (int i = 1; i <= repeats; i++)
            {
                acl.IncrementDepth();
                acl.StartCommandChunk(false, bodyStart, $"body{i + 1}");
                int tmp2 = acl.CopyToNew(i, true);  // different ordered source, same NextSource command
                acl.Increment(total, false, tmp2);  // same IncrementBy command
                acl.EndCommandChunk(endingRepeatedChunk: true);
                acl.DecrementDepth();
            }

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList();

            var data = new double[] { 1, 2, 3, 4 }; // sum = 10
            acl.CompileAndRunOnce(data, parallel);

            acl.VirtualStack[total].Should().Be(10);
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
