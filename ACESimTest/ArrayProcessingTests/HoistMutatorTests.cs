using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class HoistMutatorTests
    {
        private const int Max = 5;

        // ───────────────────────────── local helper ─────────────────────────────
        // Collapse any run of whitespace (tabs, CR/LF, spaces) to one space so
        // cosmetic differences do not break the tests.
        private static string W(string s) =>
            Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();

        private static void ApplySingleRound(ArrayCommandList acl)
        {
            var planner = new HoistPlanner(acl.UnderlyingCommands, Max);
            var plan = planner.BuildPlan(acl.CommandTree);
            HoistMutator.ApplyPlan(acl, plan);
        }

        // ───────────────────────────── tests ─────────────────────────────

        // 1 ▸ Already-balanced leaf
        [TestMethod]
        public void BlankLeaf_AlreadyBalanced_RemainsUnchanged()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                    rec => rec.InsertBlankCommands(Max),
                    maxNumCommands: Max + 2,
                    maxCommandsPerChunk: Max);

                const string expectedTree =
                    "Root: ID0: 5 Commands:[0,5) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "initial structure");

                HoistMutator.MutateUntilBalanced(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "final structure");
            });
        }

        // 2a ▸ Oversize conditional body – single round
        [TestMethod]
        public void Conditional_OversizeBody_SingleRound_SplitsIntoGateAndSlices()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.MakeOversizeIfBody(Max + 3, Max).acl;

                const string expectedInitialTree =
                    "Root: ID0: 12 Commands:[0,12) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
    "Root: ID0: 0 Commands:[2,2) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID2: 2 Commands:[0,2) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "0S-1U1S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID1: 10 Commands:[2,12) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID3: 5 Commands:[3,8) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "3R-7U7S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID4: 3 Commands:[8,11) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "8R-10U10S,-,-,-,-,-,-,-,-,-,-,";


                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                ApplySingleRound(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // 2b ▸ Oversize conditional body – full balance
        [TestMethod]
        public void Conditional_OversizeBody_FullyBalanced_NoLeafExceedsLimit()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.MakeOversizeIfBody(Max * 3, Max).acl;

                const string expectedInitialTree =
                    "Root: ID0: 19 Commands:[0,19) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
    "Root: ID0: 0 Commands:[2,2) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID2: 2 Commands:[0,2) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 0S-1U1S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID1: 17 Commands:[2,19) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID3: 5 Commands:[3,8) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 3R-7U7S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID4: 5 Commands:[8,13) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 8R-12U12S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 3: ID5: 5 Commands:[13,18) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 13R-17U17S,-,-,-,-,-,-,-,-,-,-,";


                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilBalanced(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // 3 ▸ Oversize depth region
        [TestMethod]
        public void DepthRegion_Oversize_SplitsIntoRegionAndSlices()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                int regionLen = Max + 4;
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                    rec =>
                    {
                        rec.InsertIncrementDepthCommand();
                        rec.InsertBlankCommands(regionLen);
                        rec.InsertDecrementDepthCommand();
                    },
                    maxNumCommands: regionLen + 4,
                    maxCommandsPerChunk: Max);

                const string expectedInitialTree =
                    "Root: ID0: 11 Commands:[0,11) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
                    "Root: ID0: 0 Commands:[0,0) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID1: 0 Commands:[11,11) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID2: 5 Commands:[1,6) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 1 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "-,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 2: ID3: 4 Commands:[6,10) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 1 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "-,-,-,-,-,-,-,-,-,-,-,";


                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                ApplySingleRound(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }


        // 4 ▸ Precedence – conditional inside depth region
        [TestMethod]
        public void Precedence_ConditionalInsideDepth_SplitsAtConditionalOnly()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    rec.InsertIncrementDepthCommand();
                    int idx = rec.NewZero();
                    rec.InsertEqualsValueCommand(idx, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 2);
                    rec.InsertEndIf();
                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: 50,
                maxCommandsPerChunk: Max);

                const string expectedInitialTree =
                    "Root: ID0: 13 Commands:[0,13) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
                    "Root: ID0: 0 Commands:[3,3) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID3: 3 Commands:[0,3) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 1S-2U2S,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 2: ID1: 9 Commands:[3,12) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID4: 5 Commands:[4,9) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 2: ID5: 2 Commands:[9,11) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 3: ID2: 1 Commands:[12,13) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-,";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilBalanced(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // 5 ▸ Two independent oversize bodies
        [TestMethod]
        public void Conditional_TwoIndependentOversizeBodies_BothSplit()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx0 = rec.NewZero();

                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 1);   // first oversize region
                    rec.InsertEndIf();

                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max + 2);   // second oversize region
                    rec.InsertEndIf();
                },
                maxNumCommands: 60,
                maxCommandsPerChunk: Max);

                // ───── initial tree (after helpers create the single leaf) ─────
                const string expectedInitialTree =
    "Root: ID0: 20 Commands:[0,20) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                // ───── mutate until the two oversize segments are split ─────
                HoistMutator.MutateUntilBalanced(acl);

                const string expectedFinalTree =
    "Root: ID0: 0 Commands:[2,2) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 6 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID3: 2 Commands:[0,2) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 6 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 0S-1U1S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID1: 8 Commands:[2,10) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 6 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID4: 5 Commands:[3,8) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 8 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID5: 1 Commands:[8,9) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 8 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 3: ID2: 0 Commands:[11,11) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 6 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 10S-10U10S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 1: ID7: 1 Commands:[10,11) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 11 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 10S-10U10S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID6: 9 Commands:[11,20) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 11 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID8: 5 Commands:[12,17) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 13 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID9: 2 Commands:[17,19) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 13 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-,";


                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }



        // 6 ▸ Nested oversize bodies – inner split only
        [TestMethod]
        public void Conditional_NestedOversizeBodies_SplitsInnerOnly()
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
                maxNumCommands: 70,
                maxCommandsPerChunk: Max);

                const string expectedInitialTree =
                    "Root: ID0: 19 Commands:[0,19) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
                    "Root: ID0: 0 Commands:[4,4) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID3: 4 Commands:[0,4) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 0S-3U3S,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 2: ID1: 8 Commands:[4,12) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
                    "Leaf 1: ID4: 5 Commands:[5,10) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 2: ID5: 1 Commands:[10,11) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-, " +
                    "Leaf 3: ID2: 7 Commands:[12,19) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: -,-,-,-,-,-,-,-,-,-,-,";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilBalanced(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // 7 ▸ Multi-leaf tree – only oversize leaf mutates
        [TestMethod]
        public void MultiLeaf_OnlyOversizeLeaf_IsMutated()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = new ArrayCommandList(256, 0, parallelize: false)
                {
                    MaxCommandsPerChunk = Max
                };
                var rec = acl.Recorder;

                // Leaf 1 – small
                rec.StartCommandChunk(false, null, name: "L1");
                rec.InsertBlankCommands(3);
                rec.EndCommandChunk();

                // Leaf 2 – oversize conditional
                rec.StartCommandChunk(false, null, name: "L2");
                int idx = rec.NewZero();
                rec.InsertEqualsValueCommand(idx, 0);
                rec.InsertIf();
                rec.InsertBlankCommands(Max + 2);
                rec.InsertEndIf();
                rec.EndCommandChunk();

                acl.MaxCommandIndex = acl.NextCommandIndex;
                acl.CommandTree.StoredValue.EndCommandRangeExclusive = acl.NextCommandIndex;

                const string expectedInitialTree =
                    "Root: ID0: 14 Commands:[0,14) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo: " +
                    "Leaf 1: ID1: L1 3 Commands:[0,3) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo: " +
                    "Leaf 2: ID2: L2 11 Commands:[3,14) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                const string expectedFinalTree =
    "Root: ID0: 14 Commands:[0,14) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0 Stackinfo: " +
    "Leaf 1: ID1: L1 3 Commands:[0,3) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0 Stackinfo: -,-, " +
    "Leaf 2: ID2: L2 0 Commands:[5,5) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 0 Contents: 0,0 Stackinfo: " +
    "Leaf 1: ID4: 2 Commands:[3,5) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0 Stackinfo: 3S-4U4S,-, " +
    "Leaf 2: ID3: 9 Commands:[5,14) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 2 Contents: 0,0 Stackinfo: " +
    "Leaf 1: ID5: 5 Commands:[6,11) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 4 Contents: 0,0 Stackinfo: -,-, " +
    "Leaf 2: ID6: 2 Commands:[11,13) Sources:[0,0) Destinations:[0,0) " +
    "CopyIncrements: VirtualStack ID: 4 Contents: 0,0 Stackinfo: -,-,";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilBalanced(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }
    }
}
