// -----------------------------------------------------------------------------
//  ArrayCommandListRunner.cs   (refactored to use OrderedBufferManager)
// -----------------------------------------------------------------------------
//  Executes a prepared ArrayCommandList by traversing its chunk‑tree and
//  delegating each chunk to an IChunkExecutor.  All staging / merging of
//  ordered‑sources and ordered‑destinations is now delegated to
//  OrderedBufferManager instead of living inside ArrayCommandList.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.Util.ArrayProcessing
{
    using System.Collections.Generic;
    using System.Linq;
    using ACESimBase.Util.ArrayProcessing;
    using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
    using ACESimBase.Util.Debugging;
    using ACESimBase.Util.NWayTreeStorage;

    public static class ArrayCommandListRunnerExtensions
    {
        /// <summary>
        /// Finalises <paramref name="acl"/>, builds an <see cref="IChunkExecutor"/>
        /// (default = interpreter), compiles once, then executes.
        /// </summary>
        /// <param name="acl">Prepared command list.</param>
        /// <param name="data">In-place data array.</param>
        /// <param name="tracing">Trace flag (same meaning as the old ExecuteAll).</param>
        /// <param name="kind">
        ///     Executor backend; <c>null</c> ⇒ <see cref="ChunkExecutorKind.Interpreted"/>.
        /// </param>
        /// <param name="fallbackThreshold">
        ///     Optional “small-chunk” threshold (routes short slices to an interpreter
        ///     when a code-gen backend is chosen).
        /// </param>
        public static void CompileAndRunOnce(
            this ArrayCommandList acl,
            double[] data,
            bool tracing = false,
            ChunkExecutorKind? kind = null,
            int? fallbackThreshold = null)
        {
            // Bake the tree (hoisting & stack metadata)
            ArrayCommandListRunner runner = GetCompiledRunner(acl, kind, fallbackThreshold);
            runner.Run(acl, data, copyBackToOriginalData: false);
        }

        public static ArrayCommandListRunner GetCompiledRunner(this ArrayCommandList acl, ChunkExecutorKind? kind, int? fallbackThreshold)
        {
            if (acl.MaxCommandIndex == 0)
                acl.CompleteCommandList();

        #if OUTPUT_HOISTING_INFO
            int originals = acl.OrderedSourceIndices.Count(i => i < acl.SizeOfMainData);
            int scratch = acl.OrderedSourceIndices.Count - originals;
            TabbedText.WriteLine($"[ACL-DBG] OrderedSourceIndices originals={originals}  scratch={scratch}");
            TabbedText.WriteLine("Commands:");
            TabbedText.WriteLine(acl.CommandListString());
            TabbedText.WriteLine("");
            TabbedText.WriteLine("Tree:");
            TabbedText.WriteLine(acl.CommandTreeString);
        #endif

            // Collect EFFECTFUL leaf chunks only (skip true no-ops like gaps/tails of blanks/comments/depth markers)
            var leafChunks = new List<ArrayCommandChunk>();
            var cmds = acl.UnderlyingCommands;

            bool SliceHasEffect(ArrayCommandChunk c)
            {
                int s = c.StartCommandRange, e = c.EndCommandRangeExclusive;
                for (int i = s; i < e; i++)
                {
                    switch (cmds[i].CommandType)
                    {
                        case ArrayCommandType.Zero:
                        case ArrayCommandType.CopyTo:
                        case ArrayCommandType.NextSource:
                        case ArrayCommandType.MultiplyBy:
                        case ArrayCommandType.IncrementBy:
                        case ArrayCommandType.DecrementBy:
                        case ArrayCommandType.EqualsOtherArrayIndex:
                        case ArrayCommandType.NotEqualsOtherArrayIndex:
                        case ArrayCommandType.GreaterThanOtherArrayIndex:
                        case ArrayCommandType.LessThanOtherArrayIndex:
                        case ArrayCommandType.EqualsValue:
                        case ArrayCommandType.NotEqualsValue:
                        case ArrayCommandType.NextDestination:
                            return true; // has side-effects or mutates cond/pointers
                        default:
                            break; // Blank/Comment/If/EndIf/IncDepth/DecDepth/Checkpoint → ignore for codegen
                    }
                }
                return false;
            }

            acl.CommandTree!.WalkTree(n =>
            {
                var node = (ACESimBase.Util.NWayTreeStorage.NWayTreeStorageInternal<ArrayCommandChunk>)n;
                var c = node.StoredValue;

                // Only compile executable leaves with content
                bool isLeaf = node.Branches is null || node.Branches.Length == 0;
                if (!isLeaf)
                    return;

                if (c.EndCommandRangeExclusive <= c.StartCommandRange)
                    return; // empty placeholder

                if (!SliceHasEffect(c))
                    return; // true no-op slice → don't schedule

                leafChunks.Add(c);
            });

            // Create executor (interpreter is default)
            var exec = ChunkExecutorFactory.Create(
                kind ?? ChunkExecutorKind.Interpreted,
                acl.UnderlyingCommands,
                start: 0,
                end: acl.MaxCommandIndex,
                useCheckpoints: acl.UseCheckpoints,
                arrayCommandListForCheckpoints: acl,
                fallbackThreshold: fallbackThreshold);

            // Compile once and run
            var runner = new ArrayCommandListRunner(leafChunks, exec);
            return runner;
        }

    }



    /// <summary>
    /// Orchestrates run‑time execution of an <see cref="ArrayCommandList"/>.
    /// The class is <b>stateless across runs</b> except for the executors it
    /// holds, so it can be reused to execute the same ACL on multiple data
    /// arrays without re‑compilation.  All per‑run staging of ordered buffers is
    /// now handled by <see cref="OrderedBufferManager"/>.
    /// </summary>
    public sealed class ArrayCommandListRunner
    {
        private readonly IChunkExecutor _executor;    
        private readonly HashSet<ArrayCommandChunk> _scheduledLeaves;

        // Runtime helpers (one instance per Run)
        private OrderedBufferManager _buffers = null!;  // created in Run()
        private ArrayCommandList _acl;
        private double[] _data;
        private int _cosi = 0;
        private int _codi = 0;
        private bool _condition = true;
        public HashSet<int> DebugBreakOnCommandIndices { get; set; }

        public ArrayCommandListRunner(IEnumerable<ArrayCommandChunk> leafChunks,
                                      IChunkExecutor compiledExecutor = null)
        {
            _executor = compiledExecutor ?? throw new ArgumentNullException(nameof(compiledExecutor));

            // Track the compiled set so we can skip no-op leaves at runtime as well
            _scheduledLeaves = new HashSet<ArrayCommandChunk>(leafChunks);

            foreach (var c in leafChunks)
                _executor.AddToGeneration(c);
            _executor.PerformGeneration();
        }


        /// <summary>
        /// Executes <paramref name="acl"/> once against <paramref name="data"/>.
        /// </summary>
        public void Run(ArrayCommandList acl, double[] data, bool copyBackToOriginalData = true, bool trace = false)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            if (data is null) throw new ArgumentNullException(nameof(data));

            _acl = acl;
            _data = data;

            // Initialize virtual stack from the incoming data.
            for (int d = 0; d < data.Length; d++)
                acl.VirtualStack[d] = data[d];
            for (int i = data.Length; i < acl.VirtualStack.Length; i++)
                acl.VirtualStack[i] = 0;

            // Stage ordered buffers (sources snapshot + zeroed destinations).
            _buffers = new OrderedBufferManager();
            _buffers.SourceIndices.AddRange(acl.OrderedSourceIndices);
            _buffers.DestinationIndices.AddRange(acl.OrderedDestinationIndices);
            _buffers.PrepareBuffers(data, false);

            // Reset consumption pointers and the current condition.
            _cosi = 0;
            _codi = 0;
            _condition = true;

            // Traverse and execute.
            acl.CommandTree!.WalkTreeWithPredicate(ShouldVisitNodesChildren, ExecuteOrSkipNode);

            // Merge buffered destination increments back into the working array.
            _buffers.ApplyDestinations(_acl.VirtualStack, false);

            // Optionally copy result back to the original data array.
            if (copyBackToOriginalData)
            {
                for (int i = 0; i < _data.Length; i++)
                    _data[i] = acl.VirtualStack[i];
            }
        }


        private bool ShouldVisitNodesChildren(NWayTreeStorage<ArrayCommandChunk> node)
        {
            return !IsConditionalNodeThatShouldBeSkipped(node);
        }

        private bool IsConditionalNodeThatShouldBeSkipped(NWayTreeStorage<ArrayCommandChunk> node)
        {
            if (!node.StoredValue.IsConditional)
            {
                return false;
            }
            return !_condition;
        }

        private void ExecuteOrSkipNode(NWayTreeStorage<ArrayCommandChunk> node)
        {
            bool skipBecauseConditionFails = IsConditionalNodeThatShouldBeSkipped(node);
            if (skipBecauseConditionFails)
            {
                _cosi += node.StoredValue.SourcesInBody;
                _codi += node.StoredValue.EndDestinationIndicesExclusive - node.StoredValue.StartDestinationIndices;
                return;
            }
            if (node.IsLeaf() && LeafContainsAnyBreakpoint(node.StoredValue))
                System.Diagnostics.Debugger.Break();


            if (!node.IsLeaf())
                return;

            // If we didn’t schedule this leaf (true no-op), silently skip at runtime.
            if (!_scheduledLeaves.Contains(node.StoredValue))
                return;

            _executor.Execute(node.StoredValue,
                              _acl.VirtualStack,
                              _buffers.Sources,
                              _buffers.Destinations,
                              ref _cosi,
                              ref _codi,
                              ref _condition);
        }

        private bool LeafContainsAnyBreakpoint(ArrayCommandChunk c)
        {
            if (DebugBreakOnCommandIndices == null || DebugBreakOnCommandIndices.Count == 0) return false;
            foreach (var b in DebugBreakOnCommandIndices)
                if (b >= c.StartCommandRange && b < c.EndCommandRangeExclusive) return true;
            return false;
        }

    }
}
