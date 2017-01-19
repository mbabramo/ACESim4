using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class OversamplingPlan
    {
        public OversamplingPlan Parent;
        public OversamplingPlan Child1;
        public OversamplingPlan Child2;
        public OversamplingPlan Top { get { if (Parent == null) return this; return Parent.Top; } }

        /// <summary>
        /// The proportion of time, in the sample used to generate this node, that the sample was from this node;
        /// </summary>
        public double ActualSampleProportion;
        /// <summary>
        /// The proportion of time, in the sample used to generate the tree, that the sample was in this node.
        /// </summary>
        public double ActualSampleProportionCumulative;
        /// <summary>
        /// The proportion of time, given that we are sampling from the parent, that we want during oversampling to sample from the bounds in this node.
        /// </summary>
        public double SamplingProportion;
        /// <summary>
        /// The proportion of all samples that should come from the bounds in this node.
        /// </summary>
        public double SamplingProportionCumulative;
        /// <summary>
        /// The proportion of time, given that we are sampling from the parent, that we would sample from the bounds in this node without oversampling.
        /// </summary>
        public double EvensamplingProportion;
        /// <summary>
        /// The proportion of all samples that would come from the bounds in this node without oversampling.
        /// </summary>
        public double EvensamplingProportionCumulative;
        /// <summary>
        /// This coefficient, when multiplied by the smallest weight, will equal 1. Thus, the richest area of input seed will have weight 1.
        /// </summary>
        public double WeightScale = 1.0;
        /// <summary>
        /// The weight that we should assign to an input seed set that eventually is sampled from this node (which can happen only if it is a leaf node).
        /// For example, if we are oversampling from this node at twice the rate that we would be sampling without oversampling, then this is 1/2, because
        /// when we calculate the optimal value, we must give reduced weight to points that were arrived at by oversampling.
        /// </summary>
        public double WeightIfSampledFromHere { get { return WeightScale * EvensamplingProportionCumulative / SamplingProportionCumulative; } }
        /// <summary>
        /// A ratio of 1.0 indicates that with the oversampling plan, we will be able to get to the target number without any failures. A ratio of 2.0 would indicate that about half the time using the oversampling plan, we will not reach the decision.
        /// </summary>
        public double AttemptRatio { get { if (Child1 == null) return AttemptRatioContributionThisNode; return Child1.AttemptRatioContributionThisNode + Child2.AttemptRatioContributionThisNode; } } 
        /// <summary>
        /// This node's contribution to the attempt ratio, which is calculated by summing all leaf nodes' contributions.
        /// </summary>
        public double AttemptRatioContributionThisNode { get { return ActualSampleProportionCumulative / SamplingProportionCumulative; } }
        /// <summary>
        /// The proportion of the time that we will have a success when evensampling from this node.
        /// </summary>
        public double SuccessRatioWhenEvensamplingFromHere;
        /// <summary>
        /// The expected success ratio when sampling pursuant to the oversampling plan.
        /// </summary>
        public double ExpectedSuccessRatioWhenSampling { get { if (Child1 == null) return SuccessRatioWhenEvensamplingFromHere; return Child1.SamplingProportion * Child1.ExpectedSuccessRatioWhenSampling + Child2.SamplingProportion * Child2.ExpectedSuccessRatioWhenSampling; } } 
        /// <summary>
        /// The cumulative number of times this node has been directly sampled from.
        /// </summary>
        public long SampledFromDirectlyCount = 0;
        /// <summary>
        /// The cumulative number of times this node or any of its descendant nodes have been sampled from.
        /// </summary>
        public long SampledFromCount { get { if (Child1 == null) return SampledFromDirectlyCount; return Child1.SampledFromCount + Child2.SampledFromCount; } }
        /// <summary>
        /// The minimum number of samples that must exist in a node for splitting to occur. This is set based on the number of samples.
        /// </summary>
        int MinSizeToSplit = 0;


        public int NumSeeds;
        public int? SeedSplitIndex;
        public double[] Lower, Upper;

        public bool Initialized = false;

        public OversamplingPlan()
        {
        }

        public OversamplingPlan(OversamplingPlan parent)
        {
            Parent = parent;
        }

        object lockObj = new object();
        public void InitializeTopOfTree(int numSeeds)
        {
            if (Parent == null && !Initialized)
            {
                lock (lockObj)
                {
                    if (!Initialized)
                    {
                        // When originally creating this, we may not know the number of input seeds.
                        NumSeeds = numSeeds;
                        Lower = new double[NumSeeds];
                        Upper = new double[NumSeeds];
                        for (int s = 0; s < NumSeeds; s++)
                        {
                            Lower[s] = 0.0;
                            Upper[s] = 1.0;
                        }
                        ActualSampleProportion = 1.0;
                        ActualSampleProportionCumulative = 1.0;
                        SamplingProportion = 1.0;
                        SamplingProportionCumulative = 1.0;
                        EvensamplingProportion = 1.0;
                        EvensamplingProportionCumulative = 1.0;
                        Initialized = true;
                    }
                }
            }
        }

        public OversamplingPlan(double oversamplingWeight, double actualSampleProportion, OversamplingPlan parent, int seedSplitIndex, double seedSplitValueLower, double seedSplitValueHigher, double successRatioWhenEvensamplingFromHere)
            : this(parent)
        {
            NumSeeds = parent.NumSeeds;
            Lower = new double[NumSeeds];
            Upper = new double[NumSeeds];
            SeedSplitIndex = seedSplitIndex;
            ActualSampleProportion = actualSampleProportion;
            ActualSampleProportionCumulative = actualSampleProportion * Parent.ActualSampleProportionCumulative;
            SamplingProportion = oversamplingWeight;
            SamplingProportionCumulative = oversamplingWeight * Parent.SamplingProportionCumulative;
            EvensamplingProportion = (seedSplitValueHigher - seedSplitValueLower) / (Parent.Upper[(int) SeedSplitIndex] - Parent.Lower[(int) SeedSplitIndex]);
            EvensamplingProportionCumulative = EvensamplingProportion * Parent.EvensamplingProportionCumulative;
            SuccessRatioWhenEvensamplingFromHere = successRatioWhenEvensamplingFromHere;
            if (Parent != null)
                MinSizeToSplit = Parent.MinSizeToSplit;
            for (int s = 0; s < NumSeeds; s++)
            {
                if (s == SeedSplitIndex)
                {
                    Lower[s] = seedSplitValueLower;
                    Upper[s] = seedSplitValueHigher;
                }
                else
                {
                    Lower[s] = Parent.Lower[s];
                    Upper[s] = Parent.Upper[s];
                }
            }
            Initialized = true;
        }

        public OversamplingPlan DeepCopy(OversamplingPlan parent)
        {
            OversamplingPlan newPlan = new OversamplingPlan();
            newPlan.Parent = parent;
            if (Child1 != null)
            {
                newPlan.Child1 = Child1.DeepCopy(newPlan);
                newPlan.Child2 = Child2.DeepCopy(newPlan);
            }
            newPlan.ActualSampleProportion = ActualSampleProportion;
            newPlan.ActualSampleProportionCumulative = ActualSampleProportionCumulative;
            newPlan.SamplingProportion = SamplingProportion;
            newPlan.SamplingProportionCumulative = SamplingProportionCumulative;
            newPlan.EvensamplingProportion = EvensamplingProportion;
            newPlan.EvensamplingProportionCumulative = EvensamplingProportionCumulative;
            newPlan.WeightScale = WeightScale;
            newPlan.SuccessRatioWhenEvensamplingFromHere = SuccessRatioWhenEvensamplingFromHere;
            newPlan.MinSizeToSplit = MinSizeToSplit;
            newPlan.NumSeeds = NumSeeds;
            newPlan.SeedSplitIndex = SeedSplitIndex;
            newPlan.Lower = Lower == null ? null : Lower.ToArray();
            newPlan.Upper = Upper == null ? null : Upper.ToArray();
            newPlan.Initialized = Initialized;
            return newPlan;
        }

        public void ApplyOversamplingPlan(double[] inputSeeds, double oversamplingCoefficient, out double weight,
            bool[] flipSeedIfForMirroredIteration = null,
            int?[] substituteSeedIfForMirroredIteration = null)
        {
            if (Parent == null && !Initialized)
                InitializeTopOfTree(inputSeeds.Length); 
            OversamplingPlan op = this;
            while (op.Child1 != null)
            { // keep going until we get to a leaf node
                if (oversamplingCoefficient < op.Child1.SamplingProportion)
                {
                    oversamplingCoefficient /= op.Child1.SamplingProportion; // dividing by this makes it so that the coefficient will once again be a random variable from 0.0 to 1.0
                    op = op.Child1;
                }
                else
                {
                    oversamplingCoefficient = (oversamplingCoefficient - op.Child1.SamplingProportion) / (1.0 - op.Child1.SamplingProportion);
                    op = op.Child2;
                }
            }
            op.SampledFromDirectlyCount++;
            weight = op.WeightIfSampledFromHere;
            for (int s = 0; s < NumSeeds; s++)
            {
                if (flipSeedIfForMirroredIteration == null && substituteSeedIfForMirroredIteration == null)
                {
                    // The input seeds are initially scaled from 0 to 1. Now we need to scale them to the bounds that we have here.
                    inputSeeds[s] = op.Lower[s] + inputSeeds[s] * (op.Upper[s] - op.Lower[s]);
                }
                else
                {
                    int seedFromOversamplingPlanToConsult = substituteSeedIfForMirroredIteration[s] ?? s;
                    double lower = op.Lower[seedFromOversamplingPlanToConsult];
                    double upper = op.Upper[seedFromOversamplingPlanToConsult];
                    if (flipSeedIfForMirroredIteration[seedFromOversamplingPlanToConsult])
                    {
                        double lowertemp = 1.0 - upper;
                        upper = 1.0 - lower;
                        lower = lowertemp;
                    }
                    inputSeeds[s] = lower + inputSeeds[s] * (upper - lower);
                }
                if (!(0 <= inputSeeds[s] && inputSeeds[s] <= 1))
                    throw new Exception("Internal error.");
            }
        }

        public bool SplitAtIfPossible(int index, double threshold, double samplingProportionForChild1, double actualSampleProportionForChild1, double expectedProportionForChild1)
        {
            double successRatioChild1 = SuccessRatioWhenEvensamplingFromHere * actualSampleProportionForChild1 / expectedProportionForChild1;
            double successRatioChild2 = SuccessRatioWhenEvensamplingFromHere * (1.0 - actualSampleProportionForChild1) / (1.0 - expectedProportionForChild1);
            if (successRatioChild1 > 1.0 || successRatioChild2 > 1.0)
                return false; // this is probably a spurious statistical significance finding, so we should not split
            Child1 = new OversamplingPlan(samplingProportionForChild1, actualSampleProportionForChild1, this, index, Lower[index], threshold, successRatioChild1);
            Child2 = new OversamplingPlan(1.0 - samplingProportionForChild1, 1.0 - actualSampleProportionForChild1, this, index, threshold, Upper[index], successRatioChild2);
            return true; // we did split
        }

        public void FindAnomalies(List<double[]> samples)
        { // at this point, this is only used for development
            if (!samples.Any())
                return;
            int length = samples[0].Length;

            List<Tuple<int,int>> pairs = new List<Tuple<int,int>>();
            for (int a = 0; a < length; a++)
                for (int b = 0; b < length; b++)
                    if (a != b)
                        pairs.Add(new Tuple<int, int>(a, b));

            var avgAbsDiffs = pairs.Select(x => samples.Average(y => Math.Abs(y[x.Item2] - y[x.Item1]))).ToList();

            Debug.WriteLine(avgAbsDiffs.Min() + " " + avgAbsDiffs.Average() + " " + avgAbsDiffs.Max() + " " + avgAbsDiffs.Stdev());
        }

        public double GetSmallestWeight()
        {
            if (Child1 == null)
                return WeightIfSampledFromHere;
            else
                return Math.Min(Child1.GetSmallestWeight(), Child2.GetSmallestWeight());
        }

        public void SetWeightScale(double weightScale)
        {
            WeightScale = weightScale;
            if (Child1 != null)
            {
                Child1.SetWeightScale(weightScale);
                Child2.SetWeightScale(weightScale);
            }
        }

        public void SplitBasedOnSamples(List<double[]> samples, double successRatioWhenEvensamplingFromHere)
        {
            TabbedText.WriteLine("Developing oversampling plan.");
            if (Parent != null)
                throw new Exception("SplitBasedOnSamples should be called only for the top node.");
            if (!Initialized)
                InitializeTopOfTree(samples[0].Length);
            WeightScale = 1.0;
            SuccessRatioWhenEvensamplingFromHere = successRatioWhenEvensamplingFromHere;
            AdjustBoundsBasedOnObserved(samples);
            int maxLevelsToAdd = 15; 
            MinSizeToSplit = (int)(samples.Count() * 0.02); 
            SplitBasedOnSamplesHelper(samples, maxLevelsToAdd);
            SetWeightScale(1.0 / GetSmallestWeight());
            //TabbedText.WriteLine(ToTreeString());
        }

        public void AdjustBoundsBasedOnObserved(List<double[]> samples)
        {
            int length = samples[0].Length;
            for (int i = 0; i < length; i++)
            {
                double lowerOrig = Lower[i];
                double upperOrig = Upper[i];
                var samplesForInputSeed = samples.Select(x => x[i]);
                double minVal = samplesForInputSeed.Min();
                double maxVal = samplesForInputSeed.Max();
                double cushion = 1.05;
                double minVal2 = maxVal - cushion * (maxVal - minVal);
                double maxVal2 = minVal + cushion * (maxVal - minVal);
                if (Lower[i] < minVal2)
                    Lower[i] = minVal2;
                if (Upper[i] > maxVal2)
                    Upper[i] = maxVal2;
                SuccessRatioWhenEvensamplingFromHere /= (Upper[i] - Lower[i])/(upperOrig - lowerOrig); // since we've disabled some failures, the success ratio when evensampling should improve
            }
        }

        private void SplitBasedOnSamplesHelper(List<double[]> samples, int maxAdditionalLevelsToAdd)
        {
            // Debug.WriteLine("Splitting based on " + samples.Count() + " " + ActualSampleProportionCumulative);
            if (maxAdditionalLevelsToAdd == 0 || !samples.Any())
                return;
            bool doSplitChildren = false;
            if (Child1 == null)
            {  // we're at a leaf node. We need to find the optimal place for a split.
                double[] chiSquared = new double[NumSeeds];
                double[] optimalThreshold = new double[NumSeeds];
                for (int s = 0; s < NumSeeds; s++)
                {
                    double sizeOfRange = Upper[s] - Lower[s];
                    double[] ordered = samples.Select(x => x[s]).OrderBy(x => x).ToArray();
                    double bestChiSquare = 0;
                    double bestValue = 0;
                    int minNumberSamplesEachSide = (int)Math.Ceiling(samples.Count() * 0.10);
                    if (ordered.Length < minNumberSamplesEachSide * 2)
                        continue; // go to next seed
                    for (int spot = minNumberSamplesEachSide; spot < ordered.Length - minNumberSamplesEachSide; spot += 1) // don't break things up on extremes
                    { 
                        double theValue = ordered[spot];
                        double expectedLessThanThisValue = ((theValue - Lower[s]) / sizeOfRange) * ordered.Length;
                        double chiSquare = ChiSquaredStatistic(spot, ordered.Length - spot, expectedLessThanThisValue, (double)ordered.Length - expectedLessThanThisValue);
                        if (chiSquare > bestChiSquare)
                        {
                            bestChiSquare = chiSquare;
                            bestValue = theValue;
                        }
                    }
                    optimalThreshold[s] = bestValue;
                    chiSquared[s] = bestChiSquare;
                    // slower method commented out -- requires repeated recounting
                    //optimalThreshold[s] = FindOptimalPoint.Optimize(Lower[s], Upper[s], (Upper[s] - Lower[s]) / 100.0, threshold => ChiSquaredStatisticForSampleDistributionAroundThreshold(samples, s, threshold), true);
                    //chiSquared[s] = ChiSquaredStatisticForSampleDistributionAroundThreshold(samples, s, optimalThreshold[s]);
                }
                // find the split that makes the most sense and continue with splitting if if is statistically significant
                var mostSignificant = chiSquared.Select((item, index) => new { ChiSquared = item, Index = index }).OrderByDescending(x => x.ChiSquared).First();
                if (mostSignificant.ChiSquared >= 3.841) // statistically significant at 0.05 level
                {
                    int lowerThan = CountSamplesOnSideOfThreshold(samples, mostSignificant.Index, optimalThreshold[mostSignificant.Index], true);
                    int greaterThanOrEqualTo = CountSamplesOnSideOfThreshold(samples, mostSignificant.Index, optimalThreshold[mostSignificant.Index], false);
                    int total = lowerThan + greaterThanOrEqualTo;
                    double actualSampleProportionForChild1 = (double)lowerThan / (double)total;
                    double samplingProportionForChild1 = actualSampleProportionForChild1;
                    double samplingProportionForChild2 = 1.0 - samplingProportionForChild1;
                    double expectedProportionLowerThan = (optimalThreshold[mostSignificant.Index] - Lower[mostSignificant.Index]) / (Upper[mostSignificant.Index] - Lower[mostSignificant.Index]);
                    double expectedProportionHigherThan = 1.0 - expectedProportionLowerThan;
                    // prevent excessive imbalances by reducing outsized sampling ratios. A limitation here is that we can still end up with imbalances across multiple layers of the hierarchy.
                    const double maxRatioBetweenSamplingWeights = 10.0;
                    double child1Weight = expectedProportionLowerThan / samplingProportionForChild1;
                    double child2Weight = expectedProportionHigherThan / samplingProportionForChild2;
                    double currentRatio = child1Weight / child2Weight;
                    if (currentRatio > maxRatioBetweenSamplingWeights) // i.e., Child1 has high weight because it will be sampled rarely since there was little success there
                    {
                        samplingProportionForChild1 = expectedProportionLowerThan / (expectedProportionLowerThan + expectedProportionHigherThan * maxRatioBetweenSamplingWeights);
                        samplingProportionForChild2 = 1.0 - samplingProportionForChild1;
                        child1Weight = expectedProportionLowerThan / samplingProportionForChild1;
                        child2Weight = expectedProportionHigherThan / samplingProportionForChild2;
                    }
                    else if ((1.0 / currentRatio) > maxRatioBetweenSamplingWeights) // i.e., Child2 has high weight because it will be sampled rarely since there was little success there
                    {
                        samplingProportionForChild2 = expectedProportionHigherThan / (expectedProportionHigherThan + expectedProportionLowerThan * maxRatioBetweenSamplingWeights);
                        samplingProportionForChild1 = 1.0 - samplingProportionForChild2;
                        child1Weight = expectedProportionLowerThan / samplingProportionForChild1;
                        child2Weight = expectedProportionHigherThan / samplingProportionForChild2;
                    }
                    const double minSamplingProportion = 0.3;
                    
                    if (Math.Abs(samplingProportionForChild1 - expectedProportionLowerThan) > 0.02) // don't bother making extremely fine-grained splits -- this is very important in part because this is a recursive algorithm. 
                    { // not a fine-grained split
                        double threshold = optimalThreshold[mostSignificant.Index];
                        doSplitChildren = SplitAtIfPossible(mostSignificant.Index, threshold, samplingProportionForChild1, actualSampleProportionForChild1, expectedProportionLowerThan);
                    }
                }
            }
            else // we only split at the leaf nodes. note that we don't change a split once it is established.
                doSplitChildren = true;
            if (doSplitChildren)
                SplitChildren(samples, maxAdditionalLevelsToAdd);
        }

        private void SplitChildren(List<double[]> samples, int maxAdditionalLevelsToAdd)
        {
            List<double[]> child1Samples = new List<double[]>();
            List<double[]> child2Samples = new List<double[]>();
            int childSplitIndex = (int)Child1.SeedSplitIndex;
            double threshold = Child1.Upper[childSplitIndex];
            foreach (var sample in samples)
            {
                if (sample[childSplitIndex] < threshold)
                    child1Samples.Add(sample);
                else
                    child2Samples.Add(sample);
            }
            Task c1Task = new Task(() =>
                {
                    if (child1Samples.Count() >= MinSizeToSplit)
                        Child1.SplitBasedOnSamplesHelper(child1Samples, maxAdditionalLevelsToAdd - 1);
                });
            Task c2Task = new Task(() =>
                {
                    if (child2Samples.Count() >= MinSizeToSplit)
                        Child2.SplitBasedOnSamplesHelper(child2Samples, maxAdditionalLevelsToAdd - 1);
                });
            Task[] bothTasks = new Task[] { c1Task, c2Task };
            foreach (Task t in bothTasks)
                t.Start();
            Task.WaitAll(bothTasks);
        }

        private double ChiSquaredStatisticForSampleDistributionAroundThreshold(List<double[]> samples, int index, double threshold)
        {
            int lowerThan = CountSamplesOnSideOfThreshold(samples, index, threshold, true);
            int greaterThanOrEqualTo = CountSamplesOnSideOfThreshold(samples, index, threshold, false);
            int total = lowerThan + greaterThanOrEqualTo;
            double expectedProportionLowerThan = (threshold - Lower[index]) / (Upper[index] - Lower[index]);
            double expectedNumberLowerThan = expectedProportionLowerThan * (double)total;
            double expectedNumberGreaterThanOrEqualTo = (double)total - expectedNumberLowerThan;
            return ChiSquaredStatistic(lowerThan, greaterThanOrEqualTo, expectedNumberLowerThan, expectedNumberGreaterThanOrEqualTo);
        }

        private static double ChiSquaredStatistic(int lowerThan, int greaterThanOrEqualTo, double expectedNumberLowerThan, double expectedNumberGreaterThanOrEqualTo)
        {
            double observedLowerMinusExpectedLower = lowerThan - expectedNumberLowerThan;
            double observedUpperMinusExpectedUpper = greaterThanOrEqualTo - expectedNumberGreaterThanOrEqualTo;
            double observedLowerMinusExpectedLowerSquaredDividedByExpected = observedLowerMinusExpectedLower * observedLowerMinusExpectedLower / expectedNumberLowerThan;
            double observedUpperMinusExpectedUpperSquaredDividedByExpected = observedUpperMinusExpectedUpper * observedUpperMinusExpectedUpper / expectedNumberGreaterThanOrEqualTo;
            double chiSquared = observedLowerMinusExpectedLowerSquaredDividedByExpected + observedUpperMinusExpectedUpperSquaredDividedByExpected;
            return chiSquared;
        }

        private int CountSamplesOnSideOfThreshold(List<double[]> samples, int index, double threshold, bool lessThan)
        {
            int count = 0;
            foreach (double[] sample in samples)
            {
                bool isOK = true;
                for (int s = 0; s < NumSeeds; s++)
                {
                    if (! // if not all in bounds, on the proper side of the specified threshold for the relevant index
                        (sample[s] >= Lower[s] && 
                        sample[s] <= Upper[s] && 
                        (   
                            s != index || 
                            (lessThan && sample[s] < threshold) ||
                            (!lessThan && sample[s] >= threshold)
                        )
                        )
                        )
                    {
                        isOK = false;
                        break;
                    }
                }
                if (isOK)
                    count++;
            }
            return count;
        }

        public int Level()
        {  // a slow recursive approach but we don't call this much
            if (Parent == null)
                return 0;
            else
                return 1 + Parent.Level();
        }

        public int NumberNodes()
        {
            if (Child1 == null)
                return 1;
            else
                return 1 + Child1.NumberNodes() + Child2.NumberNodes();
        }

        public string NodeToString()
        {
            //string ranges = String.Concat(
            //                Lower
            //                .Zip(Upper,
            //                        (lower, upper) => lower.ToSignificantFigures() + "<-->" + upper.ToSignificantFigures() + " ")
            //                .Select((item, index) => new { Index = index, Item = item })
            //                .Where(x => x.Item != "0<-->1 ")
            //                .Select(x => "[" + x.Index + "]:" + x.Item));
            string ranges = SeedSplitIndex != null && SeedSplitIndex < NumSeeds ? "[" + SeedSplitIndex + "]: " + Lower[(int)SeedSplitIndex].ToSignificantFigures() + "<-->" + Upper[(int)SeedSplitIndex].ToSignificantFigures() : "No split";
            string thisNode = "Sample " + SamplingProportion.ToSignificantFigures() + " (observed " + ActualSampleProportion.ToSignificantFigures() + ", expected " + EvensamplingProportion.ToSignificantFigures() + "): " + ranges + " ===> Cumulative " + SamplingProportionCumulative.ToSignificantFigures() + " (observed " + ActualSampleProportionCumulative.ToSignificantFigures() + ", expected " + EvensamplingProportionCumulative.ToSignificantFigures() + ") -> Weight " + WeightIfSampledFromHere.ToSignificantFigures() + " Cum. ct.: " + SampledFromCount + " Cum. %: " + ((double)SampledFromCount / (double)Top.SampledFromCount).ToSignificantFigures() + " Succ. no samp.: " + SuccessRatioWhenEvensamplingFromHere.ToSignificantFigures() + " Exp. succ: " + ExpectedSuccessRatioWhenSampling.ToSignificantFigures();
            return thisNode;
        }

        public override string ToString()
        {
            return NodeToString() + "Number nodes: " + NumberNodes() + " Exp. succ: " + ExpectedSuccessRatioWhenSampling.ToSignificantFigures() + "\n";
        }

        public string NodeToIndentedString()
        {
            int level = Level();
            string theString = "";
            for (int i = 0; i < level; i++)
                theString += "   ";
            return theString + NodeToString() + "\n";
        }

        public string ToTreeStringHelper()
        {
            if (Child1 == null)
                return NodeToIndentedString();
            return NodeToIndentedString() + Child1.ToTreeStringHelper() + Child2.ToTreeStringHelper();
        }

        public string ToTreeString()
        {
            return ToTreeStringHelper() + "Number nodes: " + NumberNodes() + "\n";
        }
    }

    public class OversamplingPlanTester
    {
        public void DoTest()
        {
            List<double[]> samples = new List<double[]>(); // these will be cases in which negotiation fails
            const int numSamplesToProduce = 100000;
            int numAttempts = ProduceSamplesAndReturnAttempts(samples, numSamplesToProduce);
            OversamplingPlan op = new OversamplingPlan() { };
            op.SplitBasedOnSamples(samples, (double) samples.Count() / (double) numAttempts);
            Debug.WriteLine("original successes/attempts: " + ((double)samples.Count()) / ((double)numAttempts));
            Debug.WriteLine("expected successes/attemtpts: " + op.ExpectedSuccessRatioWhenSampling);
            samples = new List<double[]>();
            numAttempts = ProduceSamplesAndReturnAttempts(samples, numSamplesToProduce, op);
            Debug.WriteLine(op.ToString());
            Debug.WriteLine("oversampled successes/attempts: " + ((double)samples.Count()) / ((double)numAttempts));
        }

        private static int ProduceSamplesAndReturnAttempts(List<double[]> samples, int numSamplesToProduce, OversamplingPlan op = null)
        {
            int numAttempts = 0;
            StatCollector allCases = new StatCollector();
            StatCollector whereDoesntSettle = new StatCollector();
            while (samples.Count < numSamplesToProduce)
            {
                numAttempts++;
                double[] inputSeeds = new double[] { RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), RandomGenerator.NextDouble() };
                double weight = 0;
                if (op != null)
                    op.ApplyOversamplingPlan(inputSeeds, RandomGenerator.NextDouble(), out weight);
                bool settles = CaseWillSettle(inputSeeds);
                double formula = inputSeeds[1] - inputSeeds[2]; // both sides optimistic => this should be high
                allCases.Add(formula);
                if (!settles) // we're looking for those cases that would theoretically go to the next bargaining round
                {
                    samples.Add(inputSeeds);
                    whereDoesntSettle.Add(formula);
                }
            }
            Debug.WriteLine(String.Format("Formula -- All cases: ({0},{1}), no settlement: ({2}, {3})", allCases.Min, allCases.Max, whereDoesntSettle.Min, whereDoesntSettle.Max));
            return numAttempts;
        }

        private static bool CaseWillSettle(double[] inputSeeds)
        {
            double actualLitigationQuality = inputSeeds[0];
            double pInputSeed = inputSeeds[1];
            double dInputSeed = inputSeeds[2];
            double jInputSeed = inputSeeds[3];
            double pBargainingRangeInputSeed = inputSeeds[4];
            double dBargainingRangeInputSeed = inputSeeds[5];
            //if (inputSeeds[4] < 0.95 || inputSeeds[5] > 0.8) 
            //    return true; // test whether it will truncate the range, to about .25 and .85 to allow a little cushion

            double pBargainingRangeInsists = -0.05; // ignore the input seed value for now
            double dBargainingRangeInsists = -0.05;
            double standardDeviationOfObfuscation = 0.15;
            double pNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(pInputSeed);
            double dNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(dInputSeed);
            double jNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(jInputSeed);
            double pSignal = actualLitigationQuality + pNoise;
            double dSignal = actualLitigationQuality + dNoise;
            double jSignal = actualLitigationQuality + jNoise;

            double pEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscation, pSignal);
            double dEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscation, dSignal);

            double pEstimatePWins = CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(pEstimateStrengthLiability, standardDeviationOfObfuscation);
            double dEstimatePWins = CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(dEstimateStrengthLiability, standardDeviationOfObfuscation);
            double pEstimateDWins = 1.0 - pEstimatePWins;
            double dEstimateDWins = 1.0 - dEstimatePWins;

            double pEstimatePDeltaUtility = -250.0 + 1000.0 * pEstimatePWins;
            double pEstimateDDeltaUtility = -1250.0 + 1000.0 * pEstimateDWins;
            double dEstimatePDeltaUtility = -250.0 + 1000.0 * dEstimatePWins;
            double dEstimateDDeltaUtility = -1250.0 + 1000.0 * dEstimateDWins;
            double bargainingRangeSize = 500.0;
            double pOffer = pEstimatePDeltaUtility + pBargainingRangeInsists * bargainingRangeSize;
            double dOffer = dEstimatePDeltaUtility + (1.0 - dBargainingRangeInsists) * bargainingRangeSize;
            double pStartWealth = 100000;
            double dStartWealth = 100000;
            double pScore = 0;
            bool settles = false;
            if (pOffer < dOffer)
            {
                double pExpensesWithSettlement = 75.0;
                double settlementAmount = (pOffer + dOffer) / 2.0;
                pScore = pStartWealth + settlementAmount - pExpensesWithSettlement;
                settles = true;
            }
            else
            {
                double pExpensesWithTrial = 250.0;
                double damagesPayment = 0.0;
                if (jSignal >= 0.5)
                    damagesPayment = 1000.0;
                pScore = pStartWealth + damagesPayment - pExpensesWithTrial;
            }
            return settles;
        }
    }
}
