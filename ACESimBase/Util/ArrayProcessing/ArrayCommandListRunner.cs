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

            // Collect leaf chunks requiring compilation
            var leafChunks = new List<ArrayCommandChunk>();
            acl.CommandTree!.WalkTree(n =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                var c = node.StoredValue;

                // a slice executes only if it is a leaf *or* the Conditional gate itself
                bool isLeaf = node.Branches is null || node.Branches.Length == 0;
                if (!isLeaf)
                    return;

                // ignore gaps and placeholders
                if (c.EndCommandRangeExclusive <= c.StartCommandRange)
                    return; 

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
        private readonly IChunkExecutor _compiled;

        // Runtime helpers (one instance per Run)
        private OrderedBufferManager _buffers = null!;  // created in Run()
        private ArrayCommandList _acl;
        private double[] _data;
        private int _cosi = 0;
        private bool _condition = true;

        public ArrayCommandListRunner(IEnumerable<ArrayCommandChunk> leafChunks,
                                  IChunkExecutor compiledExecutor = null)
        {
            _compiled  = compiledExecutor ?? throw new ArgumentNullException(nameof(compiledExecutor));

            // register all slices with the executor and compile once
            foreach (var c in leafChunks)
                _compiled.AddToGeneration(c);
            _compiled.PerformGeneration();
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

            //------------------------------------------------------------------
            // Virtual stack initialization
            //------------------------------------------------------------------
            for (int d = 0; d < data.Length; d++)
                acl.VirtualStack[d] = data[d];
            for (int i = data.Length; i < acl.VirtualStack.Length; i++)
                acl.VirtualStack[i] = 0;

            //------------------------------------------------------------------
            // Stage ordered buffers
            //------------------------------------------------------------------
            _buffers = new OrderedBufferManager();
            _buffers.SourceIndices.AddRange(acl.OrderedSourceIndices);
            _buffers.PrepareBuffers(data, false);
            _cosi = 0;
            _condition = true;

            //------------------------------------------------------------------
            // Depth-first traversal with pre/post hooks
            //------------------------------------------------------------------
            acl.CommandTree!.WalkTreeWithPredicate(ShouldVisitNodesChildren, ExecuteOrSkipNode);

            //------------------------------------------------------------------
            // Copy back to original data
            //------------------------------------------------------------------
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
                _cosi += node.StoredValue.SourcesInBody;
            else
            {
                bool isLeaf = node.IsLeaf();
                if (!isLeaf)
                    return; // this node has been split into other nodes, so we'll execute then
                _compiled.Execute(node.StoredValue,
                                  _acl.VirtualStack,
                                  _buffers.Sources,
                                  ref _cosi,
                                  ref _condition);
            }
        }
    }
}
