using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.NWayTreeStorage;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.ArrayProcessingTests
{
    /// <summary>
    /// Shared mini‑DSL helpers for unit‑tests that exercise HoistPlanner, HoistMutator
    /// and ArrayCommandList execution.  All helpers remain <strong>test‑only</strong>
    /// (they never leak into production assemblies).
    /// </summary>
    internal static class ArrayProcessingTestHelpers
    {
        //───────────────────────────────────────────────────────────────────────────
        //  0️⃣  Global‑state helpers
        //───────────────────────────────────────────────────────────────────────────
        /// <summary>Reset <see cref="ArrayCommandChunk.NextID"/> so tests have deterministic IDs.</summary>
        public static void ResetIds() => ArrayCommandChunk.NextID = 0;

        /// <summary>
        /// Execute <paramref name="body"/> with <see cref="ResetIds"/> called before and after.
        /// Handy when running data‑driven tests in parallel.
        /// </summary>
        public static void WithDeterministicIds(Action body)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));
            int saved = ArrayCommandChunk.NextID;
            try { ResetIds(); body(); }
            finally { ArrayCommandChunk.NextID = saved; }
        }

        // ArrayProcessingTestHelpers.cs
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

            // Record directly into the root leaf.
            var rec = acl.Recorder;
            script(rec);

            // Finalise the root’s metadata; hoisting is left to the tests.
            acl.MaxCommandIndex = acl.NextCommandIndex;

            var root = acl.CommandTree.StoredValue;
            root.EndCommandRangeExclusive = acl.NextCommandIndex;
            root.EndSourceIndicesExclusive = acl.OrderedSourceIndices.Count;
            root.EndDestinationIndicesExclusive = acl.OrderedDestinationIndices.Count;

            acl.MaxArrayIndex = Math.Max(acl.MaxArrayIndex, maxArrayIndex);
            return acl;
        }



        /// <summary>
        /// Create an ACL from an existing list of commands.  The command list is assumed
        /// to form the entire program contained in one leaf.
        /// </summary>
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
                : -1;                                        // no indices used when empty

            var root = new NWayTreeStorageInternal<ArrayCommandChunk>(null);
            root.StoredValue = new ArrayCommandChunk
            {
                ID = 0,
                StartCommandRange = 0,
                EndCommandRangeExclusive = cmds.Count
            };
            acl.CommandTree = root;
            return acl;
        }


        /// <summary>
        /// Build and finalise a simple ACL that contains one oversize <c>If…EndIf</c>
        /// body of <paramref name="bodyLen"/> commands.  The <see cref="ArrayCommandList.MaxCommandsPerChunk"/>
        /// is set to <paramref name="threshold"/> so the list is guaranteed to be oversize.
        /// Returns the ACL <em>and</em> the body length for convenience.
        /// </summary>
        public static (ArrayCommandList acl, int bodyLen) MakeOversizeIfBody(int bodyLen, int threshold)
        {
            ArrayCommandList acl = BuildAclWithSingleLeaf(rec =>
            {
                int idx0 = rec.NewZero();
                rec.InsertEqualsValueCommand(idx0, 0);   // always true
                rec.InsertIf();
                for (int i = 0; i < bodyLen; i++)
                    rec.Increment(idx0, false, idx0);
                rec.InsertEndIf();
            },
            maxNumCommands: bodyLen + 5,
            maxCommandsPerChunk: threshold);

            acl.MaxCommandsPerChunk = threshold; // enforce threshold
            return (acl, bodyLen);
        }

        /// <summary>
        /// Produce an ACL containing <paramref name="depth"/> nested <c>If</c> blocks.
        /// All conditions are <c>true</c>; the innermost body is a single increment.
        /// </summary>
        public static ArrayCommandList MakeNestedIf(int depth, int maxPerChunk)
        {
            return BuildAclWithSingleLeaf(rec =>
            {
                int idx0 = rec.NewZero();
                for (int d = 0; d < depth; d++)
                {
                    rec.InsertEqualsValueCommand(idx0, 0);
                    rec.InsertIf();
                }
                rec.Increment(idx0, false, idx0);
                for (int d = 0; d < depth; d++)
                    rec.InsertEndIf();
            },
            maxNumCommands: depth * 2 + 3,
            maxCommandsPerChunk: maxPerChunk);
        }

        //───────────────────────────────────────────────────────────────────────────
        //  2️⃣  Assertion helpers
        //───────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Asserts that the <c>If</c> / <c>EndIf</c> tokens are balanced within the
        /// command range of <paramref name="chunk"/>.
        /// </summary>
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
                depth.Should().BeGreaterOrEqualTo(0, "unexpected EndIf before If");
            }
            depth.Should().Be(0, "unbalanced If/EndIf tokens in chunk");
        }

        /// <summary>
        /// Traverse <paramref name="acl"/> and assert that every executable leaf has
        /// no more than <paramref name="max"/> commands.
        /// </summary>
        public static void AssertLeafSizeUnder(ArrayCommandList acl, int max)
        {
            foreach (var nodeObj in EnumerateNodes(acl.CommandTree))
            {
                var n = (NWayTreeStorageInternal<ArrayCommandChunk>)nodeObj;

                // skip non-leaf nodes
                if (n.Branches is { Length: > 0 })
                    continue;

                var info = n.StoredValue;
                int len = info.EndCommandRangeExclusive - info.StartCommandRange;

                // control-flow leaves (still contain If/EndIf) are exempt
                bool hasFlowTokens = false;
                for (int i = info.StartCommandRange; i < info.EndCommandRangeExclusive; i++)
                {
                    var t = acl.UnderlyingCommands[i].CommandType;
                    if (t == ArrayCommandType.If || t == ArrayCommandType.EndIf)
                    {
                        hasFlowTokens = true;
                        break;
                    }
                }
                if (hasFlowTokens)
                    continue;

                // pure linear slice → must obey the limit
                len.Should().BeLessOrEqualTo(max, "leaf {0} exceeds threshold", info.ID);
            }
        }


#if DEBUG
        /// <summary>Pretty‑print the command tree to the debug output.</summary>
        public static void DumpTree(ArrayCommandList acl, string title = null)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            Debug.WriteLine("==== " + (title ?? "Command tree") + " ====");
            acl.CommandTree.WalkTree(n =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                var info = node.StoredValue;
                Debug.WriteLine($"ID {info.ID,4}  range=[{info.StartCommandRange},{info.EndCommandRangeExclusive})  children={(node.Branches?.Length ?? 0)}");
            });
        }
#endif

        //───────────────────────────────────────────────────────────────────────────
        //  3️⃣  Execution comparison helper
        //───────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Runs <paramref name="acl"/> once via the interpreter and once via
        /// <see cref="ArrayCommandList.ExecuteAll"/> (which may use compiled chunks)
        /// and asserts the resulting arrays are identical.
        /// </summary>
        public static void InterpreterVsCompiled(ArrayCommandList acl, int arraySize = 200)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            double[] interp = new double[arraySize];
            double[] compiled = new double[arraySize];

            for (int i = 0; i < arraySize; i++)
                interp[i] = compiled[i] = i % 7;   // deterministic seed

            acl.ExecuteAll(interp, false, ChunkExecutorKind.Interpreted);
            foreach (var kind in new ChunkExecutorKind[] {
                ChunkExecutorKind.Roslyn,
                ChunkExecutorKind.RoslynWithLocalVariableRecycling,
                ChunkExecutorKind.IL,
                ChunkExecutorKind.ILWithLocalVariableRecycling })
            {
                acl.ExecuteAll(compiled, false, kind, null);
                compiled.Should().Equal(interp);
            }
        }

        //───────────────────────────────────────────────────────────────────────────
        //  Utility: enumerate tree nodes depth‑first
        //───────────────────────────────────────────────────────────────────────────
        private static IEnumerable<NWayTreeStorageInternal<ArrayCommandChunk>> EnumerateNodes(
            NWayTreeStorageInternal<ArrayCommandChunk> root)
        {
            if (root == null) yield break;
            var stack = new Stack<NWayTreeStorageInternal<ArrayCommandChunk>>();
            stack.Push(root);
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
                                     .Select(i => acl.UnderlyingCommands[i].CommandType.ToString())
                                     .ToArray();
                string cmdList = cmds.Length == 0 ? "-" : string.Join(",", cmds);

                list.Add($"ID{v.ID}{(string.IsNullOrEmpty(v.Name) ? "" : $" {v.Name}")} "
                       + $"[{v.StartCommandRange},{v.EndCommandRangeExclusive}) "
                       + $"Children={n.Branches?.Count(b => b is not null) ?? 0} "
                       + $"Cmds:{cmdList}");
            });

            return list;
        }


        //───────────────────────────────────────────────────────────────────────
        //  🔍  Helper – locate the first Conditional gate in <acl>
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
}
