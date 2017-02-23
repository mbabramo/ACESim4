using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class EquallySpaced
    {
        // Suppose that there are two regions within (0, 1.0) --> the midpoints will be (0.25, 0.75), because these are the midpoints of (0, 0.5) and (0.5, 1.0). If we have 10 points, we want (0.05, 0.15, ..., 0.95). So the formula for point i (starting with zero) is (i + 0.5) / 10.
        public static double[] GetMidpointsOfEquallySpacedRegions(int numRegions, double from = 0, double to = 1.0)
        {
            return Enumerable.Range(0, numRegions)
                .Select(x => (double)(x + 0.5) / (double)numRegions)
                .Select(x => from + (to - from) * x)
                .ToArray();
        }

        // If we have ten regions, then we want 0.1, 0.2, ..., 0.9. 
        public static double[] GetCutoffsBetweenRegions(int numRegions, double from = 0, double to = 1.0)
        {
            return Enumerable.Range(1, numRegions - 1)
                .Select(x => (double)x / (double)numRegions)
                .Select(x => from + (to - from) * x)
                .ToArray();
        }

        // If we want 10 points, then we'll want the cutoffs between 11 regions.
        public static double[] GetEquallySpacedPoints(int numPoints, double from = 0, double to = 1.0)
        {
            return GetCutoffsBetweenRegions(numPoints + 1, from, to);
        }

        public static double GetLocationOfMidpoint(int regionIndex, int numRegions, double from = 0, double to = 1.0)
        {
            return from + (to - from) * ((double)(regionIndex + 0.5)) / (double)(numRegions);
        }
    }
}
