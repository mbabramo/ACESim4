using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{

    [Serializable]
    public class SignalOfValue
    {
        public double Signal;
        public double StandardDeviationOfErrorTerm;

        public SignalOfValue()
        {
        }

        public SignalOfValue DeepCopy()
        {
            return new SignalOfValue()
            {
                Signal = Signal,
                StandardDeviationOfErrorTerm = StandardDeviationOfErrorTerm
            };
        }
    }

    [Serializable]
    public abstract class ValueFromSignalEstimatorBase
    {
        // TODO: Add Recyclable support. [NOTE: Maybe this project is not worth the trouble now that we have found a better way to do garbage collection.]
        // After creating object for first time, instantiate array (change size if necessary). 
        // Remove constructor overrides. Replace with after creating routine that looks at arguments. Use same routine for after creating and for after recycling. Add ResetWeights to each of them. 
        // Change calls to Zip and ToArray so that we just copy the array and multiply together.
        // When DeepCopy is called, copy arrays
        // List is a further problem. Simplest thing to do is probably just to remove everything from the List. Allocate a default number of slots initially. Add Recyclable support to SignalOfValue.
        // Figure out where we know that we won't need ValueFromSignalEstimator anymore and then dispose of it. 

        public CumulativeDistribution UnderlyingDistribution;

        public List<SignalOfValue> Signals = new List<SignalOfValue>();
        public double ProbabilityValueIsAtLeastZeroPointFive; // this measures the probability that the underlying value really is at least zero point five
        public double ProbabilityOfSpecifiedEvent; // if we're not interested in whether the underlying value is > 0.5, but instead whether something will happen (e.g., a party will win a lawsuit), then this of what we want, but we must set ProbabilityOfSpecifiedEventBasedOnUnderlyingValue
        [FieldwiseComparisonSkip]
        public Func<double[], double[], double> ProbabilityOfSpecifiedEventBasedOnUnderlyingValue; // a function accepting the stored points and weights as parameter, and returning the probability of a specified event given those underlying values (i.e., the function must calculate the probability of the event given each underlying value in the stored points, and then multiply by the weights)

        public double ExpectedValue;
        public double StandardDeviationOfExpectedValue;
        public double ExpectedValueOtherSetOfSignalStrengths;
        public double? PerfectSignal; // if a signal of 0 is received, this resolves the signal.
        public bool SummaryStatisticsUpToDate;
        // public double StandardDeviationOfExpectedValueOtherSetOfSignalStrengths;

        public ValueFromSignalEstimatorBase(CumulativeDistribution underlyingDistribution, Func<double[], double[], double> probabilityOfSpecifiedEventBasedOnUnderlyingValue) : this(underlyingDistribution)
        {
            ProbabilityOfSpecifiedEventBasedOnUnderlyingValue = probabilityOfSpecifiedEventBasedOnUnderlyingValue;
        }

        public ValueFromSignalEstimatorBase(CumulativeDistribution underlyingDistribution)
        {
            if (UnderlyingDistribution != underlyingDistribution)
            {
                UnderlyingDistribution = underlyingDistribution;
                ResetToNoSignals();
            }
        }

        //public ValueFromSignalEstimator(ValueFromSignalEstimator vse1, ValueFromSignalEstimator vse2)
        //{
        //    UnderlyingDistribution = vse1.UnderlyingDistribution;
        //    Weights = vse1.Weights.Zip(vse2.Weights, (x, y) => x * y).ToArray();
        //    Signals = vse1.Signals.Concat(vse2.Signals).ToList();
        //    UpdateSummaryStatistics();
        //}

        public ValueFromSignalEstimatorBase()
        {
        }

        public abstract ValueFromSignalEstimatorBase DeepCopy();

        public double ExpectedValueOrProbability(bool isSignalOfProbability)
        {
            if (!SummaryStatisticsUpToDate)
                throw new Exception("Internal error: Attempted to obtain expected value or probability without updating summary statistics.");
            if (PerfectSignal != null)
            {
                if (isSignalOfProbability && ProbabilityOfSpecifiedEventBasedOnUnderlyingValue != null)
                    return ProbabilityOfSpecifiedEventBasedOnUnderlyingValue(new double[] { (double)PerfectSignal }, new double[] { 1.0 }); // there is only 1 possible value which receives the complete weight
                return (double)PerfectSignal;
            }
            if (isSignalOfProbability)
            {
                if (ProbabilityOfSpecifiedEventBasedOnUnderlyingValue == null)
                    return ProbabilityValueIsAtLeastZeroPointFive;
                else
                    return ProbabilityOfSpecifiedEvent;
            }
            else
                return ExpectedValue;
        }

        public abstract void ResetToNoSignals();

        public void AddSignal(SignalOfValue signal)
        {
            if (signal.StandardDeviationOfErrorTerm >= 1000.0 && UnderlyingDistribution != null && UnderlyingDistribution.IsRoughlySymmetricFrom0To1)
                return; // such a bad signal that we'll ignore it
            Signals.Add(signal);
            if (UnderlyingDistribution == null || UnderlyingDistribution.ConstantValue)
                return;
            ApplySignal(signal);
            SummaryStatisticsUpToDate = false;
        }

        public abstract void ApplySignal(SignalOfValue signal);

        public abstract void UpdateSummaryStatistics();

        public void UpdateUnderlyingDistribution(CumulativeDistribution underlyingDistributionWithSameNumberOfPoints)
        {
            if (UnderlyingDistribution != null && underlyingDistributionWithSameNumberOfPoints == null)
                return;
            if (UnderlyingDistribution != underlyingDistributionWithSameNumberOfPoints) // if it's the same object, then we assume it has not been updated
            {
                bool noUnderlyingDistributionPreviouslySet = UnderlyingDistribution == null;
                UnderlyingDistribution = underlyingDistributionWithSameNumberOfPoints;
                bool anySignals = Signals.Any();
                if (noUnderlyingDistributionPreviouslySet || anySignals)
                    ResetToNoSignals();
                if (anySignals)
                {
                    foreach (var s in Signals)
                    {
                        ApplySignal(s);
                        if (GameProgressLogger.LoggingOn)
                            GameProgressLogger.Log("Applying signal: " + s.Signal + " error: " + s.StandardDeviationOfErrorTerm);
                    }
                    UpdateSummaryStatistics();
                }
            }
        }

    }

    [Serializable]
    public class ValueFromSignalEstimator_UnusedMethod : ValueFromSignalEstimatorBase
    {
        public CumulativeDistribution UpdatedCumulativeDistribution;
        public bool UseUpdatedCumulativeDistribution;
        
        public ValueFromSignalEstimator_UnusedMethod(CumulativeDistribution underlyingDistribution, Func<double[], double[], double> probabilityOfSpecifiedEventBasedOnUnderlyingValue) : base(underlyingDistribution, probabilityOfSpecifiedEventBasedOnUnderlyingValue)
        {
        }

        public ValueFromSignalEstimator_UnusedMethod(CumulativeDistribution underlyingDistribution) : base(underlyingDistribution)
        {
        }

        public ValueFromSignalEstimator_UnusedMethod()
        {
        }

        public override ValueFromSignalEstimatorBase DeepCopy()
        {
            return new ValueFromSignalEstimator_UnusedMethod()
            {
                UnderlyingDistribution = UnderlyingDistribution == null ? null : UnderlyingDistribution.DeepCopy(),
                Signals = Signals == null ? null : Signals.Select(x => x.DeepCopy()).ToList(),
                ExpectedValue = ExpectedValue,
                StandardDeviationOfExpectedValue = StandardDeviationOfExpectedValue,
                ExpectedValueOtherSetOfSignalStrengths = ExpectedValueOtherSetOfSignalStrengths,
                ProbabilityValueIsAtLeastZeroPointFive = ProbabilityValueIsAtLeastZeroPointFive,
                ProbabilityOfSpecifiedEvent = ProbabilityOfSpecifiedEvent,
                ProbabilityOfSpecifiedEventBasedOnUnderlyingValue = ProbabilityOfSpecifiedEventBasedOnUnderlyingValue,
                PerfectSignal = PerfectSignal,
                SummaryStatisticsUpToDate = SummaryStatisticsUpToDate,
                UpdatedCumulativeDistribution = UpdatedCumulativeDistribution,
                UseUpdatedCumulativeDistribution = UseUpdatedCumulativeDistribution
            };
        }

        public override void ResetToNoSignals()
        {
            UseUpdatedCumulativeDistribution = false;
        }

        [ThreadStatic]
        public static double[] SpotsOnDistributionOfSignal;
        [ThreadStatic]
        public static int[] DensityForCorrespondingPoints;
        public override void ApplySignal(SignalOfValue signal)
        {
            bool previousUseUpdatedCumulativeDistribution = UseUpdatedCumulativeDistribution;
            CumulativeDistribution cumulativeDistribution = UseUpdatedCumulativeDistribution ? UpdatedCumulativeDistribution : UnderlyingDistribution;
            int numPointsToStore = UnderlyingDistribution.NumPointsToStore;
            // take different spots along the cumulative normal distribution for this standard deviation
            double[] equallyLikelyDistancesGivenSignal = InverseCumulativeNormalDistributionPoints.GetDistributionPoints(UnderlyingDistribution.NumPointsToStore, signal.StandardDeviationOfErrorTerm);
            if (SpotsOnDistributionOfSignal == null || SpotsOnDistributionOfSignal.Length != UnderlyingDistribution.NumPointsToStore)
            {
                SpotsOnDistributionOfSignal = new double[numPointsToStore];
                DensityForCorrespondingPoints = new int[numPointsToStore];
            }
            const double proportionOfDistanceToCountInMeasuringDensity = 0.05;
            double distanceForMeasuringDensity = proportionOfDistanceToCountInMeasuringDensity * (cumulativeDistribution.StoredPoints[numPointsToStore - 1] - cumulativeDistribution.StoredPoints[0]);
            int indexOfNextItem = 0;
            int totalDensity = 0;
            int density = 0;
            int lastIndex = -1;
            int lastDensity = 0;
            // calculate the density for each spot in the distribution of the signal
            // first, do this by looking at the next greater point, if any.
            bool pastEnd = false;
            for (int i = 0; i < numPointsToStore; i++)
            {
                double spotOnDistributionOfSignal = signal.Signal + equallyLikelyDistancesGivenSignal[i];
                density = 0;
                while (!pastEnd && cumulativeDistribution.StoredPoints[indexOfNextItem] < spotOnDistributionOfSignal)
                {
                    indexOfNextItem++;
                    pastEnd = indexOfNextItem == numPointsToStore;
                }
                if (!pastEnd)
                {
                    if (indexOfNextItem == lastIndex)
                        density = lastDensity;
                    else
                        density = cumulativeDistribution.GetDensityAroundSpecificIndexInCumulativeDistribution(indexOfNextItem, distanceForMeasuringDensity);
                }
                lastDensity = density;
                totalDensity += density;
                SpotsOnDistributionOfSignal[i] = spotOnDistributionOfSignal;
                DensityForCorrespondingPoints[i] = density;
            }
            // now, do this in the other direction
            pastEnd = false;
            density = 0;
            lastIndex = -1;
            lastDensity = 0;
            indexOfNextItem = numPointsToStore - 1;
            for (int i = numPointsToStore - 1; i >= 0; i--)
            {
                double spotOnDistributionOfSignal = SpotsOnDistributionOfSignal[i];
                density = 0;
                while (!pastEnd && cumulativeDistribution.StoredPoints[indexOfNextItem] > spotOnDistributionOfSignal)
                {
                    indexOfNextItem--;
                    pastEnd = indexOfNextItem == 0;
                }
                if (!pastEnd)
                {
                    if (indexOfNextItem == lastIndex)
                        density = lastDensity;
                    else
                        density = cumulativeDistribution.GetDensityAroundSpecificIndexInCumulativeDistribution(indexOfNextItem, distanceForMeasuringDensity);
                }
                lastDensity = density;
                totalDensity += density;
                DensityForCorrespondingPoints[i] += density;
            }
            double portionForEachItemOfDensity = (double) numPointsToStore / (double) totalDensity;
            double sumDensityProcessed = 0;
            int indexProcessed = 0;
            // we're trying to avoid creating a new cumulative distribution object (or any other new memory) to save time
            if (UpdatedCumulativeDistribution == null)
                UpdatedCumulativeDistribution = new CumulativeDistribution(numPointsToStore);
            // now, update the cumulative distribution by checking if the density so far is enough to justify one or more additional items
            for (int i = 0; i < numPointsToStore; i++)
            {
                for (int d = 0; d < DensityForCorrespondingPoints[i]; d++)
                    sumDensityProcessed += portionForEachItemOfDensity;
                while (indexProcessed < sumDensityProcessed && indexProcessed < numPointsToStore)
                {
                    UpdatedCumulativeDistribution.StoredPoints[indexProcessed] = SpotsOnDistributionOfSignal[i];
                    indexProcessed++;
                }
            }
            if (indexProcessed == 0)
            {
                UseUpdatedCumulativeDistribution = previousUseUpdatedCumulativeDistribution;
            }
            else if (indexProcessed < numPointsToStore)
            {
                while (indexProcessed < numPointsToStore)
                {
                    UpdatedCumulativeDistribution.StoredPoints[indexProcessed] = SpotsOnDistributionOfSignal[numPointsToStore - 1];
                    indexProcessed++;
                    UseUpdatedCumulativeDistribution = true;
                }
            }
            else
                UseUpdatedCumulativeDistribution = true;
        }

        public override void UpdateSummaryStatistics()
        {
            bool previousUseUpdatedCumulativeDistribution = UseUpdatedCumulativeDistribution;
            CumulativeDistribution cumulativeDistribution = UseUpdatedCumulativeDistribution ? UpdatedCumulativeDistribution : UnderlyingDistribution;
            SummaryStatisticsUpToDate = true;
            if (cumulativeDistribution == null || PerfectSignal != null)
                return;
            if (cumulativeDistribution.ConstantValue)
            {
                ExpectedValue = UnderlyingDistribution.StoredPoints[0];
                ProbabilityValueIsAtLeastZeroPointFive = ExpectedValue < 0.5 ? 0.0 : 1.0;
                StandardDeviationOfExpectedValue = 0.0;
                return;
            }
            StatCollectorFasterButNotThreadSafe sc = new StatCollectorFasterButNotThreadSafe();
            int weightsLessThanZeroPointFive = 0, weightsGreaterThanOrEqualToThanZeroPointFive = 0;
            for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
            {
                if (cumulativeDistribution.StoredPoints[p] < 0.5)
                    weightsLessThanZeroPointFive++;
                else
                    weightsGreaterThanOrEqualToThanZeroPointFive++;
                sc.Add(cumulativeDistribution.StoredPoints[p]);
            }

            ExpectedValue = sc.Average();
            ProbabilityValueIsAtLeastZeroPointFive = weightsGreaterThanOrEqualToThanZeroPointFive / (weightsLessThanZeroPointFive + weightsGreaterThanOrEqualToThanZeroPointFive);
            if (ProbabilityOfSpecifiedEventBasedOnUnderlyingValue != null)
                ProbabilityOfSpecifiedEvent = ProbabilityOfSpecifiedEventBasedOnUnderlyingValue(UnderlyingDistribution.StoredPoints, null);
            StandardDeviationOfExpectedValue = sc.StandardDeviation();
            if (double.IsNaN(StandardDeviationOfExpectedValue))
                StandardDeviationOfExpectedValue = 0.0; // this should be very rare
        }
    }

    [Serializable]
    public class ValueFromSignalEstimator : ValueFromSignalEstimatorBase
    {
        public double[] Weights;
        // internal double[] WeightsForOtherSetOfSignals;
        internal double[] SumOfWeightsForEachQ;
        internal double[] DistanceInStandardDeviationsApplyingSignal;
        
        
        public ValueFromSignalEstimator(CumulativeDistribution underlyingDistribution, Func<double[], double[], double> probabilityOfSpecifiedEventBasedOnUnderlyingValue) : base(underlyingDistribution, probabilityOfSpecifiedEventBasedOnUnderlyingValue)
        {
        }

        public ValueFromSignalEstimator(CumulativeDistribution underlyingDistribution) : base(underlyingDistribution)
        {
        }

        public ValueFromSignalEstimator()
        {
        }

        //public ValueFromSignalEstimator(ValueFromSignalEstimator vse1, ValueFromSignalEstimator vse2)
        //{
        //    UnderlyingDistribution = vse1.UnderlyingDistribution;
        //    Weights = vse1.Weights.Zip(vse2.Weights, (x, y) => x * y).ToArray();
        //    Signals = vse1.Signals.Concat(vse2.Signals).ToList();
        //    UpdateSummaryStatistics();
        //}


        public override ValueFromSignalEstimatorBase DeepCopy()
        {
            return new ValueFromSignalEstimator()
            {
                UnderlyingDistribution = UnderlyingDistribution, // UnderlyingDistribution == null ? null : UnderlyingDistribution.DeepCopy(), 
                Weights = Weights == null ? null : Weights.ToArray(),
                // WeightsForOtherSetOfSignals = WeightsForOtherSetOfSignals == null ? null : WeightsForOtherSetOfSignals.ToArray(),
                SumOfWeightsForEachQ = SumOfWeightsForEachQ == null ? null : SumOfWeightsForEachQ.ToArray(),
                DistanceInStandardDeviationsApplyingSignal = DistanceInStandardDeviationsApplyingSignal == null ? null : DistanceInStandardDeviationsApplyingSignal.ToArray(),
                Signals = Signals == null ? null : Signals.Select(x => x.DeepCopy()).ToList(),
                ExpectedValue = ExpectedValue,
                StandardDeviationOfExpectedValue = StandardDeviationOfExpectedValue,
                ExpectedValueOtherSetOfSignalStrengths = ExpectedValueOtherSetOfSignalStrengths,
                ProbabilityValueIsAtLeastZeroPointFive = ProbabilityValueIsAtLeastZeroPointFive,
                ProbabilityOfSpecifiedEvent = ProbabilityOfSpecifiedEvent,
                ProbabilityOfSpecifiedEventBasedOnUnderlyingValue = ProbabilityOfSpecifiedEventBasedOnUnderlyingValue,
                PerfectSignal = PerfectSignal,
                SummaryStatisticsUpToDate = SummaryStatisticsUpToDate
            };
        }

        public override void ResetToNoSignals()
        {
            if (Weights == null || Weights.Length != UnderlyingDistribution.NumPointsToStore)
                Weights = new double[UnderlyingDistribution.NumPointsToStore];
            for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                Weights[p] = 1.0;
            SummaryStatisticsUpToDate = false;
        }


        public override void ApplySignal(SignalOfValue signal)
        {
            if (signal.StandardDeviationOfErrorTerm == 0.0)
            {
                if (PerfectSignal != null && PerfectSignal != signal.Signal)
                    throw new Exception("Internal error: Two perfect signals of different values.");
                PerfectSignal = signal.Signal;
                ExpectedValue = signal.Signal;
                StandardDeviationOfExpectedValue = 0;
                return;
            }
            else if (PerfectSignal != null)
                return; // we have a perfect signal -- why pay attention to imperfect signals?
            if (DistanceInStandardDeviationsApplyingSignal == null)
                DistanceInStandardDeviationsApplyingSignal = new double[UnderlyingDistribution.NumPointsToStore];
            double lowestStandardDeviation = 0;
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Signal: " + signal.Signal);
            for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
            {
                DistanceInStandardDeviationsApplyingSignal[p] = Math.Abs((signal.Signal - UnderlyingDistribution.StoredPoints[p]) / signal.StandardDeviationOfErrorTerm);

                //if (GameProgressLogger.LoggingOn)
                //    GameProgressLogger.Log("Point p " + p + " value " + UnderlyingDistribution.StoredPoints[p] + " stdvs " + DistanceInStandardDeviationsApplyingSignal[p]);
                if (p == 0 || DistanceInStandardDeviationsApplyingSignal[p] < lowestStandardDeviation)
                    lowestStandardDeviation = DistanceInStandardDeviationsApplyingSignal[p];
            }
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Lowest stdev " + lowestStandardDeviation);
            if (lowestStandardDeviation < 3)
            { // this is the standard approach to use
                for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                {
                    const int disregardBeyondStandardDeviations = 7;
                    if (DistanceInStandardDeviationsApplyingSignal[p] > disregardBeyondStandardDeviations)
                        Weights[p] = 0; // so far off that we are going to save time by counting it as 0
                    else
                        Weights[p] *= NormalDistributionPDF.GetStandardNormalDistributionPDFApproximate(DistanceInStandardDeviationsApplyingSignal[p]);
                }
            }
            else if (lowestStandardDeviation > 15)
            { // this is so far off that we will likely end up with weights of 0 everywhere using other methods, so instead let's just set the closest weight to 1.0 and all others to 0.0
                for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                    Weights[p] = 0;
                int closest;
                if (signal.Signal > UnderlyingDistribution.StoredPoints[UnderlyingDistribution.NumPointsToStore - 1])
                    closest = UnderlyingDistribution.NumPointsToStore - 1;
                else if (signal.Signal < UnderlyingDistribution.StoredPoints[0])
                    closest = 0;
                else
                    closest = DistanceInStandardDeviationsApplyingSignal.Select((item, index) => new { Item = item, Index = index }).OrderBy(x => x.Item).First().Index;
                Weights[closest] = 1.0;
            }
            else
            { // we need a more precise measure of the standard deviation, though we also may be counting many weights as 0; this may happen when we have a very precise signal
                // A more sophisticated approach (in cases in which we are within the cumulative distribution rather than beyond the tails) would be to interpolate the cumulative distribution
                // to points between the stored points and hone in on those. The advantage would be less error with very accurate signals. 
                for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                {
                    if (DistanceInStandardDeviationsApplyingSignal[p] > lowestStandardDeviation * 4)
                        Weights[p] = 0;
                    else
                        Weights[p] *= NormalDistributionPDF.GetStandardNormalDistributionPDFExact(DistanceInStandardDeviationsApplyingSignal[p]);
                }
            }
        }

        public override void UpdateSummaryStatistics()
        {
            SummaryStatisticsUpToDate = true;
            if (UnderlyingDistribution == null || PerfectSignal != null)
                return;
            if (UnderlyingDistribution.ConstantValue)
            {
                ExpectedValue = UnderlyingDistribution.StoredPoints[0];
                ProbabilityValueIsAtLeastZeroPointFive = ExpectedValue < 0.5 ? 0.0 : 1.0;
                StandardDeviationOfExpectedValue = 0.0;
                return;
            }
            bool keepGoing = true;
            do
            {
                StatCollectorFasterButNotThreadSafe sc = new StatCollectorFasterButNotThreadSafe();
                double weightsLessThanZeroPointFive = 0, weightsGreaterThanOrEqualToThanZeroPointFive = 0;
                for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                {
                    if (UnderlyingDistribution.StoredPoints[p] < 0.5)
                        weightsLessThanZeroPointFive += Weights[p];
                    else if (UnderlyingDistribution.StoredPoints[p] > 0.5)
                        weightsGreaterThanOrEqualToThanZeroPointFive += Weights[p];
                    else
                    { // exact equality case -- must have this in case we are using an exact uniform distribution, otherwise we have biased results
                        double halfWeight = Weights[p] / 2.0;
                        weightsLessThanZeroPointFive += halfWeight;
                        weightsGreaterThanOrEqualToThanZeroPointFive += halfWeight;
                    }
                    if (Weights[p] != 0)
                        sc.Add(UnderlyingDistribution.StoredPoints[p], Weights[p]);
                }
                if (Signals.Count() == 1 && UnderlyingDistribution.IsRoughlySymmetricFrom0To1 && UnderlyingDistribution.IsUniformFrom0To1)
                {
                    ExpectedValue = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(Signals[0].StandardDeviationOfErrorTerm, Signals[0].Signal);
                    ProbabilityValueIsAtLeastZeroPointFive = weightsGreaterThanOrEqualToThanZeroPointFive / (weightsLessThanZeroPointFive + weightsGreaterThanOrEqualToThanZeroPointFive);
                    if (ProbabilityOfSpecifiedEventBasedOnUnderlyingValue != null)
                        ProbabilityOfSpecifiedEvent = ProbabilityOfSpecifiedEventBasedOnUnderlyingValue(new double[] { ExpectedValue }, new double[] { 1.0 });
                }
                else
                {
                    ExpectedValue = sc.Average();
                    ProbabilityValueIsAtLeastZeroPointFive = weightsGreaterThanOrEqualToThanZeroPointFive / (weightsLessThanZeroPointFive + weightsGreaterThanOrEqualToThanZeroPointFive);
                    if (ProbabilityOfSpecifiedEventBasedOnUnderlyingValue != null)
                        ProbabilityOfSpecifiedEvent = ProbabilityOfSpecifiedEventBasedOnUnderlyingValue(UnderlyingDistribution.StoredPoints, Weights);
                }
                if (double.IsNaN(ExpectedValue))
                {
                    if (Weights.Any(x => double.IsNaN(x) || double.IsInfinity(x)))
                    {
                        for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                        {
                            if (double.IsNaN(Weights[p]) || double.IsInfinity(Weights[p]))
                                Weights[p] = 1.0; // this isn't a very good remedy, but this seems to happen only very rarely
                            else
                                Weights[p] = 0.0;
                        }
                    }
                    else
                    {
                        // normalize the weights so that the largest is 1
                        double maxWeight = Weights.Max();
                        for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
                            Weights[p] /= maxWeight;
                    }
                }
                else
                    keepGoing = false;
                StandardDeviationOfExpectedValue = sc.StandardDeviation();
                if (double.IsNaN(StandardDeviationOfExpectedValue))
                    StandardDeviationOfExpectedValue = 0.0; // again, this should be very rare
            }
            while (keepGoing);
        }


        //public void EstimateValuesGivenNoiseButNotValueFromAnotherSetOfSignals(List<SignalOfValue> otherSetOfSignals)
        //{
        //    if (WeightsForOtherSetOfSignals == null)
        //        WeightsForOtherSetOfSignals = new double[UnderlyingDistribution.NumPointsToStore];
        //    for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
        //        WeightsForOtherSetOfSignals[p] = 1.0;
        //    // for each pair of items (p, q) in the underlying distribution, we need to figure out the probability of getting q if the real answer is p. We thus take the weight variable for p, and then multiply it by a weight variable indicating the difference between p and q. We then want to sum all of these products to get an indication of the relative likelihood of q given a signal of a particular size.

        //    foreach (SignalOfValue s in otherSetOfSignals)
        //    {
        //        if (SumOfWeightsForEachQ == null)
        //            SumOfWeightsForEachQ = new double[UnderlyingDistribution.NumPointsToStore];
        //        for (int q = 0; q < UnderlyingDistribution.NumPointsToStore; q++)
        //            SumOfWeightsForEachQ[q] = 0;
        //        const int stepSize = 20; // larger sizes can speed things up at some cost to accuracy
        //        for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
        //        {
        //            for (int q = 0; q < UnderlyingDistribution.NumPointsToStore; q += stepSize)
        //            {
        //                double distance = Math.Abs(UnderlyingDistribution.StoredPoints[p] - UnderlyingDistribution.StoredPoints[q]);
        //                double distanceInStandardDeviations = distance / s.StandardDeviationOfErrorTerm;
        //                double normalDistributionProbabilityDensity =  NormalDistributionPDF.GetStandardNormalDistributionPDFApproximate(distanceInStandardDeviations);
        //                SumOfWeightsForEachQ[q] += Weights[p] * normalDistributionProbabilityDensity;
        //            }
        //        }
        //        for (int q = 0; q < UnderlyingDistribution.NumPointsToStore; q++)
        //        {
        //            WeightsForOtherSetOfSignals[q] *= SumOfWeightsForEachQ[q];
        //        }
        //    }

        //    StatCollectorFasterButNotThreadSafe sc = new StatCollectorFasterButNotThreadSafe();
        //    for (int p = 0; p < UnderlyingDistribution.NumPointsToStore; p++)
        //    {
        //        sc.Add(UnderlyingDistribution.StoredPoints[p], WeightsForOtherSetOfSignals[p]);
        //    }
        //    ExpectedValueOtherSetOfSignalStrengths = sc.Average();
        //    // this isn't right, because we need to figure out the standard deviation for each p and then do the weighted average based on the weights of p; not worth it right now StandardDeviationOfExpectedValueOtherSetOfSignalStrengths = sc.StandardDeviation();
        //}

    }

    public static class ValueFromSignalEstimatorBasedOnSingleSignal
    {
        public static void DoEstimate(CumulativeDistribution cd, double actualValue, double noiseLevel, double bias, bool useProbabilityRatherThanExpectedValue, out double expectedValueOrProbability, double randomSeed = 0.5, Func<double[], double[], double> probabilityOfSpecifiedEventBasedOnUnderlyingValue = null)
        {
            ValueFromSignalEstimator estimator = new ValueFromSignalEstimator(cd, probabilityOfSpecifiedEventBasedOnUnderlyingValue);
             double obfuscation = noiseLevel * (double)alglib.normaldistr.invnormaldistribution(randomSeed);
            double proxy = actualValue + obfuscation + bias;
            SignalOfValue signal = new SignalOfValue() { Signal = proxy, StandardDeviationOfErrorTerm = noiseLevel };
            estimator.AddSignal(signal);
            estimator.UpdateSummaryStatistics();
            if (useProbabilityRatherThanExpectedValue)
                expectedValueOrProbability = estimator.ExpectedValueOrProbability(true);
            else
                expectedValueOrProbability = estimator.ExpectedValueOrProbability(false);
        }
    }

    public static class ValueFromSignalExample
    {
        public static void DoExample()
        {
            List<double> numbersFromUniformDistribution = Enumerable.Range(0, 10001).Select(x => RandomGenerator.NextDouble()).OrderBy(x => x).ToList();

            List<double> trulyUniformCD = Enumerable.Range(1, 10000).Select(x => (double)x / 10001.0).ToList();
            CumulativeDistribution cdUnif = new CumulativeDistribution(101, trulyUniformCD);

            List<double> cdCenteredAt05 = Enumerable.Range(0, 10001).Select(x => RandomGenerator.NextDouble()).OrderBy(x => x).ToList();
            CumulativeDistribution cdSymm = new CumulativeDistribution(101, cdCenteredAt05);
            Debug.WriteLine("Symmetric distribution reported as such: " + cdSymm.IsRoughlySymmetricFrom0To1);
            double probShouldBe05 = cdSymm.GetProbabilityOfSignalThatWouldProduceEstimateOrHigherValue(0.5, 0.5, 0.1);
            Debug.WriteLine("Probability of getting signal of at least 0.5 when probability is 0.5 (should be 0.5): " + probShouldBe05);


            ValueFromSignalEstimator_UnusedMethod estSym_0a = new ValueFromSignalEstimator_UnusedMethod(cdUnif);
            estSym_0a.AddSignal(new SignalOfValue() { Signal = 0.60, StandardDeviationOfErrorTerm = 0.30 });
            estSym_0a.UpdateSummaryStatistics();
            estSym_0a.AddSignal(new SignalOfValue() { Signal = 0.70, StandardDeviationOfErrorTerm = 0.10 });
            estSym_0a.UpdateSummaryStatistics();


            ValueFromSignalEstimator estSym_0 = new ValueFromSignalEstimator(cdUnif);
            estSym_0.AddSignal(new SignalOfValue() { Signal = 0.60, StandardDeviationOfErrorTerm = 0.30 });
            estSym_0.UpdateSummaryStatistics();
            double correctAnswer = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(0.30, 0.60);
            Debug.WriteLine("Signal of 0.60 with stdev of 0.3 ==> " + estSym_0.ExpectedValue + " (correct answer " + correctAnswer + ")");
            estSym_0.AddSignal(new SignalOfValue() { Signal = 0.70, StandardDeviationOfErrorTerm = 0.10 });
            estSym_0.UpdateSummaryStatistics();
            Debug.WriteLine("Adding signal of 0.70 with stdev of 0.1 ==> " + estSym_0.ExpectedValue);
            estSym_0 = new ValueFromSignalEstimator(cdSymm);
            estSym_0.AddSignal(new SignalOfValue() { Signal = 0.70, StandardDeviationOfErrorTerm = 0.10 });
            estSym_0.UpdateSummaryStatistics();
            correctAnswer = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(0.10, 0.70);
            Debug.WriteLine("Starting again, with single signal of 0.70 with stdev of 0.1 ==> " + estSym_0.ExpectedValue + " (correct answer " + correctAnswer + ")");

            Debug.WriteLine("Old method:");
            ValueFromSignalEstimator_UnusedMethod estSym_1 = new ValueFromSignalEstimator_UnusedMethod(cdUnif);
            estSym_1.AddSignal(new SignalOfValue() { Signal = 0.60, StandardDeviationOfErrorTerm = 0.30 });
            estSym_1.UpdateSummaryStatistics();
            correctAnswer = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(0.30, 0.60);
            Debug.WriteLine("Signal of 0.60 with stdev of 0.3 ==> " + estSym_1.ExpectedValue + " (correct answer " + correctAnswer + ")");
            estSym_1.AddSignal(new SignalOfValue() { Signal = 0.70, StandardDeviationOfErrorTerm = 0.10 });
            estSym_1.UpdateSummaryStatistics();
            Debug.WriteLine("Adding signal of 0.70 with stdev of 0.1 ==> " + estSym_1.ExpectedValue);
            estSym_1 = new ValueFromSignalEstimator_UnusedMethod(cdSymm);
            estSym_1.AddSignal(new SignalOfValue() { Signal = 0.70, StandardDeviationOfErrorTerm = 0.10 });
            estSym_1.UpdateSummaryStatistics();
            correctAnswer = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(0.10, 0.70);
            Debug.WriteLine("Starting again, with single signal of 0.70 with stdev of 0.1 ==> " + estSym_1.ExpectedValue + " (correct answer " + correctAnswer + ")");

            Stopwatch s2 = new Stopwatch();
            s2.Start();
            for (int i = 0; i < 10000; i++)
            {
                estSym_0 = new ValueFromSignalEstimator(cdUnif);
                estSym_0.AddSignal(new SignalOfValue() { Signal = 0.60, StandardDeviationOfErrorTerm = 0.30 });
                estSym_0.UpdateSummaryStatistics();
            }
            s2.Stop();
            Debug.WriteLine("Time for current method: " + s2.ElapsedMilliseconds);

            Stopwatch s3 = new Stopwatch();
            s3.Start();
            for (int i = 0; i < 10000; i++)
            {
                estSym_1 = new ValueFromSignalEstimator_UnusedMethod(cdUnif);
                estSym_1.AddSignal(new SignalOfValue() { Signal = 0.60, StandardDeviationOfErrorTerm = 0.30 });
                estSym_1.UpdateSummaryStatistics();
            }
            s3.Stop();
            Debug.WriteLine("Time for unused method: " + s3.ElapsedMilliseconds);

            ValueFromSignalEstimator estSyma = new ValueFromSignalEstimator(cdSymm);
            for (int i = 0; i < 6; i++)
                estSyma.AddSignal(new SignalOfValue() { Signal = 0.50, StandardDeviationOfErrorTerm = 0.30 });
            estSyma.UpdateSummaryStatistics();

            ValueFromSignalEstimator estSymb = new ValueFromSignalEstimator(cdSymm);
            for (int i = 0; i < 7; i++)
                estSymb.AddSignal(new SignalOfValue() { Signal = 0.50, StandardDeviationOfErrorTerm = 0.30 });
            estSymb.UpdateSummaryStatistics();

            ValueFromSignalEstimator estSym = new ValueFromSignalEstimator(cdSymm);
            estSym.AddSignal(new SignalOfValue() { Signal = 0.55, StandardDeviationOfErrorTerm = 0.05 });
            estSym.UpdateSummaryStatistics();

            Debug.WriteLine("");
            ValueFromSignalEstimator estSym2 = new ValueFromSignalEstimator(cdSymm);
            estSym2.AddSignal(new SignalOfValue() { Signal = 0.25, StandardDeviationOfErrorTerm = 0.05 });
            estSym2.UpdateSummaryStatistics(); 
            Debug.WriteLine("Probability greater than 0.5 (should be low): " + estSym2.ExpectedValueOrProbability(true));
            estSym2.AddSignal(new SignalOfValue() { Signal = -0.05, StandardDeviationOfErrorTerm = 0.30 });
            estSym2.UpdateSummaryStatistics();
            Debug.WriteLine("Probability greater than 0.5 (should be low): " + estSym2.ExpectedValueOrProbability(true));

            List<double> cdCenteredAt06 = Enumerable.Range(0, 10001).Select(x => 0.1 + (RandomGenerator.NextDouble() * 0.9)).OrderBy(x => x).ToList();
            CumulativeDistribution cdAsymm = new CumulativeDistribution(101, cdCenteredAt06);
            ValueFromSignalEstimator estAsymm = new ValueFromSignalEstimator(cdAsymm);
            estAsymm.AddSignal(new SignalOfValue() { Signal = 0.3, StandardDeviationOfErrorTerm = 0.1 });
            estAsymm.UpdateSummaryStatistics();
            Stopwatch srev = new Stopwatch();
            srev.Start();
            double proxyProducingSignal = cdAsymm.GetProxyCorrespondingToEstimate(estAsymm.ExpectedValueOrProbability(false), 0.1);
            srev.Stop();
            Debug.WriteLine("Going from expected value to proxy should produce value of 0.3: " + proxyProducingSignal + " milliseconds: " + srev.ElapsedMilliseconds);
            double probabilityOfProxyAtLeastThisHigh = cdAsymm.GetProbabilityOfSignalThatWouldProduceEstimateOrHigherValue(0.7, 0.3, 0.5);
            Debug.WriteLine("Probability of getting signal that would produce estimate of 0.3 or more when correct value is 0.7 when standard deviation is 0.5 (should be considerably greater than 0.50): " + probabilityOfProxyAtLeastThisHigh); 
            probabilityOfProxyAtLeastThisHigh = cdAsymm.GetProbabilityOfSignalThatWouldProduceEstimateOrHigherValue(0.7, 0.3, 0.1);
            Debug.WriteLine("Probability of getting signal that would produce estimate of 0.3 or more when correct value is 0.7 when standard deviation is 0.1 (should be close to 1.0): " + probabilityOfProxyAtLeastThisHigh);
            probabilityOfProxyAtLeastThisHigh = cdAsymm.GetProbabilityOfSignalThatWouldProduceEstimateOrHigherValue(0.7, 0.3, 200.09);
            Debug.WriteLine("Probability of getting signal that would produce estimate of 0.3 or more when correct value is 0.7 when standard deviation is 200.09 (should be close to 0.5): " + probabilityOfProxyAtLeastThisHigh);


            CumulativeDistribution cdWithFewPoints = new CumulativeDistribution(21, numbersFromUniformDistribution);
            ValueFromSignalEstimator esFewPoints = new ValueFromSignalEstimator(cdWithFewPoints);
            const double sampleSignal0 = 0.3234;
            const double sampleSDVeryPrecise = 0.001;
            esFewPoints.AddSignal(new SignalOfValue() { Signal = sampleSignal0, StandardDeviationOfErrorTerm = sampleSDVeryPrecise });
            esFewPoints.UpdateSummaryStatistics();
            Debug.WriteLine("Error with very precise signal using few points: " + (sampleSignal0 - esFewPoints.ExpectedValueOrProbability(false)));

            List<double> compressedDistribution = Enumerable.Range(0, 10001).Select(x => 0.8 + RandomGenerator.NextDouble() / 5.0).OrderBy(x => x).ToList();
            CumulativeDistribution cd = new CumulativeDistribution(1001, numbersFromUniformDistribution);
            CumulativeDistribution cd2 = new CumulativeDistribution(1001, compressedDistribution);
            double sampleSignal = -0.1;
            double sampleSD = 0.8;
            double sampleSignal2 = 1.4;
            double sampleSD2 = 120.0; // Should have only tiny effect of increasing the estimate
            double sampleSignal3 = 0.99;
            double sampleSD3 = 0.1; // Should lead closer to 0.85
            double sampleSignal4 = 0.85;
            double sampleSD4 = 0.01; // Should lead to a very precise estimate near 0.85


            ValueFromSignalEstimator es = new ValueFromSignalEstimator(cd);
            Stopwatch s = new Stopwatch();
            s.Start();
            es.AddSignal(new SignalOfValue() { Signal = sampleSignal, StandardDeviationOfErrorTerm = sampleSD });
            es.UpdateSummaryStatistics();
            s.Stop();
            //double correctAnswer = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(sampleSignal, sampleSD);
            //Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " " + correctAnswer + " calculated in " + s.ElapsedMilliseconds + " milliseconds");
            //es.EstimateValuesGivenNoiseButNotValueFromAnotherSetOfSignals(new List<SignalOfValue> { new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD } });
            //Debug.WriteLine("Other's estimated value " + es.ExpectedValueOtherSetOfSignalStrengths);
            //es.EstimateValuesGivenNoiseButNotValueFromAnotherSetOfSignals(new List<SignalOfValue> { new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD }, new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD2 } });
            //Debug.WriteLine("Other's estimated value " + es.ExpectedValueOtherSetOfSignalStrengths);
            //s = new Stopwatch();
            //s.Start();
            //es.EstimateValuesGivenNoiseButNotValueFromAnotherSetOfSignals(new List<SignalOfValue> { new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD }, new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD } });
            //s.Stop();
            //Debug.WriteLine("Other's estimated value " + es.ExpectedValueOtherSetOfSignalStrengths + " calculated in " + s.ElapsedMilliseconds + " milliseconds");

            Debug.WriteLine("Changing distribution.");
            es.UpdateUnderlyingDistribution(cd2);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");
            Debug.WriteLine("Changing distribution back.");
            es.UpdateUnderlyingDistribution(cd);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");

            Debug.WriteLine("Adding imprecise signal.");
            es.AddSignal(new SignalOfValue() { Signal = sampleSignal2, StandardDeviationOfErrorTerm = sampleSD2 });
            es.UpdateSummaryStatistics();
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue);
            Debug.WriteLine("Changing distribution.");
            es.UpdateUnderlyingDistribution(cd2);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");
            Debug.WriteLine("Changing distribution back.");
            es.UpdateUnderlyingDistribution(cd);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");


            Debug.WriteLine("Adding moderate signal of 0.99.");
            es.AddSignal(new SignalOfValue() { Signal = sampleSignal3, StandardDeviationOfErrorTerm = sampleSD3 });
            es.UpdateSummaryStatistics();
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue);
            Debug.WriteLine("Changing distribution.");
            es.UpdateUnderlyingDistribution(cd2);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");
            Debug.WriteLine("Changing distribution back.");
            es.UpdateUnderlyingDistribution(cd);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");


            Debug.WriteLine("Adding precise signal of 0.85.");
            es.AddSignal(new SignalOfValue() { Signal = sampleSignal4, StandardDeviationOfErrorTerm = sampleSD4 });
            es.UpdateSummaryStatistics();
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue);
            Debug.WriteLine("Changing distribution.");
            es.UpdateUnderlyingDistribution(cd2);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");
            Debug.WriteLine("Changing distribution back.");
            es.UpdateUnderlyingDistribution(cd);
            Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue + " calculated in " + s.ElapsedMilliseconds + " milliseconds");



            //es.EstimateValuesGivenNoiseButNotValueFromAnotherSetOfSignals(new List<SignalOfValue> { new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD }, new SignalOfValue() { Signal = 0, StandardDeviationOfErrorTerm = sampleSD2 } });
            //Debug.WriteLine("Other's estimated value " + es.ExpectedValueOtherSetOfSignalStrengths);
            //Debug.WriteLine(es.ExpectedValueOrProbability(false) + " " + es.StandardDeviationOfExpectedValue);
        }
    }
}
