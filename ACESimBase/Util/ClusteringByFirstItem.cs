using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class ClusteringByFirstItem
    {
        public static List<double[]> GetClusters(List<double[]> trainingData, int numberClusters)
        {
            bool useEvenlySpacedClustersFrom0To1 = false;
            if (useEvenlySpacedClustersFrom0To1)
            {
                return GetEvenlySpacedClustersFrom0To1(numberClusters);
            }
            var ordered = trainingData.OrderBy(x => x[0]).ToList();
            List<double[]>[] partitions = Partition(ordered, numberClusters);
            List<double[]> clusters = partitions.Select(x => MoveClusterBasedOnCorrespondingTrainingData(x)).ToList();
            return clusters;
        }

        public static List<double[]> GetEvenlySpacedClustersFrom0To1(int numberClusters)
        {
            List<double[]> clusters2 = new List<double[]>();
            for (int i = 0; i < numberClusters; i++)
            {
                clusters2.Add(new double[] { ((double)(i + 1)) / ((double)(numberClusters + 1)) });
            }
            return clusters2;
        }

        internal static double[] MoveClusterBasedOnCorrespondingTrainingData(List<double[]> correspondingTrainingData)
        {
            int dimensions = correspondingTrainingData[0].Length;
            double[] newClusterLoc = new double[dimensions];
            for (int d = 0; d < dimensions; d++)
            {
                newClusterLoc[d] = correspondingTrainingData.Average(x => x[d]);
            }
            return newClusterLoc;
        }

        private static List<double[]>[] Partition(List<double[]> list, int totalPartitions)
        {
            if (list == null)
                throw new ArgumentNullException("list");

            if (totalPartitions < 1)
                throw new ArgumentOutOfRangeException("totalPartitions");

            List<double[]>[] partitions = new List<double[]>[totalPartitions];

            //int maxSize = (int)Math.Ceiling(list.Count / (double)totalPartitions);
            int k = 0;

            for (int i = 0; i < partitions.Length; i++)
            {
                partitions[i] = new List<double[]>();
                int max = (int) Math.Ceiling((double)list.Count * (double)(i + 1) / (double)totalPartitions);
                for (int j = k; j < max; j++)
                {
                    if (j >= list.Count)
                        break;
                    partitions[i].Add(list[j]);
                }
                k = max;
            }

            return partitions;
        }
    }
}
