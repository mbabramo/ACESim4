using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.Parallelization;
using System;
using System.Collections.Generic;

public sealed class OrderedBufferManager
{
    // indices recorded at author-time
    public readonly List<OsIndex> SourceIndices = new();
    public readonly List<OdIndex> DestinationIndices = new();

    // live buffers…
    public double[] Sources = Array.Empty<double>();
    public double[] Destinations = Array.Empty<double>();

    public void PrepareBuffers(double[] data, bool parallel = false)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        int sourceCount = SourceIndices.Count;
        if (Sources.Length != sourceCount)
            Sources = new double[sourceCount];

        if (sourceCount == 0)
        {
            Sources = Array.Empty<double>();
        }
        else
        {
            Parallelizer.Go(parallel, 0, sourceCount, i =>
            {
                Sources[i] = data[SourceIndices[i].Value];
            });
        }

        int destCount = DestinationIndices.Count;
        if (Destinations.Length != destCount)
            Destinations = new double[destCount];
        else
            Array.Clear(Destinations, 0, Destinations.Length);

        if (destCount == 0)
            Destinations = Array.Empty<double>();
    }

    public void ApplyDestinations(double[] data, bool parallel = false)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        int destCount = DestinationIndices.Count;
        if (destCount == 0)
            return;

        Parallelizer.Go(parallel, 0, destCount, i =>
        {
            int targetIdx = DestinationIndices[i].Value;
            double increment = Destinations[i];
            if (increment != 0.0)
                data[targetIdx] += increment;
        });
    }
}
