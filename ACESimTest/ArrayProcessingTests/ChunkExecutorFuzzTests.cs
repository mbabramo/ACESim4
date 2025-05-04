using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class ChunkExecutorFuzzTests
    {
        private const int Runs = 100;
        private const int OriginalSourcesCount = 8;

        [TestMethod]
        public void Fuzz_CompareExecutors_ByDepthThenSize()
        {
            int[] maxDepths = { 0, 1, 2, 3 };
            int?[] maxCommands = { 3, 5, 10, 25 };

            (int d, int c, int s)? jumpTo = null;
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
                            CompareExecutors(cmds, s, builder.MaxVirtualStackSize, info);
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
                                      double[] os)
        {
            exec.AddToGeneration(chunk);
            exec.PreserveGeneratedCode = true;
            exec.PerformGeneration();

            var vs = new double[chunk.VirtualStack.Length];
            for (int i = 0; i < os.Length; i++)
                vs[i] = os[i]; // Initialize virtual stack
            int cosi = 0;
            bool cond = true;

            exec.Execute(chunk, vs, os, ref cosi, ref cond);

            return new ExecResult(cosi, cond, vs, exec.GeneratedCode);
        }

        private void CompareExecutors(ArrayCommand[] cmds,
                              int seed,
                              int maxVs,
                              string info)
        {
            /* ───────────── single-chunk baseline ───────────── */
            var chunk = new ArrayCommandChunk
            {
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Length,
                VirtualStack = new double[maxVs + 1]
            };

            Random _rnd = new Random(seed * 2 + 1);
            var os = Enumerable.Range(0, OriginalSourcesCount)
                               .Select(i => _rnd.NextDouble() < 0.2 ? 0 : (_rnd.NextDouble() - 0.5) * 1000.0)
                               .ToArray();

            var baseline = Run(
                new InterpreterChunkExecutor(cmds, 0, cmds.Length, false, null),
                chunk,
                os);

            var variants = new (Func<IChunkExecutor> Make, string Tag)[]
            {
                (() => new RoslynChunkExecutor(cmds, 0, cmds.Length, false, false), "Roslyn-NoReuse"),
                (() => new RoslynChunkExecutor(cmds, 0, cmds.Length, false,  true), "Roslyn-Reuse"),
                (() =>  new ILChunkExecutor(cmds, 0, cmds.Length, false),          "IL-NoReuse"),
                (() =>  new ILChunkExecutor(cmds, 0, cmds.Length,  true),          "IL-Reuse")
            };

            foreach (var (make, tag) in variants)
            {
                var res = Run(make(), chunk, os);

                Assert.AreEqual(baseline.Cosi, res.Cosi,
                    $"Seed {seed} {info}: cosi mismatch [{tag}]{res.Code}");
                Assert.AreEqual(baseline.Cond, res.Cond,
                    $"Seed {seed} {info}: cond mismatch [{tag}]{res.Code}");
                CollectionAssert.AreEqual(baseline.Vs, res.Vs,
                    $"Seed {seed} {info}: vs mismatch [{tag}]{res.Code}");
            }

            bool alsoTestArrayCommandList = true;

            if (!alsoTestArrayCommandList)
                return;

            /* ───────────── full ACL + hoisting ───────────── */
            const int HoistThreshold = 12;   // deliberately small

            double[] ExecuteAcl(ChunkExecutorKind kind)
            {
                var acl = new ArrayCommandList(cmds, OriginalSourcesCount, HoistThreshold)
                {
                    MaxCommandsPerSplittableChunk = HoistThreshold
                };

                /* ensure recorder metadata is populated */
                int maxIdx = cmds.SelectMany(c => new[] { c.Index, c.SourceIndex })
                                 .Where(i => i >= 0)
                                 .Max();
                acl.Recorder.MaxArrayIndex = maxIdx;
                acl.Recorder.NextArrayIndex = maxIdx + 1;

                acl.CompleteCommandList(hoistLargeIfBodies: true);

                var data = new double[acl.FullArraySize];
                for (int i = 0; i < OriginalSourcesCount; i++)
                    data[i] = i;

                acl.ExecuteAll(data, tracing: false, kind: kind);

                var vsCopy = new double[acl.VirtualStack.Length];
                Array.Copy(acl.VirtualStack, vsCopy, vsCopy.Length);
                return vsCopy;
            }

            var aclBaselineVs = ExecuteAcl(ChunkExecutorKind.Interpreted);

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

            CollectionAssert.AreEqual(baseline.Vs, aclBaselineVs,
                $"Seed {seed} {info}: hoisted ACL result differs from single-chunk baseline");
        }


    }
}
