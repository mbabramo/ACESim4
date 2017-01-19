using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class GRNNGradient : GRNN
    {
        List<double> AlternativeClusterOutputs;
        int DerivativeLevels;
        int DerivativeRepetitions;
        List<GRNNGradient> DerivativeGRNNs;

        public new GRNNGradient DeepCopy()
        {
            GRNNGradient copy = new GRNNGradient();
            SetCopyFields(copy);
            return copy;
        }

        public virtual void SetCopyFields(GRNNGradient copy)
        {
            copy.AlternativeClusterOutputs = AlternativeClusterOutputs == null ? null : AlternativeClusterOutputs.ToList();
            base.SetCopyFields(copy);
        }

        enum ClusterOutputSourceEnum
        {
            Original,
            Alternative
        }
        ClusterOutputSourceEnum ClusterOutputSource;

        enum ClusterOutputModeEnum
        {
            SourceValue,
            DerivativeRelativeToSpecifiedPoint,
            SourceValueTakingIntoAccountDerivative
        }
        ClusterOutputModeEnum ClusterOutputMode;
        int? DimensionForSpecifiedPointToCalculateDerivative;
        double[] InputValuesForSpecifiedPointToCalculateDerivative;
        double? OutputValueForSpecifiedPointToCalculateDerivative;

        public override double GetClusterOutput(int i)
        {
            if (ClusterOutputSource == ClusterOutputSourceEnum.Original)
                return ClusterOutputs[i];
            else if (ClusterOutputSource == ClusterOutputSourceEnum.Alternative)
                return AlternativeClusterOutputs[i];
            else throw new Exception("Internal exception.");
        }

        public override double GetValueForCalculatingContributionOfPointToOutputAtSpecifiedPoint(int indexOfContributingPoint)
        {
            if (ClusterOutputMode == ClusterOutputModeEnum.SourceValue)
                return GetClusterOutput(indexOfContributingPoint);
            else if (ClusterOutputMode == ClusterOutputModeEnum.DerivativeRelativeToSpecifiedPoint)
            {
                double valueAtContributingPoint = GetClusterOutput(indexOfContributingPoint);
                double valueAtSpecifiedPoint = (double)OutputValueForSpecifiedPointToCalculateDerivative;
                double locationOfContributingPointThisDimension = ClusterCenters[indexOfContributingPoint][(int)DimensionForSpecifiedPointToCalculateDerivative];
                double locationOfSpecifiedPointThisDimension = InputValuesForSpecifiedPointToCalculateDerivative[(int)DimensionForSpecifiedPointToCalculateDerivative];
                double distance = (locationOfContributingPointThisDimension - locationOfSpecifiedPointThisDimension);
                if (distance == 0)
                    return 0;
                return (valueAtContributingPoint - valueAtSpecifiedPoint) / distance;
            }
            else if (ClusterOutputMode == ClusterOutputModeEnum.SourceValueTakingIntoAccountDerivative)
            {
                double sourceValueAdjusted = GetClusterOutput(indexOfContributingPoint);
                for (int d = 0; d < Dimensions; d++)
                {
                    double derivativeAtSource = DerivativeGRNNs[d].GetClusterOutput(indexOfContributingPoint);

                    double locationOfContributingPointThisDimension = ClusterCenters[indexOfContributingPoint][d];
                    double locationOfSpecifiedPointThisDimension = InputValuesForSpecifiedPointToCalculateDerivative[d];
                    double distance = locationOfSpecifiedPointThisDimension - locationOfContributingPointThisDimension; // note that we're calculating the distance in the direction of the specified point

                    double adjustment = derivativeAtSource * distance;
                    sourceValueAdjusted += adjustment;
                }
                return sourceValueAdjusted;
            }
            else throw new Exception("Internal exception.");
        }

        public GRNNGradient()
        {
        }

        public GRNNGradient(List<double[]> trainingInputs, List<double> trainingOutputs, List<List<int>> neighborsOfTrainingPoints, int? numClusters, int derivativeLevels, int derivativeRepetitions, KDTree existingTreeToLocateNearestNeighbor = null) 
        {
            InitializeBasedOnTrainingOutputs(trainingInputs, trainingOutputs, neighborsOfTrainingPoints, numClusters, existingTreeToLocateNearestNeighbor);
            ClusterOutputSource = ClusterOutputSourceEnum.Original;
            ClusterOutputMode = ClusterOutputModeEnum.SourceValue;
            DerivativeRepetitions = derivativeRepetitions;
            DerivativeLevels = derivativeLevels;
            CreateDerivativeGRNNs(derivativeRepetitions);
        }

        private void CreateDerivativeGRNNs(int derivativeRepetitions)
        {
            if (DerivativeLevels > 0)
            {
                for (int r = 0; r < derivativeRepetitions; r++)
                {
                    DerivativeGRNNs = new List<GRNNGradient>();
                    for (int d = 0; d < Dimensions; d++)
                        DerivativeGRNNs.Add(CreateDerivativeGRNNForDimension(d));
                    CreateAlternativeOutputsTakingIntoAccountDerivatives();
                    CalculateWidthParameter();
                    OptimizeDistanceInDimensionMultiplier();
                }
            }
        }

        private GRNNGradient CreateDerivativeGRNNForDimension(int dimension)
        {
            List<double> trainingOutputsForDerivativeGRNN = new List<double>();
            int numberPoints = ClusterCenters.Count();
            for (int i = 0; i < numberPoints; i++)
            {
                double approximateDerivative = ApproximateDerivativeAtClusterPoint(dimension, i);
                trainingOutputsForDerivativeGRNN.Add( approximateDerivative);
            }
            GRNNGradient newGRNN = new GRNNGradient(ClusterCenters, trainingOutputsForDerivativeGRNN, NeighborsOfTrainingPoints, null, DerivativeLevels - 1, DerivativeRepetitions, TreeToLocateNearestNeighbor);
            return newGRNN;
        }

        private double ApproximateDerivativeAtClusterPoint_Old(int dimension, int i)
        {
            var origMode = ClusterOutputMode;
            ClusterOutputMode = ClusterOutputModeEnum.DerivativeRelativeToSpecifiedPoint;
            DimensionForSpecifiedPointToCalculateDerivative = dimension;
            InputValuesForSpecifiedPointToCalculateDerivative = ClusterCenters[i];
            OutputValueForSpecifiedPointToCalculateDerivative = GetClusterOutput(i);
            double approximateDerivative = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], null, NeighborsOfTrainingPoints[i]);
            ClusterOutputMode = origMode;
            return approximateDerivative;
        }

        private double ApproximateDerivativeAtClusterPoint(int dimension, int i)
        {
            var origMode = ClusterOutputMode;
            ClusterOutputMode = ClusterOutputModeEnum.SourceValue;

            int? nearestNeighborToUse = null; 
            double originalValue = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], nearestNeighborToUse, NeighborsOfTrainingPoints[i]);
            double[] forCalculatingDerivative = ClusterCenters[i].ToArray();
            double derivDistance = 0.001;
            forCalculatingDerivative[dimension] += 0.001;
            double nearbyValue = CalculateOutputNearestNeighborsOnly(forCalculatingDerivative, nearestNeighborToUse, NeighborsOfTrainingPoints[i]);
            double approximateDerivative = (nearbyValue - originalValue) / derivDistance;
            ClusterOutputMode = origMode;

            return approximateDerivative;
        }

        private void CreateAlternativeOutputsTakingIntoAccountDerivatives()
        {
            List<double> alternativeOutputs = new List<double>();
            ClusterOutputMode = ClusterOutputModeEnum.SourceValueTakingIntoAccountDerivative;
            int numberPoints = ClusterCenters.Count();
            for (int i = 0; i < numberPoints; i++)
            {
                InputValuesForSpecifiedPointToCalculateDerivative = ClusterCenters[i];
                double newValueEstimate = CalculateOutputNearestNeighborsOnly(ClusterCenters[i], i, NeighborsOfTrainingPoints[i]);
                alternativeOutputs.Add(newValueEstimate);
            }
            AlternativeClusterOutputs = alternativeOutputs;
            ClusterOutputSource = ClusterOutputSourceEnum.Alternative;
            ClusterOutputMode = ClusterOutputModeEnum.SourceValue;
        }

    }
}
