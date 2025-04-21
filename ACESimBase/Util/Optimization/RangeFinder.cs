using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace ACESimBase.Util.Optimization
{
    public class InfiniteRecursionException : Exception
    {
        public InfiniteRecursionException(string message)
            : base(message)
        {
            // Nothing to do
        }
    }

    public static class FindOptimalPoint
    {
        public static double OptimizeStartingAtPossible(double lowest, double highest, double approximateValue, double precision, Func<double, double> theTest, bool highestIsBest)
        {
            // We will gradually increase the possible range until the approximate value is better than the endpoints.
            double rangeEachDirection = precision * 100.0 * (highest - lowest); // start with 100 times the precision range, since there will ordinarily be some need to expand the range
            double potentialLowest = approximateValue - rangeEachDirection;
            double potentialHighest = approximateValue + rangeEachDirection;
            bool isBetterThanPotentialLowestAndHighest = false;
            int numRepetitions = 0;
            bool abort = false;
            do
            {
                numRepetitions++;
                abort = potentialLowest < lowest || potentialHighest > highest;
                if (!abort)
                {
                    double testResultPotentialLowest = theTest(potentialLowest);
                    double testResultPotentialHighest = theTest(potentialHighest);
                    double testResultApproximateValue = theTest(approximateValue);
                    if (highestIsBest)
                        isBetterThanPotentialLowestAndHighest = testResultApproximateValue > testResultPotentialHighest && testResultApproximateValue > testResultPotentialLowest;
                    else
                        isBetterThanPotentialLowestAndHighest = testResultApproximateValue < testResultPotentialHighest && testResultApproximateValue < testResultPotentialLowest;
                    if (!isBetterThanPotentialLowestAndHighest)
                    {
                        rangeEachDirection *= 2.0;
                        potentialLowest = approximateValue - rangeEachDirection;
                        potentialHighest = approximateValue + rangeEachDirection;
                    }
                }
            }
            while (!isBetterThanPotentialLowestAndHighest && !abort);
            if (abort)
            {
                potentialLowest = lowest;
                potentialHighest = highest;
            }
            return OptimizeByNarrowingRanges(potentialLowest, potentialHighest, precision, theTest, highestIsBest, 4, 3);
        }

        public static double Optimize(double lowest, double highest, double precision, Func<double, double> theTest, bool highestIsBest, int numberRangesToTestFirstCall = 10, int numberRangesToTestGenerally = 4, bool useGoldenSection = true, bool allowBoundsToExpandIfNecessary = false)
        {
            if (useGoldenSection)
                return OptimizeUsingGoldenSectionAfterInitialSearch(lowest, highest, precision, theTest, highestIsBest, numberRangesToTestFirstCall, allowBoundsToExpandIfNecessary);
            else
                return OptimizeByNarrowingRanges(lowest, highest, precision, theTest, highestIsBest, numberRangesToTestFirstCall, numberRangesToTestGenerally);
        }

        public static double OptimizeUsingGoldenSectionAfterInitialSearch(double lowest, double highest, double precision, Func<double, double> theTest, bool highestIsBest, int numberRangesToTestFirstCall = 10, bool allowBoundsToExpandIfNecessary = false)
        {
            double adjustedPrecision = precision * Math.Abs(highest - lowest);
            // first, narrow the range
            OptimizeHelper(ref lowest, ref highest, adjustedPrecision, numberRangesToTestFirstCall, 0, theTest, highestIsBest);
            // now, use golden section optimization
            GoldenSectionOptimizer opt = new GoldenSectionOptimizer() { TheFunction = theTest, LowExtreme = lowest, HighExtreme = highest, Minimizing = !highestIsBest, Precision = adjustedPrecision };
            double result = allowBoundsToExpandIfNecessary ? opt.OptimizeAllowingRangeToExpandIfNecessary() : opt.Optimize();
            return result;
        }

        public static double OptimizeByNarrowingRanges(double lowest, double highest, double precision, Func<double, double> theTest, bool highestIsBest, int numberRangesToTestFirstCall = 10, int numberRangesToTestGenerally = 4, double targetValue = 0, bool onlyAboveTargetValue = false)
        {
            return Math.Max(lowest, Math.Min(highest, OptimizeHelper(ref lowest, ref highest, precision * Math.Abs(highest - lowest), numberRangesToTestFirstCall, numberRangesToTestGenerally, theTest, highestIsBest, targetValue, onlyAboveTargetValue)));
        }

        internal static double OptimizeHelper(ref double lowest, ref double highest, double adjPrecision, int numberRangesToTestFirstCall, int numberRangesToTestGenerally, Func<double, double> theTest, bool highestIsBest, double targetValue = 0, bool onlyAboveTargetValue = false)
        {
            bool printOutInfo = false;
            bool firstTimeThrough = true;
            int numberRangesToTest = numberRangesToTestFirstCall;
            while (Math.Abs(highest - lowest) > adjPrecision && (numberRangesToTestGenerally > 0 || firstTimeThrough))
            {
                int bestr = -1;
                double bestResult = 0, bestMidpoint = 0;
                double rangeSize = (highest - lowest) / numberRangesToTest;
                bool foundTestResultAbove = false;
                for (int r = 0; r < numberRangesToTest; r++)
                {
                    double bottomOfRange = lowest + r * rangeSize;
                    double topOfRange = lowest + (r + 1.0) * rangeSize;
                    double midpointOfRange = lowest + (r + 0.5) * rangeSize;
                    double testResult = theTest(midpointOfRange);
                    if (targetValue != 0)
                    {
                        double originalTestResult = testResult;
                        testResult = (testResult - targetValue) * (testResult - targetValue);
                        if (onlyAboveTargetValue)
                        {
                            if (originalTestResult < targetValue)
                                testResult *= 1000000; // make it a very high result  so that it does not get set as optimal
                            else
                                foundTestResultAbove = true;
                        }
                    }
                    if (printOutInfo)
                        Debug.WriteLine(
                            $"Bottom {bottomOfRange} Mid {midpointOfRange} Top {topOfRange} ==> Result {testResult}");

                    if (r == 0 || highestIsBest && testResult > bestResult || !highestIsBest && testResult < bestResult)
                    {
                        bestr = r;
                        bestResult = testResult;
                        bestMidpoint = midpointOfRange;
                    }
                }

                // We will include the winning range, plus adjacent ranges up to their midpoint.
                double bottomOfOverallRange = 0, topOfOverallRange = 0;
                if (bestr == 0)
                {
                    bottomOfOverallRange = lowest;
                    topOfOverallRange = lowest + 1.5 * rangeSize;
                }
                else if (targetValue == 0 || !onlyAboveTargetValue || foundTestResultAbove)
                { // if we are in target value mode seeking only results above and did not find a test result above, then we return the lowest range
                    if (bestr == numberRangesToTestFirstCall - 1)
                    {

                        bottomOfOverallRange = highest - 1.5 * rangeSize;
                        topOfOverallRange = highest;
                    }
                    else
                    {
                        bottomOfOverallRange = lowest + (bestr - 1.0 + 0.5) * rangeSize;
                        topOfOverallRange = lowest + (bestr + 1.0 + 0.5) * rangeSize;
                    }
                }

                lowest = bottomOfOverallRange;
                highest = topOfOverallRange;
                numberRangesToTest = numberRangesToTestGenerally;
                firstTimeThrough = false;
            }
            double returnVal = (highest + lowest) / 2.0;
            if (printOutInfo)
                Debug.WriteLine("Result: " + returnVal);
            return returnVal;
            throw new Exception(); // guarantees to compiler that all code paths return a value
        }
    }

    public static class CutoffFinder
    {
        public static double FindFarthestPointFromZeroPassingTest(bool positiveNumbers, double startingPoint,
            Func<double, bool> theTest, double doneWhenFurtherTimesThisGreaterThanClosest = (double)1.01, int maxTries = 20)
        {
            double? furthestFromZeroKnownToSucceed = null;
            double? closestToZeroKnownToFail = null;

            double numberToTest = startingPoint;
            int numTries = 0;
            do
            {
                GetValueToTest(positiveNumbers, furthestFromZeroKnownToSucceed, closestToZeroKnownToFail, ref numberToTest);
                bool resultOfTest = theTest(numberToTest);
                if (resultOfTest)
                    furthestFromZeroKnownToSucceed = numberToTest;
                else
                    closestToZeroKnownToFail = numberToTest;

                if (closestToZeroKnownToFail == 0 || furthestFromZeroKnownToSucceed == 0)
                    return 0;
                numTries++;
                if (furthestFromZeroKnownToSucceed == null && numTries == maxTries)
                    return 0;
            }
            while (furthestFromZeroKnownToSucceed == null
                || closestToZeroKnownToFail == null
                || Math.Abs((double)furthestFromZeroKnownToSucceed * doneWhenFurtherTimesThisGreaterThanClosest) < Math.Abs((double)closestToZeroKnownToFail));

            double returnVal = ((double)closestToZeroKnownToFail + (double)furthestFromZeroKnownToSucceed) / 2;

            return returnVal;
        }

        internal static void GetValueToTest(bool positiveNumbers, double? furthestFromZeroKnownToSucceed, double? closestToZeroKnownToFail, ref double valueToTest)
        {
            if (furthestFromZeroKnownToSucceed == null && closestToZeroKnownToFail == null)
            {
                if (!positiveNumbers)
                    valueToTest = 0 - Math.Abs(valueToTest);
            }
            else if (furthestFromZeroKnownToSucceed == null)
            {
                valueToTest /= 2;
            }
            else if (closestToZeroKnownToFail == null)
            {
                valueToTest *= 2;
            }
            else
            {
                valueToTest = ((double)furthestFromZeroKnownToSucceed + (double)closestToZeroKnownToFail) / 2;
            }
        }
    }

    // Assume score is a simple concave function that is increasing as it passes 0. This finds the maximum of that function.
    public static class FindOptimalValueGreaterThanZero
    {
        public static double Maximize(Func<double, double> scoreFunc, double initialValue, double initialExponentialFactor, double desiredPrecision, int maxAttemptsToFindBounds = 25)
        {
            double exponentialFactor = initialExponentialFactor;
            const double exponentialFactorMultiplier = 1.0;
            // First, we must make higherValue and lowerValue larger until they are starting to get worse.
            double scoreInitialValue = scoreFunc(initialValue);
            double higherValue = initialValue, lowerValue = initialValue;
            double scoreForHigherValue, scoreForLowerValue;
            double scoreLastValue = scoreInitialValue;
            bool done;
            int numAttempts = 0;
            do
            {
                numAttempts++;
                higherValue *= exponentialFactor;
                scoreForHigherValue = scoreFunc(higherValue);
                if (numAttempts == maxAttemptsToFindBounds)
                    return higherValue;
                done = scoreLastValue >= scoreForHigherValue;
                scoreLastValue = scoreForHigherValue;
                exponentialFactor *= exponentialFactorMultiplier;
            }
            while (!done);
            scoreLastValue = scoreInitialValue;
            numAttempts = 0;
            exponentialFactor = initialExponentialFactor;
            do
            {
                numAttempts++;
                lowerValue /= exponentialFactor;
                scoreForLowerValue = scoreFunc(lowerValue);
                if (numAttempts == maxAttemptsToFindBounds)
                    return lowerValue;
                done = scoreLastValue >= scoreForLowerValue;
                scoreLastValue = scoreForLowerValue;
                exponentialFactor *= exponentialFactorMultiplier;
            }
            while (!done);

            // Now, keep considering the midpoint and adjusting the higher value and lower value
            double midpoint = 0.0;
            do
            {
                midpoint = (higherValue + lowerValue) / 2;
                double scoreForMidpoint = scoreFunc(midpoint);
                double scoreJustPastMidpoint = scoreFunc(midpoint + desiredPrecision / 10.0);
                if (scoreJustPastMidpoint > scoreForMidpoint)
                { // optimum is between midpoint and high score
                    lowerValue = midpoint;
                    scoreForLowerValue = scoreForMidpoint;
                }
                else
                {
                    higherValue = midpoint;
                    scoreForHigherValue = scoreForMidpoint;
                }
            }
            while ((higherValue - lowerValue) / higherValue > desiredPrecision);
            return midpoint;
        }
    }

    public static class RangeFinder
    {
        public static double MaximumPrecision = 0.00001;

        public delegate double Score(double guess);

        /// <summary>
        /// The current mode of an exponential search process.
        /// </summary>
        public enum SearchStage
        {
            /// <summary>
            /// The search is in the growth stage, where the current guess increases in magnitude from some reference value
            /// (default 0.0) in either a negative or positive direction, depending on the context.
            /// </summary>
            Growth,

            /// <summary>
            /// The search has passed the target value and is now shrinking towards the reference value, but within
            /// the boundaries established by previous growth or shrinkage.  The search should update the boundaries based upon 
            /// the new guess.
            /// </summary>
            InwardShrink,

            /// <summary>
            /// The search has passed the target value and is now shrinking away from the reference value, but within
            /// the boundaries established by previous growth or shrinkage.  The search should update the boundaries based upon 
            /// the new guess.
            /// </summary>
            OutwardShrink
        }

        public static double CalculatePrecision(double value1, double value2)
        {
            // Min vs. Max?
            // As the values approach each other, the difference disappears, so Min vs. Max doesn't matter.
            // As the larger value increases relative to the smaller value:
            //   Max will make the precision approach one: (large - small) / large => large/large => 1.
            //   Min will make the precision approach positive infinity: (large-small)/small => large/small => +infinity.
            // As the smaller value decreases relative to the larger value:
            //   Max will make the precision one: (large - small) / large => large/large => 1.
            //   Min will make the precision approach positive infinity: (large-small)/small => large/small => +infinity
            // Since as the larger increases relative to smaller the precision should decrease the number we return 
            //  sould be larger (a larger number indicates something that is less precise.)
            double numerator = Math.Abs(value1 - value2);
            double denominator = Math.Abs(Math.Min(value1, value2));
            if (denominator == float.PositiveInfinity)
                return float.PositiveInfinity;
            else
                return numerator / denominator;
        }

        /// <summary>
        /// Returns value (theValue) of the greatest magnitude that satisfies callback(theValue) >= minimumRequiredResult.
        /// Performs an initial exponential growth to discover the first value that does not satisfy the constraint, and then performs
        /// binary search between the last guess known to work and the guess that didn't work to find the value (to within requiredUncertainty.)
        /// </summary>
        /// <param name="initialGuess">A guess for the value; must satisfy the constraint (callback(guess) >= minimumRequiredResult.</param>
        /// <param name="center"></param>
        /// <param name="exponentialFactor">The number by which to multiply the current guess during exponential growth.  Larger values will more quickly
        /// reach binary search stage, but will have a larger binary search space.</param>
        /// <param name="requiredUncertainty">The precision to which to perform the binary search.</param>
        /// <param name="proportionOfProportionInBounds">A callback that gets the current guess and returns the proportion of 
        /// calculations that were in bounds to the required proportion of calculations in bounds (so if the proportion of calculations in bounds
        /// was 0.8 and the required proportion in bounds was 0.8, would return 0.8/0.8 = 1.0.  Range: [0.0, 1.0 / requiredProportionInBouns]</param>
        /// <returns></returns>
        public static double UncertaintySearch(
            double initialGuess,
            double center,
            double exponentialFactor,
            double requiredUncertainty,
            Score proportionOfProportionInBounds
            )
        {
            if (initialGuess == center)
            {
                throw new ArgumentOutOfRangeException(
                    "init must not equal around, because exponential growth begins on (init - around) and exponential growth on zero never increases.");
            }
            if (requiredUncertainty < MaximumPrecision)
            {
                throw new ArgumentOutOfRangeException(
                    $"Uncertainty cannot be less than the maximum precision for Single: {MaximumPrecision}.");
            }

            SearchStage stage;
            if (proportionOfProportionInBounds(initialGuess) >= 1.0)
            {
                stage = SearchStage.Growth;
            }
            else
            {
                stage = SearchStage.InwardShrink;
            }

            double innerBound = center;
            double outerBound = initialGuess > center ? float.PositiveInfinity : float.NegativeInfinity;
            double guess = initialGuess;
            double guessProportionOfProportionInBounds = proportionOfProportionInBounds(initialGuess);

            double uncertainty = float.MaxValue;
            while (uncertainty > requiredUncertainty)
            {
                //Debug.WriteLine(
                //    "RangeFinder.UncertaintytSearch: {0}:{1}:{2}; Uncertainty: {3}; Proportion of proportion in bounds: {4})",
                //    new object[] { innerBound, guess, outerBound, uncertainty, guessProportionOfProportionInBounds });

                switch (stage)
                {
                    case SearchStage.Growth:
                        // If the proportionInBounds has fallen below the requirements, we have surpassed the boundary and should start shrinking to better discover it.
                        if (guessProportionOfProportionInBounds < 1.0)
                        {
                            stage = SearchStage.InwardShrink;
                            outerBound = guess;
                            guess = (outerBound - innerBound) / exponentialFactor + innerBound; //One time shrink by the exponential factor since we were growing exponentially.  Subsequent shrinks will be binary.
                        }
                        // Otherwise continue growing
                        else
                        {
                            innerBound = guess;
                            guess = (guess - center) * exponentialFactor + center;
                        }
                        break;
                    case SearchStage.InwardShrink:
                        if (guessProportionOfProportionInBounds >= 1.0)
                        {
                            // If we are back within acceptable requirements, shrink upwards
                            stage = SearchStage.OutwardShrink;
                            innerBound = guess;
                        }
                        else
                        {
                            // Otherwise keep shrinking inwards
                            outerBound = guess;
                            guess = (innerBound + outerBound) / 2.0;
                        }
                        break;
                    case SearchStage.OutwardShrink:
                        if (guessProportionOfProportionInBounds < 1.0)
                        {
                            // If we have fallen below the requirement, start shrinking downwards
                            stage = SearchStage.InwardShrink;
                            outerBound = guess;
                        }
                        else
                        {
                            innerBound = guess;
                            guess = (innerBound + outerBound) / 2.0;
                        }
                        break;
                }

                uncertainty = CalculatePrecision(outerBound, innerBound);
                guessProportionOfProportionInBounds = proportionOfProportionInBounds(guess);
            }

            return guess;
        }
    }
}
