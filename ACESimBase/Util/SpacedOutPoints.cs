using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util
{
    public class SpacedOutPoints
    {
        public int TargetNumPoints;
        public int NumPointsToTry;
        public int NumDimensions;
        public double[] MaxSizePerDimension;
        public double[][] CurrentPoints;
        public (double closestDistance, int indexOfClosest)[] ClosestData;
        public ConsistentRandomSequenceProducer Randomizer;

        public SpacedOutPoints(int targetNumPoints, int numPointsToTry, double[] maxSizePerDimension, ConsistentRandomSequenceProducer randomizer)
        {
            TargetNumPoints = targetNumPoints;
            NumPointsToTry = numPointsToTry;
            MaxSizePerDimension = maxSizePerDimension;
            NumDimensions = MaxSizePerDimension.Length;
            CurrentPoints = new double[TargetNumPoints][];
            ClosestData = new (double closestDistance, int indexOfClosest)[TargetNumPoints];
            Randomizer = randomizer;
        }

        public double[][] CalculatePoints()
        {
            FillWithInitialPoints();
            for (int i = TargetNumPoints; i < NumPointsToTry; i++)
                AddAdditionalPoint();
            return CurrentPoints;
        }

        public void FillWithInitialPoints()
        {
            for (int i = 0; i < TargetNumPoints; i++)
                CurrentPoints[i] = GenerateRandomPoint();
            for (int i = 0; i < TargetNumPoints; i++)
            {
                var closestInfo = GetClosest(i);
                ClosestData[i] = closestInfo;
            }
        }

        public void AddAdditionalPoint()
        {
            int pointToConsiderKickingOutIndex = GetIndexOfPointClosestToNeighbor();
            var pointToConsiderKickingOutInfo = CurrentPoints[pointToConsiderKickingOutIndex];
            var randomPoint = GenerateRandomPoint();
            CurrentPoints[pointToConsiderKickingOutIndex] = randomPoint;
            var closestToRandomPoint = GetClosest(pointToConsiderKickingOutIndex);
            if (closestToRandomPoint.closestDistance > ClosestData[pointToConsiderKickingOutIndex].closestDistance)
            {
                // random point is farther from point we're considering kicking out, so we will go ahead with kicking it out.
                // now we need to update data on any other point that was closest to that one, as well as the new point.
                for (int j = 0; j < TargetNumPoints; j++)
                {
                    if (pointToConsiderKickingOutIndex == j || ClosestData[j].indexOfClosest == pointToConsiderKickingOutIndex)
                        ClosestData[j] = GetClosest(j);
                }
            }
            else
                CurrentPoints[pointToConsiderKickingOutIndex] = pointToConsiderKickingOutInfo; // restore this point -- we're not kicking it out
        }

        public int GetIndexOfPointClosestToNeighbor()
        {
            return ClosestData.Select((item, index) => (item, index)).OrderBy(x => x.item.closestDistance).First().index;
        }

        public (double closestDistance, int indexOfClosest) GetClosest(int i)
        {
            int indexOfClosest = i == 0 ? 1 : 0;
            double closestDistance = CalculateDistanceSq(i, indexOfClosest);
            for (int j = 0; j < TargetNumPoints; j++)
            {
                if (i != j && j != indexOfClosest)
                {
                    double distanceToThisIndex = CalculateDistanceSq(i, j);
                    if (distanceToThisIndex < closestDistance)
                    {
                        closestDistance = distanceToThisIndex;
                        indexOfClosest = j;
                    }
                }
            }
            return (closestDistance, indexOfClosest);
        }

        private double CalculateDistanceSq(int i, int j)
        {
            double[] first = CurrentPoints[i];
            double[] second = CurrentPoints[j];
            double total = 0;
            for (int d = 0; d < NumDimensions; d++)
            {
                double dimensionDistance = first[d] - second[d];
                double squared = dimensionDistance * dimensionDistance;
                total += squared;
            }
            return total;
        }

        public double[] GenerateRandomPoint()
        {
            double[] result = new double[NumDimensions];
            for (int d = 0; d < NumDimensions; d++)
            {
                result[d] = Randomizer.NextDouble() * MaxSizePerDimension[d];
            }
            return result;
        }
    }
}
