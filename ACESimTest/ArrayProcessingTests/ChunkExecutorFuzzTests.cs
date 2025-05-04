using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using Microsoft.CodeAnalysis.Operations;
using System.Configuration;
using System.Reflection;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class ChunkExecutorFuzzTests
    {
        private const int Runs = 100;
        private const int OrigSlotCount = 8;

        [TestMethod]
        public void Fuzz_CompareExecutors_ByDepthThenSize()
        {
            int[] maxDepths = { 0, 1, 2, 3 };
            int?[] maxCommands = { 3, 5, 10, 18 };
            (int dIndex, int cIndex, int s)? jumpToIteration = null;
            bool jump = jumpToIteration != null;
            for (int depthIndex = 0; depthIndex < maxDepths.Length; depthIndex++)
            {
                for (int maxCommandIndex = 0; maxCommandIndex < maxCommands.Length; maxCommandIndex++)
                {
                    for (int seed = 0; seed < Runs; seed++)
                    {
                        if (jump)
                        {
                            depthIndex = jumpToIteration.Value.dIndex;
                            maxCommandIndex = jumpToIteration.Value.cIndex;
                            seed = jumpToIteration.Value.s;
                            jump = false;
                        }
                        int maxDepth = maxDepths[depthIndex];
                        int? maxCommand = maxCommands[maxCommandIndex];
                        // build with a very small maxBody (we only care about total truncation here)
                        var builder = new FuzzCommandBuilder(seed, OrigSlotCount);
                        var acl = builder.Build(
                            targetSize: maxCommand ?? 50,
                            maxDepth: maxDepth);
                        var cmds = acl.UnderlyingCommands;
                        var formatted = builder.ToFormattedString();

                        var iterationInfo = $"Depth index {depthIndex}, Max command index {maxCommandIndex}, Seed {seed}";
                        try
                        {
                            CompareExecutors(cmds, seed, builder.MaxVirtualStackSize, iterationInfo);
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

        private void CompareExecutors(ArrayCommand[] cmds, int seed, int maxVirtualStackSize, string iterationInfo)
        {
            // prepare chunk bounds
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length,
                VirtualStack = new double[maxVirtualStackSize + 1]
            };

            // inputs
            var os0 = Enumerable.Range(0, OrigSlotCount).Select(i => (double)i).ToArray();

            // Interpreter baseline
            var vsInterp = new double[chunk.VirtualStack.Length];
            int cosi0 = 0; bool cond0 = true;
            var interp = new InterpreterChunkExecutor(cmds, 0, cmds.Length, false, null);
            interp.Execute(chunk, vsInterp, os0, ref cosi0, ref cond0);

            // Roslyn without local variable reuse
            var vsNoLoc = new double[chunk.VirtualStack.Length];
            int cosi1 = 0;
            bool cond1 = true;
            var rosNoLoc = new RoslynChunkExecutor(cmds, 0, cmds.Length, useCheckpoints: false, localVariableReuse: false);
            rosNoLoc.AddToGeneration(chunk);
            rosNoLoc.PreserveGeneratedCode = true;
            rosNoLoc.PerformGeneration();
            string generatedCode = "\r\n // Generated code (no local variable reuse) \r\n" + rosNoLoc.GeneratedCode;
            rosNoLoc.Execute(chunk, vsNoLoc, os0, ref cosi1, ref cond1);

            Assert.AreEqual(cosi0, cosi1, $"Seed {seed} {iterationInfo}: cosi mismatch {generatedCode}");
            Assert.AreEqual(cond0, cond1, $"Seed {seed} {iterationInfo}: condition mismatch{generatedCode}");
            CollectionAssert.AreEqual(vsInterp, vsNoLoc, $"Seed {seed} {iterationInfo}: vs mismatch {generatedCode}");

            // Roslyn with local variable reuse
            var vsLoc = new double[chunk.VirtualStack.Length];
            int cosi2 = 0; 
            bool cond2 = true;
            var rosLoc = new RoslynChunkExecutor(
                cmds, 0, cmds.Length, useCheckpoints: false, localVariableReuse: true);
            rosLoc.AddToGeneration(chunk);
            rosLoc.PreserveGeneratedCode = true;
            rosLoc.PerformGeneration(); 
            generatedCode = "\r\n // Generated code \r\n" + rosLoc.GeneratedCode;
            rosLoc.Execute(chunk, vsLoc, os0, ref cosi2, ref cond2);

            Assert.AreEqual(cosi0, cosi2, $"Seed {seed} {iterationInfo}: cosi mismatch with locals {generatedCode}");
            Assert.AreEqual(cond0, cond2, $"Seed {seed} {iterationInfo}: condition mismatch with locals {generatedCode}");
            CollectionAssert.AreEqual(vsInterp, vsLoc, $"Seed {seed} {iterationInfo}: vs mismatch with locals {generatedCode}");
        }
    }
}
