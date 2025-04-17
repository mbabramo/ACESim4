using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;

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
    }
}
