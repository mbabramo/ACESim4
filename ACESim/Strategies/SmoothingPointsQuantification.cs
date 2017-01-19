using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SmoothingPointsQuantification
    {
        public int NumSmoothingPoints;
        public long NumIterations;
        public bool ChunkIterationsForRemoting;
        public bool RemotingCanSeparateFindingAndSmoothing;
        public bool UseWorkerRolesForRemoting;
        public int ChunkSizeForRemoting;
        public double SmoothingPointsMultiplierForEachInputAboveTwo;
        public double IterationsMultiplierForEachInputAboveTwo;
        public int MaxIterationsPerSmoothingPoint;

        public int GetNumSmoothingPoints(int numInputs)
        {
            if (SmoothingPointsMultiplierForEachInputAboveTwo == 1.0 || numInputs <= 2)
                return NumSmoothingPoints;
            double numPoints = (double)NumSmoothingPoints;
            for (int input = 3; input <= numInputs; input++)
                numPoints *= SmoothingPointsMultiplierForEachInputAboveTwo;
            return (int)numPoints;
        }

        public long GetNumIterations(int numInputs)
        {
            if (IterationsMultiplierForEachInputAboveTwo == 1.0 || numInputs <= 2)
                return NumIterations;
            double numIterations = (double)NumIterations;
            for (int input = 3; input <= numInputs; input++)
                numIterations *= IterationsMultiplierForEachInputAboveTwo;
            return (long)numIterations;
        }
    }

    [Serializable]
    public class SmoothingPointsMainSetQuantification : SmoothingPointsQuantification
    {
        public bool ChooseSmoothingPointsByClusteringLargerNumberOfPoints;
        public int LargerNumberOfPointsFromWhichToClusterSmoothingPoints;
    }

    [Serializable]
    public class SmoothingPointsValidationSetQuantification : SmoothingPointsQuantification
    {
        public bool CreateValidationSet;
    }
}
