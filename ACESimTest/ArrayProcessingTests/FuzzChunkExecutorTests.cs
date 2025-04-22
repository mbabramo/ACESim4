using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using Microsoft.CodeAnalysis.Operations;
using System.Configuration;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class FuzzChunkExecutorTests
    {
        private const int Runs = 100; // DEBUG
        private const int OrigSlotCount = 8;
        private const int MaxSources = 20;
        private const int MaxDests = 20;

        [TestMethod]
        public void Fuzz_CompareExecutors_ByDepthThenSize()
        {
            int[] maxDepths = { 0, 1, 2, 3 };
            int?[] maxCommands = { 3, 5, 10, null };
            (int dIndex, int cIndex, int s)? jumpToIteration = (0, 2, 21); // DEBUG
            for (int depthIndex = 0; depthIndex < maxDepths.Length; depthIndex++)
            {
                for (int maxCommandIndex = 0; maxCommandIndex < maxCommands.Length; maxCommandIndex++)
                {
                    for (int seed = 0; seed < Runs; seed++)
                    {
                        if (jumpToIteration.HasValue)
                        {
                            depthIndex = jumpToIteration.Value.dIndex;
                            maxCommandIndex = jumpToIteration.Value.cIndex;
                            seed = jumpToIteration.Value.s;
                        }
                        int maxDepth = maxDepths[depthIndex];
                        int? maxCommand = maxCommands[maxCommandIndex];
                        // build with a very small maxBody (we only care about total truncation here)
                        var builder = new FuzzCommandBuilder(seed, OrigSlotCount);
                        var cmds = builder.Build(
                            maxDepth: maxDepth,
                            maxBody: maxCommand ?? 50,
                            maxCommands: maxCommand);

                        var iterationInfo = $"Depth index {depthIndex}, Max command index {maxCommandIndex}, Seed {seed}";
                        try
                        {
                            CompareExecutors(cmds, seed, iterationInfo);
                        }
                        catch (AssertFailedException ex)
                        {
                            var dump = string.Join(
                                Environment.NewLine,
                                cmds.Select((c, i) => $"{i:000}: {c.CommandType,-25} idx={c.Index,3} src={c.SourceIndex,3}")
                            );
                            Assert.Fail(
                                $"{iterationInfo} failed:{Environment.NewLine}" +
                                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                                $"Commands:{Environment.NewLine}{dump}"
                            );
                        }
                    }
                }
            }
        }

        private void CompareExecutors(ArrayCommand[] cmds, int seed, string iterationInfo)
        {
            // prepare chunk bounds
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length,
                VirtualStack = new double[OrigSlotCount + cmds.Length + 1]
            };

            // inputs
            var os0 = Enumerable.Range(0, MaxSources).Select(i => (double)i).ToArray();
            var od0 = new double[MaxDests];

            // Interpreter baseline
            var vsInterp = new double[chunk.VirtualStack.Length];
            int cosi0 = 0, codi0 = 0; bool cond0 = true;
            var interp = new InterpreterChunkExecutor(cmds);
            interp.Execute(chunk, vsInterp, os0, od0, ref cosi0, ref codi0, ref cond0);

            // Roslyn without local variable reuse
            var vsNoLoc = new double[chunk.VirtualStack.Length];
            var odNoLoc = new double[MaxDests];
            int cosi1 = 0, codi1 = 0; bool cond1 = true;
            var rosNoLoc = new RoslynChunkExecutor(cmds, 0, cmds.Length, useCheckpoints: false);
            rosNoLoc.AddToGeneration(chunk);
            rosNoLoc.PerformGeneration();
            rosNoLoc.Execute(chunk, vsNoLoc, os0, odNoLoc, ref cosi1, ref codi1, ref cond1);

            Assert.AreEqual(cosi0, cosi1, $"Seed {seed} {iterationInfo}: cosi mismatch");
            Assert.AreEqual(codi0, codi1, $"Seed {seed} {iterationInfo}: codi mismatch");
            Assert.AreEqual(cond0, cond1, $"Seed {seed} {iterationInfo}: condition mismatch");
            CollectionAssert.AreEqual(vsInterp, vsNoLoc, $"Seed {seed} {iterationInfo}: vs mismatch");
            CollectionAssert.AreEqual(od0, odNoLoc, $"Seed {seed} {iterationInfo}: od mismatch");

            // Roslyn with local variable reuse
            var vsLoc = new double[chunk.VirtualStack.Length];
            var odLoc = new double[MaxDests];
            int cosi2 = 0, codi2 = 0; bool cond2 = true;
            var rosLoc = new RoslynChunkExecutor(
                cmds, 0, cmds.Length, useCheckpoints: false, localVariableReuse: true);
            rosLoc.AddToGeneration(chunk);
            rosLoc.PerformGeneration();
            rosLoc.Execute(chunk, vsLoc, os0, odLoc, ref cosi2, ref codi2, ref cond2);

            Assert.AreEqual(cosi0, cosi2, $"Seed {seed} {iterationInfo}: cosi mismatch with locals");
            Assert.AreEqual(codi0, codi2, $"Seed {seed} {iterationInfo}: codi mismatch with locals");
            Assert.AreEqual(cond0, cond2, $"Seed {seed} {iterationInfo}: condition mismatch with locals");
            CollectionAssert.AreEqual(vsInterp, vsLoc, $"Seed {seed} {iterationInfo}: vs mismatch with locals");
            CollectionAssert.AreEqual(od0, odLoc, $"Seed {seed} {iterationInfo}: od mismatch with locals");
        }

        private string Describe(ArrayCommand[] cmds)
        {
            return string.Join(
                Environment.NewLine,
                cmds.Select((c, i) => $"{i:000}: {c.CommandType,-25} idx={c.Index,3} src={c.SourceIndex,3}")
            );
        }
    }
}
