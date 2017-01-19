using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class KDTreeFromClusterCenters
    {
        public List<double[]> ClusterCenters;
        public int Dimensions;
        public KDTree TreeToLocateNearestNeighbor;
        public List<double> InputAveragesForNormalization;
        public List<double> InputStdevsForNormalization;

        public void PrepareKDTree()
        {
            List<Point> normalizedPointList = new List<Point>();
            GetStatsForNormalization();
            for (int i = 0; i < ClusterCenters.Count; i++)
            {
                NormalizedPoint normalizedPoint = new NormalizedPoint(ClusterCenters[i].ToList(), InputAveragesForNormalization, InputStdevsForNormalization, i);
                normalizedPointList.Add(normalizedPoint);
            }
            double[] lowerBounds = new double[Dimensions];
            double[] upperBounds = new double[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                lowerBounds[d] = normalizedPointList.Min(x => x.GetLocation()[d]);
                upperBounds[d] = normalizedPointList.Max(x => x.GetLocation()[d]);
            }
            const int numPerHypercube = 5;
            TreeToLocateNearestNeighbor = new KDTree(Dimensions, normalizedPointList, null, lowerBounds, upperBounds, -1, numPerHypercube);
            TreeToLocateNearestNeighbor.CompleteInitializationAfterAddingAllPoints();
        }


        private void GetStatsForNormalization()
        {
            StatCollectorArray statCollectorForSmoothingSet = new StatCollectorArray();
            foreach (double[] cluster in ClusterCenters)
                statCollectorForSmoothingSet.Add(cluster);
            InputAveragesForNormalization = statCollectorForSmoothingSet.Average();
            InputStdevsForNormalization = statCollectorForSmoothingSet.StandardDeviation();
        }

        public List<int> GetKNearestNeighbors(List<double> unnormalizedLocation, bool excludeExactMatch, int k)
        {
            NormalizedPoint normalizedPoint = new NormalizedPoint(unnormalizedLocation.ToList(), InputAveragesForNormalization, InputStdevsForNormalization, -1);
            var nearestNeighbors = TreeToLocateNearestNeighbor.GetKNearestNeighbors(normalizedPoint, excludeExactMatch, k).Select(x => (NormalizedPoint)x);
            return nearestNeighbors.Select(x => x.AssociatedIndex).ToList();
        }

        public int GetNearestNeighbor(List<double> unnormalizedLocation)
        {
            NormalizedPoint normalizedPoint = new NormalizedPoint(unnormalizedLocation.ToList(), InputAveragesForNormalization, InputStdevsForNormalization, -1);
            NormalizedPoint nearestNeighbor = (NormalizedPoint) TreeToLocateNearestNeighbor.GetNearestNeighbor(normalizedPoint, false);
            return nearestNeighbor.AssociatedIndex;
        }

        public List<int> GetNearestNeighborForEachPoint(List<double[]> points)
        {
            return points.AsParallel().Select(x => GetNearestNeighbor(x.ToList())).ToList();
        }

        public List<int> GetNumberOfPointsInEachCluster(List<double[]> points)
        {
            List<int> clusterAssignments = GetNearestNeighborForEachPoint(points);
            return ClusterCenters.Select((item, index) => clusterAssignments.Where(y => y == index).Count()).ToList();
        }
    }
}
