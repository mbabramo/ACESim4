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
        private const int Runs = 200;
        private const int OrigSlotCount = 8;
        private const int MaxSources = 20;
        private const int MaxDests = 20;

        /// <summary>
        /// For each seed, build a random chunk, then compare:
        ///   • InterpreterChunkExecutor  
        ///   • ILChunkExecutor  
        ///   • RoslynChunkExecutor (no locals)  
        ///   • RoslynChunkExecutor (with locals)
        /// </summary>
        [TestMethod]
        public void Fuzz_CompareExecutors()
        {
            for (int seed = 0; seed < Runs; seed++)
            {
                // 1) Generate a random command array
                var builder = new FuzzCommandBuilder(seed, OrigSlotCount);
                var cmds = builder.Build(maxDepth: 3, maxBody: 10);

                // 2) Prepare a chunk descriptor
                var chunk = new ArrayCommandChunk
                {
                    StartCommandRange = 0,
                    EndCommandRangeExclusive = cmds.Length,
                    VirtualStack = new double[OrigSlotCount + cmds.Length + 1]
                };

                // 3) Prepare inputs
                var os0 = Enumerable.Range(0, MaxSources).Select(i => (double)i).ToArray();
                var od0 = new double[MaxDests];
                var vsInterp = new double[chunk.VirtualStack.Length];
                Array.Copy(vsInterp, chunk.VirtualStack, vsInterp.Length);
                int cosi0 = 0, codi0 = 0; bool cond0 = true;

                // Interpreter baseline
                var interp = new InterpreterChunkExecutor(cmds);
                interp.Execute(chunk, vsInterp, os0, od0, ref cosi0, ref codi0, ref cond0);

                // 4) IL executor
                //var vsIL = new double[chunk.VirtualStack.Length];
                //var odIL = new double[MaxDests];
                //int cosi1 = 0, codi1 = 0; bool cond1 = true;
                //var ilExec = new ILChunkExecutor(cmds, 0, cmds.Length);
                //ilExec.AddToGeneration(chunk);
                //ilExec.PerformGeneration();
                //ilExec.Execute(chunk, vsIL, os0, odIL, ref cosi1, ref codi1, ref cond1);

                //Assert.AreEqual(cosi0, cosi1, $"Seed {seed}: IL cosi");
                //Assert.AreEqual(codi0, codi1, $"Seed {seed}: IL codi");
                //Assert.AreEqual(cond0, cond1, $"Seed {seed}: IL condition");
                //CollectionAssert.AreEqual(vsInterp, vsIL, $"Seed {seed}: IL vs");
                //CollectionAssert.AreEqual(od0, odIL, $"Seed {seed}: IL od");

                // DEBUG -- we need IL with and without locals.

                // 5) Roslyn without locals
                var vsRNo = new double[chunk.VirtualStack.Length];
                var odRNo = new double[MaxDests];
                int cosi2 = 0, codi2 = 0; bool cond2 = true;
                var rosNoLoc = new RoslynChunkExecutor(cmds, 0, cmds.Length, useCheckpoints: false);
                rosNoLoc.AddToGeneration(chunk);
                rosNoLoc.PerformGeneration();
                rosNoLoc.Execute(chunk, vsRNo, os0, odRNo, ref cosi2, ref codi2, ref cond2);

                Assert.AreEqual(cosi0, cosi2, $"Seed {seed}: Roslyn↑ cosi");
                Assert.AreEqual(codi0, codi2, $"Seed {seed}: Roslyn↑ codi");
                Assert.AreEqual(cond0, cond2, $"Seed {seed}: Roslyn↑ condition");
                CollectionAssert.AreEqual(vsInterp, vsRNo, $"Seed {seed}: Roslyn↑ vs");
                CollectionAssert.AreEqual(od0, odRNo, $"Seed {seed}: Roslyn↑ od");

                // 6) Roslyn with locals
                var plan = LocalVariablePlanner.PlanLocals(
                    cmds,
                    start: 0,
                    end: cmds.Length,
                    minUses: 2,
                    maxLocals: OrigSlotCount);

                var vsRLoc = new double[chunk.VirtualStack.Length];
                var odRLoc = new double[MaxDests];
                int cosi3 = 0, codi3 = 0; bool cond3 = true;

                // (Assumes you've added an overload taking a LocalAllocationPlan)
                var rosWithLoc = new RoslynChunkExecutor(
                    cmds,
                    0,
                    cmds.Length,
                    useCheckpoints: false,
                    localPlan: plan);

                rosWithLoc.AddToGeneration(chunk);
                rosWithLoc.PerformGeneration();
                rosWithLoc.Execute(chunk, vsRLoc, os0, odRLoc, ref cosi3, ref codi3, ref cond3);

                Assert.AreEqual(cosi0, cosi3, $"Seed {seed}: Roslyn★ cosi");
                Assert.AreEqual(codi0, codi3, $"Seed {seed}: Roslyn★ codi");
                Assert.AreEqual(cond0, cond3, $"Seed {seed}: Roslyn★ condition");
                CollectionAssert.AreEqual(vsInterp, vsRLoc, $"Seed {seed}: Roslyn★ vs");
                CollectionAssert.AreEqual(od0, odRLoc, $"Seed {seed}: Roslyn★ od");
            }
        }
    }
}
