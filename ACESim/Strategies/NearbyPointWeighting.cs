using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using ACESim.Util;
using System.Collections.Concurrent;

namespace ACESim
{

    // This class is used to avoid problems that we otherwise wouldhave with overflow errors as a result of very large exponents. Instead of storing the weighting factors as doubles, we store
    // the exponents of the weighting factors, where the weighting factor = globalWeightingFactor^ExponentForGlobalWeightingFactor * localWeightingFactor^ExponentForLocalWeightingFactor.
    // These weighting factors can then be converted to relative weights before a weighted average is calculated.
    public class WeightingFactor
    {
        public double ExponentForGlobalWeightingFactor;
        public double ExponentForLocalWeightingFactor;

        public double GetRelativeWeightOfAnother(double globalWeightingFactor, double localWeightingFactor, WeightingFactor weightingFactor)
        {
            double returnVal = Math.Pow(globalWeightingFactor, weightingFactor.ExponentForGlobalWeightingFactor - ExponentForGlobalWeightingFactor) * Math.Pow(localWeightingFactor, weightingFactor.ExponentForLocalWeightingFactor);
            if (returnVal > 1E+50 || double.IsPositiveInfinity(returnVal))
                returnVal = 1E+50;
            if (returnVal < 1E-50)
                returnVal = 1E-50;
            return returnVal;
        }
    }

    // The weighting scheme indicates how we should weight neighbors near a particular point to produce a smoothed value.
    [Serializable]
    public class WeightScheme
    {
        public int MaxDimension, NumDifferentExponentsBasedOnDistance;
        public double[,] DistanceInDimensionCoefficients;
        public const double MinMultiplier = 0.5, MaxMultiplier = 1.5; // we multiple our tentative values by a random number in this range to test different possible coefficients
        public double GlobalWeightingFactorAdjustment {get; set;}
        public double LocalWeightingFactorAdjustment = 1.0;
        public double AvgValueOfUnweightedOutputs = 0.0;
        public double StdevOfUnweightedOutputs = 1.0;
        public object AdditionalInformation = null;

        public WeightScheme DeepCopy()
        {
            int dim0 = DistanceInDimensionCoefficients.GetLength(0);
            int dim1 = DistanceInDimensionCoefficients.GetLength(1);
            double[,] distanceInDimensionCoefficientsCopy = new double[dim0, dim1];
            for (int d0 = 0; d0 < dim0; d0++)
                for (int d1 = 0; d1 < dim1; d1++)
                    distanceInDimensionCoefficientsCopy[d0, d1] = DistanceInDimensionCoefficients[d0, d1];

            return new WeightScheme()
            {
                MaxDimension = MaxDimension,
                NumDifferentExponentsBasedOnDistance = NumDifferentExponentsBasedOnDistance,
                DistanceInDimensionCoefficients = distanceInDimensionCoefficientsCopy,
                GlobalWeightingFactorAdjustment = GlobalWeightingFactorAdjustment,
                LocalWeightingFactorAdjustment = LocalWeightingFactorAdjustment,
                AvgValueOfUnweightedOutputs = AvgValueOfUnweightedOutputs,
                StdevOfUnweightedOutputs = StdevOfUnweightedOutputs,
                AdditionalInformation = (object) ((double[])AdditionalInformation).ToArray()
            };
        }

        public WeightScheme()
        {
        }

        public WeightScheme(int maxDimension, int numDifferentExponentsBasedOnDistance, double[] unweightedData)
        {
            // Uncomment to simplify the model
            if (numDifferentExponentsBasedOnDistance > 1)
                numDifferentExponentsBasedOnDistance = 1;
            MaxDimension = maxDimension;
            NumDifferentExponentsBasedOnDistance = numDifferentExponentsBasedOnDistance;
            DistanceInDimensionCoefficients = new double[MaxDimension, NumDifferentExponentsBasedOnDistance];
            StatCollector sc = new StatCollector();
            foreach (double d in unweightedData)
                sc.Add(d);
            AvgValueOfUnweightedOutputs = sc.Average();
            StdevOfUnweightedOutputs = sc.StandardDeviation();
        }

        public void SetCoefficientsFromList(List<double> coefficientsList, bool endTestingMode = false)
        {
            int i = 0;
            for (int dimension = 0; dimension < MaxDimension; dimension++)
                for (int distanceExponent = 1; distanceExponent <= NumDifferentExponentsBasedOnDistance; distanceExponent++)
                {
                    if (double.IsNaN(coefficientsList[i]))
                        throw new Exception("Not a number error.");
                    DistanceInDimensionCoefficients[dimension, distanceExponent - 1] = coefficientsList[i];
                    i++;
                }
        }

        public WeightingFactor CalculateWeightingFactor(List<double> normalizedInputsForPoint, List<double> normalizedInputsForNeighbor, double localWeightingFactor)
        {
            double total = EstimateErrorFromNeighborEstimate(normalizedInputsForPoint, normalizedInputsForNeighbor);

            double normalizedTotal = (total - AvgValueOfUnweightedOutputs) / StdevOfUnweightedOutputs;

            if (double.IsNaN(total))
                throw new Exception("Not a number error.");

            // If GlobalWeightingFactorAdjustment == 1, then all neighbors will receive same weight. As it rises above 1, there will be greater weight on closer neighbors.
            // If localWeightingFactor == 0, then there should be no change to GlobalWeightingFactorAdjustment for this particular weighting factor. 
            // If localWeightingFactor is positive, then GlobalWeightingFactor should go up (i.e., greater weight on closer neighbors)
            // If localWeightingFactor is negative, then GlobalWeightingFactor should go down (i.e., more even weight on all neighbors)
            // If LocalWeightingFactorAdjustment is 1, then the localWeightingFactor has no effect. As it gets greater than zero, it starts to have an effect.

            return new WeightingFactor() { ExponentForLocalWeightingFactor = localWeightingFactor * (0 - normalizedTotal), ExponentForGlobalWeightingFactor = 0 - normalizedTotal };
        }

        private double OverallDistance(List<double> normalizedInputsForPoint, List<double> normalizedInputsForNeighbor)
        {
            double total = 0;
            for (int i = 0; i < normalizedInputsForPoint.Count; i++)
                total += (normalizedInputsForPoint[i] - normalizedInputsForNeighbor[i]) * (normalizedInputsForPoint[i] - normalizedInputsForNeighbor[i]);
            return Math.Sqrt(total);
        }

        public double EstimateErrorFromNeighborEstimate(List<double> normalizedInputsForPoint, List<double> normalizedInputsForNeighbor)
        {
            double total = 0.0;
            //Multiplies each coefficient by the correct value and calculates the sum.
            for (int dimension = 0; dimension < MaxDimension; dimension++)
                for (int distanceExponent = 1; distanceExponent <= NumDifferentExponentsBasedOnDistance; distanceExponent++)
                {
                    // the following is designed to save a function call to Math.Abs -- this code needs to be highly optimized
                    double distance = (normalizedInputsForNeighbor[dimension] > normalizedInputsForPoint[dimension]) ? normalizedInputsForNeighbor[dimension] - normalizedInputsForPoint[dimension] : normalizedInputsForPoint[dimension] - normalizedInputsForNeighbor[dimension];
                    double distanceToExponent = 1.0;
                    for (int i = 0; i < distanceExponent; i++)
                        distanceToExponent *= distance;
                    double addToTotal = DistanceInDimensionCoefficients[dimension, distanceExponent - 1] * distanceToExponent;
                    total += addToTotal;
                }
            return total;
        }

        public double[] GetValuesToMultiplyCoefficientsBy(List<double> normalizedInputsForPoint, List<double> normalizedInputsForNeighbor)
        {
            double[] values = new double[MaxDimension * NumDifferentExponentsBasedOnDistance];
            int index = 0; 
            for (int dimension = 0; dimension < MaxDimension; dimension++)
                for (int distanceExponent = 1; distanceExponent <= NumDifferentExponentsBasedOnDistance; distanceExponent++)
                {
                    double distance = Math.Abs(normalizedInputsForNeighbor[dimension] - normalizedInputsForPoint[dimension]);
                    double distanceToExponent = 1.0;
                    for (int i = 0; i < distanceExponent; i++)
                        distanceToExponent *= distance;
                    values[index] = distanceToExponent;
                    index++;
                }
            //double totalDistance = OverallDistance(normalizedInputsForPoint, normalizedInputsForNeighbor);
            return values;
        }

    }

    public class WeightSchemeApplication
    {
        int NumNeighbors;
        double[] DerivativeAtPoint;
        double[] UnweightedEstimateAtPoint;
        double[] ValueAtNeighbor;
        double DegreeOfBiasMeasure;
        List<double> NormalizedDecisionInputsForPoint;
        List<double>[] NormalizedDecisionInputsForNeighbor;
        int[,] NeighborIndex;
        double[][] DerivativeAtNeighbor;
        WeightScheme WeightingScheme;
        public WeightSchemeApplication(int numNeighbors, Func<int, double> valueAtNeighborFunc, Func<int, List<double>> normalizedDecisionInputsForNeighborFunc, Func<int /* neighbor */, int /* dimension */, double> derivativeAtNeighborFunc, List<double> normalizedDecisionInputsForPoint, Func<int, double> derivativeAtPointFunc, WeightScheme weightingScheme)
        {
            NumNeighbors = numNeighbors;
            WeightingScheme = weightingScheme;
            NormalizedDecisionInputsForPoint = normalizedDecisionInputsForPoint;
            ValueAtNeighbor = new double[NumNeighbors];
            double[] proxyForBias = new double[NumNeighbors];
            DerivativeAtNeighbor = new double[NumNeighbors][];
            NormalizedDecisionInputsForNeighbor = new List<double>[NumNeighbors];
            for (int n = 0; n < NumNeighbors; n++)
            {
                NormalizedDecisionInputsForNeighbor[n] = normalizedDecisionInputsForNeighborFunc(n);
                DerivativeAtNeighbor[n] = new double[WeightingScheme.MaxDimension];
                for (int d = 0; d < WeightingScheme.MaxDimension; d++)
                    DerivativeAtNeighbor[n][d] = derivativeAtNeighborFunc(n, d);
                ValueAtNeighbor[n] = valueAtNeighborFunc(n);
                proxyForBias[n] = GetEstTotalErrorForNeighbor(n);
            }
            //ValueAtNeighbor = DebiasUsingRegression.Debias(ValueAtNeighbor, proxyForBias);
            DerivativeAtPoint = new double[WeightingScheme.MaxDimension];
            for (int d = 0; d < WeightingScheme.MaxDimension; d++)
                DerivativeAtPoint[d] = derivativeAtPointFunc(d);
            double magnitudeOfBias = DebiasUsingRegression.CalculateMagnitudeOfBias(ValueAtNeighbor, proxyForBias);
            double[] avgAndStdev = (double[]) WeightingScheme.AdditionalInformation;
            DegreeOfBiasMeasure = (magnitudeOfBias - avgAndStdev[0]) / avgAndStdev[1]; // normalize the data
        }

        public double GetEstimate()
        {
            WeightedAverageCalculator weightAvg = new WeightedAverageCalculator();
            WeightingFactor referenceWeightingFactor = null;
            for (int n = 0; n < NumNeighbors; n++)
            {
                if (n == 0)
                    referenceWeightingFactor = GetWeightingFactorForNeighbor(0, DegreeOfBiasMeasure);
                double estimateBasedOnNeighbor = CalculateEstimateBasedOnNeighbor(n);
                double weightForNeighbor = (n == 0) ? 1.0 : GetWeightForNeighbor(n, DegreeOfBiasMeasure, referenceWeightingFactor);
                weightAvg.Add(estimateBasedOnNeighbor, weightForNeighbor);
            }
            return weightAvg.Calculate();
        }

        internal double GetEstTotalErrorForNeighbor(int neighborNum)
        {
            //List<double> absoluteValueOfDifferenceInDerivative = new List<double>();
            //for (int d = 0; d < WeightingScheme.MaxDimension; d++)
            //{
            //    double derivativeAtPoint = DerivativeAtPoint[d];
            //    double derivativeAtNeighbor = DerivativeAtNeighbor[neighborNum][d];
            //    absoluteValueOfDifferenceInDerivative.Add(Math.Abs(derivativeAtPoint - derivativeAtNeighbor));
            //}
            return WeightingScheme.EstimateErrorFromNeighborEstimate(NormalizedDecisionInputsForPoint, NormalizedDecisionInputsForNeighbor[neighborNum]);
        }

        internal double GetWeightForNeighbor(int neighborNum, double localWeightingFactorAdjustment, WeightingFactor referenceWeightingFactor)
        {
            //List<double> absoluteValueOfDifferenceInDerivative = new List<double>();
            //for (int d = 0; d < WeightingScheme.MaxDimension; d++)
            //{
            //    double derivativeAtPoint = DerivativeAtPoint[d];
            //    double derivativeAtNeighbor = DerivativeAtNeighbor[neighborNum][d];
            //    absoluteValueOfDifferenceInDerivative.Add(Math.Abs(derivativeAtPoint - derivativeAtNeighbor));
            //}
            WeightingFactor weightingFactor = GetWeightingFactorForNeighbor(neighborNum, localWeightingFactorAdjustment);
            return referenceWeightingFactor.GetRelativeWeightOfAnother(WeightingScheme.GlobalWeightingFactorAdjustment, WeightingScheme.LocalWeightingFactorAdjustment, weightingFactor);
        }

        private WeightingFactor GetWeightingFactorForNeighbor(int neighborNum, double localWeightingFactorAdjustment)
        {
            WeightingFactor weightingFactor = WeightingScheme.CalculateWeightingFactor(NormalizedDecisionInputsForPoint, NormalizedDecisionInputsForNeighbor[neighborNum], localWeightingFactorAdjustment);
            return weightingFactor;
        }

        private double CalculateEstimateBasedOnNeighbor(int n)
        {
            double valueAtPoint = ValueAtNeighbor[n];
            for (int dim = 0; dim < WeightingScheme.MaxDimension; dim++)
            {
                valueAtPoint += (NormalizedDecisionInputsForPoint[dim] - NormalizedDecisionInputsForNeighbor[n][dim]) * DerivativeAtNeighbor[n][dim];
            }
            return valueAtPoint;
        }
    }

    public class WeightSchemeOptimization
    {
        int NumPoints;
        int NumNeighborsPerPoint;
        bool[] PointIsEligibleToBeWeighted;
        double[] UnweightedEstimateAtPoint;
        double[,] EstimateBasedOnNeighbor;
        double[] DegreeOfBiasMeasure;
        List<double>[] NormalizedDecisionInputs;
        int[,] NeighborIndex;
        double[,] DerivativeAtPoint;
        WeightScheme WeightingScheme;
        bool ParallelismEnabled;

        public WeightSchemeOptimization(int numPoints, int numNeighborsPerPoint, Func<int, bool> pointIsEligibleToBeWeightedFunc, Func<int, double> unweightedEstimateAtPointFunc, Func<int /* (point number) */, int /* (neighbor number) */, double> estimateBasedOnNeighborFunc, Func<int /* (point number) */, int /* (neighbor number) */, int> neighborIndexFunc, Func<int, List<double>> normalizedDecisionInputsFunc, Func<int /* point */, int /* dimension */, double> derivativeAtPointFunc, WeightScheme weightingScheme, bool parallelismEnabled)
        {
            NumPoints = numPoints;
            NumNeighborsPerPoint = numNeighborsPerPoint;
            WeightingScheme = weightingScheme; 
            if (unweightedEstimateAtPointFunc != null)
                UnweightedEstimateAtPoint = new double[NumPoints];
            PointIsEligibleToBeWeighted = new bool[NumPoints];
            EstimateBasedOnNeighbor = new double[NumPoints, NumNeighborsPerPoint];
            NeighborIndex = new int[NumPoints, NumNeighborsPerPoint];
            if (derivativeAtPointFunc != null)
                DerivativeAtPoint = new double[NumPoints, WeightingScheme.MaxDimension];
            NormalizedDecisionInputs = new List<double>[NumPoints];
            ParallelismEnabled = parallelismEnabled;
            CompleteInitialization(pointIsEligibleToBeWeightedFunc, unweightedEstimateAtPointFunc, estimateBasedOnNeighborFunc, neighborIndexFunc, normalizedDecisionInputsFunc, derivativeAtPointFunc);
        }

        private void CompleteInitialization(Func<int, bool> pointIsEligibleToBeWeightedFunc, Func<int, double> unweightedEstimateAtPointFunc, Func<int /* (point number) */, int /* (neighbor number) */, double> estimateBasedOnNeighborFunc, Func<int /* (point number) */, int /* (neighbor number) */, int> neighborIndexFunc, Func<int, List<double>> normalizedDecisionInputsFunc, Func<int /* point */, int /* dimension */, double> derivativeAtPointFunc)
        {
            Parallelizer.Go(ParallelismEnabled, 0, NumPoints, p =>
                InitializePoint(pointIsEligibleToBeWeightedFunc, unweightedEstimateAtPointFunc, estimateBasedOnNeighborFunc, neighborIndexFunc, normalizedDecisionInputsFunc, derivativeAtPointFunc, p)
            );
        }

        private void InitializePoint(Func<int, bool> pointIsEligibleToBeWeightedFunc, Func<int, double> unweightedEstimateAtPointFunc, Func<int /* (point number) */, int /* (neighbor number) */, double> estimateBasedOnNeighborFunc, Func<int /* (point number) */, int /* (neighbor number) */, int> neighborIndexFunc, Func<int, List<double>> normalizedDecisionInputsFunc, Func<int /* point */, int /* dimension */, double> derivativeAtPointFunc, int p)
        {
            PointIsEligibleToBeWeighted[p] = pointIsEligibleToBeWeightedFunc(p);
            if (PointIsEligibleToBeWeighted[p])
            {
                if (unweightedEstimateAtPointFunc != null)
                    UnweightedEstimateAtPoint[p] = unweightedEstimateAtPointFunc(p);
                NormalizedDecisionInputs[p] = normalizedDecisionInputsFunc(p);
                if (derivativeAtPointFunc != null)
                    for (int d = 0; d < WeightingScheme.MaxDimension; d++)
                        DerivativeAtPoint[p, d] = derivativeAtPointFunc(p, d);
                for (int n = 0; n < NumNeighborsPerPoint; n++)
                {
                    EstimateBasedOnNeighbor[p, n] = estimateBasedOnNeighborFunc(p, n);
                    NeighborIndex[p, n] = neighborIndexFunc(p, n);
                }
            }
        }

        public double[] GetWeightedEstimates(bool countPointItself = true, int? pointToReport = null, List<double> inputAvgsInSmoothingSet = null, List<double> inputStdevsInSmoothingSet = null)
        {
            double[] weightedEstimates = new double[NumPoints];
            Parallelizer.Go(ParallelismEnabled, 0, NumPoints, p => 
                AssignWeightedEstimateForPoint(countPointItself, pointToReport, inputAvgsInSmoothingSet, inputStdevsInSmoothingSet, weightedEstimates, p)
            );
            return weightedEstimates;
        }

        private void AssignWeightedEstimateForPoint(bool countPointItself, int? pointToReport, List<double> inputAvgsInSmoothingSet, List<double> inputStdevsInSmoothingSet, double[] weightedEstimates, int p)
        {
            if (PointIsEligibleToBeWeighted[p])
            { // otherwise, we'll record a 0.0 value, but that won't be used for anything
                WeightingFactor referenceWeightingFactor = GetWeightingFactorForNeighbor(p, 0, DegreeOfBiasMeasure[p]);
                bool reportOnPoint = pointToReport == p;
                WeightedAverageCalculator weightAvg = new WeightedAverageCalculator();
                if (countPointItself)
                { // when doing the smoothing, we want to count the point itself -- but we don't want to when we're just testing to see how good a particular weighting scheme is, because then the weighting schemes that put the most emphasis on the first one will always win out
                    double weightForSelf = GetWeightForNeighbor(p, -1, 0, referenceWeightingFactor);
                    weightAvg.Add(UnweightedEstimateAtPoint[p], weightForSelf);
                    if (reportOnPoint)
                        TabbedText.WriteLine("Self Estimate {0} Weight {1} NormLocation {2} Distance {3}", Math.Round(UnweightedEstimateAtPoint[p], 5), Math.Round(weightForSelf, 5), String.Join(",", NormalizedDecisionInputs[p]), String.Join(",", NormalizedDecisionInputs[p].Select((item, index) => Math.Abs(item - NormalizedDecisionInputs[p][index]))));
                }
                double total = 0;
                double totalTop20 = 0;
                double neighbor0 = 0;
                for (int n = 0; n < NumNeighborsPerPoint; n++)
                {
                    double estimateBasedOnNeighbor = EstimateBasedOnNeighbor[p, n];
                    double weightForNeighbor = GetWeightForNeighbor(p, n, DegreeOfBiasMeasure[p], referenceWeightingFactor);
                    if (reportOnPoint)
                    {
                        if (n == 0)
                            neighbor0 = weightForNeighbor;
                        total += weightForNeighbor;
                        if (n < 20)
                            totalTop20 += weightForNeighbor;
                        string location = String.Join(",", NormalizedDecisionInputs[NeighborIndex[p, n]].Select((item, index) => Math.Round(item * inputStdevsInSmoothingSet[index] + inputAvgsInSmoothingSet[index], 5)));
                        string distance = String.Join(",", NormalizedDecisionInputs[NeighborIndex[p, n]].Select((item, index) => Math.Round(Math.Abs(item - NormalizedDecisionInputs[p][index]), 5)));
                        TabbedText.WriteLine("Neighbor {0} Estimate {1} UnadjErr {2} Weight {3} Pct {4} Location {5} DerivDiff {6} ",
                            n,
                            Math.Round(estimateBasedOnNeighbor, 5),
                            Math.Round(WeightingScheme.EstimateErrorFromNeighborEstimate(NormalizedDecisionInputs[p], NormalizedDecisionInputs[NeighborIndex[p, n]]), 5),
                            Math.Round(weightForNeighbor, 5),
                            Math.Round(weightForNeighbor / neighbor0, 5),
                            location,
                            DerivativeAtPoint == null ? 0.0 : Math.Abs(DerivativeAtPoint[NeighborIndex[p, n], 0] - DerivativeAtPoint[p, 0])
                            );
                    }
                    weightAvg.Add(estimateBasedOnNeighbor, weightForNeighbor);
                }
                if (reportOnPoint)
                    TabbedText.WriteLine("Weighted average: " + weightAvg.Calculate() + " % from top 20: " + (totalTop20 / total));
                weightedEstimates[p] = weightAvg.Calculate();
                if (double.IsNaN(weightedEstimates[p]))
                    throw new Exception("Not a number error");
            }
        }

        public double GetTotalEstimatedErrorForNeighbor(int pointNum, int neighborNum)
        {
            int neighborIndex = pointNum;
            if (neighborNum >= 0)
                neighborIndex = NeighborIndex[pointNum, neighborNum];
            return WeightingScheme.EstimateErrorFromNeighborEstimate(NormalizedDecisionInputs[pointNum], NormalizedDecisionInputs[neighborIndex]);
        }

        public double GetWeightForNeighbor(int pointNum, int neighborNum, double localWeightingFactor, WeightingFactor referenceWeightingFactor)
        {
            WeightingFactor weightingFactor = GetWeightingFactorForNeighbor(pointNum, neighborNum, localWeightingFactor);
            return referenceWeightingFactor.GetRelativeWeightOfAnother(WeightingScheme.GlobalWeightingFactorAdjustment, WeightingScheme.LocalWeightingFactorAdjustment, weightingFactor);
        }

        private WeightingFactor GetWeightingFactorForNeighbor(int pointNum, int neighborNum, double localWeightingFactor)
        {
            int neighborIndex = pointNum;
            if (neighborNum >= 0)
                neighborIndex = NeighborIndex[pointNum, neighborNum];
            WeightingFactor weightingFactor = WeightingScheme.CalculateWeightingFactor(NormalizedDecisionInputs[pointNum], NormalizedDecisionInputs[neighborIndex], localWeightingFactor);
            return weightingFactor;
        }

        public class TestingPoints
        {
            public int pointNum;
            public int neighborOne;
            public int neighborTwo;

            public TestingPoints()
            {
            }

            public TestingPoints(int numPoints, int numNeighbors)
            {
            //    pointNum = RandomGenerator.Next(pointNum);
            //    neighborOne = RandomGenerator.Next(numNeighbors);
            //    neighborTwo = RandomGenerator.Next(numNeighbors);
                //if (numNeighbors > 20)
                //    numNeighbors = 20;
                pointNum = (int) RandomGenerator.NextLogarithmic(numPoints, 10.0);
                neighborOne = (int)RandomGenerator.NextLogarithmic(numNeighbors, 10.0);
                neighborTwo = (int)RandomGenerator.NextLogarithmic(numNeighbors, 10.0);
            }
        }

        internal List<TestingPoints> testingPointsList;

        internal void RandomizeTestingPoints(int numTestingPoints)
        {
            testingPointsList = new List<TestingPoints>();
            for (int p = 0; p < numTestingPoints; p++)
            {
                TestingPoints tp;
                do
                {
                    tp = new TestingPoints(NumPoints, NumNeighborsPerPoint);
                } while (!PointIsEligibleToBeWeighted[tp.pointNum]);
                testingPointsList.Add(tp);
            }
        }

        public double EstimateErrorOfWeightingSchemeFromTestingPoints()
        {
            double total = 0.0;
            foreach (var testingPoint in testingPointsList)
            {
                int neighborIndex1 = NeighborIndex[testingPoint.pointNum, testingPoint.neighborOne];
                int neighborIndex2 = NeighborIndex[testingPoint.pointNum, testingPoint.neighborTwo];
                List<double> absoluteValueOfDifferenceInDerivative1 = new List<double>();
                List<double> absoluteValueOfDifferenceInDerivative2 = new List<double>();
                //if (DerivativeAtPoint != null)
                //    for (int d = 0; d < WeightingScheme.MaxDimension; d++)
                //    {
                //        absoluteValueOfDifferenceInDerivative1.Add(Math.Abs(DerivativeAtPoint[testingPoint.pointNum, d] - DerivativeAtPoint[neighborIndex1, d]));
                //        absoluteValueOfDifferenceInDerivative2.Add(Math.Abs(DerivativeAtPoint[testingPoint.pointNum, d] - DerivativeAtPoint[neighborIndex2, d]));
                //    }
                WeightingFactor weightingFactor1f = WeightingScheme.CalculateWeightingFactor(NormalizedDecisionInputs[testingPoint.pointNum], NormalizedDecisionInputs[neighborIndex1], DegreeOfBiasMeasure[testingPoint.pointNum]);
                WeightingFactor weightingFactor2f = WeightingScheme.CalculateWeightingFactor(NormalizedDecisionInputs[testingPoint.pointNum], NormalizedDecisionInputs[neighborIndex2], DegreeOfBiasMeasure[testingPoint.pointNum]);
                double weightingFactor1 = 1.0;
                double weightingFactor2 = weightingFactor1f.GetRelativeWeightOfAnother(WeightingScheme.GlobalWeightingFactorAdjustment, WeightingScheme.LocalWeightingFactorAdjustment, weightingFactor1f);
                double estimateBasedOnNeighbor1 = EstimateBasedOnNeighbor[testingPoint.pointNum, testingPoint.neighborOne];
                double estimateBasedOnNeighbor2 = EstimateBasedOnNeighbor[testingPoint.pointNum, testingPoint.neighborTwo];
                double estimateBasedOnTwoNeighbors = (estimateBasedOnNeighbor1 * weightingFactor1 + estimateBasedOnNeighbor2 * weightingFactor2) / (weightingFactor1 + weightingFactor2);
                double unweightedEstimateAtPoint = UnweightedEstimateAtPoint[testingPoint.pointNum];
                total += Math.Abs(estimateBasedOnTwoNeighbors - unweightedEstimateAtPoint);
            }
            return total / testingPointsList.Count;
        }


        private double EstimateErrorOfWeightingSchemeWithSpecifiedGlobalFactorAdjustment(double exponentValue, bool useTestingPoints)
        {
            WeightingScheme.GlobalWeightingFactorAdjustment = exponentValue;
            double returnVal = useTestingPoints ? EstimateErrorOfWeightingSchemeFromTestingPoints() : EstimateErrorOfWeightingScheme();
            //TabbedText.WriteLine("Estimated error for exponent " + exponentValue + ": " + returnVal);
            return returnVal;
        }

        private double EstimateErrorOfWeightingSchemeWithSpecifiedLocalWeightingFactorAdjustment(double exponentValue, bool useTestingPoints)
        {
            WeightingScheme.LocalWeightingFactorAdjustment = exponentValue;
            double returnVal = useTestingPoints ? EstimateErrorOfWeightingSchemeFromTestingPoints() : EstimateErrorOfWeightingScheme();
            //TabbedText.WriteLine("Estimated error for exponent " + exponentValue + ": " + returnVal);
            return returnVal;
        }

        private double EstimateErrorOfWeightingSchemeWithSpecifiedGlobalFactorAdjustmentFromTestingPoints(double exponentValue)
        {
            WeightingScheme.GlobalWeightingFactorAdjustment = exponentValue;
            double returnVal = EstimateErrorOfWeightingSchemeFromTestingPoints();
            //TabbedText.WriteLine("Estimated error for exponent " + exponentValue + ": " + returnVal);
            return returnVal;
        }

        private double EstimateErrorOfWeightingSchemeFromTestingPoints(List<double> coefficientsSoFar, int coefficient, double coefficientValue)
        {
            coefficientsSoFar[coefficient] = coefficientValue;
            WeightingScheme.SetCoefficientsFromList(coefficientsSoFar);
            return EstimateErrorOfWeightingSchemeFromTestingPoints();
        }

        private double EstimateErrorOfWeightingScheme(List<double> coefficientsSoFar, int coefficient, double coefficientValue)
        {
            coefficientsSoFar[coefficient] = coefficientValue;
            WeightingScheme.SetCoefficientsFromList(coefficientsSoFar);
            double returnVal = EstimateErrorOfWeightingScheme();
            TabbedText.WriteLine("Coefficient: " + coefficientValue + " error: " + returnVal);
            return returnVal;
        }

        private double EstimateErrorOfWeightingScheme()
        {
            double[] weightedEstimates = GetWeightedEstimates(false);
            return weightedEstimates.Zip(UnweightedEstimateAtPoint, (first, second) => Math.Abs(first - second)).Average(); // note that ineligible points will count as 0 in the average
        }


        public void OptimizeWeightingScheme( int numObsInOptimizeWeightingRegression, int numPointsPerWeightingRegressionObs)
        {
            double[] optimizedCoefficients = OptimizeWeightingSchemeCoefficients(numObsInOptimizeWeightingRegression);

            OptimizeWeightingSchemeWeightingFactor(numPointsPerWeightingRegressionObs);
        }


        private double[] OptimizeWeightingSchemeCoefficients(int numObsInOptimizeWeightingRegression)
        {
            ConcurrentBag<LinearRegressionObservation> obsBag = new ConcurrentBag<LinearRegressionObservation>();
            Parallelizer.Go(ParallelismEnabled, 0, numObsInOptimizeWeightingRegression, ws =>
                AddObservationForOptimizeWeightingSchemeCoefficients(obsBag)
            );
            bool success;
            double[] optimizedCoefficients = null;
            //ProfileSimple.Start("DoLinearRegression");
            LinearRegressionSimple.DoLinearRegression(obsBag.ToList(), out optimizedCoefficients, out success, false);
            //ProfileSimple.End("DoLinearRegression");
            if (!success)
                throw new Exception("Optimization regression failed.");
            WeightingScheme.SetCoefficientsFromList(optimizedCoefficients.ToList());
            return optimizedCoefficients;
        }

        private void AddObservationForOptimizeWeightingSchemeCoefficients(ConcurrentBag<LinearRegressionObservation> obsBag)
        {
            TestingPoints tp;
            do
            {
                tp = new TestingPoints(NumPoints, NumNeighborsPerPoint);
            }
            while (!PointIsEligibleToBeWeighted[tp.pointNum]);
            // We are figuring out how far off our estimates of a point are likely to be based on distance and the difference in the absolute value of the
            // derivative at the neighbor and at the point.
            List<double> normalizedInputsForPoint, normalizedInputsForNeighbor1, absoluteValueOfDifferenceInDerivativeForNeighbor1 = new List<double>(), absoluteValueOfDifferenceInDerivativeForNeighbor2 = new List<double>();
            normalizedInputsForPoint = NormalizedDecisionInputs[tp.pointNum];
            int neighborIndex1 = NeighborIndex[tp.pointNum, tp.neighborOne];
            normalizedInputsForNeighbor1 = NormalizedDecisionInputs[neighborIndex1];
            //if (DerivativeAtPoint != null)
            //    for (int d = 0; d < WeightingScheme.MaxDimension; d++)
            //    {
            //        double derivativeAtPoint = DerivativeAtPoint[tp.pointNum, d];
            //        absoluteValueOfDifferenceInDerivativeForNeighbor1.Add(Math.Abs(derivativeAtPoint - DerivativeAtPoint[neighborIndex1, d]));
            //    }
            double[] valuesToMultiplyCoefficientsByNeighbor1 = WeightingScheme.GetValuesToMultiplyCoefficientsBy(normalizedInputsForPoint, normalizedInputsForNeighbor1);

            List<double> independentVars = valuesToMultiplyCoefficientsByNeighbor1.ToList();

            double dependentVar = Math.Abs(UnweightedEstimateAtPoint[tp.pointNum] - UnweightedEstimateAtPoint[neighborIndex1]);

            LinearRegressionObservation singleObs = new LinearRegressionObservation() { DependentVariable = dependentVar, IndependentVariables = independentVars, Weight = 1 };
            obsBag.Add(singleObs);
        }


        private void OptimizeWeightingSchemeWeightingFactor(int numPointsPerWeightingRegressionObs)
        {
            // This relies on the coefficients already being set.
            CalculateDegreeOfBiasMeasure();

            RandomizeTestingPoints(numPointsPerWeightingRegressionObs);
            WeightingScheme.LocalWeightingFactorAdjustment = 0; // start with this
            //TabbedText.WriteLine("Error with very high exponent: " + EstimateErrorOfWeightingSchemeWithSpecifiedGlobalFactorAdjustment(99999999999.0));
            const double exponentOfErrors = 1.0; //use 2.0 to minimize squared errors.
            const bool useTestingPoints = false; // a faster algorithm that samples points to estimate the error of the weighitng scheme
            WeightingScheme.LocalWeightingFactorAdjustment = 1.0; // start with no local weighting factor adjustment
            double optimum = FindOptimalValueGreaterThanZero.Maximize(x => Math.Pow(0 - EstimateErrorOfWeightingSchemeWithSpecifiedGlobalFactorAdjustment(x, useTestingPoints), exponentOfErrors), 5.0, 3.0, 0.1);
            WeightingScheme.GlobalWeightingFactorAdjustment = optimum;
            double optimum2 = FindOptimalValueGreaterThanZero.Maximize(x => Math.Pow(0 - EstimateErrorOfWeightingSchemeWithSpecifiedLocalWeightingFactorAdjustment(1.0 + x, useTestingPoints), exponentOfErrors), 0.5, 1.5, 0.1);
            WeightingScheme.LocalWeightingFactorAdjustment = 1.0 + optimum2;
            TabbedText.WriteLine("GlobalWeightingFactorAdjustment " + WeightingScheme.GlobalWeightingFactorAdjustment + " LocalWeightingFactorAdjustment " + WeightingScheme.LocalWeightingFactorAdjustment);
        }

        private void CalculateDegreeOfBiasMeasure()
        {
            double[] magnitudeOfBias = new double[NumPoints];
            Parallelizer.Go(ParallelismEnabled, 0, NumPoints, p =>
                SetMagnitudeOfBiasForPoint(magnitudeOfBias, p)
                );
            double avg, stdev;
            DegreeOfBiasMeasure = NormalizeData.Normalize(magnitudeOfBias, out avg, out stdev, x => PointIsEligibleToBeWeighted[x]);
            WeightingScheme.AdditionalInformation = new double[] { avg, stdev };
        }

        private void SetMagnitudeOfBiasForPoint(double[] magnitudeOfBias, int p)
        {
            if (PointIsEligibleToBeWeighted[p])
            {
                double[] proxyForBias = new double[NumNeighborsPerPoint];
                double[] biasedData = new double[NumNeighborsPerPoint];
                double[] debiasedData = new double[NumNeighborsPerPoint];
                for (int n = 0; n < NumNeighborsPerPoint; n++)
                {
                    biasedData[n] = EstimateBasedOnNeighbor[p, n];
                    proxyForBias[n] = GetTotalEstimatedErrorForNeighbor(p, n);
                }
                magnitudeOfBias[p] = DebiasUsingRegression.CalculateMagnitudeOfBias(biasedData, proxyForBias);
            }
        }

    }

    //public static class CorrelationWithIndex
    //{
    //    public static double Calculate(double[] valuesArray)
    //    {
    //        double[] indices = valuesArray.Select((item, index) => (double)index).ToArray();
    //        return Math.Abs(alglib.correlation.pearsoncorrelation(ref valuesArray, ref indices, valuesArray.Length));
    //    }
    //}


}
