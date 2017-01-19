//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using ACESim.Util;
//using System.Diagnostics;

//namespace ACESim
//{
//    [Serializable]
//    public class GRNN
//    {
//        internal List<double[]> ClusterCenters;
//        internal List<double> ClusterOutputs;
//        bool UseOriginalClusterOutputs = true;
//        internal List<List<int>> NeighborsOfTrainingPoints;
//        internal KDTree TreeToLocateNearestNeighbor;
//        List<double> InputAveragesForNormalization;
//        List<double> InputStdevsForNormalization;
//        int NumberClusters;
//        double Sigma;
//        double TwoSigmaSquared;
//        double[] DistanceInDimensionMultiplier;
//        //List<double> WidthParameterSigma;
//        internal int Dimensions;
//        bool UsePointSpecificWidths = false; // we need a new algorithm to do this. one problem with the current algorithm is that we can't really just consider the contribution to other training points; we really need a validation set.

//        public GRNN()
//        {
//        }

//        public GRNN(List<double[]> trainingInputs, List<double> trainingOutputs) :
//            this(trainingInputs, trainingOutputs, null, null)
//        {
//        }

//        public GRNN DeepCopy()
//        {
//            GRNN copy = new GRNN();
//            SetCopyFields(copy);
//            return copy;
//        }

//        public virtual void SetCopyFields(GRNN copy)
//        {
//            copy.ClusterCenters = ClusterCenters.Select(x => x.ToArray()).ToList();
//            copy.ClusterOutputs = ClusterOutputs.ToList();
//            copy.NeighborsOfTrainingPoints = NeighborsOfTrainingPoints.Select(x => x.ToList()).ToList();
//            copy.TreeToLocateNearestNeighbor = TreeToLocateNearestNeighbor.DeepCopy(null);
//            copy.InputAveragesForNormalization = InputAveragesForNormalization.ToList();
//            copy.InputStdevsForNormalization = InputStdevsForNormalization.ToList();
//            copy.NumberClusters = NumberClusters;
//            copy.Sigma = Sigma;
//            copy.TwoSigmaSquared = TwoSigmaSquared;
//            copy.Dimensions = Dimensions;
//            copy.DistanceInDimensionMultiplier = DistanceInDimensionMultiplier == null ? null : DistanceInDimensionMultiplier.ToArray();
//        }

//        public GRNN(List<double[]> trainingInputs, List<double> trainingOutputs, List<List<int>> neighborsOfTrainingPoints, int? numClusters, KDTree existingTreeToLocateNearestNeighbor = null)
//        {
//            InitializeBasedOnTrainingOutputs(trainingInputs, trainingOutputs, neighborsOfTrainingPoints, numClusters, existingTreeToLocateNearestNeighbor);
//        }

//        internal void InitializeBasedOnTrainingOutputs(List<double[]> trainingInputs, List<double> trainingOutputs, List<List<int>> neighborsOfTrainingPoints, int? numClusters, KDTree existingTreeToLocateNearestNeighbor)
//        {
//            if (neighborsOfTrainingPoints != null && numClusters != null)
//                throw new Exception("Cannot both specify neighbors to consider and enable clustering for GRNN.");
//            if (numClusters == null)
//            {
//                ClusterCenters = trainingInputs.Select(x => x.ToArray()).ToList(); // copy the data
//                ClusterOutputs = trainingOutputs.ToList();
//            }
//            else
//            {
//                FuzzyCMeansClustering clusterer = new FuzzyCMeansClustering();
//                clusterer.GetClusters(trainingInputs, trainingOutputs, null, (int)numClusters, out ClusterCenters, out ClusterOutputs);
//                //for (int c = 0; c < numClusters; c++)
//                //{
//                //    Debug.WriteLine(ObfuscationGame.ObfuscationCorrectAnswer.Calculate(ClusterCenters[c][1], ClusterCenters[c][0]) + " " + ClusterOutputs[c]);
//                //    ClusterOutputs[c] = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(ClusterCenters[c][1], ClusterCenters[c][0]); // see if this will work better with preclustering and thus proper optimization.
//                //}
//            }
//            NumberClusters = ClusterCenters.Count;
//            Dimensions = ClusterCenters.First().Length;
//            DistanceInDimensionMultiplier = new double[Dimensions];
//            for (int d = 0; d < DistanceInDimensionMultiplier.Length; d++)
//                DistanceInDimensionMultiplier[d] = 1.0;
//            NeighborsOfTrainingPoints = neighborsOfTrainingPoints;
//            if (existingTreeToLocateNearestNeighbor != null)
//                TreeToLocateNearestNeighbor = existingTreeToLocateNearestNeighbor;
//            else if (NeighborsOfTrainingPoints != null)
//                PrepareKDTree();
//            CalculateWidthParameter();
//            OptimizeDistanceInDimensionMultiplier();
//        }

//        public virtual double GetClusterOutput(int i)
//        {
//            return ClusterOutputs[i];
//        }

//        public virtual double GetValueForCalculatingContributionOfPointToOutputAtSpecifiedPoint(int indexOfContributingPoint)
//        {
//            return GetClusterOutput(indexOfContributingPoint);
//        }

//        private void PrepareKDTree()
//        {
//            List<Point> normalizedPointList = new List<Point>();
//            GetStatsForNormalization();
//            for (int i = 0; i < ClusterCenters.Count; i++)
//            {
//                NormalizedPoint normalizedPoint = new NormalizedPoint(ClusterCenters[i].ToList(), InputAveragesForNormalization, InputStdevsForNormalization, i);
//                normalizedPointList.Add(normalizedPoint);
//            }
//            double[] lowerBounds = new double[Dimensions];
//            double[] upperBounds = new double[Dimensions];
//            for (int d = 0; d < Dimensions; d++)
//            {
//                lowerBounds[d] = normalizedPointList.Min(x => x.GetLocation()[d]);
//                upperBounds[d] = normalizedPointList.Max(x => x.GetLocation()[d]);
//            }
//            const int numPerHypercube = 5;
//            TreeToLocateNearestNeighbor = new KDTree(Dimensions, normalizedPointList, null, lowerBounds, upperBounds, -1, numPerHypercube);
//            TreeToLocateNearestNeighbor.CompleteInitializationAfterAddingAllPoints();
//        }

//        public void GetNearestNeighborsForPoint(List<double> unnormalizedLocation, out int nearestNeighborOfPoint, out List<int> nearestNeighborsOfThatNeighbor, bool speedUpWithApproximateNearestNeighbor)
//        {
//            NormalizedPoint normalizedPoint = new NormalizedPoint(unnormalizedLocation.ToList(), InputAveragesForNormalization, InputStdevsForNormalization, -1);
//            NormalizedPoint nearestNeighbor = (NormalizedPoint)(speedUpWithApproximateNearestNeighbor ? TreeToLocateNearestNeighbor.GetApproximateNearestNeighbor(normalizedPoint, false) : TreeToLocateNearestNeighbor.GetNearestNeighbor(normalizedPoint, false));
//            nearestNeighborOfPoint = nearestNeighbor.AssociatedIndex;
//            nearestNeighborsOfThatNeighbor = NeighborsOfTrainingPoints[nearestNeighborOfPoint];
//        }

//        private void GetStatsForNormalization()
//        {
//            StatCollectorArray statCollectorForSmoothingSet = new StatCollectorArray();
//            foreach (double[] cluster in ClusterCenters)
//                statCollectorForSmoothingSet.Add(cluster);
//            InputAveragesForNormalization = statCollectorForSmoothingSet.Average();
//            InputStdevsForNormalization = statCollectorForSmoothingSet.StandardDeviation();
//        }

//        public void CalculateWidthParameter()
//        {
//            CalculateStartingPointWidth();
//            Sigma = FindOptimalValueGreaterThanZero.Maximize(p => 0 - AssessPossibleWidthValue(p), Sigma, 1.2, 0.001, 100);
//            TwoSigmaSquared = 2 * Sigma * Sigma;
//            if (UsePointSpecificWidths)
//                CalculatePointSpecificWidths();
//        }

//        internal void CalculateStartingPointWidth()
//        {
//            double squaredTotal = 0;
//            for (int d = 0; d < Dimensions; d++)
//            {
//                double avgDistanceThisDimension = (ClusterCenters.Max(x => x[d]) - ClusterCenters.Min(x => x[d])) / (double)NumberClusters;
//                squaredTotal += avgDistanceThisDimension;
//            }
//            Sigma = Math.Sqrt(squaredTotal) * 2.0;
//            TwoSigmaSquared = 2 * Sigma * Sigma;
//        }

//        internal double AssessPossibleWidthValue(double possibleWidth)
//        {
//            double originalSigma = Sigma;
//            Sigma = possibleWidth;
//            TwoSigmaSquared = 2 * Sigma * Sigma;
//            double returnVal = AssessPerformanceByCalculatingEachTrainingDatumFromOthers();
//            Sigma = originalSigma;
//            TwoSigmaSquared = 2 * Sigma * Sigma;
//            return returnVal;
//        }

//        double?[] PointSpecificWidths;
//        internal void CalculatePointSpecificWidths()
//        {
//            PointSpecificWidths = new double?[NumberClusters];
//            for (int i = 0; i < NumberClusters; i++)
//            {
//                PointSpecificWidths[i] = FindOptimalValueGreaterThanZero.Maximize(p => 0 - AssessPossiblePointSpecificWidthValue(i, p), Sigma, 1.2, 0.001, 100);
//            }
//        }

//        internal double AssessPossiblePointSpecificWidthValue(int pointIndex, double possibleWidth)
//        {
//            PointSpecificWidths[pointIndex] = possibleWidth;
//            double returnVal = AssessPerformanceByCalculatingEffectOfPointOnOtherPointsHavingItAsNeighbor(pointIndex); // AssessPerformanceByCalculatingEachTrainingDatumFromOthers();
//            return returnVal;
//        }

//        private double AssessPerformanceByCalculatingEffectOfPointOnOtherPointsHavingItAsNeighbor(int pointIndex)
//        {
//            double valueToTest = (double)PointSpecificWidths[pointIndex];
//            double sumDeltaSquareErrors = 0;
//            for (int i = 0; i < NumberClusters; i++)
//            {
//                if (i != pointIndex)
//                {
//                    PointSpecificWidths[pointIndex] = null;
//                    double numer = 0, denom = 0;
//                    CalculateContributionOfPointToOutput(ClusterCenters[i], ref numer, ref denom, 1.0, pointIndex);

//                    PointSpecificWidths[pointIndex] = valueToTest;
//                    double numer2 = 0, denom2 = 0;
//                    CalculateContributionOfPointToOutput(ClusterCenters[i], ref numer2, ref denom2, 1.0, pointIndex);

//                    double revisedValueForPointI = (NumeratorTotalsStandardWidth[i] + numer2 - numer) / (DenominatorTotalsStandardWidth[i] + denom2 - denom);
//                    double revisedError = revisedValueForPointI - GetClusterOutput(i);
//                    double revisedErrorSq = revisedError * revisedError;

//                    double origError = ResultWithStandardWidth[i] - GetClusterOutput(i);
//                    double origErrorSq = origError * origError;

//                    double deltaErrorSq = revisedErrorSq - origErrorSq;
//                    sumDeltaSquareErrors += deltaErrorSq;
//                }
//            }
//            return sumDeltaSquareErrors;
//        }

//        internal void OptimizeDistanceInDimensionMultiplier()
//        {
//            for (int d = 0; d < DistanceInDimensionMultiplier.Length; d++)
//                DistanceInDimensionMultiplier[d] = FindOptimalValueGreaterThanZero.Maximize(p => 0 - AssessPossibleDistanceInDimension(d, p), 1.0, 1.2, 0.01, 100);
//        }

//        internal double AssessPossibleDistanceInDimension(int dimension, double possibleMultiplier)
//        {
//            double originalMultiplier = DistanceInDimensionMultiplier[dimension];
//            DistanceInDimensionMultiplier[dimension] = possibleMultiplier;
//            double returnVal = AssessPerformanceByCalculatingSomeOfTrainingDataFromOthers(); // AssessPerformanceByCalculatingEachTrainingDatumFromOthers();
//            DistanceInDimensionMultiplier[dimension] = originalMultiplier;
//            return returnVal;
//        }

//        double[] ResultWithStandardWidth;
//        double[] NumeratorTotalsStandardWidth;
//        double[] DenominatorTotalsStandardWidth;
//        internal double AssessPerformanceByCalculatingEachTrainingDatumFromOthers()
//        {
//            double total = 0;
//            ResultWithStandardWidth = new double[NumberClusters];
//            NumeratorTotalsStandardWidth = new double[NumberClusters];
//            DenominatorTotalsStandardWidth = new double[NumberClusters];
//            for (int i = 0; i < NumberClusters; i++)
//            {
//                double result;
//                if (TreeToLocateNearestNeighbor == null)
//                    result = CalculateOutput(ClusterCenters[i], i);
//                else
//                {
//                    result = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], null, NeighborsOfTrainingPoints[i]); // we don't need to look up nearest neighbor using tree, because we already have it precalculated for each training point
//                    NumeratorTotalsStandardWidth[i] = numeratorTotal;
//                    DenominatorTotalsStandardWidth[i] = denominatorTotal;
//                }
//                ResultWithStandardWidth[i] = result;
//                double difference = result - GetClusterOutput(i);
//                double squaredError = difference * difference;
//                total += squaredError;
//            }
//            double returnVal = total / (double)NumberClusters;
//            return returnVal;
//        }

//        internal double AssessPerformanceByCalculatingSomeOfTrainingDataFromOthers()
//        {
//            int calculateEveryNthDatum = 10;
//            double total = 0;
//            for (int i = 0; i < NumberClusters; i++)
//            {
//                if (i % calculateEveryNthDatum == 0)
//                {
//                    double result;
//                    result = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], null, NeighborsOfTrainingPoints[i].Where(x => x % calculateEveryNthDatum != 0).ToList());
//                    double difference = result - GetClusterOutput(i);
//                    double squaredError = difference * difference;
//                    total += squaredError;
//                }
//            }
//            double returnVal = total / (double)NumberClusters;
//            return returnVal;
//        }

//        internal double CalculateEuclideanDistanceSquared(double[] pointOne, double[] pointTwo)
//        {
//            double total = 0.0;
//            for (int d = 0; d < Dimensions; d++)
//            {
//                double valueToSquare = (pointOne[d] - pointTwo[d]) * DistanceInDimensionMultiplier[d];
//                total += valueToSquare * valueToSquare;
//            }
//            return total;
//        }

//        // Keep these outside the function so that they can be referenced as an optional additional output.
//        double numeratorTotal = 0, denominatorTotal = 0;
//        public double CalculateOutputNearestNeighborsOnly(double[] newInputSet, int? nearestNeighborOfPoint, List<int> nearestNeighborsOfThatNeighbor)
//        {
//            numeratorTotal = 0;
//            denominatorTotal = 0;
//            double divideBy = 1.0;
//            while (denominatorTotal == 0)
//            { // this outer loop is necessary because if the points are sufficiently far, expTerm will always be zero and we'll have division by zero error
//                // first, find the nearest cluster center itself, and add it to the numerator and denominator
//                if (nearestNeighborOfPoint != null)
//                    CalculateContributionOfPointToOutput(newInputSet, ref numeratorTotal, ref denominatorTotal, divideBy, (int)nearestNeighborOfPoint);
//                // second, look at all of the nearest neighbors of the nearest cluster center
//                foreach (int i in nearestNeighborsOfThatNeighbor)
//                    CalculateContributionOfPointToOutput(newInputSet, ref numeratorTotal, ref denominatorTotal, divideBy, i);
//                // change divideBy to reduce the chance that everything will be zero
//                if (denominatorTotal == 0)
//                    divideBy *= 1.5;
//            }
//            return numeratorTotal / denominatorTotal;
//        }

//        private void CalculateContributionOfPointToOutput(double[] newInputSet, ref double numeratorTotal, ref double denominatorTotal, double divideByThisBeforeApplyingExponential, int i)
//        {
//            double distanceSquared = CalculateEuclideanDistanceSquared(newInputSet, ClusterCenters[i]);
//            double twoSigmaSquared = TwoSigmaSquared;
//            if (PointSpecificWidths != null && PointSpecificWidths[i] != null)
//                twoSigmaSquared = 2 * (double)PointSpecificWidths[i] * (double)PointSpecificWidths[i];
//            double expTerm = Math.Exp(-distanceSquared / (twoSigmaSquared * divideByThisBeforeApplyingExponential));
//            double contributionOfPoint = GetValueForCalculatingContributionOfPointToOutputAtSpecifiedPoint(i);
//            numeratorTotal += contributionOfPoint * expTerm;
//            denominatorTotal += expTerm;
//        }

//        public double CalculateOutput(double[] newInputSet, int? omittedObservation = null, bool speedUpWithApproximateNearestNeighbor = false)
//        {
//            if (TreeToLocateNearestNeighbor != null)
//            {
//                int nearestNeighborOfPoint;
//                List<int> nearestNeighborsOfThatNeighbor;
//                GetNearestNeighborsForPoint(newInputSet.ToList(), out nearestNeighborOfPoint, out nearestNeighborsOfThatNeighbor, speedUpWithApproximateNearestNeighbor);
//                return CalculateOutputNearestNeighborsOnly(newInputSet, nearestNeighborOfPoint, nearestNeighborsOfThatNeighbor);
//            }
//            else
//            {
//                double numeratorTotal = 0, denominatorTotal = 0;
//                for (int i = 0; i < NumberClusters; i++)
//                {
//                    if (i != omittedObservation)
//                    {
//                        double distanceSquared = CalculateEuclideanDistanceSquared(newInputSet, ClusterCenters[i]);
//                        double expTerm = Math.Exp(-distanceSquared / TwoSigmaSquared);
//                        numeratorTotal += GetClusterOutput(i) * expTerm;
//                        denominatorTotal += expTerm;
//                    }
//                }
//                return numeratorTotal / denominatorTotal;
//            }
//        }
//    }
//}
