// -----------------------------------------------------------------------------
//  ArrayCommandListRunner.cs
//  -----------------------------------------------------------------------------
//  Executes a prepared ArrayCommandList by traversing its chunk‑tree and
//  delegating each chunk to an IChunkExecutor.  A single "compiled" executor
//  (Roslyn / IL / …) is supplied together with a fallback Interpreted executor;
//  the runner decides per‑chunk which one to invoke based on MinCompileLength.
// -----------------------------------------------------------------------------

using System;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Orchestrates run‑time execution of an <see cref="ArrayCommandList"/>.
    /// The class is <b>stateless across runs</b> except for the executors it
    /// holds, so it can be reused to execute the same ACL on multiple data
    /// arrays without re‑compilation.
    /// </summary>
    public sealed class ArrayCommandListRunner
    {
        private readonly IChunkExecutor _compiled;
        private readonly IChunkExecutor _fallback;   // always interpreted
        private readonly int _minCompileLength;

        // These two mirror the fields that used to live in ArrayCommandList:
        private bool _condition = true;   // current If/EndIf condition flag
        private int _globalSkipDepth = 0; // nested‑If skip counter

        public ArrayCommandListRunner(
            IChunkExecutor compiledExecutor,
            IChunkExecutor fallbackInterpreter,
            int minCompileLength)
        {
            _compiled = compiledExecutor ?? throw new ArgumentNullException(nameof(compiledExecutor));
            _fallback = fallbackInterpreter ?? throw new ArgumentNullException(nameof(fallbackInterpreter));
            _minCompileLength = minCompileLength;
        }

        /// <summary>
        /// Executes <paramref name="acl"/> once against <paramref name="data"/>.
        /// </summary>
        public void Run(ArrayCommandList acl, double[] data, bool tracing = false)
        {
            if (acl is null) throw new ArgumentNullException(nameof(acl));
            if (data is null) throw new ArgumentNullException(nameof(data));

            // 1️⃣  Stage ordered source / destination buffers
            acl.PrepareOrderedSourcesAndDestinations(data);

            // 2️⃣  Depth‑first traversal with pre/post hooks
            acl.CommandTree!.WalkTree(
                beforeDescending: n => Pre((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl),
                afterAscending: n => Post((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl),
                parallel: n => ParallelPredicate((NWayTreeStorageInternal<ArrayCommandChunk>)n, acl));

            // 3️⃣  Flush ordered destinations back to user array
            acl.CopyOrderedDestinations(data);
        }

        // ──────────────────────────────────────────────────────────────
        //  Pre‑order action  – stack sync + evaluate Conditional gate
        // ──────────────────────────────────────────────────────────────
        private void Pre(NWayTreeStorageInternal<ArrayCommandChunk> node, ArrayCommandList acl)
        {
            var c = node.StoredValue;
            c.CopyParentVirtualStack();

            if (c.Skip) return;
            if (c.Name == "Conditional")
                ExecuteChunk(c, acl);
        }

        // ──────────────────────────────────────────────────────────────
        //  Post‑order action – run leaf, merge increments
        // ──────────────────────────────────────────────────────────────
        private void Post(NWayTreeStorageInternal<ArrayCommandChunk> node, ArrayCommandList acl)
        {
            var c = node.StoredValue;
            if (c.Skip) return;

            // body‑slices of a Conditional gate were executed by the gate itself
            if (node.Parent is NWayTreeStorageInternal<ArrayCommandChunk> p &&
                p.StoredValue?.Name == "Conditional")
                return;

            bool isLeaf = node.Branches is null || node.Branches.Length == 0;
            if (isLeaf && c.Name != "Conditional")
                ExecuteChunk(c, acl);

            c.CopyIncrementsToParentIfNecessary();
            c.ResetIncrementsForParent();
        }

        // Decide whether children of <node> may run concurrently
        private static bool ParallelPredicate(NWayTreeStorageInternal<ArrayCommandChunk> node,
                                              ArrayCommandList acl)
            => acl.DoParallel && node.StoredValue.ChildrenParallelizable;

        // ──────────────────────────────────────────────────────────────
        //  Execute a single chunk using compiled or fallback executor
        // ──────────────────────────────────────────────────────────────
        private void ExecuteChunk(ArrayCommandChunk c, ArrayCommandList acl)
        {
            // Cross‑chunk skip when inside a false branch
            if (_globalSkipDepth > 0) { ScanForNestedIfs(c, acl); return; }

            int len = c.EndCommandRangeExclusive - c.StartCommandRange;
            var exec = (len < _minCompileLength) ? _fallback : _compiled;

            int cosi = c.StartSourceIndices;
            int codi = c.StartDestinationIndices;

            exec.Execute(c,
                         c.VirtualStack,
                         acl.OrderedSources,
                         acl.OrderedDestinations,
                         ref cosi,
                         ref codi,
                         ref _condition);

            // Propagate pointer updates back to chunk for its siblings
            c.StartSourceIndices = cosi;
            c.StartDestinationIndices = codi;
        }

        // ──────────────────────────────────────────────────────────────
        //  Helper: while skipping, count nested If/EndIf pairs so depth
        //  is accurate when we exit the skipped span.
        // ──────────────────────────────────────────────────────────────
        private void ScanForNestedIfs(ArrayCommandChunk c, ArrayCommandList acl)
        {
            var cmds = acl.UnderlyingCommands;
            for (int i = c.StartCommandRange; i < c.EndCommandRangeExclusive; i++)
            {
                switch (cmds[i].CommandType)
                {
                    case ArrayCommandType.If: _globalSkipDepth++; break;
                    case ArrayCommandType.EndIf: if (_globalSkipDepth > 0) _globalSkipDepth--; break;
                }
            }
        }
    }
}
