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
    /// Test-only helpers and structural assertions for the ArrayProcessing
    /// subsystem.  No production code depends on this file.
    /// </summary>
    internal static class ArrayProcessingTestHelpers
    {
        /* --------------------------------------------------------------------
           SECTION 0  Global-state helpers
           ------------------------------------------------------------------*/
        public static void ResetIds() => ArrayCommandChunk.NextID = 0;

        public static void WithDeterministicIds(Action body)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));
            int saved = ArrayCommandChunk.NextID;
            try { ResetIds(); body(); }
            finally { ArrayCommandChunk.NextID = saved; }
        }

        /* --------------------------------------------------------------------
           SECTION 1  ACL construction helpers
           ------------------------------------------------------------------*/
        public static ArrayCommandList BuildAclWithSingleLeaf(
            Action<CommandRecorder> script,
            int maxNumCommands = 512,
            int initialArrayIndex = 0,
            int maxArrayIndex = 10,
            int maxCommandsPerChunk = int.MaxValue,
            bool hoistLargeIfBodies = true)
        {
            var acl = new ArrayCommandList(maxNumCommands, initialArrayIndex)
            {
                MaxCommandsPerSplittableChunk = maxCommandsPerChunk
            };

            var rec = acl.Recorder;
            script(rec);
            acl.CompleteCommandList(hoistLargeIfBodies);

            return acl;
        }

        public static (ArrayCommandList acl, int bodyLen) MakeOversizeIfBody(int bodyLen, int threshold, bool addDepthChanges = false)
        {
            var acl = BuildAclWithSingleLeaf(rec =>
            {
                int idx0 = rec.NewZero();
                rec.InsertEqualsValueCommand(idx0, 0);
                rec.InsertIf();
                if (addDepthChanges)
                    for (int i = 0; i < bodyLen; i++)
                        rec.IncrementDepth(); // providing places for splitting
                for (int i = 0; i < bodyLen; i++)
                    rec.Increment(idx0, false, idx0);

                if (addDepthChanges)
                    for (int i = 0; i < bodyLen; i++)
                        rec.DecrementDepth();
                rec.InsertEndIf();
            },
            maxNumCommands: bodyLen + 5,
            maxCommandsPerChunk: threshold);

            acl.MaxCommandsPerSplittableChunk = threshold;
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

            acl.MaxCommandsPerSplittableChunk = threshold;
            return (acl, regionLen);
        }

        public static ArrayCommandList BuildAclWithTwoLeaves(
            Action<CommandRecorder> first,
            Action<CommandRecorder> second,
            int maxCommandsPerChunk)
        {
            if (first is null) throw new ArgumentNullException(nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));

            var acl = new ArrayCommandList(1024, 0)
            {
                MaxCommandsPerSplittableChunk = maxCommandsPerChunk
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
            return acl;
        }

        /// <summary>Allocate and seed an array with a deterministic pattern.</summary>
        public static double[] Seed(int length, Func<int, double> f)
        {
            var a = new double[length];
            for (int i = 0; i < length; i++) a[i] = f(i);
            return a;
        }

        /// <summary>Quick one-liner to create a new ACL with common defaults.</summary>
        public static ArrayCommandList NewAcl(int maxCmds = 1_000,
                                              int initialIdx = 0,
                                              int maxPerChunk = 50)
            => new(maxCmds, initialIdx) { MaxCommandsPerSplittableChunk = maxPerChunk };

        /* --------------------------------------------------------------------
           SECTION 2  Assertion helpers
           ------------------------------------------------------------------*/
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
                int len = node.StoredValue.EndCommandRangeExclusive -
                          node.StoredValue.StartCommandRange;
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

            acl.CompileAndRunOnce(interp, false, ChunkExecutorKind.Interpreted);
            foreach (var kind in new[]
            {
                ChunkExecutorKind.Roslyn,
                ChunkExecutorKind.RoslynWithLocalVariableRecycling,
                ChunkExecutorKind.IL,
                ChunkExecutorKind.ILWithLocalVariableRecycling
            })
            {
                acl.CompileAndRunOnce(compiled, false, kind, null);
                compiled.Should().Equal(interp);
            }
        }

        /* --------------------------------------------------------------------
           SECTION 3  Debug helpers
           ------------------------------------------------------------------*/
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
                list.Add(
                    $"ID{v.ID}{(string.IsNullOrEmpty(v.Name) ? "" : $" {v.Name}")} " +
                    $"[{v.StartCommandRange},{v.EndCommandRangeExclusive}) " +
                    $"Children={{n.Branches?.Count(b => b is not null) ?? 0}} " +
                    $"Cmds:{cmdList}");
            });
            return list;
        }


        /* --------------------------------------------------------------------
           SECTION 4  Mini-DSL extensions for recording commands
           ------------------------------------------------------------------*/
        public static void InsertBlankCommands(this CommandRecorder rec, int count)
        {
            if (rec == null) throw new ArgumentNullException(nameof(rec));
            for (int i = 0; i < count; i++)
                rec.InsertBlankCommand();
        }

        /* --------------------------------------------------------------------
           SECTION 5  Gate helpers (added)
           ------------------------------------------------------------------*/
        public static NWayTreeStorageInternal<ArrayCommandChunk> FirstGate(this ArrayCommandList acl) =>
            acl.Nodes()
               .OfType<NWayTreeStorageInternal<ArrayCommandChunk>>()
               .FirstOrDefault(n =>
               {
                   var info = n.StoredValue;
                   if (n.Branches is not { Length: > 0 }) return false;
                   if (info.StartCommandRange >= info.EndCommandRangeExclusive) return false;
                   return acl.UnderlyingCommands[info.StartCommandRange].CommandType == ArrayCommandType.If;
               });

        public static int GateCount(this ArrayCommandList acl) =>
            acl.Nodes().Count(n =>
            {
                var info = n.StoredValue;
                if (n.Branches is not { Length: > 0 }) return false;
                if (info.StartCommandRange >= info.EndCommandRangeExclusive) return false;
                return acl.UnderlyingCommands[info.StartCommandRange].CommandType == ArrayCommandType.If;
            });
    }

    /* ------------------------------------------------------------------------
       Structural comparers & traversal extensions
       ----------------------------------------------------------------------*/
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
            a.Name.Should().Be(e.Name, because);

            var expChildren = ChildList(expected);
            var actChildren = ChildList(actual);
            actChildren.Length.Should().Be(expChildren.Length, because);
            for (int i = 0; i < expChildren.Length; i++)
                AreEqual(expChildren[i], actChildren[i], because);
        }

        public static void AreEqual(ArrayCommandList expected,
                                    ArrayCommandList actual,
                                    string because = "")
            => AreEqual(expected.CommandTree, actual.CommandTree, because);

        private static NWayTreeStorageInternal<ArrayCommandChunk>[] ChildList(
            NWayTreeStorageInternal<ArrayCommandChunk> node) =>
            node.Branches == null
                ? Array.Empty<NWayTreeStorageInternal<ArrayCommandChunk>>()
                : node.Branches
                      .Where(b => b is NWayTreeStorageInternal<ArrayCommandChunk>)
                      .Select(b => (NWayTreeStorageInternal<ArrayCommandChunk>)b!)
                      .ToArray();
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
