// SlotsIntegrationTests.cs

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.ArrayProcessing.Slots;
using static ACESimTest.ArrayProcessingTests.ArrayProcessingTestHelpers;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class SlotsIntegrationTests
    {
        /// <summary>
        /// Straight-line authoring via Slots over VS only (no ordered IO).
        /// Verifies execution and backend parity.
        /// </summary>
        [TestMethod]
        public void Slots_VSOnly_Straightline_Parity()
        {
            // Arrange author-time
            var acl = new ArrayCommandList(maxNumCommands: 256, initialArrayIndex: 3);
            var slots = new ArraySlots(acl);

            acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: null, name: "root");

            // Compute: vs[2] = (vs[0] + vs[1])^2
            var a = new VsSlot(0);
            var b = new VsSlot(1);
            var sum = slots.CopyToNew(a);
            slots.Add(sum, b);
            slots.Mul(sum, sum);
            slots.CopyTo(new VsSlot(2), sum);

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // Act: run once with specific data
            var data = new double[] { 10.0, 4.0, 0.0 };
            acl.CompileAndRunOnce(data, tracing: false, kind: ChunkExecutorKind.Interpreted);

            // Assert: expected outputs in the working VirtualStack (not 'data')
            Assert.AreEqual(10.0, acl.VirtualStack[0], 1e-9);
            Assert.AreEqual( 4.0, acl.VirtualStack[1], 1e-9);
            Assert.AreEqual(Math.Pow(10.0 + 4.0, 2), acl.VirtualStack[2], 1e-9);

            // Backend parity using a VS large enough for the runner’s copy-in
            // (runner copies data.Length into VirtualStack)
            int n = Math.Max(acl.VirtualStackSize, 3);
            var interp = Enumerable.Repeat(0.0, n).ToArray();
            var compiled = Enumerable.Repeat(0.0, n).ToArray();

            // Fill both inputs with a stable pattern
            for (int i = 0; i < n; i++) interp[i] = compiled[i] = i % 7;

            // Baseline (interpreter)
            acl.CompileAndRunOnce(interp, false, ChunkExecutorKind.Interpreted);
            var baseline = acl.VirtualStack.ToArray();

            // Roslyn
            acl.CompileAndRunOnce(compiled, false, ChunkExecutorKind.Roslyn);
            var roslynVs = acl.VirtualStack.ToArray();
            CollectionAssert.AreEqual(baseline, roslynVs, "Roslyn parity failed.");

            // IL
            compiled = Enumerable.Repeat(0.0, n).ToArray();
            for (int i = 0; i < n; i++) compiled[i] = i % 7;
            acl.CompileAndRunOnce(compiled, false, ChunkExecutorKind.IL);
            var ilVs = acl.VirtualStack.ToArray();
            CollectionAssert.AreEqual(baseline, ilVs, "IL parity failed.");
        }

        /// <summary>
        /// Ordered sources/destinations and ParamSlot integration:
        /// - StageParam from OS, UseParam multiple times,
        /// - Accumulate to multiple ODs,
        /// - Validate ordered index lists and command stream shape,
        /// - Validate execution results and backend parity.
        /// </summary>
        [TestMethod]
        public void Slots_OrderedIO_Param_And_Accumulate_Integration()
        {
            // Arrange author-time
            var acl = new ArrayCommandList(maxNumCommands: 256, initialArrayIndex: 3)
            {
                UseOrderedSourcesAndDestinations = true
            };
            var slots = new ArraySlots(acl);

            acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: null, name: "root");

            // Stage a parameter from ordered source index 0 (one OS consumption)
            var p = slots.StageParam(new OsPort(0));

            // Use the parameter twice (no additional OS consumption)
            var v1 = slots.UseParam(p);
            var v2 = slots.UseParam(p);

            // Accumulate to destination 1 (once with param, once with a fresh OS read of 1)
            slots.Accumulate(new OdPort(1), v1);
            var os1 = slots.Read(new OsPort(1));        // second OS consumption
            slots.Accumulate(new OdPort(1), os1);

            // Accumulate to destination 2 (param again)
            slots.Accumulate(new OdPort(2), v2);

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // Author-time invariants
            CollectionAssert.AreEqual(new[] { 0, 1 }, acl.OrderedSourceIndices.ToArray());
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, acl.OrderedDestinationIndices.ToArray());

            int nextSourceCmds = acl.UnderlyingCommands
                                     .Take(acl.MaxCommandIndex)
                                     .Count(c => c.CommandType == ArrayCommandType.NextSource);
            int nextDestCmds = acl.UnderlyingCommands
                                   .Take(acl.MaxCommandIndex)
                                   .Count(c => c.CommandType == ArrayCommandType.NextDestination);
            Assert.AreEqual(acl.OrderedSourceIndices.Count, nextSourceCmds);
            Assert.AreEqual(acl.OrderedDestinationIndices.Count, nextDestCmds);

            // Act + Assert: use identical inputs across backends
            var input = new double[] { 2.0, 5.0, 10.0 };

            // Interpreter
            var interpIn = (double[])input.Clone();
            acl.CompileAndRunOnce(interpIn, tracing: false, kind: ChunkExecutorKind.Interpreted);
            var baseline = acl.VirtualStack.ToArray();

            // Roslyn
            var roslynIn = (double[])input.Clone();
            acl.CompileAndRunOnce(roslynIn, tracing: false, kind: ChunkExecutorKind.Roslyn);
            var roslynVs = acl.VirtualStack.ToArray();
            CollectionAssert.AreEqual(baseline, roslynVs, "Roslyn parity failed.");

            // IL
            var ilIn = (double[])input.Clone();
            acl.CompileAndRunOnce(ilIn, tracing: false, kind: ChunkExecutorKind.IL);
            var ilVs = acl.VirtualStack.ToArray();
            CollectionAssert.AreEqual(baseline, ilVs, "IL parity failed.");

            // Spot-check the expected values from the problem description
            Assert.AreEqual(2.0,  baseline[0], 1e-9);
            Assert.AreEqual(12.0, baseline[1], 1e-9);
            Assert.AreEqual(12.0, baseline[2], 1e-9);
        }

    }
}
