using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public class CumulativeDistribution
    {
        public int NumPointsToStore;
        public double[] StoredPoints;
        public bool ConstantValue;
        public bool IsRoughlySymmetricFrom0To1;
        public bool IsUniformFrom0To1;
        public string Name;

        public CumulativeDistribution()
        {
        }

        public CumulativeDistribution(int numPoints)
        {
            List<double> sourcePointsAlreadySorted = new List<double>();
            for (int i = 0; i < numPoints; i++)
                sourcePointsAlreadySorted.Add(0);
            Initialize(numPoints, sourcePointsAlreadySorted);
        }

        public CumulativeDistribution(int numPoints, List<double> sourcePointsAlreadySorted)
        {
            Initialize(numPoints, sourcePointsAlreadySorted);
        }

        private void Initialize(int numPoints, List<double> sourcePointsAlreadySorted)
        {

            NumPointsToStore = numPoints;
            SetDistribution(sourcePointsAlreadySorted);
            AssessSymmetry();
            if (IsUniformFrom0To1)
                SetDistribution(Enumerable.Range(1, sourcePointsAlreadySorted.Count).Select(x => (double)(x - 1) / ((double) sourcePointsAlreadySorted.Count - 1.0)).ToList());
        }

        private void AssessSymmetry()
        {
            int numSourcePoints = StoredPoints.Length;
            int midpointIndex = numSourcePoints % 2 == 0 ? (int)(numSourcePoints / 2) : (int)((numSourcePoints - 1) / 2);
            int oneHalfDistanceEachWay = numSourcePoints / 4;
            int oneThirdDistanceEachWay = numSourcePoints / 6;
            int oneFourthDistanceEachWay = numSourcePoints / 8;
            int oneFifthDistanceEachWay = numSourcePoints / 10;

            const double tolerance = 0.09;
            IsRoughlySymmetricFrom0To1 = Math.Abs(StoredPoints[midpointIndex] - 0.5) < tolerance
                && Math.Abs((StoredPoints[midpointIndex + oneHalfDistanceEachWay] - StoredPoints[midpointIndex]) - (StoredPoints[midpointIndex] - StoredPoints[midpointIndex - oneHalfDistanceEachWay])) < tolerance
                && Math.Abs((StoredPoints[midpointIndex + oneThirdDistanceEachWay] - StoredPoints[midpointIndex]) - (StoredPoints[midpointIndex] - StoredPoints[midpointIndex - oneThirdDistanceEachWay])) < tolerance
                && Math.Abs((StoredPoints[midpointIndex + oneFourthDistanceEachWay] - StoredPoints[midpointIndex]) - (StoredPoints[midpointIndex] - StoredPoints[midpointIndex - oneFourthDistanceEachWay])) < tolerance
                && Math.Abs((StoredPoints[midpointIndex + oneFifthDistanceEachWay] - StoredPoints[midpointIndex]) - (StoredPoints[midpointIndex] - StoredPoints[midpointIndex - oneFifthDistanceEachWay])) < tolerance;
            IsUniformFrom0To1 = IsRoughlySymmetricFrom0To1 && Math.Abs(StoredPoints[midpointIndex + oneHalfDistanceEachWay] - 0.75) < tolerance && Math.Abs(StoredPoints[midpointIndex + oneFourthDistanceEachWay] - 0.625) < tolerance && Math.Abs(StoredPoints[midpointIndex + oneFifthDistanceEachWay] - 0.6) < tolerance && Math.Abs(StoredPoints[midpointIndex + 2 * oneFifthDistanceEachWay] - 0.7) < tolerance && Math.Abs(StoredPoints[midpointIndex + 3 * oneFifthDistanceEachWay] - 0.8) < tolerance && Math.Abs(StoredPoints[midpointIndex + 4 * oneFifthDistanceEachWay] - 0.9) < tolerance;
        }

        public override string ToString()
        {
            string theString = "";
            const int numPointsToShow = 9;
            for (int i = 0; i < numPointsToShow; i++)
            {
                double indexDouble = (((double)i) / ((double)numPointsToShow - 1) * ((double)NumPointsToStore - 1.0));
                int indexRound = (int) Math.Round(indexDouble);
                double number;
                if (Math.Abs(indexDouble - (double)indexRound) < 0.001 || indexRound == numPointsToShow - 1)
                    number = StoredPoints[indexRound];
                else
                {
                    int indexFloor = (int)Math.Floor(indexDouble);
                    double weightOnFirst = 1.0 - (indexDouble - (double)indexFloor);
                    number = weightOnFirst * StoredPoints[indexFloor] + (1.0 - weightOnFirst) * StoredPoints[indexFloor + 1];
                }
                theString += number.ToSignificantFigures(4);
                if (i != numPointsToShow - 1)
                    theString += ", ";
            }
            double avg = StoredPoints.Average();
            return theString + "(Avg. " + avg + ")";
        }

        public CumulativeDistribution DeepCopy()
        {
            return new CumulativeDistribution()
            {
                NumPointsToStore = NumPointsToStore,
                StoredPoints = StoredPoints == null ? null : StoredPoints.ToArray(),
                ConstantValue = ConstantValue,
                IsRoughlySymmetricFrom0To1 = IsRoughlySymmetricFrom0To1,
                IsUniformFrom0To1 = IsUniformFrom0To1,
                Name = Name
            };
        }

        public double GetCumulativeDistributionValue(double valueFromBottomToTop)
        {
            if (IsRoughlySymmetricFrom0To1 && valueFromBottomToTop > 0.5)
                return 1.0 - GetCumulativeDistributionValue(1.0 - valueFromBottomToTop); 
            return StoredPoints[(int)((NumPointsToStore - 1) * valueFromBottomToTop)];
        }

        public void ChangeDistribution(double[] newPoints)
        {
            for (int i = 0; i < NumPointsToStore; i++)
                StoredPoints[i] = newPoints[i];
            AssessSymmetry();
        }

        public void SetDistribution(List<double> sourcePointsAlreadySorted)
        {
            int numSourcePoints = sourcePointsAlreadySorted.Count;
            if (numSourcePoints == NumPointsToStore)
            {
                StoredPoints = sourcePointsAlreadySorted.ToArray();
                return;
            }
            StoredPoints = new double[NumPointsToStore];
            if ((numSourcePoints - 1) % (NumPointsToStore - 1) == 0)
            {
                int skipLength = (numSourcePoints - 1) / (NumPointsToStore - 1);
                int currentSourcePoint = 0;
                for (int p = 0; p < NumPointsToStore; p++)
                {
                    StoredPoints[p] = sourcePointsAlreadySorted[currentSourcePoint];
                    currentSourcePoint += skipLength;
                }
            }
            else
            {
                double skipLength = ((double) (numSourcePoints - 1)) / (double) (NumPointsToStore - 1);
                double currentSourcePoint = 0;
                for (int p = 0; p < NumPointsToStore; p++)
                {
                    StoredPoints[p] = sourcePointsAlreadySorted[(int) Math.Round(currentSourcePoint)];
                    currentSourcePoint += skipLength;
                }
            }
            ConstantValue = StoredPoints.All(x => x == StoredPoints[0]);
        }

        #region Not currently used in ACESim

        public double GetProbabilityOfSignalThatWouldProduceEstimateOrHigherValue(double correctValue, double estimateWithProxy, double standardDeviation)
        {
            double signalProducingEstimate = GetSignalThatWouldProduceEstimate(correctValue, estimateWithProxy, standardDeviation);
            double probabilityOfSignalAtLeastThisHigh = 1.0 - alglib.normaldistr.normaldistribution(signalProducingEstimate / standardDeviation);
            return probabilityOfSignalAtLeastThisHigh;
        }

        public double GetSignalThatWouldProduceEstimate(double correctValue, double estimateWithProxy, double standardDeviation)
        {
            double proxy = GetProxyCorrespondingToEstimate(estimateWithProxy, standardDeviation);
            double signal = proxy - correctValue;
            return signal;
        }

        public double GetProxyCorrespondingToEstimate(double estimateWithProxy, double standardDeviation)
        {
            if (IsRoughlySymmetricFrom0To1 && estimateWithProxy == 0.5)
                return 0.5;
            // Note that the proxy that might produce the estimate could be beyond the range of the cumulative distribution, so we'll allow some room on either end.
            double cumDistrMin = this.StoredPoints[0];
            double cumDistrMax = this.StoredPoints[this.StoredPoints.Count() - 1];
            double cumDistrDistanceDividedBy5 = (cumDistrMax - cumDistrMin) / 5.0;
            double beginningOfProxyRangeToSearch = cumDistrMin - cumDistrDistanceDividedBy5;
            double endOfProxyRangeToSearch = cumDistrMax + cumDistrDistanceDividedBy5;
            double returnVal = FindOptimalPoint.OptimizeByNarrowingRanges(beginningOfProxyRangeToSearch, endOfProxyRangeToSearch, 0.02, valueToTry =>
                {
                    ValueFromSignalEstimator est = new ValueFromSignalEstimator(this);
                    est.AddSignal(new SignalOfValue() { Signal = valueToTry, StandardDeviationOfErrorTerm = standardDeviation });
                    est.UpdateSummaryStatistics();
                    return Math.Abs(est.ExpectedValueOrProbability(false) - estimateWithProxy);
                }, false, 4, 4);
            return returnVal;
        }

        public int GetDensityAroundSpecificIndexInCumulativeDistribution(int index, double distanceForMeasuringDensity)
        {
            int density = 1;
            int currentIndex = index - 1;
            while (currentIndex >= 0 && StoredPoints[index] - StoredPoints[currentIndex] < distanceForMeasuringDensity)
            {
                density++;
                currentIndex--;
            }
            currentIndex = index + 1;
            while (currentIndex < StoredPoints.Length && StoredPoints[currentIndex] - StoredPoints[index] < distanceForMeasuringDensity)
            {
                density++;
                currentIndex++;
            }
            return density;
        }

        #endregion

        // A cumulative distribution is stored as a double[1001] representing the different points of the continuous distribution. The first array element represents the lowest input. We then calculate numInputs/1000, so that the second array element is numInputs / 1000 (or if this is decimal, we interpolate between two inputs), the third is 2 * numInputs / 1000, and the last is numInputs. We must also add a routine for when we are calculating the cumulative distribution. In this case, we take a number from 0 to 1, map that to 0 to 1000, and interpolate if necessary. [We can try values other than 1001 as well; perhaps 101 would be so much faster that it would be worthwhile. We will use numPointsToRepresentCumulativeDistribution]

    }

    public static class GetUniformCumulativeDistribution
    {
        private static CumulativeDistribution UniformDistribution;
        private static object lockObj = new object();

        public static CumulativeDistribution Get()
        {
            if (UniformDistribution == null)
            {
                lock (lockObj)
                {
                    if (UniformDistribution == null)
                    {                        
                        List<double> trulyUniformCD = Enumerable.Range(0, 101).Select(x => (double)x / 101.0).ToList();
                        UniformDistribution = new CumulativeDistribution(101, trulyUniformCD);
                    }
                }
            }
            return UniformDistribution;
        }


    }
}
