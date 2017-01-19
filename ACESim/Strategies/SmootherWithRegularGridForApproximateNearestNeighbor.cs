using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public abstract class SmootherWithRegularGridForApproximateNearestNeighbor : OptimizePointsAndSmooth
    {

        internal List<double[]> percentileValuesEachDimensionForSmoothingSetPoints;
        internal int regularGridPointsEachDimension;
        internal int regularGridPoints;
        internal List<int> nearestNeighborInSmoothingSetBasedOnRegularGridPointNum;

        public SmootherWithRegularGridForApproximateNearestNeighbor()
        {
        }

        public override void SetCopyFields(IStrategyComponent copy)
        {
            SmootherWithRegularGridForApproximateNearestNeighbor copyCast = (SmootherWithRegularGridForApproximateNearestNeighbor)copy;
            copyCast.percentileValuesEachDimensionForSmoothingSetPoints = percentileValuesEachDimensionForSmoothingSetPoints.Select(x => x.ToArray()).ToList();
            copyCast.regularGridPointsEachDimension = regularGridPointsEachDimension;
            copyCast.regularGridPoints = regularGridPoints;
            copyCast.nearestNeighborInSmoothingSetBasedOnRegularGridPointNum = nearestNeighborInSmoothingSetBasedOnRegularGridPointNum.ToList();
            base.SetCopyFields(copyCast);
        }

        public SmootherWithRegularGridForApproximateNearestNeighbor(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
            : base(overallStrategy, dimensions, evolutionSettings, decision, name)
        {
        }

        public abstract int GetRegularGridPoints();

        /// <summary>
        /// After smoothing everything in the smoothing set, we calculate a regular grid of points so that we can find approximate nearest neighbors
        /// without using the Hypercube-based nearest neighbors algorithm.
        /// </summary>
        internal void SetUpRegularGrid()
        {
            // The number of regular grid points is approximated in the settings file, but will be calculated exactly here, so that it works out as a power of an integral number of points.
            regularGridPointsEachDimension = (int)Math.Round(Math.Pow(GetRegularGridPoints(), (1.0 / Dimensions)));
            CalculatePercentileValuesForEachDimension();
            regularGridPoints = 1;
            for (int d = 0; d < Dimensions; d++)
                regularGridPoints *= regularGridPointsEachDimension;
            nearestNeighborInSmoothingSetBasedOnRegularGridPointNum = new List<int>(regularGridPoints);
            nearestNeighborInSmoothingSetBasedOnRegularGridPointNum.AddRange(Enumerable.Repeat(-1, regularGridPoints)); // initialize the list
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, regularGridPoints, r =>
            {
                List<int> gridIndex = GetRegularGridIndexInEachDimensionForPointNumber(r);
                int nearestNeighborInSmoothingSet = GetNearestNeighborInSmoothingSetForRegularGridPoint(gridIndex);
                nearestNeighborInSmoothingSetBasedOnRegularGridPointNum[r] = nearestNeighborInSmoothingSet;
            }
            );
        }

        /// <summary>
        /// This divides up the smoothing set point input values in each dimension, effectively allowing percentile scores (though not necessarily with 100 per dimension).
        /// </summary>
        private void CalculatePercentileValuesForEachDimension()
        {
            percentileValuesEachDimensionForSmoothingSetPoints = new List<double[]>();
            for (int d = 0; d < Dimensions; d++)
            {
                double[] percentileValuesThisDimension = new double[regularGridPointsEachDimension];
                List<double> orderedValues = SmoothingSetPointInfos.Select(x => x.decisionInputs[d]).OrderBy(x => x).ToList();
                double orderedIndicesCount = orderedValues.Count;
                double numIndicesBetweenGridPoints = orderedIndicesCount / regularGridPointsEachDimension;
                double currentIndex = numIndicesBetweenGridPoints / 2.0;
                for (int r = 0; r < regularGridPointsEachDimension; r++)
                {
                    int indexInOrderedValues = (int)Math.Round(currentIndex);
                    percentileValuesThisDimension[r] = orderedValues[indexInOrderedValues];
                    currentIndex += numIndicesBetweenGridPoints;
                }
                percentileValuesEachDimensionForSmoothingSetPoints.Add(percentileValuesThisDimension);
            }
        }

        /// <summary>
        /// Given an index to a regular grid point, calculates the nearest neighbor in the smoothing set. This is called only when initially setting up the regular grid.
        /// After the regular grid is set up, this is stored in nearestNeighborInSmoothingSetBasedOnRegularGridPointNum.
        /// </summary>
        /// <param name="regularGridIndex"></param>
        /// <returns></returns>
        private int GetNearestNeighborInSmoothingSetForRegularGridPoint(List<int> regularGridIndex)
        {
            List<double> inputs = new List<double>();
            for (int d = 0; d < Dimensions; d++)
            {
                inputs.Add(percentileValuesEachDimensionForSmoothingSetPoints[d][regularGridIndex[d]]);
            }

            NormalizedPoint normalizedPoint = new NormalizedPoint(inputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            List<double> normalizedPointLocation = normalizedPoint.GetLocation().ToList();
            List<Point> neighbors = KDTreeForInputs.GetKNearestNeighbors(normalizedPoint, false, 1);
            int smoothingSetIndex = ((NormalizedPoint)neighbors[0]).AssociatedIndex;
            return smoothingSetIndex;
        }

        /// <summary>
        /// Calculates the closest regular grid index point for a set of inputs.
        /// </summary>
        /// <param name="inputs"></param>
        /// <returns></returns>
        private List<int> GetClosestRegularGridIndexInEachDimension(List<double> inputs)
        {
            List<int> regularGridIndex = new List<int>();
            for (int d = 0; d < Dimensions; d++)
            {
                double targetValue = inputs[d];
                int lowestPossibleIndex = 0;
                int highestPossibleIndex = regularGridPointsEachDimension - 1;
                while (highestPossibleIndex != lowestPossibleIndex + 1)
                {
                    int guessIndex = (int)Math.Round((lowestPossibleIndex + highestPossibleIndex) / 2.0);
                    double pleValueForGuess = percentileValuesEachDimensionForSmoothingSetPoints[d][guessIndex];
                    if (targetValue < pleValueForGuess)
                        highestPossibleIndex = guessIndex;
                    else
                        lowestPossibleIndex = guessIndex;
                }
                double lowPossibility = percentileValuesEachDimensionForSmoothingSetPoints[d][lowestPossibleIndex];
                double highPossibility = percentileValuesEachDimensionForSmoothingSetPoints[d][highestPossibleIndex];
                double distanceToLowPossibility = Math.Abs(targetValue - lowPossibility);
                double distanceToHighPossibility = Math.Abs(targetValue - highPossibility);
                if (distanceToLowPossibility <= distanceToHighPossibility)
                    regularGridIndex.Add(lowestPossibleIndex);
                else
                    regularGridIndex.Add(highestPossibleIndex);
            }
            return regularGridIndex;
        }

        /// <summary>
        /// Converts a regular grid index referred to by the index in each dimension to the point number (i.e., the index in the overall list).
        /// </summary>
        /// <param name="indexInEachDimension"></param>
        /// <returns></returns>
        private int GetRegularGridPointNumber(List<int> indexInEachDimension)
        {
            int pointNumber = 0;
            int multiplier = 1;
            for (int d = 0; d < Dimensions; d++)
            {
                pointNumber += indexInEachDimension[d] * multiplier;
                multiplier *= regularGridPointsEachDimension;
            }
            return pointNumber;
        }

        /// <summary>
        /// Converts the regular grid index point number into a list of indices for each dimension.
        /// </summary>
        /// <param name="pointNumber"></param>
        /// <returns></returns>
        private List<int> GetRegularGridIndexInEachDimensionForPointNumber(int pointNumber)
        {
            List<int> regularGridIndex = new List<int>();
            int[] multiplierForEachDimension = new int[Dimensions];
            // Calculate multiplier for each dimension
            int multiplier = 1;
            for (int d = 0; d < Dimensions; d++)
            {
                multiplierForEachDimension[d] = multiplier;
                multiplier *= regularGridPointsEachDimension;
                regularGridIndex.Add(-1); // temporary holding value
            }
            // Now calculate regular grid index for each dimension working backwords
            for (int d = Dimensions - 1; d >= 0; d--)
            {
                regularGridIndex[d] = pointNumber / multiplierForEachDimension[d];
                pointNumber -= regularGridIndex[d] * multiplierForEachDimension[d];
            }
            // pointNumber should now be zero
            if (pointNumber != 0)
                throw new Exception("Internal error!");
            return regularGridIndex;
        }

        /// <summary>
        /// Returns the nearest neighbors in the smoothing set for a regular grid point number.
        /// </summary>
        /// <param name="regularGridPointNumber"></param>
        /// <param name="numberToReturn"></param>
        /// <returns></returns>
        private List<int> GetSmoothingSetNearestNeighborsForRegularGridPointNumber(int regularGridPointNumber, int numberToReturn)
        {
            return SmoothingSetPointInfos[nearestNeighborInSmoothingSetBasedOnRegularGridPointNum[regularGridPointNumber]].nearestNeighbors.Take(numberToReturn).ToList();
        }

        internal List<int> GetApproximateSmoothingSetNearestNeighborsForInputs(List<double> inputs, int numberToReturn)
        {
            List<int> regularGridIndex = GetClosestRegularGridIndexInEachDimension(inputs);
            int regularGridPointNumber = GetRegularGridPointNumber(regularGridIndex);
            return GetSmoothingSetNearestNeighborsForRegularGridPointNumber(regularGridPointNumber, numberToReturn);
        }
    }
}
