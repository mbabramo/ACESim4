using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Regression-style tests for <see cref="HoistMutator"/> and supporting
    /// classes.  The suite focuses on the observable tree-shaping behaviour
    /// rather than the exact command contents.  In most cases we build a small
    /// synthetic <see cref="ArrayCommandList"/>, invoke the mutator, then assert
    /// on the <c>ToTreeString</c> representation.
    ///
    /// <para>
    /// <strong>Why so many literal strings?</strong>  Visualising the tree as a
    /// single-line string makes failures readable while keeping the assertions
    /// compact.  For deterministic IDs the helper
    /// <see cref="ArrayProcessingTestHelpers.WithDeterministicIds"/> is used.
    /// </para>
    ///
    /// The expected structures have also been reviewed to ensure the comments
    /// and test names accurately describe the final behaviour (especially where
    /// an oversize region cannot be split due to the absence of balanced
    /// If/EndIf or IncrementDepth/DecrementDepth delimiters).
    /// </summary>
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
                    maxCommandsPerChunk: Max,
                    hoistLargeIfBodies: false);

                /*  expectedTree – single leaf containing exactly Max commands.  */
                const string expectedTree =
                    "Root: ID0: 5 Commands:[0,5) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedTree), "final structure");
            });
        }

        // Oversize conditional body – single round only
        [TestMethod]
        public void Conditional_OversizeBody_SingleRound_SplitsIntoGateAndSlices()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers
                          .MakeOversizeIfBody(Max + 3, Max, hoistLargeIfBodies: false).acl;

                /*  expectedInitialTree */
                const string expectedInitialTree =
                    "Root: ID0: 14 Commands:[0,14) ";

                /*  expectedFinalTree – body sliced once; only one round applied. */
                const string expectedFinalTree =
                    "Root: ID0: 14 Commands:[0,14) " +
                    "Leaf 1: ID1: 3 Commands:[0,3) " +
                    "Leaf 2: ID2: 8 Commands:[4,12) " +
                    "Leaf 3: ID3: 1 Commands:[13,14) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                ApplySingleRound(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Oversize conditional body – fully balanced (no more valid split points)
        [TestMethod]
        public void Conditional_OversizeBody_FullyBalanced_NoFurtherSplits()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers
                          .MakeOversizeIfBody(Max * 3, Max, hoistLargeIfBodies: false).acl;

                /*  expectedInitialTree – identical shape to previous test but body = 21 cmds. */
                const string expectedInitialTree =
                    "Root: ID0: 21 Commands:[0,21) ";

                /*  expectedFinalTree – inner body (15 cmds) still oversize but contains no
                    balanced If/EndIf or depth delimiters, so the mutator stops here.          */
                const string expectedFinalTree =
                    "Root: ID0: 21 Commands:[0,21) " +
                    "Leaf 1: ID1: 3 Commands:[0,3) " +
                    "Leaf 2: ID2: 15 Commands:[4,19) " +
                    "Leaf 3: ID3: 1 Commands:[20,21) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Oversize depth region – no split when increment/decrement are outer delimiters
        [TestMethod]
        public void DepthRegion_Oversize_NoSplitWhenOuterDelimiters()
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
                    maxCommandsPerChunk: Max,
                    hoistLargeIfBodies: false);

                /*  expectedInitialTree – one oversized IncrementDepth…DecrementDepth sequence. */
                const string expectedInitialTree =
                    "Root: ID0: 11 Commands:[0,11) ";

                /*  expectedFinalTree – identical, because the depth delimiters themselves form
                    the oversize region and therefore are not split further.                      */
                const string expectedFinalTree =
                    "Root: ID0: 11 Commands:[0,11) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                ApplySingleRound(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Precedence – an oversize Conditional inside a depth region; mutator should slice the conditional first
        [TestMethod]
        public void Precedence_ConditionalInsideDepth()
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
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

                /*  expectedInitialTree – depth region plus an oversize If-body inside it.        */
                const string expectedInitialTree =
                    "Root: ID0: 13 Commands:[0,13) ";

                /*  expectedFinalTree – inner conditional sliced, outer depth region unchanged. */
                const string expectedFinalTree =
                    "Root: ID0: 13 Commands:[0,13) " +
                    "Leaf 1: ID1: 3 Commands:[0,3) " +
                    "Leaf 2: ID2: 7 Commands:[4,11) " +
                    "Leaf 3: ID3: 1 Commands:[12,13) ";

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
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

                /*  expectedInitialTree – single leaf, two separate If-blocks. */
                const string expectedInitialTree =
                    "Root: ID0: 20 Commands:[0,20) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                /*  expectedFinalTree – both If-blocks sliced; IDs reflect deterministic assignment. */
                const string expectedFinalTree =
                    "Root: ID0: 20 Commands:[0,20) " +
                    "Leaf 1: ID1: 11 Commands:[0,11) " +
                    "Leaf 1: ID4: 2 Commands:[0,2) " +
                    "Leaf 2: ID5: 6 Commands:[3,9) " +
                    "Leaf 3: ID6: 1 Commands:[10,11) " +
                    "Leaf 2: ID2: 7 Commands:[12,19) " +
                    "Leaf 3: ID3: 0 Commands:[20,20) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Nested oversize bodies – only inner split expected
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
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

                /*  expectedInitialTree – outer If, inner If, both oversize. */
                const string expectedInitialTree =
                    "Root: ID0: 19 Commands:[0,19) ";

                /*  expectedFinalTree – only inner If-body sliced; outer remains whole. */
                const string expectedFinalTree =
                    "Root: ID0: 19 Commands:[0,19) " +
                    "Leaf 1: ID1: 4 Commands:[0,4) " +
                    "Leaf 2: ID2: 6 Commands:[5,11) " +
                    "Leaf 3: ID3: 7 Commands:[12,19) ";

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedInitialTree), "initial structure");

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expectedFinalTree), "final structure");
            });
        }

        // Multi-leaf tree – only oversize leaf should mutate
        [TestMethod]
        public void MultiLeaf_OnlyOversizeLeaf_IsMutated()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = new ArrayCommandList(256, 0)
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

                /*  expectedInitialTree – two leaves, L1 (small) and L2 (oversize). */
                const string expectedInitialTree =
                    "Root: ID0: 14 Commands:[0,14) " +
                    "Leaf 1: ID1: L1 3 Commands:[0,3) " +
                    "Leaf 2: ID2: L2 11 Commands:[3,14) ";

                /*  expectedFinalTree – only L2 is sliced into three. */
                const string expectedFinalTree =
                    "Root: ID0: 14 Commands:[0,14) " +
                    "Leaf 1: ID1: L1 3 Commands:[0,3) " +
                    "Leaf 2: ID2: L2 11 Commands:[3,14) " +
                    "Leaf 1: ID3: 2 Commands:[3,5) " +
                    "Leaf 2: ID4: 7 Commands:[6,13) " +
                    "Leaf 3: ID5: 0 Commands:[14,14) ";

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
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

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

        [TestMethod]
        public void Conditional_BodyExactlyAtLimit_SplitsOutBody()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx = rec.NewZero();
                    rec.InsertEqualsValueCommand(idx, 0);
                    rec.InsertIf();
                    rec.InsertBlankCommands(Max);   // exactly the limit (5)
                    rec.InsertEndIf();
                },
                maxNumCommands: 32,
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                const string expected =
                    "Root: ID0: 9 Commands:[0,9) " +
                    "Leaf 1: ID1: 2 Commands:[0,2) " +   // Zero + Equals
                    "Leaf 2: ID2: 5 Commands:[3,8) " +   // hoisted body (== Max)
                    "Leaf 3: ID3: 0 Commands:[9,9) ";    // EndIf marker slice

                W(acl.CommandTree.ToTreeString(_ => "Leaf"))
                    .Should().Be(W(expected), "body exactly at Max is still hoisted");
            });
        }

        [TestMethod]
        public void NestedRegions_MultiRoundSplit_FixedPointReached()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    rec.InsertIncrementDepthCommand();

                    for (int i = 0; i < 2; i++)
                    {
                        int idx = rec.NewZero();
                        rec.InsertEqualsValueCommand(idx, 0);
                        rec.InsertIf();
                        rec.InsertBlankCommands(Max + 3);   // deliberately oversize
                        rec.InsertEndIf();
                    }

                    rec.InsertDecrementDepthCommand();
                },
                maxNumCommands: 128,
                maxCommandsPerChunk: Max,
                hoistLargeIfBodies: false);

                HoistMutator.MutateUntilAsBalancedAsPossible(acl);

                // 1️⃣  There should be no further balanced splits available.
                var planner = new HoistPlanner(acl.UnderlyingCommands, Max);
                var plan = planner.BuildPlan(acl.CommandTree);
                plan.Should().BeEmpty("mutator ran until no plan remained");

                // 2️⃣  At least one leaf is still > Max, illustrating why the loop stopped.
                bool oversizeExists = acl.PureSlices().Any(l =>
                    l.StoredValue.EndCommandRangeExclusive - l.StoredValue.StartCommandRange > Max);
                oversizeExists.Should().BeTrue("fixture is meaningful (contains an unsplittable oversize leaf)");
            });
        }


    }
}
