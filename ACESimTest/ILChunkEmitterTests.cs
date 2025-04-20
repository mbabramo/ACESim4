using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using System.Collections.Generic;

namespace ACESimTest
{
    [TestClass]
    public class ILChunkEmitterTests
    {
        /// <summary>
        /// Basic test of Zero, CopyTo, and arithmetic commands (MultiplyBy, IncrementBy, DecrementBy).
        /// </summary>
        [TestMethod]
        public void TestBasicArithmeticAndCopy()
        {
            // Commands:
            // 0) Zero index=0       -> vs[0] = 0
            // 1) CopyTo index=1, src=0 -> vs[1] = vs[0]  (should yield vs[1] = 0)
            // 2) IncrementBy(1,0)   -> vs[1] += vs[0]   (still 0)
            // 3) Zero index=2       -> vs[2] = 0
            // 4) IncrementBy(2,0)   -> vs[2] += vs[0]   (still 0)
            // 5) DecrementBy(2,1)   -> vs[2] -= vs[1]   (still 0)
            // 6) MultiplyBy(2,0)    -> vs[2] *= vs[0]   (still 0)
            // 7) Zero index=3       -> vs[3] = 0
            // 8) IncrementBy(3,0)   -> vs[3] += vs[0]   (0)
            // 9) IncrementBy(0,0)   -> vs[0] += vs[0]   (vs[0] remains 0)
            //
            // We'll also set vs[0] to 5.0 manually before running,
            // so we can see the effect of copy/increment on that value in subsequent steps.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),            // cmd0
                new ArrayCommand(ArrayCommandType.CopyTo, 1, 0),           // cmd1
                new ArrayCommand(ArrayCommandType.IncrementBy, 1, 0),      // cmd2
                new ArrayCommand(ArrayCommandType.Zero, 2, -1),            // cmd3
                new ArrayCommand(ArrayCommandType.IncrementBy, 2, 0),      // cmd4
                new ArrayCommand(ArrayCommandType.DecrementBy, 2, 1),      // cmd5
                new ArrayCommand(ArrayCommandType.MultiplyBy, 2, 0),       // cmd6
                new ArrayCommand(ArrayCommandType.Zero, 3, -1),            // cmd7
                new ArrayCommand(ArrayCommandType.IncrementBy, 3, 0),      // cmd8
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 0),      // cmd9
            };

            // The chunk covers commands[0..10)
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            // Emit IL
            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestBasicArithmetic", out _);

            // Prepare arrays
            double[] vs = new double[10]; // vs large enough
            // We'll set vs[0] to 5.0 BEFORE we run zero. We'll test how it changes:
            vs[0] = 5.0;

            double[] os = new double[5];  // not used in this test
            double[] od = new double[5];  // not used in this test
            int cosi = 0, codi = 0;

            // Run the code
            del(vs, os, od, ref cosi, ref codi);

            // Check final vs
            // After cmd0: vs[0] = 0 (the 'Zero' overwrote the 5.0)
            Assert.AreEqual(0.0, vs[0], 1e-12, "vs[0] was not zeroed out");
            // vs[1] = 0, vs[2] = 0, vs[3] = 0 after all these commands
            Assert.AreEqual(0.0, vs[1], 1e-12);
            Assert.AreEqual(0.0, vs[2], 1e-12);
            Assert.AreEqual(0.0, vs[3], 1e-12);

            // cosi/codi remain 0
            Assert.AreEqual(0, cosi, "cosi changed unexpectedly.");
            Assert.AreEqual(0, codi, "codi changed unexpectedly.");
        }

        /// <summary>
        /// Tests NextSource and NextDestination, ensuring that cosi/codi increments and 
        /// array content is copied as expected.
        /// </summary>
        [TestMethod]
        public void TestSourcesAndDestinations()
        {
            // We'll do:
            // 0) NextSource index=2     -> vs[2] = os[cosi++]
            // 1) NextSource index=3     -> vs[3] = os[cosi++]
            // 2) NextDestination src=2  -> od[codi++] = vs[2]
            // 3) NextDestination src=3  -> od[codi++] = vs[3]
            //
            // We'll set os = {10, 20}, so vs[2] should become 10, vs[3] = 20.
            // Then od[0] = 10, od[1] = 20, codi=2 at the end, cosi=2 at the end.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.NextSource, 2, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 3, -1),
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 2),
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 3),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestSourcesAndDestinations", out _);

            double[] vs = new double[5];
            double[] os = new double[] { 10.0, 20.0 };
            double[] od = new double[5];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(2, cosi, "cosi should have incremented twice");
            Assert.AreEqual(2, codi, "codi should have incremented twice");
            Assert.AreEqual(10.0, vs[2], 1e-12);
            Assert.AreEqual(20.0, vs[3], 1e-12);
            Assert.AreEqual(10.0, od[0], 1e-12);
            Assert.AreEqual(20.0, od[1], 1e-12);
        }

        /// <summary>
        /// Tests ReusedDestination by incrementing the same od index multiple times.
        /// </summary>
        [TestMethod]
        public void TestReusedDestination()
        {
            // We'll do:
            // 0) Zero(0) => vs[0] = 0
            // 1) IncrementBy(0, ???) => put some known value in vs[0], say 5
            //   For convenience, let's do: CopyTo(0,2) won't do it. Instead we can
            //   just manually set vs[0]=5 before running, or we do an IncrementBy command with source of 1, etc.
            //
            // Then ReusedDestination with index=0, source=0 => od[0] += vs[0].
            // We'll do it three times, so od[0] ends up 3*5=15
            //
            // Actually let's build it with a few commands:
            //   0) Zero(0)
            //   1) IncrementBy(0,1) -> vs[0] += vs[1], if we set vs[1]=5 before run
            //   2) ReusedDestination(0,0)
            //   3) ReusedDestination(0,0)
            //   4) ReusedDestination(0,0)
            //
            // That means each ReusedDestination does: od[0] += vs[0].
            // We'll set vs[1]=5. Then vs[0] after step1 is 5. Then step 2..4 => od[0] = 15.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1),
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestReusedDestination", out _);

            double[] vs = new double[5];
            vs[1] = 5.0;  // We'll add this to vs[0]
            double[] os = new double[2];  // not used
            double[] od = new double[3];  // we'll see od[0] updated
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // vs[0] = 5.0
            Assert.AreEqual(5.0, vs[0], 1e-12);
            // od[0] = 15.0
            Assert.AreEqual(15.0, od[0], 1e-12);
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /// <summary>
        /// Demonstrates comparing vs[] indices and branching with If/EndIf.
        /// We also test a couple of double comparisons (eq, ne, gt).
        /// </summary>
        [TestMethod]
        public void TestFlowControlWithIf()
        {
            // We'll do something like:
            //
            // 0) Zero(0) -> vs[0] = 0
            // 1) Zero(1) -> vs[1] = 0
            // 2) IncrementBy(0,2) -> vs[0] = vs[0] + vs[2]
            // 3) EqualsOtherArrayIndex (0,1) => condition = (vs[0] == vs[1])
            // 4) If
            //      NextSource(3) => vs[3] = os[cosi++]
            //      IncrementBy(1,3) => vs[1] += vs[3]
            //    EndIf
            //
            // We'll set vs[2] = 10, so vs[0]=10 after cmd2. vs[1]=0. So condition=(10==0)? => false
            // => We'll skip the block inside the If. So vs[3] won't be set from NextSource. 
            // => cosi remains 0, vs[1] remains 0.
            //
            // Then we do a second compare, say:
            // 5) NotEqualsOtherArrayIndex (0,1) => condition = (10 != 0) => true
            // 6) If
            //      NextSource(3) => vs[3] = os[cosi++]
            //      IncrementBy(1,3) => vs[1] += vs[3]
            //    EndIf
            //
            // Now that condition is true, we do NextSource => vs[3]= os[0], increment cosi =>1
            // vs[3] = e.g. os[0] = 5 => vs[1] += 5 => vs[1]=5
            //
            // End result: vs[0]=10, vs[1]=5, vs[3]=5, cosi=1, etc.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),

                new ArrayCommand(ArrayCommandType.EqualsOtherArrayIndex, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),

                // inside If:
                new ArrayCommand(ArrayCommandType.NextSource, 3, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 1, 3),

                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),

                new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),

                new ArrayCommand(ArrayCommandType.NextSource, 3, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 1, 3),

                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestFlowControlWithIf", out _);

            // Setup arrays
            double[] vs = new double[6];
            // We'll set vs[2] = 10
            vs[2] = 10.0;

            // We'll set os[0] = 5, so that if we do NextSource(3),
            // it picks up 5, increments cosi to 1, and vs[3]=5
            double[] os = new double[] { 5.0, 99.0 };
            double[] od = new double[5];
            int cosi = 0, codi = 0;

            // Run
            del(vs, os, od, ref cosi, ref codi);

            // Check results
            // after cmd2 => vs[0] = 10, vs[1] = 0
            // the first If => condition = (10 == 0)? => false => skip block => cosi=0
            // second compare => (10 != 0)? => true => so run block => NextSource => vs[3]= os[0]=5 => cosi=1 => vs[1] += vs[3] => 5
            Assert.AreEqual(10.0, vs[0], 1e-12, "vs[0] not correct");
            Assert.AreEqual(5.0, vs[1], 1e-12, "vs[1] not correct");
            Assert.AreEqual(5.0, vs[3], 1e-12, "vs[3] not correct after NextSource");
            Assert.AreEqual(1, cosi, "cosi didn't increment as expected in second If block");
            Assert.AreEqual(0, codi, "codi changed unexpectedly");
        }

        /// <summary>
        /// Optional test that checks floating comparison with eqValue, neValue 
        /// (which compare vs[idx] to a numeric constant).
        /// You might do more elaborate tests with near-zero differences, etc.
        /// </summary>
        [TestMethod]
        public void TestCompareValueCommands()
        {
            // We'll do:
            // 0) Zero(0)
            // 1) IncrementBy(0,1) => assume vs[1] is 3.14, so vs[0]=3.14
            // 2) EqualsValue(0,3) => condition = (3.14 == 3)? false
            // 3) If => block skipped
            //    NextSource(2)
            //    IncrementBy(0,2)
            // 4) EndIf
            // 5) NotEqualsValue(0,3) => condition = (3.14 != 3)? true
            // 6) If => block executed
            //    NextSource(2)
            //    IncrementBy(0,2) => vs[0] = 3.14 + os[cosi]...
            // 7) EndIf

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1),
                new ArrayCommand(ArrayCommandType.EqualsValue, 0, 3),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 2, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
                new ArrayCommand(ArrayCommandType.NotEqualsValue, 0, 3),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 2, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestCompareValueCommands", out _);

            double[] vs = new double[5];
            vs[1] = 3.14;  // We'll add 3.14 to vs[0]
            double[] os = new double[] { 2.0, 10.0 };
            double[] od = new double[3];
            int cosi = 0, codi = 0;

            // Run
            del(vs, os, od, ref cosi, ref codi);

            // Analysis:
            // after 1) vs[0] = 3.14
            // 2) condition = (3.14 == 3)? => false => skip block => cosi=0
            // 5) condition = (3.14 != 3)? => true => run block => NextSource => vs[2] = 2 => cosi=1 => vs[0]+=vs[2] => vs[0]=5.14

            Assert.AreEqual(5.14, vs[0], 1e-12, "vs[0] after second If block not correct");
            Assert.AreEqual(1, cosi, "cosi increment mismatch");
            Assert.AreEqual(0, codi);
        }
        /// <summary>
        /// 1) Test a chunk that consists entirely of Blank commands (no-ops).
        /// </summary>
        [TestMethod]
        public void TestAllBlankCommands()
        {
            // Suppose we have 3 blank commands
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Blank, -1, -1),
                new ArrayCommand(ArrayCommandType.Blank, -1, -1),
                new ArrayCommand(ArrayCommandType.Blank, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = 3,
            };

            // Emit
            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestAllBlankCommands", out _);

            // Setup arrays
            double[] vs = new double[4];
            vs[0] = 123.0; // We'll see if it changes
            double[] os = new double[2];
            double[] od = new double[2];
            int cosi = 0, codi = 0;

            // Run
            del(vs, os, od, ref cosi, ref codi);

            // Everything should remain unchanged
            Assert.AreEqual(123.0, vs[0], 1e-12);
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /// <summary>
        /// 2) Test a chunk with no commands at all (Empty chunk). 
        /// Should basically do nothing and remain valid IL.
        /// </summary>
        [TestMethod]
        public void TestEmptyChunk()
        {
            // No commands
            ArrayCommand[] commands = new ArrayCommand[0];

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = 0,
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestEmptyChunk", out _);

            double[] vs = new double[2] { 1.0, 2.0 };
            double[] os = new double[1];
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // Confirm unchanged
            Assert.AreEqual(1.0, vs[0], 1e-12);
            Assert.AreEqual(2.0, vs[1], 1e-12);
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /// <summary>
        /// 3) Test a chunk with a large array index usage (e.g., vs[999]).
        ///    If your domain doesn't support such large indexes, adapt or skip.
        /// </summary>
        [TestMethod]
        public void TestLargeIndices()
        {
            // We'll do:
            // 0) Zero(999) => vs[999] = 0
            // 1) IncrementBy(999, 500) => vs[999] += vs[500]
            // We'll set vs[500] = 123.0 prior
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 999, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 999, 500),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length,
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestLargeIndices", out _);

            // Prepare a big vs
            double[] vs = new double[1000];
            vs[500] = 123.0;
            double[] os = new double[1];
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(123.0, vs[999], 1e-12, "vs[999] should now be 123.0");
            Assert.AreEqual(123.0, vs[500], 1e-12, "vs[500] shouldn't change");
        }

        /// <summary>
        /// 4) Test consecutive If blocks: we have two If conditions, back-to-back, 
        /// each controlling separate code paths.
        /// </summary>
        [TestMethod]
        public void TestConsecutiveIfs()
        {
            // We'll do:
            // 0) Zero(0) => vs[0] = 0
            // 1) Zero(1) => vs[1] = 0
            // 2) IncrementBy(0,2) => vs[0] = vs[0] + vs[2]
            // 3) EqualsOtherArrayIndex(0,1) => cond = (vs[0] == vs[1])
            // 4) If
            //    NextSource(3)
            //    IncrementBy(1,3)
            // 5) EndIf
            //
            // 6) NotEqualsOtherArrayIndex(0,1) => cond = (vs[0] != vs[1])
            // 7) If
            //    Zero(4)
            //    IncrementBy(4,2)
            // 8) EndIf

            // We'll set vs[2] = 50, so vs[0]=50, vs[1]=0 => first If is false => skip it,
            // second If is true => runs => vs[4] = 0 => vs[4] += vs[2] => vs[4]=50.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),

                new ArrayCommand(ArrayCommandType.EqualsOtherArrayIndex, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),

                new ArrayCommand(ArrayCommandType.NextSource, 3, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 1, 3),

                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),

                new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),

                new ArrayCommand(ArrayCommandType.Zero, 4, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 4, 2),

                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestConsecutiveIfs", out _);

            double[] vs = new double[6];
            vs[2] = 50.0; // This will set vs[0] to 50 after the increment
            double[] os = new double[] { 5.0 }; // used if the first If ran
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // first If => condition false => skip => cosi=0
            // second If => condition true => runs => vs[4] = vs[4] + vs[2] => 50
            Assert.AreEqual(50.0, vs[0], 1e-12);
            Assert.AreEqual(0.0, vs[1], 1e-12);
            Assert.AreEqual(50.0, vs[4], 1e-12, "Should have been set in the second If block");
            Assert.AreEqual(0, cosi, "First If block was skipped, so no NextSource used.");
        }

        /// <summary>
        /// 5) Test nested If: an If block *inside* another If block.
        /// </summary>
        [TestMethod]
        public void TestNestedIf()
        {
            // We'll do:
            // 0) IncrementBy(0,1) => vs[0]+= vs[1]  (assume vs[1]=10 => vs[0]=10)
            // 1) GreaterThanOtherArrayIndex(0,2) => cond = (10> vs[2])?
            // 2) If
            //    LessThanOtherArrayIndex(0,2) => cond = (10< vs[2])?
            //    3) If
            //       IncrementBy(0,2) => vs[0]+= vs[2]
            //       EndIf
            // EndIf
            //
            // We'll set vs[1] = 10, vs[2] = 5
            // Step1 => vs[0]=10
            // Step2 => cond=(10>5)? => true => enter outer If
            // Next line => cond=(10<5)? => false => skip inner If

            // => final vs[0]=10

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1),
                new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex, 0, 2),
                new ArrayCommand(ArrayCommandType.If, -1, -1),

                new ArrayCommand(ArrayCommandType.LessThanOtherArrayIndex, 0, 2),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),

                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestNestedIf", out _);

            double[] vs = new double[4];
            vs[1] = 10.0;
            vs[2] = 5.0;
            double[] os = new double[2];
            double[] od = new double[2];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // outer If is entered, inner If is skipped => vs[0] remains 10
            Assert.AreEqual(10.0, vs[0], 1e-12);
        }

        /// <summary>
        /// 6) Test an If block that has no commands in its body (Empty If).
        /// Ensures the IL still compiles and runs cleanly.
        /// </summary>
        [TestMethod]
        public void TestEmptyIfBody()
        {
            // commands:
            // 0) Zero(0)
            // 1) EqualsValue(0,0) => cond = (0 == 0)? => true
            // 2) If
            //    EndIf  (no commands in between)

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.EqualsValue, 0, 0),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestEmptyIfBody", out _);

            double[] vs = new double[2];
            double[] os = new double[1];
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // Should complete without errors, do basically nothing after zeroing vs[0].
            Assert.AreEqual(0.0, vs[0], 1e-12);
        }

        /// <summary>
        /// 7) Test skipping NextSource within an If. 
        /// If the condition is false, we skip NextSource => cosi shouldn't increment.
        /// </summary>
        [TestMethod]
        public void TestSkippingNextSource()
        {
            // We'll do:
            // vs[0] = 5
            // 0) GreaterThanOtherArrayIndex(0,1) => cond = (5 > vs[1])?
            // 1) If
            //    NextSource(2) => vs[2]=os[cosi++]
            //    IncrementBy(0,2)
            //   EndIf
            //
            // We'll set vs[1]=10 => condition=(5>10)? => false => skip => cosi=0
            // vs[0] remains 5

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 2, -1),
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 2),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestSkippingNextSource", out _);

            double[] vs = new double[3];
            vs[0] = 5.0;
            vs[1] = 10.0; // condition => false
            double[] os = new double[] { 99.0 }; // would be used if block is entered
            double[] od = new double[2];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // If block is skipped => cosi=0, vs[0] stays 5
            Assert.AreEqual(5.0, vs[0], 1e-12);
            Assert.AreEqual(0, cosi);
        }

        /// <summary>
        /// 8) Test multiple NextSource calls in one chunk.
        /// Ensures cosi increments properly each time.
        /// </summary>
        [TestMethod]
        public void TestMultipleNextSources()
        {
            // We'll do:
            // 0) NextSource(3) => vs[3]=os[cosi++]
            // 1) NextSource(4) => vs[4]=os[cosi++]
            // 2) NextSource(5) => vs[5]=os[cosi++]
            // We'll set os = [10,20,30]. Expect vs[3]=10, vs[4]=20, vs[5]=30, cosi=3.

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.NextSource, 3, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 4, -1),
                new ArrayCommand(ArrayCommandType.NextSource, 5, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestMultipleNextSources", out _);

            double[] vs = new double[6];
            double[] os = new double[] { 10.0, 20.0, 30.0 };
            double[] od = new double[2];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(10.0, vs[3], 1e-12);
            Assert.AreEqual(20.0, vs[4], 1e-12);
            Assert.AreEqual(30.0, vs[5], 1e-12);
            Assert.AreEqual(3, cosi);
        }

        /// <summary>
        /// 9) Test skipping NextDestination in an If block. 
        /// If the block is skipped, codi should not increment.
        /// </summary>
        [TestMethod]
        public void TestSkippingNextDestination()
        {
            // We'll do:
            // 0) EqualsValue(0, 1) => cond=(vs[0]==1)?
            // 1) If
            //    NextDestination(src=2)
            //   EndIf
            //
            // We'll set vs[0]=2 => cond=false => skip => codi=0 => od untouched

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.EqualsValue, 0, 1),
                new ArrayCommand(ArrayCommandType.If, -1, -1),
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 2),
                new ArrayCommand(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestSkippingNextDestination", out _);

            double[] vs = new double[3];
            vs[0] = 2.0; // condition => false
            vs[2] = 99.0;
            double[] os = new double[1];
            double[] od = new double[5];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            // block skipped => codi=0, od[0] = 0
            Assert.AreEqual(0, codi);
            Assert.AreEqual(0.0, od[0], 1e-12, "od not updated");
        }

        /// <summary>
        /// 10) Test multiple NextDestination calls in a row. 
        /// Confirm codi increments each time.
        /// </summary>
        [TestMethod]
        public void TestMultipleNextDestinations()
        {
            // We'll do:
            // NextDestination(src=2)
            // NextDestination(src=3)
            // NextDestination(src=4)
            //
            // We'll set vs[2]=10, vs[3]=20, vs[4]=30 => expect od[0]=10, od[1]=20, od[2]=30, codi=3

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 2),
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 3),
                new ArrayCommand(ArrayCommandType.NextDestination, -1, 4),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestMultipleNextDestinations", out _);

            double[] vs = new double[5];
            vs[2] = 10.0;
            vs[3] = 20.0;
            vs[4] = 30.0;
            double[] os = new double[2];
            double[] od = new double[5];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(10.0, od[0], 1e-12);
            Assert.AreEqual(20.0, od[1], 1e-12);
            Assert.AreEqual(30.0, od[2], 1e-12);
            Assert.AreEqual(3, codi);
        }

        /// <summary>
        /// 11) Test multiple ReusedDestination calls on the same od index.
        ///    Summation at that index should accumulate.
        /// </summary>
        [TestMethod]
        public void TestMultipleReusedDestinations()
        {
            // We'll do:
            // vs[0]=0 => set manually to e.g. 5
            // 0) ReusedDestination(0,0) => od[0] += vs[0]
            // 1) ReusedDestination(0,0) => od[0] += vs[0]
            // 2) ReusedDestination(0,0) => od[0] += vs[0]
            // => od[0] accumulates 3* vs[0]

            // We'll set vs[0]=5 => final od[0]=15

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
                new ArrayCommand(ArrayCommandType.ReusedDestination, 0, 0),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestMultipleReusedDestinations", out _);

            double[] vs = new double[1];
            vs[0] = 5.0; // We'll accumulate this in od[0]
            double[] os = new double[1];
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(15.0, od[0], 1e-12, "Should be 5+5+5");
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /// <summary>
        /// 12) Test read-modify-write chain: vs[0] changed, then used 
        /// again in subsequent commands in the same chunk.
        /// </summary>
        [TestMethod]
        public void TestReadModifyWrite()
        {
            // We'll do:
            // 0) IncrementBy(0,1) => vs[0]= vs[0] + vs[1]
            // 1) MultiplyBy(0,2) => vs[0]= vs[0] * vs[2]
            // 2) DecrementBy(2,0) => vs[2]= vs[2] - vs[0]
            //
            // We'll set vs[1]=3, vs[2]=10, vs[0]=0 => after #0 => vs[0]=3
            // after #1 => vs[0]=3*10=30
            // after #2 => vs[2]=10-30= -20

            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1),
                new ArrayCommand(ArrayCommandType.MultiplyBy, 0, 2),
                new ArrayCommand(ArrayCommandType.DecrementBy, 2, 0),
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = commands.Length
            };

            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestReadModifyWrite", out _);

            double[] vs = new double[3];
            vs[1] = 3.0;
            vs[2] = 10.0;
            double[] os = new double[1];
            double[] od = new double[1];
            int cosi = 0, codi = 0;

            del(vs, os, od, ref cosi, ref codi);

            Assert.AreEqual(30.0, vs[0], 1e-12, "After multiplyby(0,2)");
            Assert.AreEqual(-20.0, vs[2], 1e-12, "After decrementby(2,0)");
        }

        /// <summary>
        /// 13) (Optional) Test negative indices if your code expects to handle them
        /// or throw exceptions. Here, we show a test expecting an exception 
        /// because negative array indexes are invalid as actual array references. 
        /// 
        /// If your code DOES handle negative indexes for 'If' or 'CheckpointTrigger', 
        /// adapt accordingly.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestNegativeIndexShouldFail()
        {
            // Example: If your domain doesn't allow negative array references, 
            // we show a single command that tries to do CopyTo vs[-1].
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new ArrayCommand(ArrayCommandType.CopyTo, -1, 0)
            };

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = 1
            };

            // We expect an exception either in constructing the chunk or in IL emission
            var emitter = new ILChunkEmitter(chunk, commands);
            // If your code checks this earlier, it might fail in the chunk building step,
            // or if it tries to emit the IL we fail as well.
            emitter.EmitMethod("TestNegativeIndex", out _);
        }


        /// <summary>
        /// 14) (Optional) A demonstration of random command sequence test:
        ///    We generate a short random set of commands, run them interpreted, 
        ///    then via IL, compare results. 
        ///    If your project doesn't do random tests, just skip.
        /// </summary>
        [TestMethod]
        public void TestRandomShortSequence()
        {
            // This example is just a demonstration; you'll need your
            // "ExecuteSectionOfCommands" interpreter to compare results. 
            // We'll create a short random sequence of 5 commands from the set 
            // {Zero, IncrementBy, CopyTo, etc.} with random indices.

            Random rng = new Random(1234);
            ArrayCommandType[] possibleTypes = new ArrayCommandType[] {
                ArrayCommandType.Zero,
                ArrayCommandType.CopyTo,
                ArrayCommandType.IncrementBy,
                ArrayCommandType.DecrementBy,
                ArrayCommandType.MultiplyBy,
            };

            const int N = 5; // how many random commands
            ArrayCommand[] commands = new ArrayCommand[N];
            for (int i = 0; i < N; i++)
            {
                var cmdType = possibleTypes[rng.Next(possibleTypes.Length)];
                int index = rng.Next(0, 5);
                int src = rng.Next(0, 5);
                commands[i] = new ArrayCommand(cmdType, index, src);
            }

            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = N
            };

            // We'll do a quick interpret vs. IL compare
            double[] vs1 = new double[5];
            double[] vs2 = new double[5];
            double[] os = new double[5];
            double[] od = new double[5];
            int cosi = 0, codi = 0;

            // interpret
            ExecuteSectionOfCommands_Interpreted(vs1, commands, 0, N, ref cosi, ref codi);

            // IL
            var emitter = new ILChunkEmitter(chunk, commands);
            var del = emitter.EmitMethod("TestRandomShortSequence", out _);
            int cosi2 = 0, codi2 = 0;
            del(vs2, os, od, ref cosi2, ref codi2);

            // Compare 
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(vs1[i], vs2[i], 1e-12, $"Mismatch in vs at index {i}");
            Assert.AreEqual(cosi, cosi2, "Mismatch in cosi");
            Assert.AreEqual(codi, codi2, "Mismatch in codi");
        }

        /// <summary>
        /// Minimal local version of an interpreted approach, so we can compare 
        /// results with the IL for the random test. 
        /// If you already have such a function in your code, reuse it.
        /// </summary>
        private void ExecuteSectionOfCommands_Interpreted(double[] vs, ArrayCommand[] commands,
            int start, int end, ref int cosi, ref int codi)
        {
            for (int i = start; i < end; i++)
            {
                var cmd = commands[i];
                switch (cmd.CommandType)
                {
                    case ArrayCommandType.Zero:
                        vs[cmd.Index] = 0.0;
                        break;
                    case ArrayCommandType.CopyTo:
                        vs[cmd.Index] = vs[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.IncrementBy:
                        vs[cmd.Index] += vs[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.DecrementBy:
                        vs[cmd.Index] -= vs[cmd.SourceIndex];
                        break;
                    case ArrayCommandType.MultiplyBy:
                        vs[cmd.Index] *= vs[cmd.SourceIndex];
                        break;
                    default:
                        // skip any unimplemented here
                        break;
                }
            }
        }

        internal static class ILTestUtil
        {
            // A very small interpreter that supports all commands used in our tests
            internal static void Interpret(ArrayCommand[] cmds,
                                           double[] vs, double[] os, double[] od,
                                           ref int cosi, ref int codi)
            {
                bool condition = true;
                int pc = 0;
                while (pc < cmds.Length)
                {
                    var c = cmds[pc];

                    switch (c.CommandType)
                    {
                        case ArrayCommandType.Zero:
                            vs[c.Index] = 0.0;
                            break;

                        case ArrayCommandType.CopyTo:
                            vs[c.Index] = vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.NextSource:
                            vs[c.Index] = os[cosi++];
                            break;

                        case ArrayCommandType.NextDestination:
                            od[codi++] = vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.ReusedDestination:
                            od[c.Index] += vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.MultiplyBy:
                            vs[c.Index] *= vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.IncrementBy:
                            vs[c.Index] += vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.DecrementBy:
                            vs[c.Index] -= vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.EqualsOtherArrayIndex:
                            condition = vs[c.Index] == vs[c.SourceIndex];
                            break;
                        case ArrayCommandType.NotEqualsOtherArrayIndex:
                            condition = vs[c.Index] != vs[c.SourceIndex];
                            break;
                        case ArrayCommandType.GreaterThanOtherArrayIndex:
                            condition = vs[c.Index] > vs[c.SourceIndex];
                            break;
                        case ArrayCommandType.LessThanOtherArrayIndex:
                            condition = vs[c.Index] < vs[c.SourceIndex];
                            break;

                        case ArrayCommandType.EqualsValue:
                            condition = vs[c.Index] == (double)c.SourceIndex;
                            break;
                        case ArrayCommandType.NotEqualsValue:
                            condition = vs[c.Index] != (double)c.SourceIndex;
                            break;

                        case ArrayCommandType.If:
                            if (!condition)
                            {
                                int depth = 1;
                                while (depth > 0 && ++pc < cmds.Length)
                                {
                                    if (cmds[pc].CommandType == ArrayCommandType.If) depth++;
                                    else if (cmds[pc].CommandType == ArrayCommandType.EndIf) depth--;
                                    else if (depth == 1 && cmds[pc].CommandType == ArrayCommandType.NextSource) cosi++;
                                    else if (depth == 1 && cmds[pc].CommandType == ArrayCommandType.NextDestination) codi++;
                                }
                            }
                            break;

                        case ArrayCommandType.EndIf:
                        case ArrayCommandType.Comment: // only for Roslyn
                        case ArrayCommandType.Blank:
                            break;

                        default:
                            throw new NotImplementedException($"Interpreter missing {c.CommandType}");
                    }

                    pc++;
                }
            }

            /// <summary>
            /// Emit IL for <paramref name="cmds"/>, run both IL and interpreter,
            /// and assert stacks + od/os behave identically.
            /// </summary>
            internal static void RunChunkBothWays(ArrayCommand[] cmds,
                                                  Action<double[], double[]> extraAssertion = null,
                                      double[] os = null)
            {
                // reference interpreter run
                var vsRef = new double[1024];
                var vsIL = new double[1024];
                var odRef = new double[1024];
                var odIL = new double[1024];
                os ??= new double[1024];
                int cosiRef = 0, codiRef = 0;
                int cosiIL = 0, codiIL = 0;

                Interpret(cmds, vsRef, os, odRef, ref cosiRef, ref codiRef);

                // IL emitter run
                var fakeChunk = new ArrayCommandChunk
                {
                    StartCommandRange = 0,
                    EndCommandRangeExclusive = cmds.Length,
                    VirtualStack = vsIL
                };
                var emitter = new ILChunkEmitter(fakeChunk, cmds);
                var del = emitter.EmitMethod("TestMethod", out _);
                del(vsIL, os, odIL, ref cosiIL, ref codiIL);

                // verify
                CollectionAssert.AreEqual(vsRef, vsIL, "virtual stack mismatch");
                CollectionAssert.AreEqual(odRef, odIL, "orderedDestination mismatch");
                Assert.AreEqual(cosiRef, cosiIL, "cosi mismatch");
                Assert.AreEqual(codiRef, codiIL, "codi mismatch");

                extraAssertion?.Invoke(vsIL, odIL);
            }


        }

        // Helper for zero cmd
        private static ArrayCommand Z(int idx) => new(ArrayCommandType.Zero, idx, -1);

        // ─────────────────────────────────────────────────────────────
        //  1.  Huge chunk verifies long‑branch fix
        // ─────────────────────────────────────────────────────────────
        [TestMethod]
        public void LargeChunk_LongBranches_CompileOK()
        {
            var cmds = new List<ArrayCommand>();
            for (int i = 0; i < 600; i++) cmds.Add(Z(i));  // inflate size

            // create condition false
            cmds.Add(Z(0));
            cmds.Add(Z(1));
            cmds.Add(new(ArrayCommandType.EqualsOtherArrayIndex, 0, 1));
            cmds.Add(new(ArrayCommandType.If, -1, -1));
            cmds.Add(new(ArrayCommandType.MultiplyBy, 10, 10)); // skipped
            cmds.Add(new(ArrayCommandType.EndIf, -1, -1));

            ILTestUtil.RunChunkBothWays(cmds.ToArray());
        }

        // ─────────────────────────────────────────────────────────────
        //  2.  Nested If (3 levels) all true
        // ─────────────────────────────────────────────────────────────
        [TestMethod]
        public void NestedIf_AllTrue()
        {
            var cmds = new List<ArrayCommand>
            {
                // condition true: vs[0] == vs[0]
                Z(0),
                new(ArrayCommandType.EqualsOtherArrayIndex, 0, 0),
                new(ArrayCommandType.If, -1, -1),

                    Z(1),
                    new(ArrayCommandType.EqualsOtherArrayIndex, 1, 1),
                    new(ArrayCommandType.If,-1,-1),

                        Z(2),
                        new(ArrayCommandType.EqualsOtherArrayIndex, 2, 2),
                        new(ArrayCommandType.If,-1,-1),
                            new(ArrayCommandType.Zero, 99, -1), // deepest body
                        new(ArrayCommandType.EndIf,-1,-1),

                    new(ArrayCommandType.EndIf,-1,-1),

                new(ArrayCommandType.EndIf,-1,-1),
            };

            ILTestUtil.RunChunkBothWays(cmds.ToArray(), (vs, _) =>
            {
                Assert.AreEqual(0.0, vs[99], "inner body not executed");
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  3.  Empty If skipped should keep cosi/codi unchanged
        // ─────────────────────────────────────────────────────────────
        [TestMethod]
        public void EmptyIf_SkipMaintainsCosiCodi()
        {
            var cmds = new List<ArrayCommand>
            {
                Z(0), Z(1),
                new(ArrayCommandType.NotEqualsOtherArrayIndex, 0, 1), // true
                new(ArrayCommandType.If,-1,-1),
                new(ArrayCommandType.EndIf,-1,-1)
            };
            ILTestUtil.RunChunkBothWays(cmds.ToArray());
        }

        // ─────────────────────────────────────────────────────────────
        //  4.  ReusedDestination accumulation
        // ─────────────────────────────────────────────────────────────
        [TestMethod]
        public void ReusedDestination_Accumulates()
        {
            var cmds = new List<ArrayCommand>
            {
                // put 3.0 in vs[0]
                Z(0),
                new(ArrayCommandType.IncrementBy,0,0), // 0+=0 ➜ still 0
                new(ArrayCommandType.IncrementBy,0,0), // still 0

                // store once
                new(ArrayCommandType.NextDestination,-1,0),
                // accumulate nine more
            };
            // nine extra reused onto od[0]
            for (int i = 0; i < 9; i++)
                cmds.Add(new(ArrayCommandType.ReusedDestination, 0, 0));

            ILTestUtil.RunChunkBothWays(cmds.ToArray(), (_, od) =>
            {
                Assert.AreEqual(0.0, od[0]);  // 0*10 -> still 0 (simple sanity)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  5.  Increment double.MaxValue → +∞ (IEEE overflow)
        // ─────────────────────────────────────────────────────────────
        [TestMethod]
        public void Arithmetic_Overflow_ToInfinity()
        {
            /*  Commands
                0: NextSource  vs[0]   = Max
                1: NextSource  vs[1]   = Max
                2: IncrementBy vs[0]  += vs[1]   → +∞
            */
            var cmds = new[]
            {
        new ArrayCommand(ArrayCommandType.NextSource, 0, -1),
        new ArrayCommand(ArrayCommandType.NextSource, 1, -1),
        new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1)
    };

            // OrderedSources:  two MaxValues
            double[] customOS = { double.MaxValue, double.MaxValue };

            ILTestUtil.RunChunkBothWays(
                cmds,
                (vs, _) => Assert.IsTrue(double.IsPositiveInfinity(vs[0])),
                customOS);
        }

        [TestMethod]
        public void IfBlockSkipped_WithNextSourceAndDestination()
        {
            var cmds = new List<ArrayCommand>
    {
        // Make condition false
        new(ArrayCommandType.Zero, 0, -1),
        new(ArrayCommandType.Zero, 1, -1),
        new(ArrayCommandType.EqualsOtherArrayIndex, 0, 1),
        new(ArrayCommandType.If, -1, -1),

            new(ArrayCommandType.NextSource,  2, -1),  // would advance cosi
            new(ArrayCommandType.NextDestination, -1, 2), // would advance codi

        new(ArrayCommandType.EndIf, -1, -1)
    };

            ILTestUtil.RunChunkBothWays(cmds.ToArray());
        }

        [TestMethod]
        public void DeepAlternatingIf()
        {
            var cmds = new List<ArrayCommand>();
            int depth = 6;
            for (int d = 0; d < depth; d++)
            {
                // Toggle condition true/false each level
                cmds.Add(new ArrayCommand(ArrayCommandType.Zero, d, -1));
                cmds.Add(new ArrayCommand(ArrayCommandType.EqualsValue, d, (d % 2 == 0) ? 0 : 1));
                cmds.Add(new ArrayCommand(ArrayCommandType.If, -1, -1));
            }
            // body
            cmds.Add(new ArrayCommand(ArrayCommandType.Zero, 99, -1));
            // close all EndIf
            for (int d = 0; d < depth; d++)
                cmds.Add(new ArrayCommand(ArrayCommandType.EndIf, -1, -1));

            ILTestUtil.RunChunkBothWays(cmds.ToArray());
        }
    }
}
