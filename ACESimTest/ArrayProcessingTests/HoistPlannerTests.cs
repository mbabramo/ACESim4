using System;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class HoistPlannerTests
    {
        //------------------------------------------------------------------
        //  Test‑level hooks & helpers
        //------------------------------------------------------------------
        [TestInitialize]
        public void ResetIds() => ArrayCommandChunk.NextID = 0;

        /// <summary>
        /// Builds an <see cref="ArrayCommandList"/> containing **one real leaf**
        /// called <c>TestLeaf</c>.  The caller supplies <paramref name="script"/>
        /// that records commands into that leaf.  We also ensure <c>MaxArrayIndex</c>
        /// is set high enough so that <see cref="SetupVirtualStack"/> can allocate
        /// a non‑zero sized stack even when the script only touches “existing”
        /// array slots (index 0, 1, 2, …).
        /// </summary>
        private static ArrayCommandList BuildAcl(Action<CommandRecorder> script, int maxIndex = 10)
        {
            const int BufferSize = 512;
            var acl = new ArrayCommandList(BufferSize, initialArrayIndex: 0, parallelize: false)
            {
                // Disable built‑in hoist so the planner is the only splitter
                MaxCommandsPerChunk = int.MaxValue,
                MaxArrayIndex = maxIndex          // allocate virtual stack
            };

            var rec = acl.Recorder;

            // Single leaf so we know its Chunk‑ID deterministically = 1
            rec.StartCommandChunk(runChildrenParallel: false,
                                  identicalStartCmdRange: null,
                                  name: "TestLeaf");
            script(rec);
            rec.EndCommandChunk();

            acl.CompleteCommandList(); // root + single leaf (ids: 0 & 1)
            return acl;
        }

        //------------------------------------------------------------------
        //  Unit tests for HoistPlanner
        //------------------------------------------------------------------

        [TestMethod]
        public void Planner_ReturnsSingleOversize()
        {
            int limit = 3;
            var acl = BuildAcl(rec =>
            {
                rec.InsertIf();
                rec.ZeroExisting(0);
                rec.ZeroExisting(1);
                rec.ZeroExisting(2); // body length 3 > limit
                rec.InsertEndIf();
            });

            var planner = new HoistPlanner(acl.UnderlyingCommands, limit);
            var plan = planner.BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            plan.Should().HaveCount(1);
            var entry = plan.Single();
            entry.LeafId.Should().Be(1);
            entry.BodyLen.Should().BeGreaterOrEqualTo(limit);
            entry.IfIdx.Should().Be(0);
            entry.EndIfIdx.Should().Be(4);
        }

        [TestMethod]
        public void Planner_IgnoresOversizeWithoutIf()
        {
            int limit = 3;
            var acl = BuildAcl(rec =>
            {
                rec.ZeroExisting(0);
                rec.ZeroExisting(1);
                rec.ZeroExisting(2);
                rec.ZeroExisting(3);
            });

            var planner = new HoistPlanner(acl.UnderlyingCommands, limit);
            var plan = planner.BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            plan.Should().BeEmpty();
        }

        [TestMethod]
        public void Planner_MultipleOversizeLeaves()
        {
            int limit = 2;
            const int BufferSize = 512;
            var acl = new ArrayCommandList(BufferSize, 0, false)
            {
                MaxCommandsPerChunk = int.MaxValue,
                MaxArrayIndex = 10
            };
            var rec = acl.Recorder;

            // Leaf1 – oversize with If
            rec.StartCommandChunk(false, null, "Leaf1");
            rec.InsertIf();
            rec.ZeroExisting(0);
            rec.ZeroExisting(1);
            rec.InsertEndIf();
            rec.EndCommandChunk();

            // Leaf2 – oversize with If (longer)
            rec.StartCommandChunk(false, null, "Leaf2");
            rec.InsertIf();
            rec.ZeroExisting(0);
            rec.ZeroExisting(1);
            rec.ZeroExisting(2);
            rec.InsertEndIf();
            rec.EndCommandChunk();

            acl.MaxArrayIndex = 10; // ensure stack size
            acl.CompleteCommandList();

            var planner = new HoistPlanner(acl.UnderlyingCommands, limit);
            var plan = planner.BuildPlan((NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            plan.Should().HaveCount(2);
            plan.Select(p => p.LeafId).Should().BeInAscendingOrder();
            plan.Select(p => p.BodyLen).Should().OnlyContain(len => len >= limit);
        }

        [TestMethod]
        public void Planner_PicksInnermost_WhenNestedOversize()
        {
            // Threshold 2 → every body of length 4 is oversize
            const int LIMIT = 2;
            var acl = BuildAcl(rec =>
            {
                rec.InsertEqualsValueCommand(0, 0); rec.InsertIf();           // outer (0..)
                rec.InsertEqualsValueCommand(0, 0); rec.InsertIf();           // middle
                rec.InsertEqualsValueCommand(0, 0); rec.InsertIf();           // inner
                for (int i = 0; i < 4; i++) rec.Increment(0, false, 0);       // body len 4
                rec.InsertEndIf(); rec.InsertEndIf(); rec.InsertEndIf();      // close all
            });

            var planner = new HoistPlanner(acl.UnderlyingCommands, LIMIT);
            var plan = planner.BuildPlan(
                (NWayTreeStorageInternal<ArrayCommandChunk>)acl.CommandTree);

            // We expect EXACTLY ONE plan entry and it must point at the innermost If
            plan.Should().HaveCount(1, "only the deepest oversize block should be hoisted");
            var entry = plan.Single();

            entry.LeafId.Should().Be(1, "all commands are in the single real leaf");
            entry.BodyLen.Should().Be(4);
            entry.IfIdx.Should().Be(5);
            entry.EndIfIdx.Should().Be(10);

        }

    }
}
