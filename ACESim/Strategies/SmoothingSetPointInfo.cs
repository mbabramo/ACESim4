using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PriorityQueue;

namespace ACESim
{
    [Serializable]
    public class SmoothingSetPointInfo
    {
        public List<double> decisionInputs;
        public List<double> decisionInputsNormalized;
        public object pointsInRunningSetLock = new object();
        int MaxPointsClosestToThisPoint;
        public PriorityQueue<double, IterationID> pointsInRunningSetClosestToThisPoint;
        public int pointsInRunningSetCount;
        public double preSmoothingValue;
        public double postSmoothingValue;
        public double[] preSmoothingValuesForSubsequentDecisions;
        public double absoluteDifferenceBetweenBipolarStrategies;
        public double? fastConvergenceShiftValue;
        public int[] nearestNeighbors;
        public double[] derivatives;
        public bool eligible;
        public bool isFromPreviousOptimization;
        public int DecisionNumber;
        public int SmoothingPointNumber;
        public bool ValidationMode;

        public SmoothingSetPointInfo()
        {
        }

        public SmoothingSetPointInfo(int decisionNumber, int smoothingPointNumber, int maxPointsClosestToThisPoint, bool validationMode)
        {
            decisionInputs = null;
            MaxPointsClosestToThisPoint = maxPointsClosestToThisPoint;
            DecisionNumber = decisionNumber;
            SmoothingPointNumber = smoothingPointNumber;
            pointsInRunningSetClosestToThisPoint = new PriorityQueue<double, IterationID>(0, maxPointsClosestToThisPoint, new ReverseComparer<double>());
            preSmoothingValue = 0;
            preSmoothingValuesForSubsequentDecisions = null;
            ValidationMode = validationMode;
            eligible = true;
        }

        public SmoothingSetPointInfo DeepCopy(bool excludeExtrinsicInformation = false)
        {
            PriorityQueue<double, IterationID> copyOfPriorityQueue = null;
            if (!excludeExtrinsicInformation && pointsInRunningSetClosestToThisPoint != null)
                copyOfPriorityQueue = pointsInRunningSetClosestToThisPoint.DeepCopy();
            return new SmoothingSetPointInfo(DecisionNumber, SmoothingPointNumber, MaxPointsClosestToThisPoint, ValidationMode)
            {
                decisionInputs = decisionInputs.ToList(),
                decisionInputsNormalized = decisionInputsNormalized == null ? null : decisionInputsNormalized.ToList(),
                pointsInRunningSetClosestToThisPoint = copyOfPriorityQueue,
                pointsInRunningSetCount = pointsInRunningSetCount,
                preSmoothingValue = preSmoothingValue,
                postSmoothingValue = postSmoothingValue,
                preSmoothingValuesForSubsequentDecisions = preSmoothingValuesForSubsequentDecisions == null ? null : preSmoothingValuesForSubsequentDecisions.ToArray(),
                absoluteDifferenceBetweenBipolarStrategies = absoluteDifferenceBetweenBipolarStrategies,
                fastConvergenceShiftValue = fastConvergenceShiftValue,
                nearestNeighbors = (excludeExtrinsicInformation || nearestNeighbors == null) ? null : nearestNeighbors.ToArray(),
                derivatives = (derivatives == null || excludeExtrinsicInformation) ? null : derivatives.ToArray(),
                eligible = eligible,
                isFromPreviousOptimization = isFromPreviousOptimization
            };
        }
    }
}
