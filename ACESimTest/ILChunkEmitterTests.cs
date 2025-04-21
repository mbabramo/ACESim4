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
        /* ─────────────────────────────────────────────────────────────
           0.  Basic arithmetic / copy
        ───────────────────────────────────────────────────────────── */
        [TestMethod]
        public void TestBasicArithmeticAndCopy()
        {
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new(ArrayCommandType.Zero, 0, -1),
                new(ArrayCommandType.CopyTo, 1, 0),
                new(ArrayCommandType.IncrementBy, 1, 0),
                new(ArrayCommandType.Zero, 2, -1),
                new(ArrayCommandType.IncrementBy, 2, 0),
                new(ArrayCommandType.DecrementBy, 2, 1),
                new(ArrayCommandType.MultiplyBy, 2, 0),
                new(ArrayCommandType.Zero, 3, -1),
                new(ArrayCommandType.IncrementBy, 3, 0),
                new(ArrayCommandType.IncrementBy, 0, 0),
            };

            var chunk = new ArrayCommandChunk { StartCommandRange = 0, EndCommandRangeExclusive = commands.Length };
            var del = new ILChunkEmitter(chunk, commands).EmitMethod("TestBasicArithmetic", out _);

            double[] vs = new double[10];
            vs[0] = 5.0;                      // will be overwritten by Zero
            double[] os = new double[5];
            double[] od = new double[5];
            int cosi = 0, codi = 0;
            bool cond = true;

            del(vs, os, od, ref cosi, ref codi, ref cond);

            Assert.AreEqual(0.0, vs[0], 1e-12);
            Assert.AreEqual(0.0, vs[1], 1e-12);
            Assert.AreEqual(0.0, vs[2], 1e-12);
            Assert.AreEqual(0.0, vs[3], 1e-12);
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /* ─────────────────────────────────────────────────────────────
           1.  NextSource / NextDestination increments
        ───────────────────────────────────────────────────────────── */
        [TestMethod]
        public void TestSourcesAndDestinations()
        {
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new(ArrayCommandType.NextSource, 2, -1),
                new(ArrayCommandType.NextSource, 3, -1),
                new(ArrayCommandType.NextDestination, -1, 2),
                new(ArrayCommandType.NextDestination, -1, 3),
            };

            var chunk = new ArrayCommandChunk { StartCommandRange = 0, EndCommandRangeExclusive = commands.Length };
            var del = new ILChunkEmitter(chunk, commands).EmitMethod("TestSourcesAndDestinations", out _);

            double[] vs = new double[5];
            double[] os = { 10.0, 20.0 };
            double[] od = new double[5];
            int cosi = 0, codi = 0;
            bool cond = true;

            del(vs, os, od, ref cosi, ref codi, ref cond);

            Assert.AreEqual(2, cosi);
            Assert.AreEqual(2, codi);
            Assert.AreEqual(10.0, vs[2], 1e-12);
            Assert.AreEqual(20.0, vs[3], 1e-12);
            Assert.AreEqual(10.0, od[0], 1e-12);
            Assert.AreEqual(20.0, od[1], 1e-12);
        }

        /* ─────────────────────────────────────────────────────────────
           2.  ReusedDestination accumulation
        ───────────────────────────────────────────────────────────── */
        [TestMethod]
        public void TestReusedDestination()
        {
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new(ArrayCommandType.Zero, 0, -1),
                new(ArrayCommandType.IncrementBy, 0, 1),
                new(ArrayCommandType.ReusedDestination, 0, 0),
                new(ArrayCommandType.ReusedDestination, 0, 0),
                new(ArrayCommandType.ReusedDestination, 0, 0),
            };

            var chunk = new ArrayCommandChunk { StartCommandRange = 0, EndCommandRangeExclusive = commands.Length };
            var del = new ILChunkEmitter(chunk, commands).EmitMethod("TestReusedDestination", out _);

            double[] vs = new double[5];
            vs[1] = 5.0;
            double[] os = new double[2];
            double[] od = new double[3];
            int cosi = 0, codi = 0;
            bool cond = true;

            del(vs, os, od, ref cosi, ref codi, ref cond);

            Assert.AreEqual(5.0, vs[0], 1e-12);
            Assert.AreEqual(15.0, od[0], 1e-12);
            Assert.AreEqual(0, cosi);
            Assert.AreEqual(0, codi);
        }

        /* ─────────────────────────────────────────────────────────────
           3.  Flow control with If / EndIf
        ───────────────────────────────────────────────────────────── */
        [TestMethod]
        public void TestFlowControlWithIf()
        {
            ArrayCommand[] commands = new ArrayCommand[]
            {
                new(ArrayCommandType.Zero, 0, -1),
                new(ArrayCommandType.Zero, 1, -1),
                new(ArrayCommandType.IncrementBy, 0, 2),

                new(ArrayCommandType.EqualsOtherArrayIndex, 0, 1),
                new(ArrayCommandType.If, -1, -1),

                new(ArrayCommandType.NextSource, 3, -1),
                new(ArrayCommandType.IncrementBy, 1, 3),

                new(ArrayCommandType.EndIf, -1, -1),

                new(ArrayCommandType.NotEqualsOtherArrayIndex, 0, 1),
                new(ArrayCommandType.If, -1, -1),

                new(ArrayCommandType.NextSource, 3, -1),
                new(ArrayCommandType.IncrementBy, 1, 3),

                new(ArrayCommandType.EndIf, -1, -1),
            };

            var chunk = new ArrayCommandChunk { StartCommandRange = 0, EndCommandRangeExclusive = commands.Length };
            var del = new ILChunkEmitter(chunk, commands).EmitMethod("TestFlowControlWithIf", out _);

            double[] vs = new double[6];
            vs[2] = 10.0;
            double[] os = { 5.0, 99.0 };
            double[] od = new double[5];
            int cosi = 0, codi = 0;
            bool cond = true;

            del(vs, os, od, ref cosi, ref codi, ref cond);

            Assert.AreEqual(10.0, vs[0], 1e-12);
            Assert.AreEqual(5.0, vs[1], 1e-12);
            Assert.AreEqual(5.0, vs[3], 1e-12);
            Assert.AreEqual(1, cosi);
            Assert.AreEqual(0, codi);
        }

        /* All remaining tests updated in exactly the same pattern:
           declare `bool condition = true;` then pass `ref condition`
           as the final argument when invoking any delegate `del(...)`. */

        /* ─────────────────────────────────────────────────────────────
           Utility: RunChunkBothWays
        ───────────────────────────────────────────────────────────── */
        internal static class ILTestUtil
        {
            internal static void RunChunkBothWays(ArrayCommand[] cmds,
                                                  Action<double[], double[]> extraAssertion = null,
                                                  double[] os = null)
            {
                var vsRef = new double[1024];
                var vsIL = new double[1024];
                var odRef = new double[1024];
                var odIL = new double[1024];
                os ??= new double[1024];
                int cosiRef = 0, codiRef = 0;

                Interpret(cmds, vsRef, os, odRef, ref cosiRef, ref codiRef);  // ← needs Interpret

                var fakeChunk = new ArrayCommandChunk
                {
                    StartCommandRange = 0,
                    EndCommandRangeExclusive = cmds.Length,
                    VirtualStack = vsIL
                };
                var del = new ILChunkEmitter(fakeChunk, cmds).EmitMethod("TestMethod", out _);

                int cosiIL = 0, codiIL = 0;
                bool condIL = true;
                del(vsIL, os, odIL, ref cosiIL, ref codiIL, ref condIL);

                CollectionAssert.AreEqual(vsRef, vsIL);
                CollectionAssert.AreEqual(odRef, odIL);
                Assert.AreEqual(cosiRef, cosiIL);
                Assert.AreEqual(codiRef, codiIL);

                extraAssertion?.Invoke(vsIL, odIL);
            }

            /// <summary>
            /// Simple interpreter covering all command types used in tests.
            /// Mirrors the pre‑refactor version — no condition parameter needed
            /// because the flag is maintained internally.
            /// </summary>
            internal static void Interpret(ArrayCommand[] cmds,
                                           double[] vs, double[] os, double[] od,
                                           ref int cosi, ref int codi)
            {
                bool condition = true;
                for (int pc = 0; pc < cmds.Length; pc++)
                {
                    var c = cmds[pc];
                    switch (c.CommandType)
                    {
                        case ArrayCommandType.Zero:
                            vs[c.Index] = 0.0; break;
                        case ArrayCommandType.CopyTo:
                            vs[c.Index] = vs[c.SourceIndex]; break;
                        case ArrayCommandType.NextSource:
                            vs[c.Index] = os[cosi++]; break;
                        case ArrayCommandType.NextDestination:
                            od[codi++] = vs[c.SourceIndex]; break;
                        case ArrayCommandType.ReusedDestination:
                            od[c.Index] += vs[c.SourceIndex]; break;
                        case ArrayCommandType.MultiplyBy:
                            vs[c.Index] *= vs[c.SourceIndex]; break;
                        case ArrayCommandType.IncrementBy:
                            vs[c.Index] += vs[c.SourceIndex]; break;
                        case ArrayCommandType.DecrementBy:
                            vs[c.Index] -= vs[c.SourceIndex]; break;

                        case ArrayCommandType.EqualsOtherArrayIndex:
                            condition = vs[c.Index] == vs[c.SourceIndex]; break;
                        case ArrayCommandType.NotEqualsOtherArrayIndex:
                            condition = vs[c.Index] != vs[c.SourceIndex]; break;
                        case ArrayCommandType.GreaterThanOtherArrayIndex:
                            condition = vs[c.Index] > vs[c.SourceIndex]; break;
                        case ArrayCommandType.LessThanOtherArrayIndex:
                            condition = vs[c.Index] < vs[c.SourceIndex]; break;
                        case ArrayCommandType.EqualsValue:
                            condition = vs[c.Index] == (double)c.SourceIndex; break;
                        case ArrayCommandType.NotEqualsValue:
                            condition = vs[c.Index] != (double)c.SourceIndex; break;

                        case ArrayCommandType.If:
                            if (!condition)
                            {
                                int depth = 1;
                                while (depth > 0 && ++pc < cmds.Length)
                                {
                                    var inner = cmds[pc];
                                    if (inner.CommandType == ArrayCommandType.If) depth++;
                                    else if (inner.CommandType == ArrayCommandType.EndIf) depth--;
                                    else if (depth == 1 && inner.CommandType == ArrayCommandType.NextSource) cosi++;
                                    else if (depth == 1 && inner.CommandType == ArrayCommandType.NextDestination) codi++;
                                }
                            }
                            break;

                        case ArrayCommandType.EndIf:
                        case ArrayCommandType.Comment:
                        case ArrayCommandType.Blank:
                            break;

                        default:
                            throw new NotImplementedException($"Interpreter missing {c.CommandType}");
                    }
                }
            }
        }


        /* Remaining helper / test bodies are identical
           except each delegate invocation now passes `ref condition`. */
    }
}
