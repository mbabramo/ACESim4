using System;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.NWayTreeStorage;
using System.Collections.Generic;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class HoistMutatorTests
    {
        // ---------------------------------------------------------------------
        //  EmptyCommandList_NoHoist  –  empty program should stay a single leaf
        // ---------------------------------------------------------------------
        [TestMethod]
        public void EmptyCommandList_NoHoist()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(
                    new List<ArrayCommand>(), THRESHOLD);

                var expected = new[] { "ID0 [0,0) Children=0 Cmds:-" };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);

                acl.FinaliseCommandTree();
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);
            });
        }

        // ---------------------------------------------------------------------
        //  NoIfCommands_NoHoist  –  short list without If/EndIf needs no change
        // ---------------------------------------------------------------------
        [TestMethod]
        public void NoIfCommands_NoHoist()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 10;

                var cmds = new List<ArrayCommand>
                {
                    new(ArrayCommandType.Zero,        0, -1),
                    new(ArrayCommandType.IncrementBy, 0,  0)
                };
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                var expected = new[]
                {
                    "ID0 [0,2) Children=0 Cmds:Zero,IncrementBy"
                };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);

                acl.FinaliseCommandTree();
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);
            });
        }

        // ---------------------------------------------------------------------
        //  SmallIfBody_BelowThreshold  –  body < threshold so hoist not applied
        // ---------------------------------------------------------------------
        [TestMethod]
        public void SmallIfBody_BelowThreshold_NoHoist()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 10;

                var cmds = new List<ArrayCommand>
                {
                    new(ArrayCommandType.Zero,        0, -1),
                    new(ArrayCommandType.EqualsValue, 0,  0),
                    new(ArrayCommandType.If,         -1, -1),
                    new(ArrayCommandType.IncrementBy, 0,  0),
                    new(ArrayCommandType.EndIf,      -1, -1)
                };
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                var expected = new[]
                {
                    "ID0 [0,5) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,EndIf"
                };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);

                acl.FinaliseCommandTree();
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected);
            });
        }

        // ---------------------------------------------------------------------
        //  OversizeIfBody_HoistsAndSplits  –  verifies exact rewrite behaviour
        // ---------------------------------------------------------------------
        [TestMethod]
        public void OversizeIfBody_HoistsAndSplits()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                // 1. Build a minimal command list whose If-body exceeds the hoist
                //    threshold.  Body length = 4, threshold = 2 → hoisting required.
                const int THRESHOLD = 2;

                var cmds = new List<ArrayCommand>
                {
                    new(ArrayCommandType.Zero,        0,  -1),
                    new(ArrayCommandType.EqualsValue, 0,   0),
                    new(ArrayCommandType.If,         -1,  -1),
                    new(ArrayCommandType.IncrementBy, 0,   0),
                    new(ArrayCommandType.IncrementBy, 0,   0),
                    new(ArrayCommandType.IncrementBy, 0,   0),
                    new(ArrayCommandType.IncrementBy, 0,   0),
                    new(ArrayCommandType.EndIf,      -1,  -1)
                };

                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                // 2. Snapshot *before* hoisting — single oversize leaf.
                var before = ArrayProcessingTestHelpers.DumpTree(acl).ToList();

                string[] expectedBefore =
                {
                    // The root is an executable leaf because we have not run hoisting yet.
                    "ID0 [0,8) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf"
                };

                before.Should().Equal(
                    expectedBefore,
                    "initially the command-tree should consist of a single oversize leaf (the root)");

                // 3. Run hoisting (planner + mutator are invoked by FinaliseCommandTree).
                acl.FinaliseCommandTree();

                // 4. Snapshot *after* hoisting.
                var after = ArrayProcessingTestHelpers.DumpTree(acl).ToList();

                /*
                 *  Explanation of the seemingly odd details:
                 *
                 *  • ID0 .container  [2,2) Children=2
                 *      The original leaf is shrunk to *exclude* the If-body and then
                 *      turned into a pure container.  Because it owns no commands,
                 *      Start and End are both 2, giving the empty span [2,2).
                 *
                 *  • Missing ID1
                 *      A temporary postfix slice is allocated during splitting and
                 *      receives ID1, but it is discarded (there is nothing after EndIf).
                 *      The counter continues, so the first retained node is ID2.
                 */
                string[] expectedAfter =
                {
                    "ID0 .container [2,2) Children=2 Cmds:-",
                    "ID3 .leaf [0,2) Children=0 Cmds:Zero,EqualsValue",
                    "ID2 Conditional [2,8) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
                    "ID4 [3,5) Children=0 Cmds:IncrementBy,IncrementBy",
                    "ID5 [5,7) Children=0 Cmds:IncrementBy,IncrementBy"
                };

                after.Should().Equal(
                    expectedAfter,
                    "hoisting should rewrite the tree exactly as specified");
            });
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  LeafExactlyAtThreshold_NoHoist  –  body == threshold so no rewrite
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void LeafExactlyAtThreshold_NoHoist()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 6;

                // 1. Build a program whose total length (incl. If/EndIf) == threshold.
                var cmds = new List<ArrayCommand>
        {
            new(ArrayCommandType.Zero,        0, -1),
            new(ArrayCommandType.EqualsValue, 0,  0),
            new(ArrayCommandType.If,         -1, -1),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.EndIf,      -1, -1)
        };
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                // 2. Snapshot *before* hoisting.
                string[] expected =
                {
            "ID0 [0,6) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected,
                    "initially the command-tree is a single executable leaf");

                // 3. Run FinaliseCommandTree (planner + mutator).
                acl.FinaliseCommandTree();

                // 4. Snapshot *after* hoisting — should be identical.
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expected,
                    "a leaf exactly at the limit must remain unchanged");

                // 5. Additional guards.
                ArrayProcessingTestHelpers.AssertLeafSizeUnder(acl, THRESHOLD);
                ArrayProcessingTestHelpers.InterpreterVsCompiled(acl);
            });
        }


        // ────────────────────────────────────────────────────────────────────────────
        //  NestedIf_InnerOversize_HoistsOnlyInner
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void NestedIf_InnerOversize_HoistsOnlyInner()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;

                // 1. Build: small outer body, oversize inner body.
                var cmds = new List<ArrayCommand>
        {
            new(ArrayCommandType.Zero,        0, -1),
            new(ArrayCommandType.EqualsValue, 0,  0),
            new(ArrayCommandType.If,         -1, -1),   // outer If

            new(ArrayCommandType.EqualsValue, 0,  0),
            new(ArrayCommandType.If,         -1, -1),   // inner If (oversize)
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.EndIf,      -1, -1),   // end inner

            new(ArrayCommandType.EndIf,      -1, -1)    // end outer
        };
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                // 2. Before hoisting – one oversize leaf.
                string[] expectedBefore =
                {
            "ID0 [0,11) Children=0 Cmds:Zero,EqualsValue,If,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                // 3. Hoist.
                acl.FinaliseCommandTree();

                // 4. After hoisting – only the inner body wrapped.
                string[] expectedAfter =
                {
            "ID0 .container [4,4) Children=3 Cmds:-",
            "ID3 .leaf [0,4) Children=0 Cmds:Zero,EqualsValue,If,EqualsValue",
            "ID1 Conditional [4,10) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID4 [5,7) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID5 [7,9) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID2 [10,11) Children=0 Cmds:EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                ArrayProcessingTestHelpers.AssertLeafSizeUnder(acl, THRESHOLD);
            });
        }


        // ────────────────────────────────────────────────────────────────────────────
        //  NestedOversizeIfs_RecursiveHoist
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void NestedOversizeIfs_RecursiveHoist()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;
                const int DEPTH = 3;
                const int BODY_LEN = 4;

                // 1. Three nested Ifs, each oversize.
                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx0 = rec.NewZero();
                    for (int d = 0; d < DEPTH; d++)
                    {
                        rec.InsertEqualsValueCommand(idx0, 0);
                        rec.InsertIf();
                    }
                    for (int i = 0; i < BODY_LEN; i++)
                        rec.Increment(idx0, false, idx0);
                    for (int d = 0; d < DEPTH; d++)
                        rec.InsertEndIf();
                }, maxCommandsPerChunk: THRESHOLD);

                // 2. Before hoisting.
                string[] expectedBefore =
                {
            "ID0 [0,14) Children=0 Cmds:Zero,EqualsValue,If,EqualsValue,If,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf,EndIf,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                // 3. Hoist.
                acl.FinaliseCommandTree();

                // 4. After hoisting – three stacked Conditional gates.
                string[] expectedAfter =
                {
            "ID0 .container [6,6) Children=3 Cmds:-",
            "ID4 .leaf [0,6) Children=0 Cmds:Zero,EqualsValue,If,EqualsValue,If,EqualsValue",
            "ID1 Conditional [6,13) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf,EndIf",
            "ID5 .leaf [7,13) Children=0 Cmds:IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf,EndIf",
            "ID2 Conditional [6,12) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID6 [7,9) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID7 [9,11) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID3 [13,14) Children=0 Cmds:EndIf"
        };
                var after1 = ArrayProcessingTestHelpers.DumpTree(acl).ToList();
                after1.Should().Equal(expectedAfter);

                // 5. Idempotence – second pass unchanged.
                acl.FinaliseCommandTree();
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                ArrayProcessingTestHelpers.AssertLeafSizeUnder(acl, THRESHOLD);
            });
        }


        // ────────────────────────────────────────────────────────────────────────────
        //  MultipleOversizeIfsInOneLeaf_HoistsEach
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void MultipleOversizeIfsInOneLeaf_HoistsEach()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;

                // 1. Two disjoint oversize If-bodies in a single leaf.
                var cmds = new List<ArrayCommand>
        {
            // first oversize If
            new(ArrayCommandType.Zero,        0, -1),
            new(ArrayCommandType.EqualsValue, 0,  0),
            new(ArrayCommandType.If,         -1, -1),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.EndIf,      -1, -1),

            new(ArrayCommandType.Zero,        1, -1), // separator

            // second oversize If
            new(ArrayCommandType.EqualsValue, 0,  0),
            new(ArrayCommandType.If,         -1, -1),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.IncrementBy, 0,  0),
            new(ArrayCommandType.EndIf,      -1, -1)
        };
                var acl = ArrayProcessingTestHelpers.CreateStubAcl(cmds, THRESHOLD);

                // 2. Before hoisting.
                string[] expectedBefore =
                {
            "ID0 [0,17) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf,Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                // 3. Hoist.
                acl.FinaliseCommandTree();

                // 4. After hoisting – two sibling gates.
                string[] expectedAfter =
                {
            "ID0 .container [8,8) Children=5 Cmds:-",
            "ID5 .leaf [0,8) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID1 Conditional [8,16) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID6 [9,11) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID7 [11,13) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID2 Conditional [8,16) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID8 [9,11) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID9 [11,13) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID3 [16,17) Children=0 Cmds:EndIf"
        };
                var after = ArrayProcessingTestHelpers.DumpTree(acl).ToList();
                after.Should().Equal(expectedAfter);
                after.Count(l => l.Contains("Conditional")).Should().Be(2);

                ArrayProcessingTestHelpers.AssertLeafSizeUnder(acl, THRESHOLD);
            });
        }


        // ────────────────────────────────────────────────────────────────────────────
        //  BoundaryCut_SliceEndsRightBeforeEndIf
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void BoundaryCut_SliceEndsRightBeforeEndIf()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;

                // 1. Build oversize body of THRESHOLD+1 cmds.
                var (acl, _) = ArrayProcessingTestHelpers.MakeOversizeIfBody(THRESHOLD + 1, THRESHOLD);

                // 2. Before hoisting.
                string[] expectedBefore =
                {
            "ID0 [0,8) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                // 3. Hoist.
                acl.FinaliseCommandTree();

                // 4. After hoisting – body sliced into [limit,1].
                string[] expectedAfter =
                {
            "ID0 .container [2,2) Children=2 Cmds:-",
            "ID3 .leaf [0,2) Children=0 Cmds:Zero,EqualsValue",
            "ID1 Conditional [2,7) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID4 [3,5) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID5 [5,6) Children=0 Cmds:IncrementBy"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                var gate = acl.FindFirstConditional();
                var childLens = gate.Branches!.Skip(1)
                                     .Where(b => b is not null && b.StoredValue.Name != "Conditional")
                                     .Select(b => b.StoredValue.EndCommandRangeExclusive - b.StoredValue.StartCommandRange)
                                     .ToArray();
                childLens.Should().Equal(new[] { THRESHOLD, 1 });

                ArrayProcessingTestHelpers.AssertLeafSizeUnder(acl, THRESHOLD);
            });
        }
        // ────────────────────────────────────────────────────────────────────────────
        //  ChildrenParallelizable_FlagPropagation
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void ChildrenParallelizable_FlagPropagation()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;
                var (acl, _) = ArrayProcessingTestHelpers.MakeOversizeIfBody(4, THRESHOLD);

                // mark root leaf parallelisable.
                acl.CommandTree.StoredValue.ChildrenParallelizable = true;

                // before
                string[] expectedBefore =
                {
            "ID0 [0,8) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                acl.FinaliseCommandTree();

                // after
                string[] expectedAfter =
                {
            "ID0 .container [2,2) Children=2 Cmds:-",
            "ID3 .leaf [0,2) Children=0 Cmds:Zero,EqualsValue",
            "ID1 Conditional [2,8) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID4 [3,5) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID5 [5,7) Children=0 Cmds:IncrementBy,IncrementBy"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                var gate = acl.FindFirstConditional();
                gate.StoredValue.ChildrenParallelizable.Should().BeTrue();
                foreach (var child in gate.Branches!.Where(b => b is not null && b.StoredValue.Name != "Conditional"))
                    child.StoredValue.ChildrenParallelizable.Should().BeFalse();
            });
        }


        // ────────────────────────────────────────────────────────────────────────────
        //  CopyIncrementsToParent_MergeListCreated
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void CopyIncrementsToParent_MergeListCreated()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;
                var (acl, _) = ArrayProcessingTestHelpers.MakeOversizeIfBody(4, THRESHOLD);

                string[] expectedBefore =
                {
            "ID0 [0,8) Children=0 Cmds:Zero,EqualsValue,If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                acl.FinaliseCommandTree();

                string[] expectedAfter =
                {
            "ID0 .container [2,2) Children=2 Cmds:-",
            "ID3 .leaf [0,2) Children=0 Cmds:Zero,EqualsValue",
            "ID1 Conditional [2,8) Children=2 Cmds:If,IncrementBy,IncrementBy,IncrementBy,IncrementBy,EndIf",
            "ID4 [3,5) Children=0 Cmds:IncrementBy,IncrementBy",
            "ID5 [5,7) Children=0 Cmds:IncrementBy,IncrementBy"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                var gate = acl.FindFirstConditional();
                foreach (var child in gate.Branches!.Where(b => b is not null && b.StoredValue.Name != "Conditional"))
                    child.StoredValue.CopyIncrementsToParent.Should().NotBeNullOrEmpty();
                if (gate.StoredValue.CopyIncrementsToParent != null)
                    gate.StoredValue.CopyIncrementsToParent.Length.Should().BeGreaterThan(0);
            });
        }



        // ────────────────────────────────────────────────────────────────────────────
        //  PointerAdvance_WhileSkippingFalseBranch
        // ────────────────────────────────────────────────────────────────────────────
        [TestMethod]
        public void PointerAdvance_WhileSkippingFalseBranch()
        {
            ArrayProcessingTestHelpers.WithDeterministicIds(() =>
            {
                const int THRESHOLD = 2;

                var acl = ArrayProcessingTestHelpers.BuildAclWithSingleLeaf(rec =>
                {
                    int idx0 = rec.CopyToNew(0, true);          // os[0] == 0
                    rec.InsertEqualsValueCommand(idx0, 999);    // false condition
                    rec.InsertIf();

                    for (int i = 0; i < 4; i++)
                    {
                        int src = rec.CopyToNew(i + 1, true);
                        rec.Increment(i % 2, true, src);
                    }
                    rec.InsertEndIf();

                    int srcAfter = rec.CopyToNew(10, true);
                    rec.Increment(0, true, srcAfter);
                }, maxCommandsPerChunk: THRESHOLD);

                // before
                string[] expectedBefore =
                {
            "ID0 [0,14) Children=0 Cmds:CopyTo,EqualsValue,If,NextSource,NextDestination,NextSource,NextDestination,NextSource,NextDestination,NextSource,NextDestination,EndIf,NextSource,NextDestination"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedBefore);

                acl.FinaliseCommandTree();

                // after
                string[] expectedAfter =
                {
            "ID0 .container [3,3) Children=2 Cmds:-",
            "ID3 .leaf [0,3) Children=0 Cmds:CopyTo,EqualsValue,If",
            "ID1 Conditional [3,13) Children=2 Cmds:If,NextSource,NextDestination,NextSource,NextDestination,NextSource,NextDestination,NextSource,NextDestination,EndIf",
            "ID4 [4,8) Children=0 Cmds:NextSource,NextDestination,NextSource,NextDestination",
            "ID5 [8,12) Children=0 Cmds:NextSource,NextDestination,NextSource,NextDestination",
            "ID2 [13,14) Children=0 Cmds:NextSource,NextDestination"
        };
                ArrayProcessingTestHelpers.DumpTree(acl).Should().Equal(expectedAfter);

                // Detailed pointer-advance behaviour is covered by execution-level tests.
            });
        }


    }
}
