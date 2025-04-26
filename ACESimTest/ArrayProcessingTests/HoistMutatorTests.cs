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
        private static string W(string s) =>
            Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();

        private static void ApplySingleRound(ArrayCommandList acl)
        {
            var planner = new HoistPlanner(acl.UnderlyingCommands, Max);
            var plan = planner.BuildPlan(acl.CommandTree);
            HoistMutator.ApplyPlan(acl, plan);
        }

        // ───────────────────────────── tests ─────────────────────────────

        // Already-balanced leaf
        [TestMethod]
        public void BlankLeaf_AlreadyBalanced_RemainsUnchanged()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                    rec => rec.InsertBlankCommands(Max),
                    maxNumCommands: Max + 2,
                    maxCommandsPerChunk: Max);

                /*  expectedTree
                    ─────────────
                    Root  ID0   – single leaf that already contains exactly Max commands.
                                 (HoistMutator should leave it alone.)                               */
                const string expectedTree =
                    "Root: ID0: 5 Commands:[0,5) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "final structure");
            });
        }

        // Oversize conditional body – single round
        [TestMethod]
        public void Conditional_OversizeBody_SingleRound_SplitsIntoGateAndSlices()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers
                          .MakeOversizeIfBody(Max + 3, Max).acl;

                /*  expectedInitialTree
                    ───────────────────
                    Root  ID0 – one oversize If/EndIf block (body length 8 > Max).                  */
                const string expectedInitialTree =
                    "Root: ID0: 12 Commands:[0,12) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    Root  ID0  – prefix (2 cmds) hoisted out => now 0 cmds printed.
                    Leaf  ID2  – *prefix* commands 0-1 (copies vars into local scratch).
                    Leaf  ID1  – **gate container** spanning If..EndIf, 0 cmds of its own.
                        Leaf ID3 – first slice of body (5 cmds, stack context ID2).
                        Leaf ID4 – second slice of body (3 cmds).                                    */
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

        // Oversize conditional body – fully balanced
        [TestMethod]
        public void Conditional_OversizeBody_FullyBalanced_NoLeafExceedsLimit()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers
                          .MakeOversizeIfBody(Max * 3, Max).acl;

                /*  expectedInitialTree – identical shape to previous test but body = 15 cmds.       */
                const string expectedInitialTree =
                    "Root: ID0: 19 Commands:[0,19) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    Root   ID0  – prefix hoisted (2 cmds) → 0 cmds shown.
                    Leaf   ID2  – prefix (2 cmds).
                    Gate   ID1  – If/EndIf container, 0 cmds.
                        Slice ID3 – body part 1 (5 cmds).
                        Slice ID4 – body part 2 (5 cmds).
                        Slice ID5 – body part 3 (5 cmds).                                            */
                const string expectedFinalTree =
    "Root: ID0: 0 Commands:[2,2) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID2: 2 Commands:[0,2) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 0S-1U1S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID1: 17 Commands:[2,19) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: " +
    "Leaf 1: ID3: 5 Commands:[3,8) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 3R-7U7S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 2: ID4: 5 Commands:[8,13) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 8R-12U12S,-,-,-,-,-,-,-,-,-,-, " +
    "Leaf 3: ID5: 5 Commands:[13,18) Sources:[0,0) Destinations:[0,0) CopyIncrements: VirtualStack ID: 2 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: 13R-17U17S,-,-,-,-,-,-,-,-,-,-,";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Oversize depth region
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

                /*  expectedInitialTree – one oversized IncrementDepth…DecrementDepth sequence.       */
                const string expectedInitialTree =
                    "Root: ID0: 11 Commands:[0,11) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    Root  ID0  – prefix removed → 0 cmds shown.
                    Region ID1 – depth-scope container (0 cmds) shares parent stack (ID0).
                        Slice ID2 – first five-cmd chunk of the body (stack ID1).
                        Slice ID3 – second four-cmd chunk of the body (stack ID1).                   */
                const string expectedFinalTree =
                    "Root: ID0: 0 Commands:[0,0) Sources:[0,0) Destinations:[0,0) "
                    + "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: "
                    + "Leaf 1: ID1: 0 Commands:[11,11) Sources:[0,0) Destinations:[0,0) "
                    + "CopyIncrements: VirtualStack ID: 0 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: "
                    + "Leaf 1: ID2: 5 Commands:[1,6) Sources:[0,0) Destinations:[0,0) "
                    + "CopyIncrements: VirtualStack ID: 1 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: "
                    + "-,-,-,-,-,-,-,-,-,-,-, "
                    + "Leaf 2: ID3: 4 Commands:[6,10) Sources:[0,0) Destinations:[0,0) "
                    + "CopyIncrements: VirtualStack ID: 1 Contents: 0,0,0,0,0,0,0,0,0,0,0 Stackinfo: "
                    + "-,-,-,-,-,-,-,-,-,-,-,";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                ApplySingleRound(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Precedence – conditional inside depth region
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

                /*  expectedInitialTree – depth region plus an oversize If-body inside it.            */
                const string expectedInitialTree =
                    "Root: ID0: 13 Commands:[0,13) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    Depth region is *not* split; only the internal If-body is sliced.                 */
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

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Two independent oversize Conditional bodies
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

                /*  expectedInitialTree – single leaf, two separate If-blocks.                       */
                const string expectedInitialTree =
                    "Root: ID0: 20 Commands:[0,20) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                /*  expectedFinalTree
                    ─────────────────
                    Both If-blocks were hoisted and sliced, producing two gate containers
                    each with its own slices; IDs reflect deterministic assignment.                 */
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

        // Nested oversize bodies – inner split only
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

                /*  expectedInitialTree – outer If, inner If, both oversize.                         */
                const string expectedInitialTree =
                    "Root: ID0: 19 Commands:[0,19) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    Only the inner If-body was sliced; the outer remains a single chunk.             */
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

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Multi-leaf tree – only oversize leaf mutates
        [TestMethod]
        public void MultiLeaf_OnlyOversizeLeaf_IsMutated()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = new ArrayCommandList(256, 0, parallelize: false)
                {
                    MaxCommandsPerSplittableChunk = Max
                };
                var rec = acl.Recorder;

                // Small leaf
                rec.StartCommandChunk(false, null, name: "L1");
                rec.InsertBlankCommands(3);
                rec.EndCommandChunk();

                // Oversize conditional leaf
                rec.StartCommandChunk(false, null, name: "L2");
                int idx = rec.NewZero();
                rec.InsertEqualsValueCommand(idx, 0);
                rec.InsertIf();
                rec.InsertBlankCommands(Max + 2);
                rec.InsertEndIf();
                rec.EndCommandChunk();

                acl.MaxCommandIndex = acl.NextCommandIndex;
                acl.CommandTree.StoredValue.EndCommandRangeExclusive = acl.NextCommandIndex;

                /*  expectedInitialTree – two leaves, L1 (small) and L2 (oversize).                   */
                const string expectedInitialTree =
                    "Root: ID0: 14 Commands:[0,14) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo: " +
                    "Leaf 1: ID1: L1 3 Commands:[0,3) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo: " +
                    "Leaf 2: ID2: L2 11 Commands:[3,14) Sources:[0,0) Destinations:[0,0) " +
                    "CopyIncrements: VirtualStack ID: 0 Contents: Stackinfo:";

                /*  expectedFinalTree
                    ─────────────────
                    L1 unchanged.  
                    L2 becomes a small prefix leaf + gate + slices.                                   */
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

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        [TestMethod]
        public void LinearOversizeBody_RemainsWhole_WhenNoSplitPoint()
        {
            const int Max = 5;
            var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(
                rec => rec.InsertBlankCommands(40),
                maxNumCommands: 42,
                maxCommandsPerChunk: Max);

            HoistMutator.MutateUntilAsBalancedAsPossible(acl);

            // every *executable* leaf either ≤ Max or > Max with no split-point
            foreach (var leaf in acl.PureSlices())
            {
                int len = leaf.StoredValue.EndCommandRangeExclusive -
                          leaf.StoredValue.StartCommandRange;

                if (len > Max)
                    len.Should().Be(40, "linear bodies stay whole");
                else
                    len.Should().BeLessOrEqualTo(Max, "splittable bodies are bounded");
            }
        }

    }
}
