using System;
using System.Collections.Generic;
using ACESimBase.Util.Parallelization;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Reduced-scope buffer helper: stages *source* values when a chunk will be
    /// executed in parallel.
    /// </summary>
    public sealed class OrderedBufferManager
    {
        // indices recorded at author-time
        public readonly List<int> SourceIndices = new();

        // live buffer, allocated on first use
        public double[] Sources = Array.Empty<double>();

        /// <remarks>
        /// Copies <paramref name="data"/>[SourceIndices[*]] into <see cref="Sources"/>.
        /// When <paramref name="parallel"/> is <c>true</c> the copy is parallelised,
        /// otherwise it is a plain for-loop.  (Destination handling has been
        /// removed because the runtime now writes directly to the real array.)
        /// </remarks>
        public void PrepareBuffers(double[] data, bool parallel = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            int count = SourceIndices.Count;
            if (Sources.Length != count)
                Sources = new double[count];

            Parallelizer.Go(parallel, 0, count, i =>
            {
                Sources[i] = data[SourceIndices[i]];
            });
        }
    }
}
