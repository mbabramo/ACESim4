using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class EquallySpaced
    {
        // We want to get n points within a range (x, y). There are two ways we can do this: First, we can divide region (x, y) into n regions and return the midpoint of each region. Note that in this case the distance from x to the first point and from y to the top point will be half the distance between any two adjacent points. Second, we can make the distance from x to the lowest point the same as the distance between adjacent points and as the distance between the highest point and y.

        // UPDATE: We always use the fully equally spaced approach. PREVIOUSLY: The following two methods choose between these based on whether the number of points is even or odd. If we want to include the midpoint, then we use an odd number of points; if not, then we use an even nujmber of points.

        public static double[] GetEquallySpacedPoints(int numPoints, bool includeEndpoints, double from = 0, double to = 1.0)
        {
            if (includeEndpoints)
            {
                return Enumerable.Range(0, numPoints).Select(x => GetLocationOfEquallySpacedPoint(x, numPoints, true, from, to)).ToArray();
            }
            if (useMidpointsOfEquallySpacedRegions) // numPoints % 2 == 0)
                return GetMidpointsOfEquallySpacedRegions(numPoints, from, to); // e.g., 0.05, 0.15, ... , 0.95
            else
                return GetPointsFullyEquallySpaced(numPoints, from, to); // e.g., 0.1, 0.2, ... 0.9
        }

        static bool useMidpointsOfEquallySpacedRegions = true;

        public static double GetLocationOfEquallySpacedPoint(int pointIndex, int numPoints, bool includeEndpoints, double from = 0, double to = 1.0)
        {
            if (includeEndpoints)
                return from + (to - from) * ((double) pointIndex / (double) (numPoints - 1));
            if (useMidpointsOfEquallySpacedRegions) // numPoints % 2 == 0)
                return GetLocationOfMidpoint(pointIndex, numPoints, from, to);
            else
                return GetLocationOfFullyEquallySpacedPoint(pointIndex, numPoints, from, to);
        }

        // Suppose that there are two regions within (0, 1.0) --> the midpoints will be (0.25, 0.75), because these are the midpoints of (0, 0.5) and (0.5, 1.0). If we have 10 points, we want (0.05, 0.15, ..., 0.95). So the formula for point i (starting with zero) is (i + 0.5) / 10.
        public static double[] GetMidpointsOfEquallySpacedRegions(int numRegions, double from = 0, double to = 1.0)
        {
            return Enumerable.Range(0, numRegions)
                .Select(x => (double)(x + 0.5) / (double)numRegions)
                .Select(x => from + (to - from) * x)
                .ToArray();
        }

        public static double GetLocationOfMidpoint(int regionIndex, int numRegions, double from = 0, double to = 1.0)
        {
            return from + (to - from) * ((double)(regionIndex + 0.5)) / (double)(numRegions);
        }

        // If we have ten regions, then we want 0.1, 0.2, ..., 0.9. 
        public static double[] GetCutoffsBetweenRegions(int numRegions, double from = 0, double to = 1.0)
        {
            return Enumerable.Range(1, numRegions - 1)
                .Select(x => (double)x / (double)numRegions)
                .Select(x => from + (to - from) * x)
                .ToArray();
        }

        public static Tuple<double, double>[] GetRegions(int numRegions, double from = 0, double to = 1.0)
        {
            double[] cutoffs = GetCutoffsBetweenRegions(numRegions, from, to);
            Tuple<double, double>[] regions = Enumerable.Range(0, numRegions)
                .Select(x => new Tuple<double, double>(x == 0 ? double.MinValue : cutoffs[x-1], x == numRegions - 1 ? double.MaxValue : cutoffs[x]))
                .ToArray();
            return regions;
        }

        // If we want 9 points, then we'll use 10 regions to generate the cutoffs.
        public static double[] GetPointsFullyEquallySpaced(int numPoints, double from = 0, double to = 1.0)
        {
            return GetCutoffsBetweenRegions(numPoints + 1, from, to);
        }

        public static double GetLocationOfFullyEquallySpacedPoint(int pointIndex, int numPoints, double from = 0, double to = 1.0)
        {
            return from + (to - from) * (pointIndex + 1.0) / (double)(numPoints + 1.0);
        }
    }
}
