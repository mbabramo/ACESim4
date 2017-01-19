using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public abstract class Point
    {
        public int NumDimensions;
        public double? MaximumSquaredDistanceForNearestNeighbor; // Setting this is a somewhat hacky means of letting the nearest neighbor algorithm know not to bother with this point if it's more than this distance away.
        public abstract double GetValue(int dimension);

        public Point(int numDimensions)
        {
            NumDimensions = numDimensions;
        }

        internal double[] Location;
        public double[] GetLocation()
        {
            if (Location == null)
            { // this must be overriden if location can change after initially being set
                Location = new double[NumDimensions];
                for (int d = 0; d < NumDimensions; d++)
                    Location[d] = GetValue(d);
            }
            return Location;
        }

        public double DistanceTo(Point another, bool useSquaredDistance = false, double? stopIfSquaredDistanceGreaterThan = null)
        {
            double squaredDistance = 0;
            for (int d = 0; d < NumDimensions && (stopIfSquaredDistanceGreaterThan == null || stopIfSquaredDistanceGreaterThan > squaredDistance); d++)
            {
                double unsquaredDistance = GetValue(d) - another.GetValue(d);
                double squaredDistanceThisDimension = unsquaredDistance * unsquaredDistance;
                squaredDistance += squaredDistanceThisDimension;
            }
            if (useSquaredDistance)
                return squaredDistance;
            return Math.Sqrt(squaredDistance);
        }

        public bool IsInKDTree(KDTree KDTree)
        {
            bool goodSoFar = true;
            for (int d = 0; goodSoFar && d < NumDimensions; d++)
            {
                goodSoFar = GetValue(d) >= KDTree.LowerBounds[d] && GetValue(d) <= KDTree.UpperBounds[d];
            }
            return goodSoFar;
        }

        public bool IsColocated(Point anotherPoint)
        {
            bool goodSoFar = true;
            for (int d = 0; goodSoFar && d < NumDimensions; d++)
            {
                goodSoFar = GetValue(d) == anotherPoint.GetValue(d);
            }
            return goodSoFar;
        }
    }

    [Serializable]
    public class ArbitrarySpot : Point
    {
        public ArbitrarySpot(double[] location, int numDimensions)
            : base(numDimensions)
        {
            Location = location;
        }

        public override string ToString()
        {
            return String.Concat(Location.Select(x => x.ToString() + " "));
        }

        public override double GetValue(int dimension)
        {
            return Location[dimension];
        }
    }

    [Serializable]
    public class NormalizedPoint : ArbitrarySpot
    {
        public int AssociatedIndex;

        public NormalizedPoint(List<double> initialPoint, List<double> averages, List<double> stdevs, int associatedIndex)
            : base(NormalizedLocation(initialPoint, averages, stdevs), initialPoint.Count)
        {
            AssociatedIndex = associatedIndex;
        }

        internal static double[] NormalizedLocation(List<double> initialPoint, List<double> averages, List<double> stdevs)
        {
            int numDimensions = initialPoint.Count;
            double[] location = new double[numDimensions];
            for (int d = 0; d < numDimensions; d++)
            {
                location[d] = (initialPoint[d] - averages[d]) / stdevs[d];
            }
            return location;
        }
    }
}
