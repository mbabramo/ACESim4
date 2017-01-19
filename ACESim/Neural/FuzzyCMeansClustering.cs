using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encog.MathUtil.Matrices;

namespace ACESim
{
    public class FuzzyCMeansClustering
    {
        int D;
        int N;
        int C;
        List<double[]> TrainingData;
        List<double> TrainingOutputs;

        public void GetClusters(List<double[]> trainingData, List<double> trainingOutputsIfAvailable, List<double[]> initialClusterCentersIfAvailable, int numClusters, out List<double[]> clusterCenters, out List<double> clusterOutputs, double maxDifferenceThreshold = 0.01, double mExponent = 2.0)
        {
            TrainingData = trainingData;
            TrainingOutputs = trainingOutputsIfAvailable;
            D = trainingData[0].Count();
            N = trainingData.Count();
            C = numClusters;
            double[,] membershipOfDataInClusters;
            InitializeClusters(initialClusterCentersIfAvailable, out clusterCenters, out membershipOfDataInClusters);

            GetClusterCenters(clusterCenters, maxDifferenceThreshold, mExponent, membershipOfDataInClusters);
            if (TrainingOutputs != null)
                clusterOutputs = GetClusterOutputs(clusterCenters, membershipOfDataInClusters);
            else
                clusterOutputs = null;
        }

        private List<double> GetClusterOutputs(List<double[]> clusterCenters, double[,] membershipOfDataInClusters)
        {
            List<double> clusterOutputs = new List<double>();
            for (int j = 0; j < C; j++)
            {
                WeightedAverageCalculator calc = new WeightedAverageCalculator();
                for (int i = 0; i < N; i++)
                {
                    calc.Add(TrainingOutputs[i], membershipOfDataInClusters[i, j]);
                }
                clusterOutputs.Add(calc.Calculate());
            }
            return clusterOutputs;
        }

        private void GetClusterCenters(List<double[]> clusterCenters, double maxDifferenceThreshold, double mExponent, double[,] membershipOfDataInClusters)
        {
            double maxDifference = 0;
            do
            {
                maxDifference = UpdateCycle(TrainingData, clusterCenters, membershipOfDataInClusters, mExponent);
            }
            while (maxDifference > maxDifferenceThreshold);
        }

        public void InitializeClusters(List<double[]> initialClusterCenters, out List<double[]> clusterCenters, out double[,] membershipOfDataInClusters)
        {
            if (initialClusterCenters == null)
                clusterCenters = RandomClustering.CreateRandomClusters(TrainingData, C, true);
            else
                clusterCenters = initialClusterCenters;
            membershipOfDataInClusters = new double[N, C];
            List<int> closestCluster = KMeansClustering.ClassifyTrainingData(clusterCenters, TrainingData);
            for (int i = 0; i < N; i++)
                for (int j = 0; j < C; j++)
                {
                    bool isClosest = closestCluster[i] == j;
                    if (isClosest)
                        membershipOfDataInClusters[i, j] = 1.0;
                    else
                        membershipOfDataInClusters[i,j] = 0.0;
                }
        }

        public double UpdateCycle(List<double[]> trainingData, List<double[]> clusterCenters, double[,] membershipOfDataInClusters, double mExponent)
        {
            //List<double[]> initialClusterCenters = clusterCenters.Select(x => x.ToArray()).ToList();
            double[,] initialMembershipOfData = (double[,])membershipOfDataInClusters.Clone();
            // calculate all euclidean distances
            double[,] euclideanDistance = new double[N, C];
            Parallelizer.Go(true, 0, N, i =>
                {
                    for (int j = 0; j < C; j++)
                        euclideanDistance[i, j] = CalculateEuclideanDistance(trainingData[i], clusterCenters[j]);
                });
            // update membership
            Parallelizer.Go(true, 0, N, i =>
                { // for each training datum, find membership of data in each cluster
                    for (int j = 0; j < C; j++)
                    { // for each cluster
                        double membership = 0;
                        for (int k = 0; k < C; k++)
                        { 
                            double ed_i_k = euclideanDistance[i,k];
                            double delta_membership = 0;
                            if (ed_i_k != 0 && !double.IsNaN(ed_i_k))
                                delta_membership = Math.Pow(euclideanDistance[i, j] / euclideanDistance[i, k], (2.0 / (mExponent - 1.0)));
                            if (delta_membership != 0 && !double.IsNaN(delta_membership))
                                membership += delta_membership;
                        }
                        membershipOfDataInClusters[i, j] = 1.0 / membership;
                    }
                });
            // update cluster centers
            
            Parallelizer.Go(true, 0, C, j =>
            {
                double denominator = 0;
                double[] uijToM = new double[N];
                for (int i = 0; i < N; i++)
                {
                    uijToM[i] = Math.Pow(membershipOfDataInClusters[i, j], mExponent);
                    denominator += uijToM[i];
                }
                for (int d = 0; d < D; d++)
                {
                    double numerator = 0;
                    for (int i = 0; i < N; i++)
                        numerator += uijToM[i] * trainingData[i][d];
                    clusterCenters[j][d] = numerator / denominator;
                }
            });
            // calculate maximum difference
            double? maxDifference = null;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < C; j++)
                {
                    double difference = Math.Abs(initialMembershipOfData[i, j] - membershipOfDataInClusters[i,j]);
                    if (maxDifference == null || difference > maxDifference)
                        maxDifference = difference;
                }
            return (double)maxDifference;
        }

        internal double CalculateEuclideanDistance(double[] pointOne, double[] pointTwo)
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
