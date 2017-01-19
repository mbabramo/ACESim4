using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class KMeansClustering
    {
        public static List<double[]> GetClusters(List<double[]> trainingData, int numberClusters)
        {
            List<double[]> clusters = RandomClustering.CreateRandomClusters(trainingData, numberClusters, true);
            MoveClustersRepeatedly(trainingData, clusters);
            return clusters;
        }

        private static void MoveClustersRepeatedly(List<double[]> trainingData, List<double[]> clusters)
        {
            double lastDistanceSum = 0;
            double distanceSum = 0;
            bool keepGoing = true;
            do
            {
                lastDistanceSum = distanceSum;
                distanceSum = MoveClustersToCenterOfCorrespondingDataAndSumDistance(trainingData, clusters, distanceSum);
                keepGoing = (lastDistanceSum == 0 || distanceSum == lastDistanceSum);
            }
            while (keepGoing);
        }

        private static double MoveClustersToCenterOfCorrespondingDataAndSumDistance(List<double[]> trainingData, List<double[]> clusters, double distanceSum)
        {
            int numberClusters = clusters.Count;
            List<int> classificationOfTrainingData = ClassifyTrainingData(clusters, trainingData);
            for (int c = 0; c < numberClusters; c++)
            {
                List<double[]> correspondingTrainingData = trainingData.Where((x, i) => classificationOfTrainingData[i] == c).ToList();
                if (correspondingTrainingData.Any())
                    clusters[c] = MoveClusterBasedOnCorrespondingTrainingData(correspondingTrainingData);
                else
                    clusters[c] = RandomClustering.CreateNewRandomCluster(trainingData, clusters);
                double distanceSumThisCluster = correspondingTrainingData.Sum(t => CalculateEuclideanDistance(t, clusters[c]));
                distanceSum += distanceSumThisCluster;
            }
            bool equalizeSizes = false; 
            if (equalizeSizes)
                classificationOfTrainingData = MoveClustersToTendToEqualizeSizes(clusters, trainingData, classificationOfTrainingData);
            return distanceSum;
        }


        private static List<int> MoveClustersToTendToEqualizeSizes(List<double[]> clusters, List<double[]> trainingData, List<int> classificationOfTrainingData)
        {
            bool keepGoing = true;
            double? previousStdev = null;
            List<double[]> previousClusters = null;
            const double initialProportionOfClustersToMove = 0.3;
            const double stopWhenProportionIsLessThan = 0.04;
            double proportionOfClustersToMove = initialProportionOfClustersToMove;
            while (keepGoing)
            {
                int numberClusters = clusters.Count();
                KDTreeFromClusterCenters k;
                classificationOfTrainingData = ClassifyTrainingDataAndReturnKDTree(clusters, trainingData, out k);
                var pointsPerCluster = clusters.Select((item, index) => new { Count = classificationOfTrainingData.Where(y => y == index).Count(), Index = index }).ToList();
                StatCollector sc = new StatCollector();
                foreach (var p in pointsPerCluster)
                    sc.Add(p.Count);
                double averagePointsPerCluster = sc.Average();
                double stdevPointsPerCluster = sc.StandardDeviation();
                if (previousStdev != null && stdevPointsPerCluster > previousStdev)
                {
                    clusters = previousClusters; // revert to previous clusters, because things got worse
                    proportionOfClustersToMove *= 0.95;
                    stdevPointsPerCluster = (double) previousStdev;
                }
                else
                {
                    proportionOfClustersToMove *= 1.40;
                    if (proportionOfClustersToMove > initialProportionOfClustersToMove)
                        proportionOfClustersToMove = initialProportionOfClustersToMove;
                    previousStdev = stdevPointsPerCluster;
                }
                previousClusters = clusters.Select(x => x.ToArray()).ToList();
                keepGoing = proportionOfClustersToMove > stopWhenProportionIsLessThan;
                if (keepGoing)
                {
                    pointsPerCluster = pointsPerCluster.OrderBy(x => x.Count).ToList();
                    for (int p = 0; p < numberClusters * proportionOfClustersToMove; p++)
                    {
                        // move this small cluster to between a big cluster and its nearest neighbor
                        int indexOfSmall = pointsPerCluster[p].Index;
                        int indexOfBig = pointsPerCluster[numberClusters - p - 1].Index;
                        int indexOfBigNearestNeighbor = k.GetKNearestNeighbors(clusters[indexOfBig].ToList(), true, 2)[1];
                        clusters[indexOfSmall] = RandomClustering.CreateNewRandomCluster(trainingData, clusters);
                        //for (int d = 0; d < k.Dimensions; d++)
                        //    clusters[indexOfSmall][d] =  // (clusters[indexOfBig][d] + clusters[indexOfBigNearestNeighbor][d]) / 2.0;
                    }
                }
            }
            return classificationOfTrainingData;
        }

        private static List<int> MoveClustersToTendToEqualizeSizes_AlternativeApproach(List<double[]> clusters, List<double[]> trainingData, List<int> classificationOfTrainingData)
        {
            // THIS DOESN'T WORK
            bool keepGoing = true;
            while (keepGoing)
            {
                int numberClusters = clusters.Count();
                KDTreeFromClusterCenters k;
                classificationOfTrainingData = ClassifyTrainingDataAndReturnKDTree(clusters, trainingData, out k);
                List<int> pointsPerCluster = clusters.Select((item, index) => classificationOfTrainingData.Where(y => y == index).Count()).ToList();
                StatCollector sc = new StatCollector();
                foreach (int p in pointsPerCluster)
                    sc.Add(p);
                double averagePointsPerCluster = sc.Average();
                double stdevPointsPerCluster = sc.StandardDeviation();
                keepGoing = stdevPointsPerCluster > 0.1 * averagePointsPerCluster;
                if (keepGoing)
                    for (int c = 0; c < numberClusters; c++)
                    {
                        double pctDifferenceFromAverage = (pointsPerCluster[c] - averagePointsPerCluster) / averagePointsPerCluster;
                        if (pctDifferenceFromAverage > 0)
                            continue;
                        const int numNearest = 5;
                        List<int> nearest = k.GetKNearestNeighbors(clusters[c].ToList(), true, numNearest);
                        double multiplier = 0.01; // arbitrary parameter determines weight to give to nearest list
                        List<double> distances = nearest.Select(x => CalculateEuclideanDistance(clusters[c], clusters[x])).ToList();
                        double minD = distances.Min();
                        List<double> distanceWeights = distances.Select(x => minD / x).ToList();
                        for (int d = 0; d < k.Dimensions; d++)
                        {
                            for (int n = 0; n < numNearest; n++)
                            {
                                clusters[c][d] -= (clusters[nearest[n]][d] - clusters[c][d]) * pctDifferenceFromAverage * multiplier * distanceWeights[n]; // this will move big ones further from neighbors and little ones closer to neighbors
                            }
                        }
                    }
            }
            return classificationOfTrainingData;
        }

        public static List<int> ClassifyTrainingData(List<double[]> clusters, List<double[]> trainingData)
        {
            return RandomClustering.ClassifyTrainingData(clusters, trainingData);
        }

        public static List<int> ClassifyTrainingDataAndReturnKDTree(List<double[]> clusters, List<double[]> trainingData, out KDTreeFromClusterCenters kdTree)
        {
            return RandomClustering.ClassifyTrainingDataAndReturnKDTree(clusters, trainingData, out kdTree);
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

        internal static double CalculateEuclideanDistance(double[] pointOne, double[] pointTwo)
        {
            double total = 0.0;
            for (int i = 0; i < pointOne.Length; i++)
            {
                double valueToSquare = pointOne[i] - pointTwo[i];
                total += valueToSquare * valueToSquare;
            }
            return Math.Sqrt(total);
        }
    }
}
