// -----------------------------------------------------------------------------
//  OrderedBufferManager.cs
//  -----------------------------------------------------------------------------
//  Encapsulates all logic for staged ordered‑sources / ordered‑destinations that
//  used to live inside ArrayCommandList.  The owner (ArrayCommandList) records
//  indices while authoring commands, and at run‑time calls
//      • PrepareBuffers(array, doParallel)
//      • FlushDestinations(array, doParallel)
//  to stage inputs and merge outputs.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.Util.Parallelization;
using ACESimBase.Util.Debugging;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Manages the ordered source / destination mechanism that enables lock‑free
    /// parallel execution.  All index lists are populated by <em>authoring</em>
    /// code; this class is used only at run‑time to stage data and write results.
    /// </summary>
    public sealed class OrderedBufferManager
    {
        // ───────────────────────── indices recorded at author‑time ─────────────────────────
        public readonly List<int> SourceIndices = new();
        public readonly List<int> DestinationIndices = new();

        // ───────────────────────── live buffers (allocated on first run) ──────────────────
        public double[] Sources = Array.Empty<double>();
        public double[] Destinations = Array.Empty<double>();

        // cache for parallel merge
        private List<int>[] _destInverted = null!;   // lazily built
        private (int target, List<int> srcList)[] _destInvertedWithTarget = null!;

        /// <summary>Ensures <see cref="Sources"/> and <see cref="Destinations"/> are
        ///        sized correctly and copies current <paramref name="data"/> values
        ///        for every recorded source index.</summary>
        public void PrepareBuffers(double[] data, bool parallel = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            int srcCount = SourceIndices.Count;
            int dstCount = DestinationIndices.Count;

            if (Sources.Length != srcCount)
                Sources = new double[srcCount];
            if (Destinations.Length != dstCount)
                Destinations = new double[dstCount];

            // Copy sources ── may be parallelised
            Parallelizer.Go(parallel, 0, srcCount, i =>
            {
                Sources[i] = data[SourceIndices[i]];
            });

            // Reset destination accumulation buffer
            Array.Clear(Destinations, 0, dstCount);
        }

        /// <summary>Merges <see cref="Destinations"/> back into <paramref name="data"/>
        ///        (add‑or‑assign semantics identical to previous ArrayCommandList
        ///        behaviour).  When <paramref name="parallel"/> is true and there are
        ///        duplicates in <see cref="DestinationIndices"/>, the method performs
        ///        a two‑phase aggregation to avoid races.</summary>
        public void FlushDestinations(double[] data, bool parallel = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            int start = 0;
            int end = DestinationIndices.Count;

            if (end == 0) return;   // nothing to do

            // ───────────────────────────── PARALLEL PATH ────────────────────────────────
            if (parallel)
            {
                // 1) Build or re‑use inverted map (dest → list of staged indices).
                // Can assume that inverted map is OK to use if data.Length hasn't changed.
                if (_destInverted == null || _destInverted.Length != data.Length)
                {
                    _destInverted = new List<int>[data.Length];
                    for (int i = 0; i < data.Length; i++) _destInverted[i] = null;

                    for (int i = 0; i < end; i++)
                    {
                        int target = DestinationIndices[i];
                        (_destInverted[target] ??= new List<int>()).Add(i);
                    }

                    // flatten to contiguous array for faster parallel loop
                    var list = new List<(int, List<int>)>();
                    for (int t = 0; t < data.Length; t++)
                        if (_destInverted[t] != null)
                            list.Add((t, _destInverted[t]));
                    _destInvertedWithTarget = list.ToArray();
                }

                int tgtCount = _destInvertedWithTarget.Length;
                Parallelizer.Go(true, 0, tgtCount, idx =>
                {
                    var (target, srcList) = _destInvertedWithTarget[idx];
                    double total = 0;
                    foreach (int s in srcList)
                        total += Destinations[s];
                    data[target] = total; // assign final value once
                });
                return;
            }

            // ───────────────────────────── SERIAL PATH ──────────────────────────────────
            var totals = new Dictionary<int, double>(end);
            for (int i = start; i < end; i++)
            {
                int destIdx = DestinationIndices[i];
                double delta = Destinations[i];
                if (totals.TryGetValue(destIdx, out double current))
                    totals[destIdx] = current + delta;
                else
                    totals.Add(destIdx, delta);
            }

            foreach (var kvp in totals)
                data[kvp.Key] = kvp.Value;
        }
    }
}
