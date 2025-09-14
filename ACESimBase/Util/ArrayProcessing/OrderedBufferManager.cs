using System;
using System.Collections.Generic;
using ACESimBase.Util.Parallelization;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Reduced-scope buffer helper: stages *source* values when a chunk will be
    /// executed in parallel, and collects *destination* increments for later merge.
    /// </summary>
    public sealed class OrderedBufferManager
    {
        // indices recorded at author-time
        public readonly List<int> SourceIndices = new();
        public readonly List<int> DestinationIndices = new();

        // live buffers, allocated on first use
        public double[] Sources = Array.Empty<double>();
        public double[] Destinations = Array.Empty<double>();

        /// <remarks>
        /// Prepares ordered buffers for execution:
        /// - Copies <paramref name="data"/>[SourceIndices[*]] into <see cref="Sources"/>.
        /// - Resets <see cref="Destinations"/> to length DestinationIndices.Count, zero-filled.
        /// When <paramref name="parallel"/> is <c>true</c> the copy is parallelised.
        /// </remarks>
        public void PrepareBuffers(double[] data, bool parallel = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            int sourceCount = SourceIndices.Count;
            if (Sources.Length != sourceCount)
                Sources = new double[sourceCount];

            if (sourceCount == 0)
            {
                // Ensure Sources is an empty array and skip the copy loop.
                Sources = Array.Empty<double>();
            }
            else
            {
                Parallelizer.Go(parallel, 0, sourceCount, i =>
                {
                    Sources[i] = data[SourceIndices[i]];
                });
            }

            int destCount = DestinationIndices.Count;
            if (Destinations.Length != destCount)
                Destinations = new double[destCount];
            else
                Array.Clear(Destinations, 0, Destinations.Length);

            if (destCount == 0)
            {
                // Normalize to empty array to avoid any downstream confusion.
                Destinations = Array.Empty<double>();
            }
        }


        /// <summary>
        /// Applies buffered destination increments back into <paramref name="data"/>.
        /// When <paramref name="parallel"/> is <c>true</c> the merge is parallelised.
        /// </summary>
        public void ApplyDestinations(double[] data, bool parallel = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            int destCount = DestinationIndices.Count;
            if (destCount == 0)
                return;

            Parallelizer.Go(parallel, 0, destCount, i =>
            {
                int targetIdx = DestinationIndices[i];
                double increment = Destinations[i];
                if (increment != 0.0)
                {
                    // += semantics preserved; if you later want true atomic add for doubles,
                    // switch to a CompareExchange loop. For this fix, the early return is key.
                    System.Threading.Interlocked.Exchange(
                        ref data[targetIdx],
                        data[targetIdx] + increment
                    );
                }
            });
        }

    }
}
