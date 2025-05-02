using System;
using System.Collections.Generic;
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
        private const int Max = 5;

        // ───────────────────────────────────────── helpers ────────────────────────────────────────
        private static ArrayCommandList BlankLeaf(int len, int maxPerChunk)
        {
            return ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                rec => rec.InsertBlankCommands(len),
                maxNumCommands: len + 2,
                maxCommandsPerChunk: maxPerChunk);
        }

        private static (ArrayCommandList acl, int ifIdx, int endIfIdx) BuildOversizeIf(int bodyLen, int maxPerChunk)
        {
            var (acl, _) = ArrayProcessingTestHelpers.MakeOversizeIfBody(bodyLen, maxPerChunk);
            int ifIdx = Array.FindIndex(acl.UnderlyingCommands, c => c.CommandType == ArrayCommandType.If);
            int endIfIdx = Array.FindIndex(acl.UnderlyingCommands, c => c.CommandType == ArrayCommandType.EndIf);
            return (acl, ifIdx, endIfIdx);
        }

        private static (ArrayCommandList acl, int incIdx, int decIdx) BuildOversizeDepthRegion(int regionLen, int maxPerChunk)
        {
            var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                rec =>
                {
                    rec.InsertIncrementDepthCommand();
                    rec.InsertBlankCommands(regionLen);
                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: regionLen + 4,
                maxCommandsPerChunk: maxPerChunk);

            int incIdx = Array.FindIndex(acl.UnderlyingCommands, c => c.CommandType == ArrayCommandType.IncrementDepth);
            int decIdx = Array.FindIndex(acl.UnderlyingCommands, c => c.CommandType == ArrayCommandType.DecrementDepth);
            return (acl, incIdx, decIdx);
        }

        private static IList<HoistPlanner.PlanEntry> Plan(ArrayCommandList acl, int max)
            => new HoistPlanner(acl.UnderlyingCommands, max).BuildPlan(acl.CommandTree);

        // ───────────────────────────────────────── baseline ───────────────────────────────────────
        [DataTestMethod]
        [DataRow(4)]
        [DataRow(Max)]
        public void Baseline_LengthLessOrEqual_Max_YieldsEmptyPlan(int length)
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = BlankLeaf(length, Max);
                Plan(acl, Max).Should().BeEmpty();
            });
        }

        [TestMethod]
        public void Pathological_LinearOversizeLeaf_YieldsEmptyPlan()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = BlankLeaf(Max + 3, Max);
                Plan(acl, Max).Should().BeEmpty();
            });
        }

        // ────────────────────────────────── conditional slicing ───────────────────────────────────
        [TestMethod]
        public void Conditional_SingleOversizeBody_ReturnsOneEntry()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                int bodyLen = Max + 1;
                var (acl, ifIdx, endIfIdx) = BuildOversizeIf(bodyLen, Max);
                var plan = Plan(acl, Max);
                plan.Should().HaveCount(1);
                var entry = plan.Single();
                entry.Kind.Should().Be(HoistPlanner.SplitKind.Conditional);
                entry.StartIdx.Should().Be(ifIdx);
                entry.EndIdxExclusive.Should().Be(endIfIdx + 1);
            });
        }

        [TestMethod]
        public void Conditional_TwoNonOverlappingBodies_ReturnsTwoSortedEntries()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx0 = rec.NewZero();

                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertEndIf();

                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 2);
                    rec.InsertEndIf();
                },
                maxNumCommands: 30,
                maxCommandsPerChunk: Max);

                var plan = Plan(acl, Max).ToList();
                plan.Should().HaveCount(2)
                     .And.BeInAscendingOrder(p => p.StartIdx)
                     .And.AllSatisfy(e => e.Kind.Should().Be(HoistPlanner.SplitKind.Conditional));
            });
        }

        [TestMethod]
        public void Conditional_NestedOversizeBodies_ReturnsInnerOnly()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx = rec.NewZero();
                    rec.InsertEqualsValueCommand(idx, 0);
                    rec.InsertIf();

                    rec.InsertEqualsValueCommand(idx, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertEndIf();

                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertEndIf();
                },
                maxNumCommands: 40,
                maxCommandsPerChunk: Max);

                var plan = Plan(acl, Max);
                plan.Should().HaveCount(1);
                plan.Single().Kind.Should().Be(HoistPlanner.SplitKind.Conditional);
            });
        }

        [TestMethod]
        public void Conditional_BodyLengthBelowThreshold_YieldsEmptyPlan()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var (acl, _, _) = BuildOversizeIf(Max - 1, Max);
                Plan(acl, Max).Should().BeEmpty();
            });
        }

        // ───────────────────────────────────── depth slicing ──────────────────────────────────────
        [TestMethod]
        public void Depth_SingleOversizeRegion_ReturnsOneEntry()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                int regionLen = Max + 2;
                var (acl, incIdx, decIdx) = BuildOversizeDepthRegion(regionLen, Max);
                var plan = Plan(acl, Max);
                plan.Should().HaveCount(1);
                var entry = plan.Single();
                entry.Kind.Should().Be(HoistPlanner.SplitKind.Depth);
                entry.StartIdx.Should().Be(incIdx);
                entry.EndIdxExclusive.Should().Be(decIdx + 1);
            });
        }

        [TestMethod]
        public void Depth_TwoNonOverlappingRegions_ReturnsTwoSortedEntries()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    rec.InsertIncrementDepthCommand();
                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertDecrementDepthCommand();

                    rec.InsertIncrementDepthCommand();
                    rec.InsertBlankCommands(Max + 2);
                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: 40,
                maxCommandsPerChunk: Max);

                var plan = Plan(acl, Max).ToList();
                plan.Should().HaveCount(2)
                     .And.BeInAscendingOrder(p => p.StartIdx)
                     .And.AllSatisfy(e => e.Kind.Should().Be(HoistPlanner.SplitKind.Depth));
            });
        }

        [TestMethod]
        public void Depth_NestedOversizeRegions_ReturnsOuterOnly()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    rec.InsertIncrementDepthCommand();
                    rec.InsertBlankCommands(2);

                    rec.InsertIncrementDepthCommand();
                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertDecrementDepthCommand();

                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: 50,
                maxCommandsPerChunk: Max);

                var plan = Plan(acl, Max);
                plan.Should().HaveCount(1);
                plan.Single().Kind.Should().Be(HoistPlanner.SplitKind.Depth);
            });
        }

        [TestMethod]
        public void Depth_RegionBelowThreshold_YieldsEmptyPlan()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                /*  A depth region is considered for splitting inclusive of its
                    IncrementDepth and DecrementDepth tokens.  Therefore the
                    *body* length must be at least (Max – 2) smaller than the
                    threshold to stay safely below it. */
                var (acl, _, _) = BuildOversizeDepthRegion(Max - 3 /* body */, Max);
                Plan(acl, Max).Should().BeEmpty();
            });
        }

        // ───────────────────────────────────────── precedence rule ──        // ───────────────────────────────────── precedence rule ─────────────────────────────────────
        [TestMethod]
        public void Precedence_ConditionalInsideDepthRegion_WinsOverDepth()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    rec.InsertIncrementDepthCommand();
                    int idx0 = rec.NewZero();
                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 1);
                    rec.InsertEndIf();
                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: 40,
                maxCommandsPerChunk: Max);

                var plan = Plan(acl, Max);
                plan.Should().HaveCount(1);
                plan.Single().Kind.Should().Be(HoistPlanner.SplitKind.Conditional);
            });
        }

        // ───────────────────────────────────── multi-leaf order ───────────────────────────────────
        [TestMethod]
        public void MultiLeaf_OnlyOversizeLeafProducesEntries()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = new ArrayCommandList(256, 0) { MaxCommandsPerSplittableChunk = Max };
                var rec = acl.Recorder;

                rec.StartCommandChunk(false, null, name: "L1");
                rec.InsertBlankCommands(2);
                rec.EndCommandChunk();

                rec.StartCommandChunk(false, null, name: "L2");
                int idx0 = rec.NewZero();
                rec.InsertEqualsValueCommand(idx0, 0);
                rec.InsertIf();
                rec.InsertBlankCommands(Max + 2);
                rec.InsertEndIf();
                rec.EndCommandChunk();

                acl.MaxCommandIndex = acl.NextCommandIndex;
                acl.CommandTree.StoredValue.EndCommandRangeExclusive = acl.NextCommandIndex;

                int leaf2Id = -1;
                acl.CommandTree.WalkTree(nObj =>
                {
                    var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nObj;
                    if (n.StoredValue.Name == "L2")
                        leaf2Id = n.StoredValue.ID;
                });
                leaf2Id.Should().BeGreaterThan(0);

                var plan = Plan(acl, Max);
                plan.Should().NotBeEmpty()
                     .And.AllSatisfy(e => e.LeafId.Should().Be(leaf2Id));
            });
        }
    }
}
