using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest
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
            for (int depthIndex = 0; depthIndex < maxDepths.Length; depthIndex++)
            {
                int maxDepth = maxDepths[depthIndex];
                for (int i = 0; i < maxCommands.Length; i++)
                {
                    int? maxCommand = maxCommands[i];
                    for (int seed = 0; seed < Runs; seed++)
                    {
                        // maxDepth = 1; maxCommand = 5; seed = 2143; // DEBUG
                        // build with a very small maxBody (we only care about total truncation here)
                        var builder = new FuzzCommandBuilder(seed, OrigSlotCount);
                        var cmds = builder.Build(
                            maxDepth: maxDepth,
                            maxBody: maxCommand ?? 50,
                            maxCommands: maxCommand);

                        var stage = $"D{maxDepth}-C{(maxCommand.HasValue ? maxCommand.Value.ToString() : "full")}";
                        try
                        {
                            CompareExecutors(cmds, seed, stage);
                        }
                        catch (AssertFailedException ex)
                        {
                            var dump = string.Join(
                                Environment.NewLine,
                                cmds.Select((c, i) => $"{i:000}: {c.CommandType,-25} idx={c.Index,3} src={c.SourceIndex,3}")
                            );
                            Assert.Fail(
                                $"Seed {seed}, {stage} failed:{Environment.NewLine}" +
                                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                                $"Commands:{Environment.NewLine}{dump}"
                            );
                        }
                    }
                }
            }
        }

        private void CompareExecutors(ArrayCommand[] cmds, int seed, string stage)
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

            // Roslyn without locals
            var vsNoLoc = new double[chunk.VirtualStack.Length];
            var odNoLoc = new double[MaxDests];
            int cosi1 = 0, codi1 = 0; bool cond1 = true;
            var rosNoLoc = new RoslynChunkExecutor(cmds, 0, cmds.Length, useCheckpoints: false);
            rosNoLoc.AddToGeneration(chunk);
            rosNoLoc.PerformGeneration();
            rosNoLoc.Execute(chunk, vsNoLoc, os0, odNoLoc, ref cosi1, ref codi1, ref cond1);

            Assert.AreEqual(cosi0, cosi1, $"Seed {seed} {stage}: cosi mismatch");
            Assert.AreEqual(codi0, codi1, $"Seed {seed} {stage}: codi mismatch");
            Assert.AreEqual(cond0, cond1, $"Seed {seed} {stage}: condition mismatch");
            CollectionAssert.AreEqual(vsInterp, vsNoLoc, $"Seed {seed} {stage}: vs mismatch");
            CollectionAssert.AreEqual(od0, odNoLoc, $"Seed {seed} {stage}: od mismatch");

            // Roslyn with locals
            var plan = LocalVariablePlanner.PlanLocals(
                cmds,
                start: 0,
                end: cmds.Length,
                minUses: 2,
                maxLocals: OrigSlotCount
            );

            var vsLoc = new double[chunk.VirtualStack.Length];
            var odLoc = new double[MaxDests];
            int cosi2 = 0, codi2 = 0; bool cond2 = true;
            var rosLoc = new RoslynChunkExecutor(
                cmds, 0, cmds.Length, useCheckpoints: false, localPlan: plan);
            rosLoc.AddToGeneration(chunk);
            rosLoc.PerformGeneration();
            rosLoc.Execute(chunk, vsLoc, os0, odLoc, ref cosi2, ref codi2, ref cond2);

            Assert.AreEqual(cosi0, cosi2, $"Seed {seed} {stage}: cosi mismatch with locals");
            Assert.AreEqual(codi0, codi2, $"Seed {seed} {stage}: codi mismatch with locals");
            Assert.AreEqual(cond0, cond2, $"Seed {seed} {stage}: condition mismatch with locals");
            CollectionAssert.AreEqual(vsInterp, vsLoc, $"Seed {seed} {stage}: vs mismatch with locals");
            CollectionAssert.AreEqual(od0, odLoc, $"Seed {seed} {stage}: od mismatch with locals");
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
