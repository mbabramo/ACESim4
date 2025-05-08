using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using System.Diagnostics;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class ChunkExecutorFuzzTests
    {
        private const int Runs = 2500;
        private const int OriginalSourcesCount = 8;
        private static ArrayCommand[] BuildFailingCommandSequence()
        {
            return new[]
            {
                new ArrayCommand(ArrayCommandType.If                       , -1, -1 ), // 017
                new ArrayCommand(ArrayCommandType.IncrementBy              ,  7, 10 ), // 018
                new ArrayCommand(ArrayCommandType.EndIf                    , -1, -1 ), // 033
                new ArrayCommand(ArrayCommandType.IncrementBy              ,  7, 10 ), // 050
                new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex ,  8,  9 ), // 055
                new ArrayCommand(ArrayCommandType.If                       , -1, -1 ), // 056
                new ArrayCommand(ArrayCommandType.IncrementBy              ,  7,  9 ), // 058
                new ArrayCommand(ArrayCommandType.IncrementBy              ,  5, 24 ), // 081
                new ArrayCommand(ArrayCommandType.EndIf                    , -1, -1 ), // 082
                new ArrayCommand(ArrayCommandType.IncrementBy              ,  5,  9 ), // 089
            };
        }


        [TestMethod]
        public void RoslynReuse_Should_Match_Interpreter()
        {
            var cmds = BuildFailingCommandSequence();

            // virtual-stack large enough for every slot we touch (max index = 28)
            const int VS_SIZE = 32;
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length,
                VirtualStack = new double[VS_SIZE]
            };

            // create deterministic original sources – matches fuzz logic (seed = 37)
            var rnd = new Random(37 * 2 + 1);
            const int ORIGINAL_SOURCES = 8;
            var originalData =
                Enumerable.Range(0, ORIGINAL_SOURCES)
                          .Select(_ => rnd.NextDouble() < 0.2
                                       ? 0
                                       : (rnd.NextDouble() - 0.5) * 1000.0)
                          .ToArray();

            var orderedSources = originalData;          // single NextSource - ok
            var vs = new double[VS_SIZE];
            Array.Copy(originalData, vs, originalData.Length); // seed VS with originals

            // baseline interpreter
            var interp = new InterpreterChunkExecutor(cmds, 0, cmds.Length, false, null);
            interp.AddToGeneration(chunk);
            interp.PerformGeneration();
            int cosi = 0; bool cond = true;
            interp.Execute(chunk, vs, orderedSources, ref cosi, ref cond);
            var expected = vs.Take(ORIGINAL_SOURCES).ToArray();     // snapshot outputs

            // reset VS and run Roslyn-Reuse executor
            vs = new double[VS_SIZE];
            Array.Copy(originalData, vs, originalData.Length);
            cosi = 0; cond = true;
            var roslyn = new RoslynChunkExecutor(cmds, 0, cmds.Length,
                                                 useCheckpoints: false,
                                                 localVariableReuse: true);
            roslyn.AddToGeneration(chunk);
            roslyn.PerformGeneration();
            roslyn.Execute(chunk, vs, orderedSources, ref cosi, ref cond);
            var actual = vs.Take(ORIGINAL_SOURCES).ToArray();

            // **FAILS** today – proves the bug
            CollectionAssert.AreEqual(expected, actual,
                "Roslyn executor with local-reuse diverged from interpreter.");
        }




        [TestMethod]
        public void Fuzz_CompareExecutors_ByDepthThenSize()
        {
            int[] maxDepths = { 0, 1, 2, 3 };
            int?[] maxCommands = { 3, 5, 10, 100 };

            (int d, int c, int s)? jumpTo = (3, 3, 0); // Note: Delete or change this if we need to find a simpler failing case.
            bool jumping = jumpTo != null;

            for (int d = 0; d < maxDepths.Length; d++)
            {
                for (int c = 0; c < maxCommands.Length; c++)
                {
                    for (int s = 0; s < Runs; s++)
                    {
                        if (jumping)
                        {
                            d = jumpTo!.Value.d;
                            c = jumpTo!.Value.c;
                            s = jumpTo!.Value.s;
                            jumping = false;
                        }

                        int maxDepth = maxDepths[d];
                        int? maxCmd = maxCommands[c];

                        var builder = new FuzzCommandBuilder(s, OriginalSourcesCount);
                        var acl = builder.Build(targetSize: maxCmd ?? 50, maxDepth: maxDepth);
                        var cmds = acl.UnderlyingCommands;

                        string info = $"Depth {d}, CmdIdx {c}, Seed {s}";

                        try
                        {
                            CompareExecutors(acl, s, builder.MaxVirtualStackSize, info);
                        }
                        catch (Exception ex)
                        {
                            var dump = string.Join(Environment.NewLine,
                                cmds.Select((cmd, i) =>
                                    $"{i:000}: {cmd.CommandType,-25} idx={cmd.Index,3} src={cmd.SourceIndex,3}"));
                            Assert.Fail($"{info} failed{Environment.NewLine}{ex.Message}{Environment.NewLine}{dump}");
                        }
                    }
                }
            }
        }

        private sealed record ExecResult(int Cosi, bool Cond, double[] Vs, string Code);

        private static ExecResult Run(IChunkExecutor exec,
                                      ArrayCommandChunk chunk,
                                      double[] originalData,
                                      double[] orderedSources)
        {
            exec.AddToGeneration(chunk);
            exec.PreserveGeneratedCode = true;
            exec.PerformGeneration();

            var vs = new double[chunk.VirtualStack.Length];
            for (int i = 0; i < originalData.Length; i++)
                vs[i] = originalData[i]; // Initialize virtual stack
            int cosi = 0;
            bool cond = true;

            exec.Execute(chunk, vs, orderedSources, ref cosi, ref cond);

            return new ExecResult(cosi, cond, vs, exec.GeneratedCode);
        }

        private void CompareExecutors(ArrayCommandList aclOriginal,
                              int seed,
                              int maxVs,
                              string info)
        {
            var cmds = aclOriginal.UnderlyingCommands;
            /* ───────────── single-chunk baseline ───────────── */
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length,
                VirtualStack = new double[maxVs + 1]
            };

            Random _rnd = new Random(seed * 2 + 1);
            var originalData = Enumerable.Range(0, OriginalSourcesCount)
                               .Select(i => _rnd.NextDouble() < 0.2 ? 0 : (_rnd.NextDouble() - 0.5) * 1000.0)
                               .ToArray();
            var orderedSourceIndices = aclOriginal.OrderedSourceIndices;
            var orderedSources = orderedSourceIndices.Select(i => originalData[i]).ToArray();

            var baseline = Run(
                new InterpreterChunkExecutor(cmds, 0, cmds.Length, false, null),
                chunk,
                originalData,
                orderedSources);
            var baselineNonScratchOutputs = baseline.Vs.Take(originalData.Length).ToArray();

            var variants = new (Func<IChunkExecutor> Make, string Tag)[]
            {
                (() => new RoslynChunkExecutor(cmds, 0, cmds.Length, false, false), "Roslyn-NoReuse"),
                (() => new RoslynChunkExecutor(cmds, 0, cmds.Length, false,  true), "Roslyn-Reuse"),
                (() =>  new ILChunkExecutor(cmds, 0, cmds.Length, false),          "IL-NoReuse"),
                (() =>  new ILChunkExecutor(cmds, 0, cmds.Length,  true),          "IL-Reuse")
            };

            foreach (var (make, tag) in variants)
            {
                var res = Run(make(), chunk, originalData, orderedSources);
                var resNonScratchOutputs = res.Vs.Take(originalData.Length).ToArray();

                Assert.AreEqual(baseline.Cosi, res.Cosi,
                    $"Seed {seed} {info}: cosi mismatch [{tag}]{res.Code}");
                Assert.AreEqual(baseline.Cond, res.Cond,
                    $"Seed {seed} {info}: cond mismatch [{tag}]{res.Code}");
                CollectionAssert.AreEqual(baselineNonScratchOutputs, resNonScratchOutputs,
                    $"Seed {seed} {info}: vs mismatch [{tag}]{res.Code}");
            }

            bool alsoTestArrayCommandList = true;

            if (!alsoTestArrayCommandList)
                return;

            /* ───────────── full ACL + hoisting ───────────── */
            int hoistThreshold = _rnd.Next(2, cmds.Length + 1);

            double[] ExecuteAcl(ChunkExecutorKind kind)
            {
                // Create a new ACL with the same commands. This will exercise the command recorder. 
                var acl = new ArrayCommandList(cmds.Length, OriginalSourcesCount, hoistThreshold);
                int nextOrderedSource = 0;
                foreach (var cmd in cmds)
                {
                    if (cmd.CommandType is ArrayCommandType.Zero or ArrayCommandType.NextSource or ArrayCommandType.CopyTo)
                    { 
                        // Simulate the increase in NextArrayIndex that we would get by using the Recorder's commands
                        if (cmd.Index > acl.Recorder.NextArrayIndex)
                            acl.Recorder.NextArrayIndex++;
                    }
                    acl.Recorder.AddCommand(cmd);
                    if (cmd.CommandType == ArrayCommandType.NextSource)
                        acl.OrderedSourceIndices.Add(orderedSourceIndices[nextOrderedSource++]);
                }
                acl.CompleteCommandList(hoistLargeIfBodies: true);

                //TabbedText.WriteLine(acl.CommandListString());
                //TabbedText.WriteLine(acl.CommandTreeString);

                var data = new double[acl.VirtualStackSize];
                for (int i = 0; i < OriginalSourcesCount; i++)
                    data[i] = originalData[i];

                acl.CompileAndRunOnce(data, tracing: false, kind: kind);

                var mainDataOutput = acl.NonScratchData.ToArray();
                return mainDataOutput;
            }

            var aclBaselineVs = ExecuteAcl(ChunkExecutorKind.Interpreted);
            var aclBaselineNonScratchOutputs = aclBaselineVs.Take(originalData.Length).ToArray();

            CollectionAssert.AreEqual(baselineNonScratchOutputs, aclBaselineNonScratchOutputs,
                $"Seed {seed} {info}: hoisted ACL result differs from single-chunk baseline");

            var aclKinds = new (ChunkExecutorKind Kind, string Tag)[]
            {
                (ChunkExecutorKind.Roslyn, "ACL-Roslyn"),
                (ChunkExecutorKind.IL,     "ACL-IL")
            };

            foreach (var (kind, tag) in aclKinds)
            {
                var vs = ExecuteAcl(kind);
                CollectionAssert.AreEqual(aclBaselineVs, vs,
                    $"Seed {seed} {info}: hoisted ACL vs mismatch [{tag}]");
            }
        }


    }
}
