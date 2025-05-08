// MinimalRoslynReuseMinimizationTest.cs
// DEBUG -- remove this file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class MinimalRoslynReuseMinimizationTest
    {
        // --------------------------------------------------------------------
        // 1.  Build original failing sequence (Seed-37 case) – NO randomness
        // --------------------------------------------------------------------
        private static ArrayCommand[] BuildFailingCommandSequence()
        {
            // Paste from the earlier minimal failing test
            return new[]
            {
                new ArrayCommand(ArrayCommandType.NextSource              ,  8, -1 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  ,  9,  8 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 10,  9 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 10,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             ,  9,  8 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 11,  9 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 11, 10 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  8, 11 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 12,  9 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 12,  9 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 13, 12 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 13, 12 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 12,  8 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             ,  8,  8 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 10, 10 ),
                new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex,12, 12 ),
                new ArrayCommand(ArrayCommandType.If                      , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  7, 10 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 12, 13 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 14, 10 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 14, 12 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  8, 14 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 15,  9 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 15,  9 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 15,  8 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              ,  8,  8 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 16, 13 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 17,  8 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 17,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 10, 17 ),
                new ArrayCommand(ArrayCommandType.Zero                    , 16, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 13, 16 ),
                new ArrayCommand(ArrayCommandType.EndIf                   , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 18, 10 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 18, 10 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 18,  9 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 19,  8 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 19,  8 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  8, 19 ),
                new ArrayCommand(ArrayCommandType.NotEqualsValue          ,  9,  2 ),
                new ArrayCommand(ArrayCommandType.If                      , -1, -1 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 20, 18 ),
                new ArrayCommand(ArrayCommandType.Zero                    , 10, -1 ),
                new ArrayCommand(ArrayCommandType.EndIf                   , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  9,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  7, 10 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 21,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 18, 18 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex,  8,  9 ),
                new ArrayCommand(ArrayCommandType.If                      , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  3, 21 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  7,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 21,  9 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 22,  8 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 23, 21 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             ,  9, 23 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 24, 21 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 24, 21 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 25, 21 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 25, 21 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  4, 25 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  1, 21 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 26, 22 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 26, 22 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 18, 26 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 22, 10 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 23,  9 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 24, 10 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 27, 10 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 27, 21 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  8, 27 ),
                new ArrayCommand(ArrayCommandType.Zero                    , 24, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  0, 22 ),
                new ArrayCommand(ArrayCommandType.Zero                    , 23, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  5, 24 ),
                new ArrayCommand(ArrayCommandType.EndIf                   , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 21,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  8,  8 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  6,  9 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  5,  9 ),
                new ArrayCommand(ArrayCommandType.IncrementDepth          , -1, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  2, 10 ),
                new ArrayCommand(ArrayCommandType.DecrementBy             , 10,  9 ),
                new ArrayCommand(ArrayCommandType.Zero                    ,  9, -1 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  4,  8 ),
                new ArrayCommand(ArrayCommandType.CopyTo                  , 28, 18 ),
                new ArrayCommand(ArrayCommandType.MultiplyBy              , 28, 18 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             , 10, 28 ),
                new ArrayCommand(ArrayCommandType.IncrementBy             ,  6,  9 ),
                new ArrayCommand(ArrayCommandType.DecrementDepth          , -1, -1 ),
            };
        }

        // --------------------------------------------------------------------
        // 2.  Fixed original-source values (no RNG)
        // --------------------------------------------------------------------
        private static readonly double[] OriginalSources =
        {
            -63.380431723915606,
              0.0,
            173.45535644832333,
            -27.334511952555985,
            382.4812534975449,
           -154.3575741347737,
           -183.0056091628185,
              0.0
        };

        // --------------------------------------------------------------------
        // 3.  Utility: run both executors and return mismatch indices
        // --------------------------------------------------------------------
        private static List<int> GetMismatchIndices(ArrayCommand[] cmds)
        {
            const int VS_SIZE = 32;                     // enough for slot 28
            const int SRC_COUNT = 8;

            List<int> CompareRun()
            {
                // helper to run one executor factory
                (double[] vsOut, Action<int, double[]> runner) Make(Func<IChunkExecutor> makeExec)
                {
                    var vs = new double[VS_SIZE];
                    Array.Copy(OriginalSources, vs, SRC_COUNT);

                    var chunk = new ArrayCommandChunk
                    {
                        StartCommandRange = 0,
                        EndCommandRangeExclusive = cmds.Length,
                        VirtualStack = new double[VS_SIZE]
                    };

                    int cosi = 0; bool cond = true;
                    var exec = makeExec();
                    exec.AddToGeneration(chunk);
                    exec.PerformGeneration();
                    exec.Execute(chunk, vs, OriginalSources, ref cosi, ref cond);
                    return (vs, (_, __) => { });
                }

                var (vsInterp, _) = Make(() => new InterpreterChunkExecutor(cmds, 0, cmds.Length, false, null));
                var (vsRoslyn, _) = Make(() => new RoslynChunkExecutor(cmds, 0, cmds.Length, false, true));

                var mism = new List<int>();
                for (int i = 0; i < SRC_COUNT; i++)
                    if (vsInterp[i] != vsRoslyn[i])
                        mism.Add(i);
                return mism;
            }

            return CompareRun();
        }

        // --------------------------------------------------------------------
        // 4.  The test that minimises commands yet preserves SAME failure
        // --------------------------------------------------------------------
        [TestMethod]
        public void MinimiseCommands_PreservesSameMismatch()
        {
            var cmds = BuildFailingCommandSequence();

            // establish baseline mismatch indices (should be non-empty)
            var baselineMismatch = GetMismatchIndices(cmds);
            Assert.IsTrue(baselineMismatch.Count > 0,
                "Original sequence no longer fails – baseline bug vanished?");

            bool removedSomething;
            do
            {
                removedSomething = false;

                for (int idx = 0; idx < cmds.Length; idx++)
                {
                    if (cmds[idx].CommandType == ArrayCommandType.Blank)
                        continue;                       // already removed

                    var saved = cmds[idx];
                    cmds[idx] = new ArrayCommand(ArrayCommandType.Blank, -1, -1);

                    bool executedWithoutError = true;
                    List<int> mism = null;
                    try
                    {
                        mism = GetMismatchIndices(cmds);
                    }
                    catch
                    {
                        executedWithoutError = false;
                    }

                    if (executedWithoutError && mism.SequenceEqual(baselineMismatch))
                    {
                        // Replacement kept the SAME failure – keep it blank
                        removedSomething = true;
                        Debug.WriteLine($"Removed cmd {idx:000} ({saved.CommandType})");
                    }
                    else
                    {
                        // Restore – removal changed behaviour
                        cmds[idx] = saved;
                    }
                }

            } while (removedSomething);    // keep trying until no more eliminations

            int eliminated = cmds.Count(c => c.CommandType == ArrayCommandType.Blank);
            Debug.WriteLine($"Total commands eliminated: {eliminated}");

            // final safety check: bug must still manifest in the SAME way
            var finalMismatch = GetMismatchIndices(cmds);
            CollectionAssert.AreEqual(baselineMismatch, finalMismatch,
                "After minimisation we must still fail with the original mismatch indices.");
        }
    }
}
