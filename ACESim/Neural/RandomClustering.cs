using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class RandomClustering
    {
        public static List<double[]> CreateRandomClusters(List<double[]> trainingData, int numberClusters, bool tryToEnsureAllHaveData)
        {
            int dimensions = trainingData[0].Length;
            int trainingDataSize = trainingData.Count;
            List<double[]> randomClusters = new List<double[]>();
            for (int i = 0; i < numberClusters; i++)
            {
                double[] randomCluster = CreateNewRandomCluster(trainingData, randomClusters);
                randomClusters.Add(randomCluster);
            }
            if (tryToEnsureAllHaveData)
            {
                bool allHaveData;
                int maxRepetitions = 15; // arbitrary, but we need to move along
                if (randomClusters.Count() > 2000)
                    maxRepetitions = 5;
                int repetitionsSoFar = 0;
                do
                {
                    List<int> clustersWithoutData = GetClustersWithoutData(randomClusters, trainingData);
                    allHaveData = !clustersWithoutData.Any();
                    if (!allHaveData)
                    {
                        foreach (var cIndex in clustersWithoutData)
                            randomClusters[cIndex] = CreateNewRandomCluster(trainingData, randomClusters);
                        repetitionsSoFar++;
                    }
                }
                while (!allHaveData && repetitionsSoFar < maxRepetitions);
            }
            return randomClusters;
        }

        public static double[] CreateNewRandomCluster(List<double[]> trainingData, List<double[]> randomClustersAlreadyExisting)
        {
            int dimensions = trainingData[0].Length;
            int trainingDataSize = trainingData.Count;
            bool isDuplicate;
            double[] randomCluster;
            do
            {
                randomCluster = new double[dimensions];
                bool goVeryNearSinglePoint = RandomGenerator.NextDouble() < 0.5;
                for (int d = 0; d < dimensions; d++)
                {
                    int rand1 = RandomGenerator.Next(trainingDataSize);
                    int rand2 = RandomGenerator.Next(trainingDataSize);
                    if (goVeryNearSinglePoint)
                    {
                        double proportionWeightOnRand1 = 0.99999;
                        randomCluster[d] = proportionWeightOnRand1 * trainingData[rand1][d] + (1.0 - proportionWeightOnRand1) * trainingData[rand2][d];
                    }
                    else
                    {
                        randomCluster[d] = (trainingData[rand1][d] + trainingData[rand2][d]) / 2.0;
                    }
                }
                isDuplicate = randomClustersAlreadyExisting.Any(x => x.SequenceEqual(randomCluster));
            } while (isDuplicate);
            return randomCluster;
        }

        public static List<int> GetClustersWithoutData(List<double[]> clusters, List<double[]> trainingData)
        {
            List<int> clustersForData = ClassifyTrainingData(clusters, trainingData);
            return Enumerable.Range(0, clusters.Count).Where(x => !clustersForData.Contains(x)).ToList();
        }

        public static List<int> ClassifyTrainingData_Old(List<double[]> clusters, List<double[]> trainingData)
        {
            return trainingData.Select(x =>
                clusters.Select((c, i) => new
                {
                    dist = KMeansClustering.CalculateEuclideanDistance(x, c), // distance of training point to cluster
                    index = i // index of cluster
                }).OrderBy(y => y.dist).First().index // index of closest cluster
            ).ToList();
        }

        public static List<int> ClassifyTrainingData(List<double[]> clusters, List<double[]> trainingData)
        {
            KDTreeFromClusterCenters k = new KDTreeFromClusterCenters() { ClusterCenters = clusters, Dimensions = clusters[0].Length };
            k.PrepareKDTree();
            return k.GetNearestNeighborForEachPoint(trainingData);
        }

        public static List<int> ClassifyTrainingDataAndReturnKDTree(List<double[]> clusters, List<double[]> trainingData, out KDTreeFromClusterCenters k)
        {
            k = new KDTreeFromClusterCenters() { ClusterCenters = clusters, Dimensions = clusters[0].Length };
            k.PrepareKDTree();
            return k.GetNearestNeighborForEachPoint(trainingData);
        }

        public static List<int> ClassifyTrainingData(KDTreeFromClusterCenters preparedKDTree, List<double[]> trainingData)
        {
            return preparedKDTree.GetNearestNeighborForEachPoint(trainingData);
        }
    }
}
