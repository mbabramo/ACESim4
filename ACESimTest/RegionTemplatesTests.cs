using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.Slots;
using ACESimBase.Util.ArrayProcessing.Templating;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class RegionTemplatesTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static int Count(ArrayCommandList acl, ArrayCommandType t)
            => acl.UnderlyingCommands.Take(acl.MaxCommandIndex).Count(c => c.CommandType == t);

        private static double[] RunOnce(ArrayCommandList acl, double[] input, ChunkExecutorKind kind)
        {
            var arr = (double[])input.Clone();
            acl.CompileAndRunOnce(arr, tracing: false, kind: kind);
            return acl.VirtualStack.ToArray();
        }

        private static void AssertBackendParity(ArrayCommandList acl, double[] input)
        {
            var interp = RunOnce(acl, input, ChunkExecutorKind.Interpreted);
            var roslyn = RunOnce(acl, input, ChunkExecutorKind.Roslyn);
            CollectionAssert.AreEqual(interp, roslyn, "Roslyn parity failed.");

            var il = RunOnce(acl, input, ChunkExecutorKind.IL);
            CollectionAssert.AreEqual(interp, il, "IL parity failed.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // IdenticalRangeTemplate
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Record first action once and replay the rest. Uses ordered IO to make
        /// consumption/accumulation observable. Verifies:
        /// - Ordered index lists reflect all actions,
        /// - Command stream contains only one body's worth of NextSource/NextDestination,
        /// - Backends produce identical results.
        /// </summary>
        [TestMethod]
        public void IdenticalRange_Basic_OS_OD_ReplaysAndRuns()
        {
            var acl = new ArrayCommandList(maxNumCommands: 512, initialArrayIndex: 3)
            {
                UseOrderedSourcesAndDestinations = true
            };
            var slots = new ArraySlots(acl);

            // Optional outer chunk to group the set.
            acl.StartCommandChunk(false, null, "root");

            var ident = new IdenticalRangeTemplate(
                acl,
                new RegionTemplateOptions
                {
                    IncludeComments = true,
                    ManageDepthScopes = false,
                    ChunkNamePrefix = "Chance"
                });

            using (ident.BeginSet("TwoActionSet"))
            {
                for (int a = 0; a < 2; a++)
                {
                    using (ident.BeginAction($"action={a + 1}"))
                    {
                        // Body: (OS[0] + OS[1]) -> accumulate to OD[2]
                        var v0 = slots.Read(new OsPort(0));
                        var v1 = slots.Read(new OsPort(1));
                        var sum = slots.CopyToNew(v0);
                        slots.Add(sum, v1);
                        slots.Accumulate(new OdPort(2), sum);
                    }
                }
            }

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // Ordered lists include both actions
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, acl.OrderedSourceIndices.ToArray());
            CollectionAssert.AreEqual(new[] { 2, 2 }, acl.OrderedDestinationIndices.ToArray());

            // Stream contains only one body's worth of NextSource/NextDestination
            Assert.AreEqual(2, Count(acl, ArrayCommandType.NextSource), "Expected one body's OS reads recorded once.");
            Assert.AreEqual(1, Count(acl, ArrayCommandType.NextDestination), "Expected one body's OD write recorded once.");

            // Execute and check parity
            // Input values: sources (0)=3, (1)=7; dest(2)=0 → OD accum twice ⇒ 2*(3+7)=20
            AssertBackendParity(acl, new double[] { 3.0, 7.0, 0.0 });

            // Spot check destination effect (interpreter result already captured by parity helper)
            var vs = RunOnce(acl, new[] { 3.0, 7.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(20.0, vs[2], 1e-9, "Expected OD[2] to accumulate both actions.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // RepeatWindowTemplate
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Record first window once and replay a second window. Pure VS math
        /// so we can assert on deterministic VS effects. Verifies backend parity.
        /// </summary>
        [TestMethod]
        public void RepeatWindow_Basic_VS_ExecutesTwice()
        {
            var acl = new ArrayCommandList(maxNumCommands: 512, initialArrayIndex: 3);
            var slots = new ArraySlots(acl);

            acl.StartCommandChunk(false, null, "root");

            var repeat = new RepeatWindowTemplate(
                acl,
                new RegionTemplateOptions
                {
                    IncludeComments = true,
                    ManageDepthScopes = false,
                    ChunkNamePrefix = "Round"
                });

            // Two identical windows; each adds (vs[0] + vs[1]) to vs[2]
            for (int w = 0; w < 2; w++)
            {
                using (repeat.Open("Window"))
                {
                    var a = new VsSlot(0);
                    var b = new VsSlot(1);
                    var tmp = slots.CopyToNew(a);
                    slots.Add(tmp, b);
                    slots.Add(new VsSlot(2), tmp);
                }
            }

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // No ordered IO in this test
            Assert.AreEqual(0, acl.OrderedSourceIndices.Count);
            Assert.AreEqual(0, acl.OrderedDestinationIndices.Count);
            Assert.AreEqual(0, Count(acl, ArrayCommandType.NextSource));
            Assert.AreEqual(0, Count(acl, ArrayCommandType.NextDestination));

            // Input: [2,3,0] → per window adds 5 to vs[2], two windows ⇒ 10
            AssertBackendParity(acl, new double[] { 2.0, 3.0, 0.0 });
            var vs = RunOnce(acl, new[] { 2.0, 3.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(10.0, vs[2], 1e-9);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ParameterFrame
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ParameterFrame from VS: set slots from vs[0],vs[1] and use them.
        /// </summary>
        [TestMethod]
        public void ParameterFrame_SetFromVS_Works()
        {
            var acl = new ArrayCommandList(maxNumCommands: 256, initialArrayIndex: 3);
            var pf = new ParameterFrame(acl, count: 2);

            acl.StartCommandChunk(false, null, "root");

            // Initialize params from VS[0], VS[1]
            pf.SetFromVirtualStack(new[] { 0, 1 });

            // Compute vs[2] = param[0] + param[1]
            int p0 = pf.Slots[0];
            int p1 = pf.Slots[1];
            int tmp = acl.CopyToNew(p0, fromOriginalSources: false);
            acl.Increment(tmp, targetOriginal: false, p1);
            acl.CopyToExisting(2, tmp);

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // No ordered IO here
            Assert.AreEqual(0, acl.OrderedSourceIndices.Count);
            Assert.AreEqual(0, acl.OrderedDestinationIndices.Count);

            // Input: [4,6,0] → vs[2] = 10
            AssertBackendParity(acl, new double[] { 4.0, 6.0, 0.0 });
            var vs = RunOnce(acl, new[] { 4.0, 6.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(10.0, vs[2], 1e-9);
        }

        /// <summary>
        /// ParameterFrame from original sources: ordered-mode NextSource semantics.
        /// </summary>
        [TestMethod]
        public void ParameterFrame_SetFromOriginalSources_WorksAndConsumesOS()
        {
            var acl = new ArrayCommandList(maxNumCommands: 256, initialArrayIndex: 3)
            {
                UseOrderedSourcesAndDestinations = true
            };
            var pf = new ParameterFrame(acl, count: 2);

            acl.StartCommandChunk(false, null, "root");

            // Initialize params from original sources 0 and 1 (consumes OS twice)
            pf.SetFromOriginalSources(new[] { 0, 1 });

            // vs[2] = param[0] + param[1]
            int p0 = pf.Slots[0];
            int p1 = pf.Slots[1];
            int tmp = acl.CopyToNew(p0, fromOriginalSources: false);
            acl.Increment(tmp, targetOriginal: false, p1);
            acl.CopyToExisting(2, tmp);

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // Ordered sources were consumed
            CollectionAssert.AreEqual(new[] { 0, 1 }, acl.OrderedSourceIndices.ToArray());
            Assert.AreEqual(2, Count(acl, ArrayCommandType.NextSource));

            // Input: [2,9,0] → vs[2] = 11
            AssertBackendParity(acl, new double[] { 2.0, 9.0, 0.0 });
            var vs = RunOnce(acl, new[] { 2.0, 9.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(11.0, vs[2], 1e-9);
        }
    }
}
