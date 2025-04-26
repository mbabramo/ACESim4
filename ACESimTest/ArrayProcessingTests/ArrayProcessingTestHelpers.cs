using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Test‑only helpers and structural assertions for the ArrayProcessing
    /// subsystem.  No production code depends on this file.
    /// </summary>
    internal static class ArrayProcessingTestHelpers
    {
        //───────────────────────────────────────────────────────────────────────
        //  0️⃣  Global‑state helpers
        //───────────────────────────────────────────────────────────────────────
        public static void ResetIds() => ArrayCommandChunk.NextID = 0;

        public static void WithDeterministicIds(Action body)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));
            int saved = ArrayCommandChunk.NextID;
            try { ResetIds(); body(); }
            finally { ArrayCommandChunk.NextID = saved; }
        }

        //───────────────────────────────────────────────────────────────────────
        //  1️⃣  ACL construction helpers
        //───────────────────────────────────────────────────────────────────────
        public static ArrayCommandList BuildAclWithSingleLeaf(
            Action<CommandRecorder> script,
            int maxNumCommands = 512,
            int initialArrayIndex = 0,
            int maxArrayIndex = 10,
            int maxCommandsPerChunk = int.MaxValue)
        {
            var acl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallelize: false)
            {
                MaxCommandsPerChunk = maxCommandsPerChunk
            };

            var rec = acl.Recorder;
            script(rec);

            acl.MaxCommandIndex = acl.NextCommandIndex;
            acl.CommandTree.StoredValue.EndCommandRangeExclusive = acl.NextCommandIndex;
            acl.CommandTree.StoredValue.EndSourceIndicesExclusive = acl.OrderedSourceIndices.Count;
            acl.CommandTree.StoredValue.EndDestinationIndicesExclusive = acl.OrderedDestinationIndices.Count;
            acl.MaxArrayIndex = Math.Max(acl.MaxArrayIndex, maxArrayIndex);
            return acl;
        }

        public static ArrayCommandList CreateStubAcl(
            IList<ArrayCommand> cmds,
            int maxCommandsPerChunk)
        {
            if (cmds is null) throw new ArgumentNullException(nameof(cmds));

            var acl = new ArrayCommandList(cmds.Count + 10, 0, parallelize: false)
            {
                MaxCommandsPerChunk = maxCommandsPerChunk
            };

            acl.UnderlyingCommands = cmds.ToArray();
            acl.NextCommandIndex = cmds.Count;
            acl.MaxCommandIndex = cmds.Count;

            acl.MaxArrayIndex = cmds.Count > 0
                ? cmds.Max(c => Math.Max(c.GetSourceIndexIfUsed(), c.GetTargetIndexIfUsed()))
                : -1;

            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null)
            {
                StoredValue = new ArrayCommandChunk
                {
                    ID = 0,
                    StartCommandRange = 0,
                    EndCommandRangeExclusive = cmds.Count
                }
            };
            acl.CommandTree = root;
            return acl;
        }

        public static (ArrayCommandList acl, int bodyLen) MakeOversizeIfBody(int bodyLen, int threshold)
        {
            var acl = BuildAclWithSingleLeaf(rec =>
            {
                int idx0 = rec.NewZero();
                rec.InsertEqualsValueCommand(idx0, 0);
                rec.InsertIf();
                for (int i = 0; i < bodyLen; i++)
                    rec.Increment(idx0, false, idx0);
                rec.InsertEndIf();
            },
            maxNumCommands: bodyLen + 5,
            maxCommandsPerChunk: threshold);

            acl.MaxCommandsPerChunk = threshold;
            return (acl, bodyLen);
        }

        public static (ArrayCommandList acl, int regionLen) MakeOversizeDepthRegion(
            int regionLen,
            int threshold)
        {
            var acl = BuildAclWithSingleLeaf(rec =>
            {
                rec.InsertIncrementDepthCommand();
                for (int i = 0; i < regionLen; i++)
                    rec.InsertBlankCommand();
                rec.InsertDecrementDepthCommand();
            },
            maxNumCommands: regionLen + 2,
            maxCommandsPerChunk: threshold);

            acl.MaxCommandsPerChunk = threshold;
            return (acl, regionLen);
        }

        public static ArrayCommandList BuildAclWithTwoLeaves(
            Action<CommandRecorder> first,
            Action<CommandRecorder> second,
            int maxCommandsPerChunk)
        {
            if (first is null) throw new ArgumentNullException(nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));

            var acl = new ArrayCommandList(1024, 0, parallelize: false)
            {
                MaxCommandsPerChunk = maxCommandsPerChunk
            };
            var rec = acl.Recorder;

            rec.StartCommandChunk(false, null, name: "L1");
            first(rec);
            rec.EndCommandChunk();

            rec.StartCommandChunk(false, null, name: "L2");
            second(rec);
            rec.EndCommandChunk();

            acl.MaxCommandIndex = acl.NextCommandIndex;
            acl.CommandTree.StoredValue.EndCommandRangeExclusive = acl.NextCommandIndex;
            acl.CommandTree.StoredValue.EndSourceIndicesExclusive = acl.OrderedSourceIndices.Count;
            acl.CommandTree.StoredValue.EndDestinationIndicesExclusive = acl.OrderedDestinationIndices.Count;
            return acl;
        }

        //───────────────────────────────────────────────────────────────────────
        //  2️⃣  Assertion helpers
        //───────────────────────────────────────────────────────────────────────
        public static void AssertBalancedIfTokens(ArrayCommandList acl, ArrayCommandChunk chunk)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            if (chunk is null) throw new ArgumentNullException(nameof(chunk));

            int depth = 0;
            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                var t = acl.UnderlyingCommands[i].CommandType;
                if (t == ArrayCommandType.If) depth++;
                if (t == ArrayCommandType.EndIf) depth--;
                depth.Should().BeGreaterOrEqualTo(0);
            }
            depth.Should().Be(0);
        }

        public static void AssertLeafSizeUnder(ArrayCommandList acl, int max)
        {
            foreach (var node in acl.PureSlices())
            {
                var info = node.StoredValue;
                int len = info.EndCommandRangeExclusive - info.StartCommandRange;
                len.Should().BeLessOrEqualTo(max);
            }
        }

        public static void InterpreterVsCompiled(ArrayCommandList acl, int arraySize = 200)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            double[] interp = new double[arraySize];
            double[] compiled = new double[arraySize];
            for (int i = 0; i < arraySize; i++)
                interp[i] = compiled[i] = i % 7;

            acl.ExecuteAll(interp, false, ChunkExecutorKind.Interpreted);
            foreach (var kind in new[]
            {
                ChunkExecutorKind.Roslyn,
                ChunkExecutorKind.RoslynWithLocalVariableRecycling,
                ChunkExecutorKind.IL,
                ChunkExecutorKind.ILWithLocalVariableRecycling
            })
            {
                acl.ExecuteAll(compiled, false, kind, null);
                compiled.Should().Equal(interp);
            }
        }

        //───────────────────────────────────────────────────────────────────────
        //  3️⃣  Debug helpers
        //───────────────────────────────────────────────────────────────────────
        public static IEnumerable<string> DumpTree(ArrayCommandList acl)
        {
            if (acl?.CommandTree == null)
                throw new ArgumentNullException(nameof(acl));

            var list = new List<string>();
            acl.CommandTree.WalkTree(nodeObj =>
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;
                var v = n.StoredValue;
                var cmds = Enumerable.Range(v.StartCommandRange,
                                             v.EndCommandRangeExclusive - v.StartCommandRange)
                                      .Select(i => acl.UnderlyingCommands[i].CommandType.ToString());
                string cmdList = !cmds.Any() ? "-" : string.Join(",", cmds);
                list.Add($"ID{v.ID}{(string.IsNullOrEmpty(v.Name) ? "" : $" {v.Name}")} [{{v.StartCommandRange}},{{v.EndCommandRangeExclusive}}) " +
                         $"Children={{n.Branches?.Count(b => b is not null) ?? 0}} Cmds:{cmdList}");
            });
            return list;
        }

        //───────────────────────────────────────────────────────────────────────
        //  4️⃣  Mini‑DSL extensions
        //───────────────────────────────────────────────────────────────────────
        public static void InsertBlankCommands(this CommandRecorder rec, int count)
        {
            if (rec == null) throw new ArgumentNullException(nameof(rec));
            for (int i = 0; i < count; i++)
                rec.InsertBlankCommand();
        }

        //───────────────────────────────────────────────────────────────────────
        //  5️⃣  Conditional helpers
        //───────────────────────────────────────────────────────────────────────
        public static NWayTreeStorageInternal<ArrayCommandChunk> FindFirstConditional(this ArrayCommandList acl)
        {
            NWayTreeStorageInternal<ArrayCommandChunk> gate = null;
            acl.CommandTree.WalkTree(n =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                if (node.StoredValue.Name == "Conditional" && gate == null)
                    gate = node;
            });
            return gate;
        }
    }

    //───────────────────────────────────────────────────────────────────────────
    //  Structural comparers & traversal extensions
    //───────────────────────────────────────────────────────────────────────────
    internal static class TreeAssert
    {
        public static void AreEqual(NWayTreeStorageInternal<ArrayCommandChunk> expected,
                                    NWayTreeStorageInternal<ArrayCommandChunk> actual,
                                    string because = "")
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));

            var e = expected.StoredValue;
            var a = actual.StoredValue;

            a.StartCommandRange.Should().Be(e.StartCommandRange, because);
            a.EndCommandRangeExclusive.Should().Be(e.EndCommandRangeExclusive, because);
            a.ChildrenParallelizable.Should().Be(e.ChildrenParallelizable, because);
            a.Name.Should().Be(e.Name, because);

            var expChildren = ChildList(expected);
            var actChildren = ChildList(actual);
            actChildren.Length.Should().Be(expChildren.Length, because);
            for (int i = 0; i < expChildren.Length; i++)
                AreEqual(expChildren[i], actChildren[i], because);
        }

        public static void AreEqual(ArrayCommandList expected, ArrayCommandList actual, string because = "")
            => AreEqual(expected.CommandTree, actual.CommandTree, because);

        private static NWayTreeStorageInternal<ArrayCommandChunk>[] ChildList(
            NWayTreeStorageInternal<ArrayCommandChunk> node)
            => node.Branches?
                   .Where(b => b is not null)
                   .Cast<NWayTreeStorageInternal<ArrayCommandChunk>>()
                   .ToArray() ?? Array.Empty<NWayTreeStorageInternal<ArrayCommandChunk>>();
    }

    internal static class TreeTraversalExtensions
    {
        public static IEnumerable<NWayTreeStorageInternal<ArrayCommandChunk>> Nodes(this ArrayCommandList acl)
        {
            if (acl?.CommandTree == null) throw new ArgumentNullException(nameof(acl));
            var stack = new Stack<NWayTreeStorageInternal<ArrayCommandChunk>>();
            stack.Push(acl.CommandTree);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                if (n.Branches != null)
                    for (int i = n.Branches.Length - 1; i >= 0; i--)
                        if (n.Branches[i] is NWayTreeStorageInternal<ArrayCommandChunk> child)
                            stack.Push(child);
            }
        }

        public static IEnumerable<NWayTreeStorageInternal<ArrayCommandChunk>> PureSlices(this ArrayCommandList acl)
        {
            foreach (var n in acl.Nodes())
            {
                if (n.Branches is { Length: > 0 }) continue;

                bool hasFlow = false;
                var info = n.StoredValue;
                for (int i = info.StartCommandRange; i < info.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If || t == ArrayCommandType.EndIf ||
                        t == ArrayCommandType.IncrementDepth || t == ArrayCommandType.DecrementDepth)
                    {
                        hasFlow = true;
                        break;
                    }
                }
                if (!hasFlow) yield return n;
            }
        }
    }
}
