using System;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandType;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class EmitterModeAndIoAndDepthScopeTests
    {
        /// <summary>
        /// Recording a read from original sources in ordered-IO mode should:
        ///  - emit NextSource (not CopyTo),
        ///  - append the chosen original index to OrderedSourceIndices.
        /// </summary>
        [TestMethod]
        public void Recording_CopyToNew_FromOriginal_EmitsNextSource_And_AppendsOrderedSource()
        {
            var acl = new ArrayCommandList(maxNumCommands: 8, initialArrayIndex: 0);
            acl.UseOrderedSourcesAndDestinations = true; // ordered IO on  :contentReference[oaicite:4]{index=4}
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;

            acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: null, name: "root");
            int vs = acl.CopyToNew(sourceIdx: 5, fromOriginalSources: true);
            acl.EndCommandChunk();
            acl.CompleteCommandList(); // verifies tree + ordered IO accounting  :contentReference[oaicite:5]{index=5}

            int end = acl.CommandTree.StoredValue.EndCommandRangeExclusive; // safe command-count bound
            var cmds = acl.UnderlyingCommands.Take(end).ToArray();

            // Exactly one command, and it is NextSource to 'vs'
            cmds.Should().HaveCount(1);
            cmds[0].CommandType.Should().Be(NextSource);
            cmds[0].Index.Should().Be(vs);

            // Ordered IO side-effect recorded
            acl.OrderedSourceIndices.Should().Equal(5.Os());
        }

        /// <summary>
        /// Replaying an identical range must:
        ///  - verify against recorded commands (no new writes to buffer),
        ///  - still append ordered-source indices for each replayed NextSource.
        ///  Here we change the source index (0 -> 1) between original and replay;
        ///  only OrderedSourceIndices should differ, not the recorded opcode sequence.
        /// </summary>
        [TestMethod]
        public void Replay_CopyToNew_WithDifferentOriginalSource_AppendsOrderedSource_ButDoesNotWriteNewCommands()
        {
            var acl = new ArrayCommandList(maxNumCommands: 32, initialArrayIndex: 0);
            acl.UseOrderedSourcesAndDestinations = true; // to prefer NextSource shape  :contentReference[oaicite:6]{index=6}
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;

            acl.StartCommandChunk(false, null, "root");

            // Original body (record mode)
            acl.IncrementDepth();
            int bodyStart = acl.NextCommandIndex; // start of the slice we will replay  :contentReference[oaicite:7]{index=7}
            acl.StartCommandChunk(false, null, "body1");
            int tmp1 = acl.CopyToNew(0, fromOriginalSources: true); // emit NextSource + OrderedSourceIndices.Add(0)
            acl.EndCommandChunk();
            acl.DecrementDepth();

            // Replay body with *different* ordered source (replay mode)
            acl.IncrementDepth();
            acl.StartCommandChunk(false, bodyStart, "body2"); // marks ACL as "repeating existing command range"  :contentReference[oaicite:8]{index=8}
            int tmp2 = acl.CopyToNew(1, fromOriginalSources: true); // should NOT write a new command; should append OrderedSourceIndices.Add(1)
            acl.EndCommandChunk(endingRepeatedChunk: true);
            acl.DecrementDepth();

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList(); // strong accounting: leaf metadata vs. ordered lists  :contentReference[oaicite:9]{index=9}

            // 1) We recorded 2 ordered-source indices (original=0, replay=1)
            acl.OrderedSourceIndices.Should().Equal(0.Os(), 1.Os());

            // 2) Only the original slice is physically present in the buffer;
            //    count NextSource in the concrete [0, end) command range.
            int end = acl.CommandTree.StoredValue.EndCommandRangeExclusive;
            int nextSourceCount = 0;
            for (int i = 0; i < end; i++)
                if (acl.UnderlyingCommands[i].CommandType == NextSource) nextSourceCount++;

            nextSourceCount.Should().Be(1, "replay verifies recorded commands without duplicating them");
        }

        /// <summary>
        /// Incrementing an original destination in ordered-IO mode should:
        ///  - route to NextDestination (not IncrementBy),
        ///  - append to OrderedDestinationIndices.
        /// Replaying the same slice with a *different* original destination index must
        /// keep the recorded opcode but append the new destination index.
        /// </summary>
        [TestMethod]
        public void RecordingAndReplay_Increment_ToOriginal_UsesNextDestination_And_AppendsOrderedDestinations()
        {
            var acl = new ArrayCommandList(maxNumCommands: 32, initialArrayIndex: 0);
            acl.UseOrderedSourcesAndDestinations = true; // required to emit NextDestination  :contentReference[oaicite:10]{index=10}
            acl.MaxCommandsPerSplittableChunk = int.MaxValue;

            acl.StartCommandChunk(false, null, "root");

            // Make a VS value to add (value doesn't matter—this is a shape test)
            int val = acl.NewZero();

            // Original slice (recording): should append dest=0 and emit NextDestination  :contentReference[oaicite:11]{index=11}
            acl.IncrementDepth();
            int bodyStart = acl.NextCommandIndex;
            acl.StartCommandChunk(false, null, "body1");
            acl.Increment(idx: 0, targetOriginal: true, indexOfIncrement: val);
            acl.EndCommandChunk();
            acl.DecrementDepth();

            // Replay slice with a *different* original destination (dest=1)
            acl.IncrementDepth();
            acl.StartCommandChunk(false, bodyStart, "body2"); // enter replay  :contentReference[oaicite:12]{index=12}
            acl.Increment(idx: 1, targetOriginal: true, indexOfIncrement: val);
            acl.EndCommandChunk(endingRepeatedChunk: true);
            acl.DecrementDepth();

            acl.EndCommandChunk(); // root
            acl.CompleteCommandList(); // ensures ordered-destination counts match leaf metadata  :contentReference[oaicite:13]{index=13}

            // Ordered destinations contain both indices (record + replay)
            acl.OrderedDestinationIndices.Should().Equal(0.Od(), 1.Od());

            // Underlying buffer contains a single concrete NextDestination opcode (the recorded one)
            int end = acl.CommandTree.StoredValue.EndCommandRangeExclusive;
            int nextDestCount = 0;
            for (int i = 0; i < end; i++)
                if (acl.UnderlyingCommands[i].CommandType == NextDestination) nextDestCount++;

            nextDestCount.Should().Be(1);
        }

        /// <summary>
        /// RAII depth scopes must pair IncrementDepth/DecrementDepth and rewind
        /// scratch to the entry pointer on dispose (with defaults that allow rewinds).
        /// </summary>
        [TestMethod]
        public void DepthScope_PairsDepthCommands_And_RewindsScratch()
        {
            var acl = new ArrayCommandList(maxNumCommands: 16, initialArrayIndex: 0);
            var rec = acl.Recorder;

            int nextBefore = rec.NextArrayIndex;

            using (rec.OpenDepthScope()) // emits IncrementDepth now; will DecrementDepth at Dispose  :contentReference[oaicite:14]{index=14}
            {
                // allocate one temp to prove the rewind occurs
                int tmp = rec.NewZero();
                tmp.Should().Be(nextBefore, "new temp should be allocated at the entry scratch pointer");
            }

            // After Dispose, scratch should be rewound to 'nextBefore' (given defaults)  :contentReference[oaicite:15]{index=15}
            rec.NextArrayIndex.Should().Be(nextBefore);

            // Also verify the concrete opcode triplet is present at the start of the stream
            int endCmd = Math.Max(3, acl.CommandTree?.StoredValue.EndCommandRangeExclusive ?? rec.NextCommandIndex);
            var first3 = acl.UnderlyingCommands.Take(endCmd).ToArray();
            first3[0].CommandType.Should().Be(IncrementDepth);
            first3[1].CommandType.Should().Be(Zero);
            first3[2].CommandType.Should().Be(DecrementDepth);
        }
    }
}
