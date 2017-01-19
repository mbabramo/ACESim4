using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class GRNN
    {
        internal List<double[]> ClusterCenters;
        internal List<double> ClusterOutputs;
        internal List<double> ClusterWeights;
        bool UseOriginalClusterOutputs = true;
        internal List<List<int>> NeighborsOfTrainingPoints;
        internal KDTree TreeToLocateNearestNeighbor;
        List<double> InputAveragesForNormalization;
        List<double> InputStdevsForNormalization;
        int NumberClusters;
        double Sigma;
        double TwoSigmaSquared;
        double[] DistanceInDimensionMultiplier;
        //List<double> WidthParameterSigma;
        internal int Dimensions;

        public GRNN()
        {
        }

        public GRNN(List<double[]> trainingInputs, List<double> trainingOutputs, List<double> weights) :
            this(trainingInputs, trainingOutputs, weights, null, null)
        {
        }

        public GRNN DeepCopy()
        {
            GRNN copy = new GRNN();
            SetCopyFields(copy);
            return copy;
        }

        public virtual void SetCopyFields(GRNN copy)
        {
            copy.ClusterCenters = ClusterCenters.Select(x => x.ToArray()).ToList();
            copy.ClusterOutputs = ClusterOutputs.ToList();
            copy.ClusterWeights = ClusterWeights.ToList();
            copy.NeighborsOfTrainingPoints = NeighborsOfTrainingPoints.Select(x => x.ToList()).ToList();
            copy.TreeToLocateNearestNeighbor = TreeToLocateNearestNeighbor.DeepCopy(null);
            copy.InputAveragesForNormalization = InputAveragesForNormalization.ToList();
            copy.InputStdevsForNormalization = InputStdevsForNormalization.ToList();
            copy.NumberClusters = NumberClusters;
            copy.Sigma = Sigma;
            copy.TwoSigmaSquared = TwoSigmaSquared;
            copy.Dimensions = Dimensions;
            copy.DistanceInDimensionMultiplier = DistanceInDimensionMultiplier == null ? null : DistanceInDimensionMultiplier.ToArray();
        }

        int? NumNeighborsPerPointBeforePreSerialization = null;
        public virtual void PreSerialize()
        {
            TreeToLocateNearestNeighbor.PreSerialize();
            if (NeighborsOfTrainingPoints != null && NeighborsOfTrainingPoints.Any())
                NumNeighborsPerPointBeforePreSerialization = NeighborsOfTrainingPoints[0].Count();
            else
                NumNeighborsPerPointBeforePreSerialization = null;
            NeighborsOfTrainingPoints = null;
        }

        public virtual void PostDeserialize()
        {
            TreeToLocateNearestNeighbor.UndoPreSerialize();
            if (NumNeighborsPerPointBeforePreSerialization != null)
            {
                NeighborsOfTrainingPoints = new List<List<int>>();
                for (int i = 0; i < ClusterCenters.Count; i++)
                {
                    NormalizedPoint normalizedPoint = new NormalizedPoint(ClusterCenters[i].ToList(), InputAveragesForNormalization, InputStdevsForNormalization, -1);
                    List<int> neighbors = TreeToLocateNearestNeighbor
                                            .GetKNearestNeighbors(normalizedPoint, true, (int)NumNeighborsPerPointBeforePreSerialization)
                                            .Select(x => ((NormalizedPoint)x).AssociatedIndex)
                                            .ToList();
                    NeighborsOfTrainingPoints.Add(neighbors);
                }
            }
        }

        public GRNN(List<double[]> trainingInputs, List<double> trainingOutputs, List<double> weights, List<List<int>> neighborsOfTrainingPoints, int? numClusters, KDTree existingTreeToLocateNearestNeighbor = null)
        {
            ClusterWeights = weights;
            if (weights == null)
            {
                ClusterWeights = new List<double>();
                for (int i = 0; i < trainingOutputs.Count(); i++)
                    ClusterWeights.Add(1.0);
            }
            InitializeBasedOnTrainingOutputs(trainingInputs, trainingOutputs, neighborsOfTrainingPoints, numClusters, existingTreeToLocateNearestNeighbor);
        }

        internal void InitializeBasedOnTrainingOutputs(List<double[]> trainingInputs, List<double> trainingOutputs, List<List<int>> neighborsOfTrainingPoints, int? numClusters, KDTree existingTreeToLocateNearestNeighbor)
        {
            if (neighborsOfTrainingPoints != null && numClusters != null)
                throw new Exception("Cannot both specify neighbors to consider and enable clustering for GRNN.");
            if (numClusters == null)
            {
                ClusterCenters = trainingInputs.Select(x => x.ToArray()).ToList(); // copy the data
                ClusterOutputs = trainingOutputs.ToList();
            }
            else
            {
                FuzzyCMeansClustering clusterer = new FuzzyCMeansClustering();
                clusterer.GetClusters(trainingInputs, trainingOutputs, null, (int)numClusters, out ClusterCenters, out ClusterOutputs);
                //for (int c = 0; c < numClusters; c++)
                //{
                //    Debug.WriteLine(ObfuscationGame.ObfuscationCorrectAnswer.Calculate(ClusterCenters[c][1], ClusterCenters[c][0]) + " " + ClusterOutputs[c]);
                //    ClusterOutputs[c] = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(ClusterCenters[c][1], ClusterCenters[c][0]); // see if this will work better with preclustering and thus proper optimization.
                //}
            }
            NumberClusters = ClusterCenters.Count;
            Dimensions = ClusterCenters.First().Length;
            DistanceInDimensionMultiplier = new double[Dimensions];
            for (int d = 0; d < DistanceInDimensionMultiplier.Length; d++)
                DistanceInDimensionMultiplier[d] = 1.0;
            NeighborsOfTrainingPoints = neighborsOfTrainingPoints;
            if (existingTreeToLocateNearestNeighbor != null)
                TreeToLocateNearestNeighbor = existingTreeToLocateNearestNeighbor;
            else if (NeighborsOfTrainingPoints != null)
                PrepareKDTree();
            CalculateStartingPointWidth();
            OptimizeDistanceInDimensionMultiplier();
            CalculateWidthParameter();
        }

        public virtual double GetClusterOutput(int i)
        {
            return ClusterOutputs[i];
        }

        public virtual double GetValueForCalculatingContributionOfPointToOutputAtSpecifiedPoint(int indexOfContributingPoint)
        {
            return GetClusterOutput(indexOfContributingPoint);
        }

        private void PrepareKDTree()
        {
            KDTreeFromClusterCenters k = new KDTreeFromClusterCenters() { ClusterCenters = ClusterCenters, Dimensions = Dimensions };
            k.PrepareKDTree();
            InputAveragesForNormalization = k.InputAveragesForNormalization;
            InputStdevsForNormalization = k.InputStdevsForNormalization;
            TreeToLocateNearestNeighbor = k.TreeToLocateNearestNeighbor;
        }

        public void GetNearestNeighborsForPoint(List<double> unnormalizedLocation, out int nearestNeighborOfPoint, out List<int> nearestNeighborsOfThatNeighbor, bool speedUpWithApproximateNearestNeighbor)
        {
            NormalizedPoint normalizedPoint = new NormalizedPoint(unnormalizedLocation.ToList(), InputAveragesForNormalization, InputStdevsForNormalization, -1);
            NormalizedPoint nearestNeighbor = (NormalizedPoint)(speedUpWithApproximateNearestNeighbor ? TreeToLocateNearestNeighbor.GetApproximateNearestNeighbor(normalizedPoint, false) : TreeToLocateNearestNeighbor.GetNearestNeighbor(normalizedPoint, false));
            nearestNeighborOfPoint = nearestNeighbor.AssociatedIndex;
            nearestNeighborsOfThatNeighbor = NeighborsOfTrainingPoints[nearestNeighborOfPoint];
        }


        public void CalculateWidthParameter()
        {
            Sigma = FindOptimalValueGreaterThanZero.Maximize(p => 0 - AssessPossibleWidthValue(p), Sigma, 1.2, 0.001, 100);
            //Sigma = 0.00000001; // uncomment to prevent smoothing -- alternative possibility is to use NearestNeighborSmoothingOptions
            TwoSigmaSquared = 2 * Sigma * Sigma;
        }

        internal void CalculateStartingPointWidth()
        {
            double squaredTotal = 0;
            for (int d = 0; d < Dimensions; d++)
            {
                double avgDistanceThisDimension = (ClusterCenters.Max(x => x[d]) - ClusterCenters.Min(x => x[d])) / (double)NumberClusters;
                squaredTotal += avgDistanceThisDimension;
            }
            Sigma = Math.Sqrt(squaredTotal) * 2.0;
            TwoSigmaSquared = 2 * Sigma * Sigma;
        }

        internal double AssessPossibleWidthValue(double possibleWidth)
        {
            double originalSigma = Sigma;
            Sigma = possibleWidth;
            TwoSigmaSquared = 2 * Sigma * Sigma;
            double returnVal = AssessPerformanceByCalculatingEachTrainingDatumFromOthers();
            //Debug.WriteLine("Possible width " + possibleWidth + " return val: " + returnVal);
            Sigma = originalSigma;
            TwoSigmaSquared = 2 * Sigma * Sigma;
            return returnVal;
        }

        internal void OptimizeDistanceInDimensionMultiplier()
        {
            for (int d = 0; d < DistanceInDimensionMultiplier.Length; d++)
                DistanceInDimensionMultiplier[d] = 1.0;
            int repetitions = 3;
            // Low values of the multiplier indicate that all points should be treated relatively equally, i.e. dimension doesn't matter much in predicting output
            // High values indicate that closer points should be given more weight, i.e. dimension matters more
            for (int r = 0; r < repetitions; r++)
                for (int d = 0; d < DistanceInDimensionMultiplier.Length; d++)
                    DistanceInDimensionMultiplier[d] = FindOptimalValueGreaterThanZero.Maximize(p => 0 - AssessPossibleDistanceInDimension(d, p), DistanceInDimensionMultiplier[d], 1.2, 0.01, 100);
        }

        internal double AssessPossibleDistanceInDimension(int dimension, double possibleMultiplier)
        {
            double originalMultiplier = DistanceInDimensionMultiplier[dimension];
            DistanceInDimensionMultiplier[dimension] = possibleMultiplier;
            double returnVal = AssessPerformanceByCalculatingEachTrainingDatumFromOthers();
            //Debug.WriteLine("Dimension " + dimension + " possible multiplier " + possibleMultiplier + " performance " + returnVal);
            DistanceInDimensionMultiplier[dimension] = originalMultiplier;
            return returnVal;
        }

        internal double AssessPerformanceByCalculatingEachTrainingDatumFromOthers()
        {
            double total = 0;
            for (int i = 0; i < NumberClusters; i++)
            {
                double result;
                if (TreeToLocateNearestNeighbor == null)
                    result = CalculateOutput(ClusterCenters[i], i);
                else
                    result = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], null, NeighborsOfTrainingPoints[i]); // we don't need to look up nearest neighbor using tree, because we already have it precalculated for each training point
                double difference = result - GetClusterOutput(i);
                double squaredError = difference * difference;
                total += squaredError;
            }
            double returnVal = total / (double)NumberClusters;
            return returnVal;
        }

        internal double CalculateEuclideanDistanceSquared(double[] pointOne, double[] pointTwo)
        {
            double total = 0.0;
            for (int d = 0; d < Dimensions; d++)
            {
                double valueToSquare = (pointOne[d] - pointTwo[d]) * DistanceInDimensionMultiplier[d];
                total += valueToSquare * valueToSquare;
            }
            return total;
        }

        public double CalculateOutputNearestNeighborsOnly(double[] newInputSet, int? nearestNeighborOfPoint, List<int> nearestNeighborsOfThatNeighbor)
        {
            double numeratorTotal = 0, denominatorTotal = 0;
            double divideBy = 1.0;
            while (denominatorTotal == 0 && divideBy < 200)
            { // this outer loop is necessary because if the points are sufficiently far, expTerm will always be zero and we'll have division by zero error
                // first, find the nearest cluster center itself, and add it to the numerator and denominator
                if (nearestNeighborOfPoint != null)
                    CalculateContributionOfPointToOutput(newInputSet, ref numeratorTotal, ref denominatorTotal, divideBy, (int)nearestNeighborOfPoint);
                // second, look at all of the nearest neighbors of the nearest cluster center
                foreach (int i in nearestNeighborsOfThatNeighbor)
                    CalculateContributionOfPointToOutput(newInputSet, ref numeratorTotal, ref denominatorTotal, divideBy, i);
                // change divideBy to reduce the chance that everything will be zero
                if (denominatorTotal == 0)
                    divideBy *= 1.5;
            }
            return divideBy < 200 ? numeratorTotal / denominatorTotal : 0;
        }

        private void CalculateContributionOfPointToOutput(double[] newInputSet, ref double numeratorTotal, ref double denominatorTotal, double divideByThisBeforeApplyingExponential, int i)
        {
            double distanceSquared = CalculateEuclideanDistanceSquared(newInputSet, ClusterCenters[i]);
            double expTerm = Math.Exp(-distanceSquared / (TwoSigmaSquared * divideByThisBeforeApplyingExponential));
            double contributionOfPoint = GetValueForCalculatingContributionOfPointToOutputAtSpecifiedPoint(i);
            numeratorTotal += contributionOfPoint * expTerm * ClusterWeights[i];
            denominatorTotal += expTerm * ClusterWeights[i];
        }

        public double CalculateOutput(double[] newInputSet, int? omittedObservation = null, bool speedUpWithApproximateNearestNeighbor = false)
        {
            if (TreeToLocateNearestNeighbor != null)
            {
                int nearestNeighborOfPoint;
                List<int> nearestNeighborsOfThatNeighbor;
                GetNearestNeighborsForPoint(newInputSet.ToList(), out nearestNeighborOfPoint, out nearestNeighborsOfThatNeighbor, speedUpWithApproximateNearestNeighbor);
                return CalculateOutputNearestNeighborsOnly(newInputSet, nearestNeighborOfPoint, nearestNeighborsOfThatNeighbor);
            }
            else
            {
                double numeratorTotal = 0, denominatorTotal = 0;
                for (int i = 0; i < NumberClusters; i++)
                {
                    if (i != omittedObservation)
                    {
                        double distanceSquared = CalculateEuclideanDistanceSquared(newInputSet, ClusterCenters[i]);
                        double expTerm = Math.Exp(-distanceSquared / TwoSigmaSquared);
                        numeratorTotal += GetClusterOutput(i) * expTerm;
                        denominatorTotal += expTerm;
                    }
                }
                return numeratorTotal / denominatorTotal;
            }
        }
    }
}

