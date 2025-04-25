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
        // -----------------------------------------------------------------------------
        //  SimpleHoist  –  verifies the exact tree listing before and after hoisting
        // -----------------------------------------------------------------------------
        [TestMethod]
        public void SimpleHoist()
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






    }
}
