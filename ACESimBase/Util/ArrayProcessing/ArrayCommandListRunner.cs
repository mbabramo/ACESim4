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
        public static void ExecuteAll(
            this ArrayCommandList acl,
            double[] data,
            bool tracing = false,
            ChunkExecutorKind? kind = null,
            int? fallbackThreshold = null)
        {
            // Bake the tree (hoisting & stack metadata)
            ArrayCommandListRunner runner = GetRunner(acl, kind, fallbackThreshold);
            runner.Run(acl, data, tracing);
        }

        public static ArrayCommandListRunner GetRunner(this ArrayCommandList acl, ChunkExecutorKind? kind, int? fallbackThreshold)
        {
            if (acl.MaxCommandIndex == 0)
                acl.CompleteCommandList();
            
            acl.Debug_LogSourceStats();             //  DEBUG
            int originals = acl.OrderedSourceIndices.Count(i => i < acl.FirstScratchIndex); // DEBUG
            int scratch = acl.OrderedSourceIndices.Count - originals; // DEBUG
            TabbedText.WriteLine($"[ACL-DBG] OrderedSourceIndices originals={originals}  scratch={scratch}"); // DEBUG

#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine("Commands:");
            TabbedText.WriteLine(acl.CommandListString());
            TabbedText.WriteLine("");
            TabbedText.WriteLine("Tree:");
            TabbedText.WriteLine(acl.CommandTreeString);
#endif


            // Collect all chunks
            var chunks = new List<ArrayCommandChunk>();
            var seen = new HashSet<(int start, int end)>();
            acl.CommandTree!.WalkTree(n =>
            {
                var node = (NWayTreeStorageInternal<ArrayCommandChunk>)n;
                var c = node.StoredValue;

                // a slice executes only if it is a leaf *or* the Conditional gate itself
                bool isLeaf = node.Branches is null || node.Branches.Length == 0;
                bool isConditionalGate = c.Name == "Conditional";
                if (!isLeaf && !isConditionalGate && !c.Skip)
                    return;

                // ignore gaps and placeholders
                if (c.EndCommandRangeExclusive <= c.StartCommandRange)
                    return;                 // completely empty

                // keep exactly one representative of any [start,end) span
                var key = (c.StartCommandRange, c.EndCommandRangeExclusive);
                if (!seen.Add(key))
                    return;                 // already scheduled elsewhere

                chunks.Add(c);
            });

            // Create executor (interpreter is default)
            var exec = ChunkExecutorFactory.Create(
                kind ?? ChunkExecutorKind.Interpreted,
                acl.UnderlyingCommands,
                start: 0,
                end: acl.MaxCommandIndex,
                useCheckpoints: acl.UseCheckpoints,
                arrayCommandListForCheckpoints: acl,
                fallbackThreshold: fallbackThreshold);        // optional

            // Compile once and run
            var runner = new ArrayCommandListRunner(chunks, exec);
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

        private int _globalSkipDepth = 0; // nested‑If skip counter
        private int _pendingSrcAdvance = 0;
        private readonly List<ArrayCommandChunk> _chunks;
        private readonly int[] _srcStartBaselines;

        public ArrayCommandListRunner(IEnumerable<ArrayCommandChunk> commandChunks,
                                  IChunkExecutor compiledExecutor = null)
        {
            _compiled  = compiledExecutor ?? throw new ArgumentNullException(nameof(compiledExecutor));

            // materialise the slice list once so we can iterate repeatedly
            _chunks = commandChunks
                        .Where(c => c.EndCommandRangeExclusive > c.StartCommandRange)
                        .ToList();

            _srcStartBaselines = _chunks.Select(c => c.StartSourceIndices).ToArray();

            // register all slices with the executor and compile once
            foreach (var c in _chunks)
                _compiled.AddToGeneration(c);
            _compiled.PerformGeneration();
        }


        /// <summary>
        /// Executes <paramref name="acl"/> once against <paramref name="data"/>.
        /// </summary>
        public void Run(ArrayCommandList acl, double[] data, bool tracing = false)
        {
            // ── restore each slice’s cursors to their original positions ──
            for (int i = 0; i < _chunks.Count; i++)
            {
                _chunks[i].StartSourceIndices = _srcStartBaselines[i];
            }

            // ── reset per-run state ──
            _globalSkipDepth = 0;
            _pendingSrcAdvance = 0;

            if (acl is null) throw new ArgumentNullException(nameof(acl));
            if (data is null) throw new ArgumentNullException(nameof(data));

            //------------------------------------------------------------------
            // 1️  Stage ordered buffers
            //------------------------------------------------------------------
            _buffers = new OrderedBufferManager();
            _buffers.SourceIndices.AddRange(acl.OrderedSourceIndices);
            _buffers.PrepareBuffers(data, false);

            //------------------------------------------------------------------
            // 2️  Depth-first traversal with pre/post hooks
            //------------------------------------------------------------------
            acl.CommandTree!.WalkTree(
                beforeDescending: n => Pre((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl),
                afterAscending: n => Post((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl),
                parallel: n => ParallelPredicate((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl));
        }


        // ──────────────────────────────────────────────────────────────────────
        //  Pre‑order action  – stack sync + evaluate Conditional gate
        // ──────────────────────────────────────────────────────────────────────
        private void Pre(NWayTreeStorageInternal<ArrayCommandChunk> node, ArrayCommandList acl)
        {
            var c = node.StoredValue;

            if (c.Skip) return;

            if (c.Name == "Conditional")
                ExecuteChunk(c, acl);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Post‑order action – run leaf, merge increments
        // ──────────────────────────────────────────────────────────────────────
        private void Post(NWayTreeStorageInternal<ArrayCommandChunk> node, ArrayCommandList acl)
        {
            var c = node.StoredValue;
            if (c.Skip) return;

            // Body slices of a Conditional gate were executed by the gate itself
            if (node.Parent is NWayTreeStorageInternal<ArrayCommandChunk> p &&
                p.StoredValue?.Name == "Conditional")
                return;

            bool isLeaf = node.Branches is null || node.Branches.Length == 0;
            if (isLeaf && c.Name != "Conditional")
                ExecuteChunk(c, acl);
        }

        // Decide whether children of <node> may run concurrently
        private static bool ParallelPredicate(NWayTreeStorageInternal<ArrayCommandChunk> node,
                                              ArrayCommandList acl)
            => false;

        // ──────────────────────────────────────────────────────────────────────
        //  Execute a single chunk using compiled or fallback executor
        // ──────────────────────────────────────────────────────────────────────
        private void ExecuteChunk(ArrayCommandChunk c, ArrayCommandList acl)
        {
#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine($"[Runner] ExecuteChunk {c.ID} StartSourceIndices: {c.StartSourceIndices} ");
#endif
            // Skip entire chunk when inside a false branch
            if (_globalSkipDepth > 0) { ScanForNestedIfs(c, acl); return; }
            // Apply any carry-over from previously skipped chunks
            if (_pendingSrcAdvance != 0)
            {
                c.StartSourceIndices += _pendingSrcAdvance;
                _pendingSrcAdvance = 0;
            }

            int len = c.EndCommandRangeExclusive - c.StartCommandRange;

            int cosi = c.StartSourceIndices;      // current ordered‑source ptr (by ref)
            bool condition = true;

            _compiled.Execute(c,
                              acl.VirtualStack,
                              _buffers.Sources,
                              ref cosi,
                              ref condition);

            // Propagate pointer updates back to chunk for its siblings
            c.StartSourceIndices = cosi;
        }

        /// <summary>
        /// While <c>_globalSkipDepth &gt; 0</c> we are walking chunks that belong
        /// to a branch whose outer <c>If</c> evaluated <c>false</c>.  We must
        /// <b>count</b> nested If/EndIf pairs so that depth returns to 0 at the
        /// correct moment <i>and</i> we must <b>pretend</b> that every
        /// NextSource inside the skipped span executed, so that
        /// the first live chunk afterwards sees the right <paramref name="cosi"/>
        /// positions.
        /// </summary>
        private void ScanForNestedIfs(ArrayCommandChunk chunk, ArrayCommandList acl)
        {
            int srcAdv = 0;           // how many NextSource we skipped

            var cmds = acl.UnderlyingCommands;

            for (int i = chunk.StartCommandRange; i < chunk.EndCommandRangeExclusive; i++)
            {
                switch (cmds[i].CommandType)
                {
                    case ArrayCommandType.If:
                        _globalSkipDepth++;
                        break;

                    case ArrayCommandType.EndIf:
                        if (_globalSkipDepth > 0) _globalSkipDepth--;
                        break;

                    case ArrayCommandType.NextSource:
                        srcAdv++;                     // simulate os[cosi++] read
                        break;
                }
            }

            //--------------------------------------------------------------------
            // Fast-forward the runner-level cursors so that the *next* real chunk
            // starts with correct positions.  We keep the cursors as private
            // fields on the runner because there is no parent pointer on the chunk.
            //--------------------------------------------------------------------
            _pendingSrcAdvance += srcAdv;
        }


    }
}
