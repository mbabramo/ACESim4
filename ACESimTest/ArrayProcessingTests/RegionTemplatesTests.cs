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

        // Nested identical sets with a single IdenticalRangeTemplate instance.
        // VS-only (no ordered IO) to focus on structural nesting and execution parity.
        [TestMethod]
        public void IdenticalRange_NestedSets_SingleTemplate_ReplaysAndRuns()
        {
            var acl = new ArrayCommandList(maxNumCommands: 1024, initialArrayIndex: 3);
            var slots = new ArraySlots(acl);

            acl.StartCommandChunk(false, null, "root");

            var ident = new IdenticalRangeTemplate(
                acl,
                new RegionTemplateOptions
                {
                    IncludeComments = true,
                    ManageDepthScopes = false,
                    ChunkNamePrefix = "Decision"
                });

            // Outer set with 2 actions; inside each action, an inner set with 2 actions.
            using (ident.BeginSet("Outer"))
            {
                for (int a = 0; a < 2; a++)
                {
                    using (ident.BeginAction($"outer={a + 1}"))
                    using (ident.BeginSet("Inner"))
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            using (ident.BeginAction($"inner={b + 1}"))
                            {
                                // Body: vs[2] += (vs[0] + vs[1])
                                var v0 = new VsSlot(0);
                                var v1 = new VsSlot(1);
                                var sum = slots.CopyToNew(v0);
                                slots.Add(sum, v1);
                                slots.Add(new VsSlot(2), sum);
                            }
                        }
                    }
                }
            }

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // No ordered IO in this test
            Assert.AreEqual(0, acl.OrderedSourceIndices.Count);
            Assert.AreEqual(0, acl.OrderedDestinationIndices.Count);
            Assert.AreEqual(0, Count(acl, ArrayCommandType.NextSource));
            Assert.AreEqual(0, Count(acl, ArrayCommandType.NextDestination));

            // Input: [2,3,0]; inner body adds 5 each time; 4 inner actions ⇒ vs[2] = 20
            AssertBackendParity(acl, new double[] { 2.0, 3.0, 0.0 });
            var vs = RunOnce(acl, new[] { 2.0, 3.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(20.0, vs[2], 1e-9);
        }


        // Ordered IO inside nested identical actions.
        // Verifies that ordered index lists reflect all actions while the command
        // stream contains only one body's worth of NextSource/NextDestination per set recording.
        [TestMethod]
        public void IdenticalRange_NestedSets_OrderedIO_CountsAndParity()
        {
            var acl = new ArrayCommandList(maxNumCommands: 1024, initialArrayIndex: 3)
            {
                UseOrderedSourcesAndDestinations = true
            };
            var slots = new ArraySlots(acl);

            acl.StartCommandChunk(false, null, "root");

            var ident = new IdenticalRangeTemplate(
                acl,
                new RegionTemplateOptions
                {
                    IncludeComments = true,
                    ManageDepthScopes = false,
                    ChunkNamePrefix = "Decision"
                });

            using (ident.BeginSet("Outer"))
            {
                for (int a = 0; a < 2; a++)
                {
                    using (ident.BeginAction($"outer={a + 1}"))
                    using (ident.BeginSet("Inner"))
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            using (ident.BeginAction($"inner={b + 1}"))
                            {
                                // Body: accumulate (OS[0] + OS[1]) into OD[2]
                                var v0 = slots.Read(new OsPort(0)); // OS
                                var v1 = slots.Read(new OsPort(1)); // OS
                                var sum = slots.CopyToNew(v0);
                                slots.Add(sum, v1);
                                slots.Accumulate(new OdPort(2), sum); // OD
                            }
                        }
                    }
                }
            }

            acl.EndCommandChunk();
            acl.CompleteCommandList();

            // There are 4 inner actions total → OS consumed 8 times (0,1,0,1,0,1,0,1)
            var expectedOs = Enumerable.Repeat(new[] { 0, 1 }, 4).SelectMany(x => x).ToArray();
            CollectionAssert.AreEqual(expectedOs, acl.OrderedSourceIndices.ToArray());

            // Destinations queued once per inner action → four entries of index 2
            CollectionAssert.AreEqual(new[] { 2, 2, 2, 2 }, acl.OrderedDestinationIndices.ToArray());

            // Command stream contains one recording of the inner body (inside the first outer action).
            Assert.AreEqual(2, Count(acl, ArrayCommandType.NextSource));
            Assert.AreEqual(1, Count(acl, ArrayCommandType.NextDestination));

            // Input: [2,3,0]; per inner action adds 5 → four inner actions ⇒ vs[2] = 20
            AssertBackendParity(acl, new double[] { 2.0, 3.0, 0.0 });
            var vs = RunOnce(acl, new[] { 2.0, 3.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(20.0, vs[2], 1e-9);
        }


        // Hoist + templates: oversize IF body inside an identical set.
        // Ensures hoisting runs and VerifyCorrectness2 (invoked during CompleteCommandList)
        // validates ordered-source/destination metadata against the command slices.
        [TestMethod]
        public void IdenticalRange_Hoist_OversizeIfBody_MetadataAndParity()
        {
            var acl = new ArrayCommandList(maxNumCommands: 8192, initialArrayIndex: 3)
            {
                UseOrderedSourcesAndDestinations = true,
                MaxCommandsPerSplittableChunk = 12 // force hoisting on an oversize IF body
            };

            acl.StartCommandChunk(false, null, "root");

            var ident = new IdenticalRangeTemplate(
                acl,
                new RegionTemplateOptions
                {
                    IncludeComments = true,
                    ManageDepthScopes = false,
                    ChunkNamePrefix = "Hoist"
                });

            using (ident.BeginSet("HoistSet"))
            {
                for (int a = 0; a < 2; a++)
                {
                    using (ident.BeginAction($"action={a + 1}"))
                    {
                        var r = acl.Recorder;

                        // IF (vs[tmp] == 0) { … oversize body … }
                        int tmp = r.NewZero();
                        r.InsertEqualsValueCommand(tmp, 0);
                        r.InsertIf();

                        // Make the IF body oversize to trigger hoisting
                        for (int i = 0; i < 40; i++)
                            r.Increment(tmp, targetOriginal: false, tmp);

                        // Include ordered IO inside the IF to exercise pointer metadata
                        int osVal = r.CopyToNew(0, fromOriginalSources: true);     // one NextSource in the recorded body
                        r.Increment(2, targetOriginal: true, osVal);               // one NextDestination in the recorded body

                        r.InsertEndIf();
                    }
                }
            }

            acl.EndCommandChunk();

            // CompleteCommandList triggers hoisting and VerifyCorrectness2 checks.
            // If metadata is inconsistent, this will throw.
            acl.CompleteCommandList();

            // Command-stream shape: recorded body appears once; the second identical action replays.
            Assert.AreEqual(1, Count(acl, ArrayCommandType.NextSource));
            Assert.AreEqual(1, Count(acl, ArrayCommandType.NextDestination));

            // Ordered lists reflect both executed actions (two OS / two OD)
            CollectionAssert.AreEqual(new[] { 0, 0 }, acl.OrderedSourceIndices.ToArray());
            CollectionAssert.AreEqual(new[] { 2, 2 }, acl.OrderedDestinationIndices.ToArray());

            // Execution parity and result check:
            // Input [2,3,0] → inside IF we accumulate OS[0] (=2) once per action ⇒ vs[2] = 4
            AssertBackendParity(acl, new double[] { 2.0, 3.0, 0.0 });
            var vs = RunOnce(acl, new[] { 2.0, 3.0, 0.0 }, ChunkExecutorKind.Interpreted);
            Assert.AreEqual(4.0, vs[2], 1e-9);
        }

    }
}
