// File: ChunkExecutorTests.cs

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using System.Collections.Generic;
using System.Linq;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Direct, chunk‑level tests for IChunkExecutor implementations.
    /// Uses Arrange/Act/Assert to validate each command type without ArrayCommandList.
    /// </summary>
    public abstract class ChunkExecutorTestBase
    {
        /// <summary>
        /// Construct the executor under test.
        /// </summary>
        protected abstract IChunkExecutor CreateExecutor();

        /// <summary>
        /// Prepare a chunk containing exactly the given commands.
        /// </summary>
        protected ArrayCommandChunk ArrangeChunk(params ArrayCommand[] commands)
        {
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };
            // Attach underlying commands buffer (executor implementations must reference this)
            UnderlyingCommands = commands;
            // Prepare a default virtual stack of adequate size
            int maxIndex = 0;
            foreach (var c in commands)
                maxIndex = Math.Max(maxIndex,
                    Math.Max(c.GetSourceIndexIfUsed(), c.GetTargetIndexIfUsed()));
            chunk.VirtualStack = new double[maxIndex + 1];
            return chunk;
        }

        /// <summary>
        /// UnderlyingCommands buffer for executors to read from.
        /// </summary>
        protected ArrayCommand[] UnderlyingCommands;

        /// <summary>
        /// Execute the chunk with the given executor.
        /// </summary>
        protected double[] ActExecute(
            ArrayCommandChunk chunk,
            double[] orderedSources,
            double[] orderedDestinations)
        {
            var executor = CreateExecutor();
            executor.AddToGeneration(chunk);
            executor.PerformGeneration();

            int cosi = 0;
            int codi = 0;
            bool condition = true;
            executor.Execute(
                chunk,
                chunk.VirtualStack,
                orderedSources,
                orderedDestinations,
                ref cosi,
                ref codi,
                ref condition);

            return chunk.VirtualStack;
        }

        [TestMethod]
        public void TestZeroCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.Zero, 2, -1);
            var chunk = ArrangeChunk(cmd);
            chunk.VirtualStack[2] = 123.4; // set non-default
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(0.0, vs[2], 1e-9);
        }

        [TestMethod]
        public void TestCopyToCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.CopyTo, 1, 0);
            var chunk = ArrangeChunk(cmd);
            chunk.VirtualStack[0] = 5.5;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(5.5, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestNextSourceCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.NextSource, 0, -1);
            var chunk = ArrangeChunk(cmd);
            double[] os = { 9.9 };
            double[] od = new double[0];
            var vs = ActExecute(chunk, os, od);
            Assert.AreEqual(9.9, vs[0], 1e-9);
        }

        [TestMethod]
        public void TestMultiplyByCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.MultiplyBy, 0, 1);
            var chunk = ArrangeChunk(cmd);
            chunk.VirtualStack[0] = 2.0;
            chunk.VirtualStack[1] = 3.5;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(7.0, vs[0], 1e-9);
        }

        [TestMethod]
        public void TestIncrementByCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1);
            var chunk = ArrangeChunk(cmd);
            chunk.VirtualStack[0] = 5.0;
            chunk.VirtualStack[1] = 2.5;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(7.5, vs[0], 1e-9);
        }

        [TestMethod]
        public void TestDecrementByCommand()
        {
            var cmd = new ArrayCommand(ArrayCommandType.DecrementBy, 0, 1);
            var chunk = ArrangeChunk(cmd);
            chunk.VirtualStack[0] = 5.0;
            chunk.VirtualStack[1] = 1.5;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(3.5, vs[0], 1e-9);
        }

        [TestMethod]
        public void TestIfConditionTrue()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.EqualsValue,        1, 1),  //  VS[1]==1 → true
                new ArrayCommand(ArrayCommandType.If,                 -1, -1),
                new ArrayCommand(ArrayCommandType.Zero,                1, -1), // should run
                new ArrayCommand(ArrayCommandType.EndIf,              -1, -1)
            };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[1] = 1.0;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(0.0, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestIfConditionFalse()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.EqualsValue,        1, 99), // false
                new ArrayCommand(ArrayCommandType.If,                 -1, -1),
                new ArrayCommand(ArrayCommandType.Zero,                1, -1), // should skip
                new ArrayCommand(ArrayCommandType.EndIf,              -1, -1)
            };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[1] = 7.7;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(7.7, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestMixedArithmeticSequences()
        {
            var cases = new[]
            {
        new
        {
            // Test 1: ((((vs[0]) + vs[0]) * (vs[0] + vs[0])) - vs[0]) copied to vs[1]
            Commands = new[]
            {
                // 1) vs[1] = 0                               (reserve slot)
                new ArrayCommand(ArrayCommandType.Zero,        1, -1),

                // 2) vs[2] = 0                               (reserve scratch)
                new ArrayCommand(ArrayCommandType.Zero,        2, -1),

                // 3) vs[2] = vs[0]                           // seed from vs[0]
                new ArrayCommand(ArrayCommandType.CopyTo,      2, 0),

                // 4) vs[2] += vs[0]                          // 5 + 5 = 10
                new ArrayCommand(ArrayCommandType.IncrementBy, 2, 0),

                // 5) vs[2] *= vs[2]                          // 10 * 10 = 100
                new ArrayCommand(ArrayCommandType.MultiplyBy,  2, 2),

                // 6) vs[2] -= vs[0]                          // 100 - 5 = 95
                new ArrayCommand(ArrayCommandType.DecrementBy, 2, 0),

                // 7) vs[1] = vs[2]                           // copy result
                new ArrayCommand(ArrayCommandType.CopyTo,      1, 2)
            },
            InitialStack   = new double[] { 5.0 }, // vs[0]=5
            OutputIndex    = 1,
            ExpectedValue  = 95.0
        },
        new
        {
            // Test 2: ((vs[0] * vs[0]) + vs[0]) copied to vs[3]
            Commands = new[]
            {
                // 1) vs[3] = 0                               (reserve slot)
                new ArrayCommand(ArrayCommandType.Zero,        3, -1),

                // 2) vs[4] = 0                               (reserve scratch)
                new ArrayCommand(ArrayCommandType.Zero,        4, -1),

                // 3) vs[4] = vs[0]                           // seed
                new ArrayCommand(ArrayCommandType.CopyTo,      4, 0),

                // 4) vs[4] *= vs[0]                          // square: 3*3=9
                new ArrayCommand(ArrayCommandType.MultiplyBy,  4, 0),

                // 5) vs[4] += vs[0]                          // add: 9+3=12
                new ArrayCommand(ArrayCommandType.IncrementBy, 4, 0),

                // 6) vs[3] = vs[4]                           // copy result
                new ArrayCommand(ArrayCommandType.CopyTo,      3, 4)
            },
            InitialStack   = new double[] { 3.0 }, // vs[0]=3
            OutputIndex    = 3,
            ExpectedValue  = 12.0
        },
        new
        {
            // Test 3: (vs[0] + vs[1]) * (vs[0] - vs[1]) copied to vs[5]
            Commands = new[]
            {
                // 1) vs[2] = 0                               (reserve for sum)
                new ArrayCommand(ArrayCommandType.Zero,        2, -1),

                // 2) vs[3] = 0                               (reserve for diff)
                new ArrayCommand(ArrayCommandType.Zero,        3, -1),

                // 3) vs[2] = vs[0]                           // seed sum
                new ArrayCommand(ArrayCommandType.CopyTo,      2, 0),

                // 4) vs[2] += vs[1]                          // sum: a+b
                new ArrayCommand(ArrayCommandType.IncrementBy, 2, 1),

                // 5) vs[3] = vs[0]                           // seed diff
                new ArrayCommand(ArrayCommandType.CopyTo,      3, 0),

                // 6) vs[3] -= vs[1]                          // diff: a-b
                new ArrayCommand(ArrayCommandType.DecrementBy, 3, 1),

                // 7) vs[2] *= vs[3]                          // product: (a+b)*(a-b)
                new ArrayCommand(ArrayCommandType.MultiplyBy,  2, 3),

                // 8) vs[5] = vs[2]                           // copy final result
                new ArrayCommand(ArrayCommandType.CopyTo,      5, 2)
            },
            InitialStack   = new double[] { 7.0, 3.0 }, // vs[0]=7, vs[1]=3
            OutputIndex    = 5,
            ExpectedValue  = (7.0 + 3.0) * (7.0 - 3.0)    // 10 * 4 = 40
        },
        new
        {
            // Test 4: (vs[0]^2) - vs[0] copied to vs[1]
            Commands = new[]
            {
                // 1) vs[1] = 0                               (reserve slot)
                new ArrayCommand(ArrayCommandType.Zero,        1, -1),

                // 2) vs[1] = vs[0]                           // seed
                new ArrayCommand(ArrayCommandType.CopyTo,      1, 0),

                // 3) vs[1] *= vs[0]                          // square
                new ArrayCommand(ArrayCommandType.MultiplyBy,  1, 0),

                // 4) vs[1] -= vs[0]                          // subtract original
                new ArrayCommand(ArrayCommandType.DecrementBy, 1, 0)
            },
            InitialStack   = new double[] { 4.0 }, // vs[0]=4
            OutputIndex    = 1,
            ExpectedValue  = 4.0*4.0 - 4.0         // 16 - 4 = 12
        }
    };

            foreach (var tc in cases)
            {
                // Arrange
                var chunk = ArrangeChunk(tc.Commands);
                for (int i = 0; i < tc.InitialStack.Length; i++)
                    chunk.VirtualStack[i] = tc.InitialStack[i];

                // Act
                var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());

                // Assert
                Assert.AreEqual(
                    tc.ExpectedValue,
                    vs[tc.OutputIndex],
                    1e-9,
                    $"Failed for OutputIndex={tc.OutputIndex}; sequence = {string.Join("→", tc.Commands.Select(c => c.CommandType))}");
            }
        }




        [TestMethod]
        public void TestOrderedSrcs()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.NextSource,      0, -1), // vs[0] := os[0]
                new ArrayCommand(ArrayCommandType.NextSource,      1, -1), // vs[1] := os[1]
                new ArrayCommand(ArrayCommandType.IncrementBy,     0, 1),  // vs[0] += vs[1]
            };
            var chunk = ArrangeChunk(cmds);
            double[] os = { 2.0, 3.0 };
            double[] od = new double[0];
            var vs = ActExecute(chunk, os, od);

            // vs[0] = 2+3 = 5
            Assert.AreEqual(5.0, vs[0], 1e-9);
            Assert.AreEqual(3.0, vs[1], 1e-9);

            // And confirm pointers on chunk
            Assert.AreEqual(2, chunk.StartSourceIndices);
        }

        [TestMethod]
        public void OrderedDestinations_TakenIf_WritesApplyInOrder_Chunk()
        {
            // vs[0] := os[0]; vs[1] := os[1];
            // if (vs[0] == 0) { od[codi++] += vs[0]; od[codi++] += vs[1]; }
            // od[codi++] += vs[1];
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.NextSource,       0, -1),
                new ArrayCommand(ArrayCommandType.NextSource,       1, -1),
                new ArrayCommand(ArrayCommandType.EqualsValue,      0, 0),
                new ArrayCommand(ArrayCommandType.If,              -1, -1),

                    new ArrayCommand(ArrayCommandType.NextDestination, 0, -1), // use vs[0]
                    new ArrayCommand(ArrayCommandType.NextDestination, 1, -1), // use vs[1]

                new ArrayCommand(ArrayCommandType.EndIf,           -1, -1),

                new ArrayCommand(ArrayCommandType.NextDestination, 1, -1),     // post-IF: vs[1]
            };


            var chunk = ArrangeChunk(cmds);

            var executor = CreateExecutor();
            executor.AddToGeneration(chunk);
            executor.PerformGeneration();

            double[] os = { 0.0, 7.0 };   // make condition TRUE; vs[1]=7 marker
            double[] od = new double[16];  // generous capacity to avoid OOB in non-planned executors
            int cosi = 0, codi = 0;
            bool cond = true;

            executor.Execute(chunk, chunk.VirtualStack, os, od, ref cosi, ref codi, ref cond);

            // THEN taken: first two writes land in od[0], od[1]; post-IF write in od[2]
            Assert.AreEqual(0.0, od[0], 1e-12, "THEN #1 should add vs[0]=0 to od[0]");
            Assert.AreEqual(7.0, od[1], 1e-12, "THEN #2 should add vs[1]=7 to od[1]");
            Assert.AreEqual(7.0, od[2], 1e-12, "post-IF write should add vs[1]=7 to od[2]");

            // Pointer accounting: three destinations consumed
            Assert.AreEqual(3, codi, "codi must reflect three destination consumptions");
        }

        [TestMethod]
        public void OrderedDestinations_SkippedIf_AdvancesCodi_Chunk()
        {
            // vs[0] := os[0]; vs[1] := os[1];
            // if (vs[0] == 999) { od[codi++] += vs[0]; od[codi++] += vs[1]; }  // skipped
            // od[codi++] += vs[1];  // must land at od[2] because two destinations were skipped
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.NextSource,       0, -1),
                new ArrayCommand(ArrayCommandType.NextSource,       1, -1),
                new ArrayCommand(ArrayCommandType.EqualsValue,      0, 999),
                new ArrayCommand(ArrayCommandType.If,              -1, -1),

                    new ArrayCommand(ArrayCommandType.NextDestination, 0, -1), // skipped, but must advance codi
                    new ArrayCommand(ArrayCommandType.NextDestination, 1, -1), // skipped, but must advance codi

                new ArrayCommand(ArrayCommandType.EndIf,           -1, -1),

                new ArrayCommand(ArrayCommandType.NextDestination, 1, -1),     // post-IF: vs[1]
            };


            var chunk = ArrangeChunk(cmds);

            var executor = CreateExecutor();
            executor.AddToGeneration(chunk);
            executor.PerformGeneration();

            double[] os = { 123.0, 22.0 }; // make condition FALSE; vs[1]=22 marker
            double[] od = new double[16];   // generous capacity to avoid OOB in non-planned executors
            int cosi = 0, codi = 0;
            bool cond = true;

            executor.Execute(chunk, chunk.VirtualStack, os, od, ref cosi, ref codi, ref cond);

            // THEN skipped: no writes to the first two OD slots
            Assert.AreEqual(0.0, od[0], 1e-12, "skipped THEN must not write od[0]");
            Assert.AreEqual(0.0, od[1], 1e-12, "skipped THEN must not write od[1]");

            // Post-IF write must respect DstSkip and land after the two skipped destinations
            Assert.AreEqual(22.0, od[2], 1e-12, "post-IF write must land in od[2] after skipped positions");

            // Pointer accounting: DstSkip (2) + post-IF (1) = 3
            Assert.AreEqual(3, codi, "codi must advance by skipped DstSkip plus the post-IF write");
        }



        [TestMethod]
        public void TestComparisonOperatorsDirectly()
        {
            // Each tuple: (cmdType, leftValue, rightValueOrIndex, isIndexCompare, expectedBool)
            var cases = new (ArrayCommandType cmdType, double left, double right, bool isIndex, bool expected)[]
            {
        // EqualsOtherArrayIndex
        (ArrayCommandType.EqualsOtherArrayIndex,      5, 5,  true,  true),
        (ArrayCommandType.EqualsOtherArrayIndex,      5, 4,  true,  false),
        (ArrayCommandType.EqualsOtherArrayIndex,      4, 5,  true,  false),

        // NotEqualsOtherArrayIndex
        (ArrayCommandType.NotEqualsOtherArrayIndex,   5, 5,  true,  false),
        (ArrayCommandType.NotEqualsOtherArrayIndex,   5, 4,  true,  true),
        (ArrayCommandType.NotEqualsOtherArrayIndex,   4, 5,  true,  true),

        // GreaterThanOtherArrayIndex (left > right)
        (ArrayCommandType.GreaterThanOtherArrayIndex, 5, 4,  true,  true),
        (ArrayCommandType.GreaterThanOtherArrayIndex, 5, 5,  true,  false),
        (ArrayCommandType.GreaterThanOtherArrayIndex, 4, 5,  true,  false),

        // LessThanOtherArrayIndex (left < right)
        (ArrayCommandType.LessThanOtherArrayIndex,    4, 5,  true,  true),
        (ArrayCommandType.LessThanOtherArrayIndex,    5, 5,  true,  false),
        (ArrayCommandType.LessThanOtherArrayIndex,    5, 4,  true,  false),

        // EqualsValue
        (ArrayCommandType.EqualsValue,                3, 3,  false, true),
        (ArrayCommandType.EqualsValue,                3, 4,  false, false),
        (ArrayCommandType.EqualsValue,                4, 3,  false, false),

        // NotEqualsValue
        (ArrayCommandType.NotEqualsValue,             3, 3,  false, false),
        (ArrayCommandType.NotEqualsValue,             3, 4,  false, true),
        (ArrayCommandType.NotEqualsValue,             4, 3,  false, true),
            };

            foreach (var (cmdType, leftVal, rightVal, isIndex, expected) in cases)
            {
                int leftIdx = 0;
                int src = isIndex ? 1 : (int)rightVal;

                // Build the chunk with a single comparison
                var cmd = new ArrayCommand(cmdType, leftIdx, src);
                var chunk = ArrangeChunk(cmd);

                // Seed the stack
                chunk.VirtualStack[leftIdx] = leftVal;
                if (isIndex)
                    chunk.VirtualStack[1] = rightVal;

                // Execute
                var executor = CreateExecutor();
                executor.AddToGeneration(chunk);
                executor.PerformGeneration();

                int cosi = 0;
                int codi = 0;
                bool condition = true;
                executor.Execute(
                    chunk,
                    chunk.VirtualStack,
                    Array.Empty<double>(),
                    Array.Empty<double>(),
                    ref cosi,
                    ref codi,
                    ref condition);

                // Assert
                Assert.AreEqual(
                    expected,
                    condition,
                    $"Comparison {cmdType} failed for left={leftVal}, right={rightVal}");
            }
        }




        [TestMethod]
        public void TestNestedIfPointerAdvancesOnSkip()
        {
            var cmds = new[]
            {
        // Compare vs[0] == 999? false
        new ArrayCommand(ArrayCommandType.EqualsValue,       0, 999),
        new ArrayCommand(ArrayCommandType.If,               -1, -1),
            // skipped:
            new ArrayCommand(ArrayCommandType.NextSource,   0, -1),
        new ArrayCommand(ArrayCommandType.EndIf,            -1, -1),
        // real NextSource:
        new ArrayCommand(ArrayCommandType.NextSource,       1, -1)
    };
            var chunk = ArrangeChunk(cmds);
            double[] os = { 11, 22 };
            double[] od = new double[0];
            var vs = ActExecute(chunk, os, od);

            // The skipped NextSource / NextDestination still consumed os[0] and reserved od[0]
            Assert.AreEqual(2, chunk.StartSourceIndices, "cosi should be 2");
            // And vs[1] gets the second source:
            Assert.AreEqual(22, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestCommentAndBlankNoops()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.Blank,   -1, -1),
                new ArrayCommand(ArrayCommandType.IncrementDepth,   -1, -1),
                new ArrayCommand(ArrayCommandType.DecrementDepth,   -1, -1),
                new ArrayCommand(ArrayCommandType.Comment, -1,  0),
                new ArrayCommand(ArrayCommandType.Zero,     2, -1)
            };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[2] = 5.0;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(0.0, vs[2], 1e-9);
        }

        [TestMethod]
        public void TestIfSkip_MultiPointerAdvances()
        {
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.EqualsValue, 0, 999), // cond = false
        new ArrayCommand(ArrayCommandType.If, -1, -1),
            new ArrayCommand(ArrayCommandType.NextSource,      0, -1),
            new ArrayCommand(ArrayCommandType.NextSource,      1, -1),
        new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
        new ArrayCommand(ArrayCommandType.NextSource,          2, -1)
    };
            var chunk = ArrangeChunk(cmds);
            double[] os = { 10, 20, 30 };
            double[] od = new double[0];
            ActExecute(chunk, os, od);

            Assert.AreEqual(3, chunk.StartSourceIndices);
            Assert.AreEqual(30, chunk.VirtualStack[2], 1e-9);
        }

        [TestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public void TestNestedIf_Combinations(bool a, bool b)
        {
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.EqualsValue, 0, 1),   // A
        new ArrayCommand(ArrayCommandType.If, -1, -1),
            new ArrayCommand(ArrayCommandType.NextSource,  2, -1),
            new ArrayCommand(ArrayCommandType.EqualsValue, 1, 1), // B
            new ArrayCommand(ArrayCommandType.If, -1, -1),
            new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
        new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
    };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[0] = a ? 1 : 0;
            chunk.VirtualStack[1] = b ? 1 : 0;
            double[] os = { 99 };
            double[] od = new double[2];
            ActExecute(chunk, os, od);

            Assert.AreEqual(1, chunk.StartSourceIndices);   // one NextSource accounted
        }

        [TestMethod]
        public void TestLocalReuse_FlushBeforeRebind()
        {
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.Zero,        0, -1),
        new ArrayCommand(ArrayCommandType.EqualsValue, 2, 5),   // cond=true
        new ArrayCommand(ArrayCommandType.If,         -1, -1),
            new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),
        new ArrayCommand(ArrayCommandType.EndIf,      -1, -1),
        new ArrayCommand(ArrayCommandType.CopyTo,      1, 0)
    };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[2] = 5.0;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(5.0, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestDepthRule_NoReuseAcrossDeeperScope()
        {
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.If, -1, -1),
            new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
        new ArrayCommand(ArrayCommandType.If, -1, -1),
            new ArrayCommand(ArrayCommandType.CopyTo, 1, 0),
        new ArrayCommand(ArrayCommandType.EndIf, -1, -1)
    };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[0] = 7.0;
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(7.0, vs[1], 1e-9);
        }

        [TestMethod]
        public void TestManySingleUseSlots_NonReuseMode()
        {
            const int N = 50;
            var list = new List<ArrayCommand>();
            for (int i = 0; i < N; i++)
                list.Add(new ArrayCommand(ArrayCommandType.Zero, i, -1));
            var chunk = ArrangeChunk(list.ToArray());
            ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            for (int i = 0; i < N; i++)
                Assert.AreEqual(0.0, chunk.VirtualStack[i], 1e-9);
        }

        [TestMethod]
        public void TestComparisonOperators_ExtendedSweep()
        {
            (double l, double r)[] pairs = { (-2, -3), (0, 0), (1e9, 1e9 - 1), (5, 5) };
            var ops = new[]
            {
        ArrayCommandType.EqualsOtherArrayIndex,
        ArrayCommandType.NotEqualsOtherArrayIndex,
        ArrayCommandType.GreaterThanOtherArrayIndex,
        ArrayCommandType.LessThanOtherArrayIndex
    };
            foreach (var (left, right) in pairs)
            {
                bool[] expect = { left == right, left != right, left > right, left < right };
                for (int k = 0; k < ops.Length; k++)
                {
                    var cmd = new ArrayCommand(ops[k], 0, 1);
                    var chunk = ArrangeChunk(cmd);
                    chunk.VirtualStack[0] = left;
                    chunk.VirtualStack[1] = right;
                    bool cond = true;
                    var exec = CreateExecutor();
                    exec.AddToGeneration(chunk); exec.PerformGeneration();
                    int cosi = 0;
                    int codi = 0;
                    exec.Execute(chunk, chunk.VirtualStack,
                                 Array.Empty<double>(), Array.Empty<double>(), ref cosi, ref codi,
                                 ref cond);
                    Assert.AreEqual(expect[k], cond, $"{ops[k]} ({left},{right})");
                }
            }
        }
        [TestMethod]
        public void TestSkippedBranchFlush()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.CopyTo,      1, 0),
                new ArrayCommand(ArrayCommandType.EqualsValue, 1, 0),
                new ArrayCommand(ArrayCommandType.If,         -1, -1),
                new ArrayCommand(ArrayCommandType.Zero,        1, -1),
                new ArrayCommand(ArrayCommandType.EndIf,      -1, -1)
            };
            var chunk = ArrangeChunk(cmds);
            chunk.VirtualStack[0] = 7.7;              // Set vs[0] non-zero so cond will be false
            var vs = ActExecute(chunk, Array.Empty<double>(), Array.Empty<double>());
            Assert.AreEqual(7.7, vs[1], 1e-9, "vs[1] should remain 7.7 if branch is correctly skipped");
        }
        [TestMethod]
        public void TestNextSource_SkippedIf_LocalReuse_Fails()
        {
            var cmds = new[]
            {
                // ─────────── Preamble ───────────
                // vs[0] = 0   (writes slot‑0, but we never change it again)
                new ArrayCommand(ArrayCommandType.NextSource,       0, -1),

                // vs[1] = 1   (writes slot‑1 **only to the local**)   ← dirty on entry
                new ArrayCommand(ArrayCommandType.NextSource,       1, -1),

                // cond ← (vs[0] != 0) ▶ FALSE  → outer branch is skipped
                new ArrayCommand(ArrayCommandType.NotEqualsValue,   0,  0),

                // ─────────── Outer IF (skipped) ───────────
                new ArrayCommand(ArrayCommandType.If,              -1, -1),

                    // inner‑if guard (any comparison that *mentions slot‑1*)
                    new ArrayCommand(ArrayCommandType.EqualsValue, 1, 1),
                    new ArrayCommand(ArrayCommandType.If,         -1, -1),

                        // last *use* of slot‑1 – makes the interval end *inside* ​the skipped branch
                        new ArrayCommand(ArrayCommandType.IncrementBy, 1, 1),

                    new ArrayCommand(ArrayCommandType.EndIf,       -1, -1),

                new ArrayCommand(ArrayCommandType.EndIf,            -1, -1),
            };


            // Arrange
            var chunk = ArrangeChunk(cmds);
            double[] os = { 0.0, 1.0 };                   // deliberately 0 then 1
            double[] od = new double[0];
            // Act
            var vs = ActExecute(chunk, os, od);

            // Assert – should be 1.0 even though branch was skipped
            Assert.AreEqual(1.0, vs[1], 1e-9,
                "vs[1] should remain 1 if NextSource wrote back correctly across skipped branch");
        }

        [TestMethod]
        public void OrderedDestinations_NestedIf_OuterTaken_InnerSkipped_Chunk()
        {
            // Arrange: outer cond TRUE (vs[0]==0), inner cond FALSE (vs[1]==999)
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.NextSource,       0, -1), // vs[0] := os[0] (= 0)
                new ArrayCommand(ArrayCommandType.NextSource,       1, -1), // vs[1] := os[1] (= 7)
                new ArrayCommand(ArrayCommandType.EqualsValue,      0, 0),  // outer: true
                new ArrayCommand(ArrayCommandType.If,              -1, -1),

                    new ArrayCommand(ArrayCommandType.EqualsValue,  1, 999), // inner: false
                    new ArrayCommand(ArrayCommandType.If,          -1, -1),

                        new ArrayCommand(ArrayCommandType.NextDestination, 0, -1), // skipped, must advance codi by 1

                    new ArrayCommand(ArrayCommandType.EndIf,       -1, -1),

                    new ArrayCommand(ArrayCommandType.NextDestination, 1, -1), // THEN write after inner-if

                new ArrayCommand(ArrayCommandType.EndIf,           -1, -1),

                new ArrayCommand(ArrayCommandType.NextDestination, 1, -1) // post-IF write
            };

            var chunk = ArrangeChunk(cmds);
            var exec = CreateExecutor(); exec.AddToGeneration(chunk); exec.PerformGeneration();

            double[] os = { 0.0, 7.0 }; // force outer=true, inner=false
            double[] od = new double[8];
            int cosi = 0, codi = 0; bool cond = true;

            // Act
            exec.Execute(chunk, chunk.VirtualStack, os, od, ref cosi, ref codi, ref cond);

            // Assert: inner skip advanced codi from 0 → 1; THEN write → od[1]; post-IF write → od[2]
            Assert.AreEqual(0.0, od[0], 1e-12, "skipped inner NextDestination must not write");
            Assert.AreEqual(7.0, od[1], 1e-12, "outer THEN write should add vs[1]=7 to od[1]");
            Assert.AreEqual(7.0, od[2], 1e-12, "post-IF write should add vs[1]=7 to od[2]");
            Assert.AreEqual(3, codi, "one skipped + two executed writes");
        }

        [TestMethod]
        public void OrderedDestinations_UnmatchedIfTail_AdvancesCodi_OnSkip_Chunk()
        {
            // Arrange a chunk that ends inside an IF (no EndIf in-range).
            // Condition FALSE → the tail ELSE-logic must advance codi by DstSkip.
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.EqualsValue,      0, 999), // false
                new ArrayCommand(ArrayCommandType.If,              -1, -1),

                    new ArrayCommand(ArrayCommandType.NextDestination, 0, -1),
                    new ArrayCommand(ArrayCommandType.NextDestination, 1, -1),

                // EndIf deliberately omitted from the chunk range
            };

            var chunk = ArrangeChunk(cmds);
            var exec = CreateExecutor(); exec.AddToGeneration(chunk); exec.PerformGeneration();

            double[] os = Array.Empty<double>();
            double[] od = new double[8];
            int cosi = 0, codi = 0; bool cond = true;

            // Act
            exec.Execute(chunk, chunk.VirtualStack, os, od, ref cosi, ref codi, ref cond);

            // Assert: two destinations were skipped → codi advanced by 2; no writes occurred
            Assert.AreEqual(0.0, od[0], 1e-12);
            Assert.AreEqual(0.0, od[1], 1e-12);
            Assert.AreEqual(2, codi);
        }
        [TestMethod]
        public void OrderedDestinations_IgnoresSourceIndex_UsesIndexSlot_Chunk()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.Zero,              0, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy,       0, 0),     // vs[0]=0+0 = 0 (keeps it simple)
                new ArrayCommand(ArrayCommandType.NextDestination,   0, 999)    // bogus SourceIndex must be ignored
            };
            var chunk = ArrangeChunk(cmds);
            var exec = CreateExecutor(); exec.AddToGeneration(chunk); exec.PerformGeneration();

            double[] od = new double[4];
            int cosi = 0, codi = 0; bool cond = true;
            exec.Execute(chunk, chunk.VirtualStack, Array.Empty<double>(), od, ref cosi, ref codi, ref cond);

            Assert.AreEqual(0.0, od[0], 1e-12, "must add vs[0], not a value derived from SourceIndex");
            Assert.AreEqual(1, codi);
        }

    }
    [TestClass]
    public class ChunkExecutorTests_Interpreter : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.Interpreted,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false);
    }

    [TestClass]
    public class ChunkExecutorTests_IL : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.IL,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false);
    }

    [TestClass]
    public class ChunkExecutorTests_ILWithPlanner : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.ILWithLocalVariableRecycling,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false);
    }

    [TestClass]
    public class ChunkExecutorTests_Roslyn : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.Roslyn,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false);
    }

    [TestClass]
    public class ChunkExecutorTests_Roslyn_WithPlanner : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.RoslynWithLocalVariableRecycling,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false);
    }

    // ────────────────────────────────────────────────────────────────
    //  NEW: Roslyn (local-reuse) + Interpreter fallback
    //      — Interpreter for chunks ≤ 5 commands, Roslyn for larger.
    // ────────────────────────────────────────────────────────────────
    [TestClass]
    public class ChunkExecutorTests_Roslyn_Fallback : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() =>
            ChunkExecutorFactory.Create(
                ChunkExecutorKind.RoslynWithLocalVariableRecycling,
                UnderlyingCommands,
                0,
                UnderlyingCommands.Length,
                useCheckpoints: false,
                fallbackThreshold: 5);   // “small” chunk = 5 cmds or fewer
    }


}
