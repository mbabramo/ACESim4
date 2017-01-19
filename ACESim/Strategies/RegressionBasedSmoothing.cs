using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using ACESim.Util;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PriorityQueue;

namespace ACESim
{
    [Serializable]
    public class RegressionBasedSmoothing : SmootherWithRegularGridForApproximateNearestNeighbor
    {

        internal WeightScheme weightingScheme; // these are used to help determine how much weight to put on the value of each neighbor of a point in producing a weighted average
        internal double weightToPlaceOnFirstDerivatives = 0.0;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal Dictionary<string, double[]> smoothingStorage;

        public RegressionBasedSmoothing()
        {
        }

        public override IStrategyComponent DeepCopy()
        {
            RegressionBasedSmoothing copy = new RegressionBasedSmoothing()
            {
            };
            SetCopyFields(copy);
            return copy;
        }

        public override void SetCopyFields(IStrategyComponent copy)
        {
            RegressionBasedSmoothing copyCast = (RegressionBasedSmoothing)copy;
            copyCast.weightingScheme = weightingScheme.DeepCopy();
            copyCast.weightToPlaceOnFirstDerivatives = weightToPlaceOnFirstDerivatives;
            copyCast.smoothingStorage = null;
            base.SetCopyFields(copyCast);
        }

        public RegressionBasedSmoothing(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name) : base(overallStrategy, dimensions, evolutionSettings, decision, name)
        {
        }


        internal RegressionBasedSmoothingOptions GetSmoothingOptions()
        {
            return (RegressionBasedSmoothingOptions)(EvolutionSettings.SmoothingOptions);
        }

        public override int GetRegularGridPoints()
        {
            return GetSmoothingOptions().RegularGridPoints;
        }

        internal override void PreSmoothingStepsForMainSetAndValidationSet(out bool stop)
        {
            stop = false;
            nearestNeighborInSmoothingSetBasedOnRegularGridPointNum = null;
            base.PreSmoothingStepsForMainSetAndValidationSet(out stop);
        }

        internal override void SmoothingSteps()
        {
            ReportSmoothingInfo();
            SmoothRepeatedly();
            if (((RegressionBasedSmoothingOptions)EvolutionSettings.SmoothingOptions).CreateRegularGridAfterRegressionBasedSmoothing)
                SetUpRegularGrid();
            else
                nearestNeighborInSmoothingSetBasedOnRegularGridPointNum = null;
        }

        internal override int CountSubstepsFromSmoothingItself()
        {
            return ((RegressionBasedSmoothingOptions)EvolutionSettings.SmoothingOptions).SmoothingRepetitions * DerivativeBasedSmoothingRecursiveSmoothingCount(new List<int>());
        }


        // The general approach to smoothing here is to store the initial pre-smoothing values, then calculate first derivatives, and then calculate second derivatives (up to the maximum derivative). Note that when calculating second derivatives, we are calculating mixed derivatives (e.g., we might calculate the derivative of (the first derivative with respect to dimension three) with respect to dimension four). We then smooth the second derivatives (using a procedure that looks only to nearby second derivatives), and then the first derivatives (by taking into account the second derivatives) and the ultimate values (by taking into account the first derivatives).
        // To store the presmothing values, we use the following dictionary, where the string indicates the derivatives (if any). So "3,4" would b the derivative of (the first derivative with respect to dimension three) with respect to dimension four, and "" would be the ultimate values. 


        // Load the smoothing values for a particular set of derivative indices.
        private double[] GetSmoothingValues(List<int> derivativeIndices, bool restoreOriginalData)
        {
            double[] result = null;
            if (smoothingStorage == null)
                smoothingStorage = new Dictionary<string, double[]>();
            string theString = (derivativeIndices == null || !derivativeIndices.Any()) ? "" : String.Join(",", derivativeIndices.Select(x => x.ToString()));
            if (smoothingStorage.ContainsKey(theString))
                result = smoothingStorage[theString];
            else
            {
                result = new double[SmoothingPoints()];
                smoothingStorage.Add(theString, result);
            }
            if (restoreOriginalData)
            { // We need to restore the ORIGINAL values from before any smoothing has taken place when we are about to smooth the original data (the last stage of the recursive derivative-based smoothing process). Note that when calculating derivatives, we want to use the updated data from the past smooth. But we don't want the errors from poorer derivative estimates on the previous smooth to affect this smooth. And we don't need successive smooths, because our process of taking into account many nearby neighbors should effectively do that anyway.
                if (derivativeIndices.Any())
                    throw new Exception("Can't restore original data for derivative-based data. Only saving original non-derivative based data.");
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, SmoothingSetPointInfos.Count, v =>
                {
                    result[v] = savedOriginalValues[v];
                });
            }
            return result;
        }

        // Load the smoothing values for a particular set of derivative indices, plus each of the next derivatives.
        private void GetSmoothingValues(List<int> derivativeIndices, out List<double[]> higherDimensionData, out double[] thisDimensionData, bool restoreOriginalData = false)
        {
            higherDimensionData = null;
            if (derivativeIndices.Count < GetSmoothingOptions().MaximumDerivativeOrder)
            {
                higherDimensionData = new List<double[]>();
                for (int dim = 0; dim < Dimensions; dim++)
                {
                    List<int> newDerivativeIndices = derivativeIndices.Concat(new List<int> { dim }).ToList();
                    double[] higherDimensionDataForThisDimension = GetSmoothingValues(newDerivativeIndices, false);
                    higherDimensionData.Add(higherDimensionDataForThisDimension);
                }
            }
            thisDimensionData = GetSmoothingValues(derivativeIndices, restoreOriginalData);
        }

        private void CopySmoothingStorageToPostSmoothingValueAndClearSmoothingStorage()
        {
            List<int> emptyList = new List<int>();
            List<double[]> firstDerivatives;
            double[] mainValues;
            GetSmoothingValues(emptyList, out firstDerivatives, out mainValues);
            int smoothingPoints = SmoothingPoints();
            for (int s = 0; s < smoothingPoints; s++)
            {
                SmoothingSetPointInfo pointInfo = SmoothingSetPointInfos[s];
                pointInfo.postSmoothingValue = mainValues[s];
                pointInfo.derivatives = null;
                if (GetSmoothingOptions().MaximumDerivativeOrder > 0)
                {
                    pointInfo.derivatives = new double[Dimensions];
                    for (int dim = 0; dim < Dimensions; dim++)
                    {
                        pointInfo.derivatives[dim] = firstDerivatives[dim][s];
                    }
                }
            }
            smoothingStorage = null;
        }


        private void SmoothRepeatedly()
        {
            SaveOriginalValues();
            CopyPreSmoothedValuesToPostSmoothed();
            for (int i = 0; i < GetSmoothingOptions().SmoothingRepetitions; i++)
            {
                TabbedText.WriteLine("Smoothing repetition " + (i + 1).ToString() + " of " + GetSmoothingOptions().SmoothingRepetitions + "(" + Name + ")");
                bool firstPhase = i < GetSmoothingOptions().SmoothingRestoreOriginalPointsForFirstNRepetitions;
                if (i != 0 && !firstPhase)
                    ReplacePresmoothingValuesWithInterpolatedValues();
                bool prepareForInterpolationAfterward = i + 1 >= GetSmoothingOptions().SmoothingRestoreOriginalPointsForFirstNRepetitions;
                if (EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet)
                {
                    double newScore = ScoreStrategyBasedOnValidationSet();
                    TabbedText.WriteLine("Average absolute error based on validation set (which itself is noisy data): " + newScore);
                }
                DerivativeBasedSmoothing(firstPhase, prepareForInterpolationAfterward);
                CopySmoothingStorageToPostSmoothingValueAndClearSmoothingStorage();
                CopyPostSmoothedValuesToPreSmoothed();
            }
        }

        private void DerivativeBasedSmoothing(bool restoreOriginalPointsBeforeSmooth, bool prepareForInterpolationAfterward)
        {
            // First, fill in the presmoothing values, which will be used in the recursive derivative based smoothing algorithm.
            double[] smoothingStorage = GetSmoothingValues(null, false);
            for (int s = 0; s < SmoothingPoints(); s++)
                smoothingStorage[s] = SmoothingSetPointInfos[s].preSmoothingValue;
            // Do the smoothing using a recursive algorithm.
            DerivativeBasedSmoothingRecursive(new List<int>(), restoreOriginalPointsBeforeSmooth);
            // Calculate the final coefficients to use when interpolating points anywhere on the surface, given the final smoothed data.
            if (prepareForInterpolationAfterward)
                SmoothPointsConsideringDerivativesIfAvailable(new List<int>(), true);
        }

        private void DerivativeBasedSmoothingRecursive(List<int> derivativeIndices, bool restoreOriginalPointsBeforeSmooth)
        {
            TabbedText.Tabs++;
            int derivative = derivativeIndices.Count;
            if (derivative < GetSmoothingOptions().MaximumDerivativeOrder)
            {
                TabbedText.Tabs++;
                SmoothDerivativesBasedOnPoints(derivativeIndices); // calculate the next derivative with respect to each dimension, based on lower-order values (e.g., the original data for calculating the first derivative
                TabbedText.Tabs--;
                for (int dim = 0; dim < Dimensions; dim++)
                {
                    List<int> newDerivativeIndices = derivativeIndices.ToList();
                    newDerivativeIndices.Add(dim);
                    DerivativeBasedSmoothingRecursive(newDerivativeIndices, restoreOriginalPointsBeforeSmooth); // call this recursively
                    // now that next derivatives are set, we can use those derivative values to calculate the smoothed value here.
                }
                SmoothPointsConsideringDerivativesIfAvailable(derivativeIndices, neverRestoreOriginalData: !restoreOriginalPointsBeforeSmooth);
            }
            else
            {
                SmoothPointsConsideringDerivativesIfAvailable(derivativeIndices, neverRestoreOriginalData: !restoreOriginalPointsBeforeSmooth);
            }
            TabbedText.Tabs--;
        }

        // Returns the number of smoothing subsets in the derivative based smoothing algorithm.
        private int DerivativeBasedSmoothingRecursiveSmoothingCount(List<int> derivativeIndices)
        {
            int numSteps = 0;
            int derivative = derivativeIndices.Count;
            if (derivative < GetSmoothingOptions().MaximumDerivativeOrder)
            {
                numSteps++;
                for (int dim = 0; dim < Dimensions; dim++)
                {
                    List<int> newDerivativeIndices = derivativeIndices.ToList();
                    newDerivativeIndices.Add(dim);
                    numSteps += DerivativeBasedSmoothingRecursiveSmoothingCount(newDerivativeIndices); // call this recursively
                    // now that next derivatives are set, we can use those derivative values to calculate the smoothed value here.
                }
                numSteps++;
            }
            else
            {
                numSteps++;
            }
            return numSteps;
        }

        private void SmoothPointsConsideringDerivativesIfAvailable(List<int> derivativeIndices, bool optimizingWeightingSchemeButNotResmoothing = false, bool neverRestoreOriginalData = false)
        {
            if (optimizingWeightingSchemeButNotResmoothing)
                TabbedText.WriteLine("Preparing smoothed data for interpolation");

            // For each point-neighbor combination, get an estimate of the true value at the point based on the value of the neighbor and the associated derivatives.
            List<double[]> higherDerivativeData;
            double[] thisLevelData;
            // We restore the original data when we are smoothing the original data rather than a derivative, but not if this is the final call to this routine, which is
            // used solely for the purpose of determining the coefficients to use when extrapolating between the smoothing points. The coefficients will be different
            // in that scenario, because the data will be smoothed (and so there will be less reliance on more distant data).
            // We also do not restore if neverStoreOriginalData is true, which is useful when we wish to do repeated smoothings of the ultimate data (which can lead to oversmoothing).
            bool restoreOriginalData = !derivativeIndices.Any() && !optimizingWeightingSchemeButNotResmoothing && !neverRestoreOriginalData;
            GetSmoothingValues(derivativeIndices, out higherDerivativeData, out thisLevelData, restoreOriginalData);
            bool consideringHigherOrderDerivatives = higherDerivativeData != null && higherDerivativeData.Any();
            if (derivativeIndices == null || !derivativeIndices.Any())
                TabbedText.WriteLine("Calculating smoothing value by weighing points" + (consideringHigherOrderDerivatives ? ", considering first-order derivatives." : "."));
            else
                TabbedText.WriteLine("Calculating smoothed value of derivatives: " + String.Join(",", derivativeIndices.ToArray()) + (consideringHigherOrderDerivatives ? ", using higher-order derivatives." : "."));
            if (restoreOriginalData)
                TabbedText.WriteLine("Restoring original data, so that refined derivatives can be used against that data");

            double weightToPlaceOnDerivativeBasedEstimate = DetermineHowMuchToWeighDerivativeBasedEstimate(higherDerivativeData, thisLevelData);
            if (!derivativeIndices.Any())
            {
                weightToPlaceOnFirstDerivatives = weightToPlaceOnDerivativeBasedEstimate;
            }
            if (consideringHigherOrderDerivatives)
                TabbedText.WriteLine("Weight on derivative based estimate: " + weightToPlaceOnDerivativeBasedEstimate);

            if (weightingScheme == null || weightingScheme.MaxDimension != Dimensions || weightingScheme.NumDifferentExponentsBasedOnDistance != 3)
                weightingScheme = new WeightScheme(Dimensions, 3, thisLevelData);
            WeightSchemeOptimization wsCalc = new WeightSchemeOptimization(
                numPoints: SmoothingPoints(),
                numNeighborsPerPoint: GetSmoothingOptions().NearbyNeighborsToWeighInSmoothing,
                pointIsEligibleToBeWeightedFunc: (int p) => SmoothingSetPointInfos[p].eligible,
                unweightedEstimateAtPointFunc: (int p) => thisLevelData[p],
                estimateBasedOnNeighborFunc: (int p, int n) => CalculateEstimateBasedOnNeighbor(higherDerivativeData, thisLevelData, weightToPlaceOnDerivativeBasedEstimate, p, n),
                neighborIndexFunc: (int p, int n) => SmoothingSetPointInfos[p].nearestNeighbors[n],
                normalizedDecisionInputsFunc: (int p) => SmoothingSetPointInfos[p].decisionInputsNormalized,
                derivativeAtPointFunc: higherDerivativeData == null ? (Func<int, int, double>)null : (int p, int d) => higherDerivativeData[d][p],
                weightingScheme: weightingScheme,
                parallelismEnabled: EvolutionSettings.ParallelOptimization
            );
            wsCalc.OptimizeWeightingScheme(GetSmoothingOptions().NumObsInOptimizeWeightingRegression, GetSmoothingOptions().NumPointsPerWeightingRegressionObs);
            if (optimizingWeightingSchemeButNotResmoothing)
                return;

            // The following is useful to track the weighting of a particular point.
            int? reportOnPoint = null;
            //if (derivativeIndices == null || !derivativeIndices.Any())
            //{
            //    NormalizedPoint normalizedPoint = new NormalizedPoint(new List<double>() {0.02, 0.0}, inputAveragesInSmoothingSet, inputStdevsInSmoothingSet, -1);
            //    List<Point> nearestNeighbors = storageForSmoothingSet.GetKNearestNeighbors(normalizedPoint, true, 1);
            //    reportOnPoint = ((NormalizedPoint)nearestNeighbors[0]).AssociatedIndex;
            //}

            double[] outputData;
            outputData = wsCalc.GetWeightedEstimates(true, reportOnPoint, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet);

            for (int s = 0; s < SmoothingPoints(); s++)
                thisLevelData[s] = outputData[s];
            SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
        }

        private double CalculateEstimateBasedOnNeighbor(List<double[]> higherDerivativeData, double[] thisLevelData, double weightToPlaceOnDerivativeBasedEstimate, int s, int n)
        {
            SmoothingSetPointInfo point = SmoothingSetPointInfos[s];
            int neighborIndex = point.nearestNeighbors[n];
            SmoothingSetPointInfo neighbor = SmoothingSetPointInfos[neighborIndex];
            double[] derivativesAtPoint = null;
            double[] derivativesAtNeighbor = null;
            if (higherDerivativeData != null)
            {
                derivativesAtNeighbor = new double[Dimensions];
                derivativesAtPoint = new double[Dimensions];
                for (int dim = 0; dim < Dimensions; dim++)
                {
                    derivativesAtNeighbor[dim] = higherDerivativeData[dim][neighborIndex];
                    derivativesAtPoint[dim] = higherDerivativeData[dim][s];
                }
            }
            double estimate = CalculateEstimateBasedOnNeighbor(point.decisionInputsNormalized, neighbor.decisionInputsNormalized, thisLevelData[neighborIndex], derivativesAtNeighbor, derivativesAtPoint, weightToPlaceOnDerivativeBasedEstimate);
            return estimate;
        }

        private double CalculateEstimateBasedOnNeighbor(List<double> locationOfPoint, List<double> locationOfNeighbor, double valueAtNeighbor, double[] derivativesAtNeighbor, double[] derivativesAtPoint, double weightToPlaceOnDerivativeBasedEstimate)
        {
            if (derivativesAtNeighbor == null)
                return valueAtNeighbor;
            double valueAtPoint = valueAtNeighbor;
            const int numRangesPerDimension = 20;
            // We will assume a constant next higher order derivative and slowly move this order derivative from the values at derivativesAtNeighbor to the values at derivativesAtPoint.
            for (int dim = 0; dim < Dimensions; dim++)
            {
                double sizeOfPointRange = (locationOfPoint[dim] - locationOfNeighbor[dim]) / numRangesPerDimension;
                double sizeOfDerivativeRange = (derivativesAtNeighbor[dim] - derivativesAtPoint[dim]) / numRangesPerDimension;
                double derivativeValue = derivativesAtNeighbor[dim];
                for (int step = 0; step < numRangesPerDimension; step++)
                { // at step 0, we use the derivative at the neighbor, and we get progressively closer to the value of the derivative at the point itself
                    valueAtPoint += sizeOfPointRange * derivativeValue;
                    derivativeValue -= sizeOfDerivativeRange;
                }
            }
            return valueAtPoint * weightToPlaceOnDerivativeBasedEstimate + valueAtNeighbor * (1.0 - weightToPlaceOnDerivativeBasedEstimate);
        }

        private double DetermineHowMuchToWeighDerivativeBasedEstimate(List<double[]> higherDerivativeData, double[] thisLevelData)
        {
            if (higherDerivativeData == null)
                return 0.0;
            List<LinearRegressionObservation> obs = new List<LinearRegressionObservation>();
            for (int o = 0; o < Math.Min(10000, GetSmoothingOptions().NumObsInOptimizeWeightingRegression); o++)
            {
                int s;
                do
                {
                    s = RandomGenerator.Next(SmoothingPoints());
                }
                while (!SmoothingSetPointInfos[s].eligible);
                SmoothingSetPointInfo pointInfo = SmoothingSetPointInfos[s];
                int n1 = RandomGenerator.Next(GetSmoothingOptions().NearbyNeighborsToWeighInSmoothing);
                int neighborIndex1 = pointInfo.nearestNeighbors[n1];
                SmoothingSetPointInfo neighbor1 = SmoothingSetPointInfos[neighborIndex1];

                double noDerivativeSimpleEstimate = thisLevelData[neighborIndex1];
                double[] derivativesAtNeighbor = new double[Dimensions];
                double[] derivativesAtPoint = new double[Dimensions];
                for (int dim = 0; dim < Dimensions; dim++)
                {
                    derivativesAtNeighbor[dim] = higherDerivativeData[dim][neighborIndex1];
                    derivativesAtPoint[dim] = higherDerivativeData[dim][s];
                }
                double derivativeBasedEstimate = CalculateEstimateBasedOnNeighbor(pointInfo.decisionInputsNormalized, neighbor1.decisionInputsNormalized, thisLevelData[neighborIndex1], derivativesAtNeighbor, derivativesAtPoint, 1.0);
                obs.Add(new LinearRegressionObservation() { DependentVariable = thisLevelData[s], IndependentVariables = new List<double>() { derivativeBasedEstimate, noDerivativeSimpleEstimate } });
            }
            // rather than run a linear regression, we'll do a simple optimization, since there are only two independent variables
            double optimalValue = Math.Round(FindOptimalPoint.OptimizeByNarrowingRanges(0.0, 1.0, 10, x => obs.Average(y => Math.Abs(y.DependentVariable - (x * y.IndependentVariables[0] + (1.0 - x) * y.IndependentVariables[1]))), false), 3);
            return optimalValue;
        }

        private void SmoothDerivativesBasedOnPoints(List<int> derivativeIndices)
        {
            if (derivativeIndices.Any())
                TabbedText.WriteLine("Estimating derivatives that are next higher order from " + String.Join(",", derivativeIndices.ToArray()));
            else
                TabbedText.WriteLine("Estimating first derivatives ");
            List<double[]> higherDerivativeData;
            double[] thisLevelData;
            GetSmoothingValues(derivativeIndices, out higherDerivativeData, out thisLevelData);

            for (int dim = 0; dim < Dimensions; dim++)
            {
                double[] estimateOfDerivativeBasedOnSingleNeighbor = new double[thisLevelData.Length];
                for (int p = 0; p < thisLevelData.Length; p++)
                    estimateOfDerivativeBasedOnSingleNeighbor[p] = SmoothingSetPointInfos[p].eligible ? GetEstimateOfDerivativeBasedOnNeighbor(dim, p, 0, thisLevelData) : 0;
                WeightScheme derivWeightScheme = new WeightScheme(Dimensions, 3, estimateOfDerivativeBasedOnSingleNeighbor);
                WeightSchemeOptimization wsCalc = new WeightSchemeOptimization(
                    SmoothingPoints(),
                    GetSmoothingOptions().NearbyNeighborsToWeighInSmoothing - 1, // we're going to use the first neighbor to get the unweighted estimate at the point
                    (int p) => SmoothingSetPointInfos[p].eligible,
                    (int p) => estimateOfDerivativeBasedOnSingleNeighbor[p],
                    (int p, int n) => GetEstimateOfDerivativeBasedOnNeighbor(dim, p, n + 1, thisLevelData),
                    (int p, int n) => SmoothingSetPointInfos[p].nearestNeighbors[n + 1],
                    (int p) => SmoothingSetPointInfos[p].decisionInputsNormalized,
                    null, // we're not estimating the next derivative now or using that
                    derivWeightScheme,
                    EvolutionSettings.ParallelOptimization
                );
                wsCalc.OptimizeWeightingScheme(GetSmoothingOptions().NumObsInOptimizeWeightingRegression, GetSmoothingOptions().NumPointsPerWeightingRegressionObs);
                double[] results = wsCalc.GetWeightedEstimates();

                for (int s = 0; s < SmoothingPoints(); s++)
                    higherDerivativeData[dim][s] = results[s];
            }
            SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
        }

        private double GetEstimateOfDerivativeBasedOnNeighbor(int dimension, int s, int n, double[] thisLevelData)
        {
            SmoothingSetPointInfo point = SmoothingSetPointInfos[s];
            int neighborIndex = point.nearestNeighbors[n];
            SmoothingSetPointInfo neighbor = SmoothingSetPointInfos[neighborIndex];
            double valueAtThisPoint = thisLevelData[s]; // must use this instead of point.preSmoothingValue;
            double valueAtNeighbor = thisLevelData[neighborIndex]; // must use this instead of neighbor.preSmoothingValue;
            double distanceAlongThisDimension = neighbor.decisionInputsNormalized[dimension] - point.decisionInputsNormalized[dimension];
            double derivativeEstimate = (valueAtNeighbor - valueAtThisPoint) / distanceAlongThisDimension;
            if (double.IsNaN(derivativeEstimate))
                throw new Exception("Not a number error.");
            return derivativeEstimate;
        }

        private void ReplacePresmoothingValuesWithInterpolatedValues()
        {
            TabbedText.WriteLine("Setting presmoothing values to interpolated values from previous smooth");
            for (int p = 0; p < SmoothingPoints(); p++)
            {
                SmoothingSetPointInfos[p].preSmoothingValue = CalculateOutputForInputs(SmoothingSetPointInfos[p].decisionInputs);
            }
        }

        internal override double CalculateOutputForInputsNotZeroDimensions(List<double> inputs)
        {
            if (IsCurrentlyBeingDeveloped || weightingScheme == null)
                return InterpolateOutputForPointUsingNearestNeighborOnly(inputs);
            if (nearestNeighborInSmoothingSetBasedOnRegularGridPointNum == null)
                return InterpolateOutputForPointExactNearestNeighbors(inputs);
            return InterpolateOutputForPointApproximateNearestNeighbors(inputs); // faster approach using regular grid
        }


        private double InterpolateOutputForPointExactNearestNeighbors(List<double> inputs)
        {
            NormalizedPoint normalizedPoint = new NormalizedPoint(inputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            List<double> normalizedPointLocation = normalizedPoint.GetLocation().ToList();
            List<Point> neighbors = KDTreeForInputs.GetKNearestNeighbors(normalizedPoint, false, GetSmoothingOptions().NearbyNeighborsToWeighInPlayMode);
            WeightSchemeApplication wsCalc = new WeightSchemeApplication(
                GetSmoothingOptions().NearbyNeighborsToWeighInPlayMode,
                (int n) => SmoothingSetPointInfos[((NormalizedPoint)neighbors[n]).AssociatedIndex].postSmoothingValue,
                (int n) => SmoothingSetPointInfos[((NormalizedPoint)neighbors[n]).AssociatedIndex].decisionInputsNormalized,
                (int n, int d) => SmoothingSetPointInfos[((NormalizedPoint)neighbors[n]).AssociatedIndex].derivatives == null ? 0 : SmoothingSetPointInfos[((NormalizedPoint)neighbors[n]).AssociatedIndex].derivatives[d],
                normalizedPoint.GetLocation().ToList(),
                (int d) => SmoothingSetPointInfos[((NormalizedPoint)neighbors[0]).AssociatedIndex].derivatives == null ? 0 : (SmoothingSetPointInfos[((NormalizedPoint)neighbors[0]).AssociatedIndex].derivatives[d] + SmoothingSetPointInfos[((NormalizedPoint)neighbors[1]).AssociatedIndex].derivatives[d] + SmoothingSetPointInfos[((NormalizedPoint)neighbors[2]).AssociatedIndex].derivatives[d]) / 3.0, // we use an average of the derivative of the three nearest neighbor as an approximation -- a slower approach would be to estimate the derivative with respect to each dimension based on nearby points
                weightingScheme);
            double estimate = wsCalc.GetEstimate();
            return estimate;
        }

        private double InterpolateOutputForPointApproximateNearestNeighbors(List<double> inputs)
        {
            List<int> neighbors = GetApproximateSmoothingSetNearestNeighborsForInputs(inputs, GetSmoothingOptions().NearbyNeighborsToWeighInPlayMode);
            WeightSchemeApplication wsCalc = new WeightSchemeApplication(
                GetSmoothingOptions().NearbyNeighborsToWeighInPlayMode,
                (int n) => SmoothingSetPointInfos[neighbors[n]].postSmoothingValue,
                (int n) => SmoothingSetPointInfos[neighbors[n]].decisionInputsNormalized,
                (int n, int d) => SmoothingSetPointInfos[neighbors[n]].derivatives == null ? 0 : SmoothingSetPointInfos[neighbors[n]].derivatives[d],
                GetNormalizedLocation(inputs),
                (int d) => SmoothingSetPointInfos[neighbors[0]].derivatives == null ? 0 : (SmoothingSetPointInfos[neighbors[0]].derivatives[d] + SmoothingSetPointInfos[neighbors[1]].derivatives[d] + SmoothingSetPointInfos[neighbors[2]].derivatives[d]) / 3.0, // we use an average of the derivative of the three nearest neighbor as an approximation -- a slower approach would be to estimate the derivative with respect to each dimension based on nearby points
                weightingScheme);
            double estimate = wsCalc.GetEstimate();
            return estimate;
        }





    }
}
