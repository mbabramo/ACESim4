using ACESim.Util;
using JetBrains.Annotations;
using MathNet.Numerics.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{

    /// <summary>
    /// Builds probability distributions over discrete indices. For example, given a function that calculates probabilities of indices in the range given indices in the domain, the probabilities of the combination of the domain and range variables are returned. These can then be used to calculate probabilities of some third discrete variable, building a multivariate probability map. Given these probability maps, one can then calculate the probability of 
    /// </summary>
    public static class DiscreteProbabilityDistribution
    {

        /// <summary>
        /// Returns a list of cross product indices corresponding to various dimensions -- e.g., { [0, 0, 0], [0, 0, 1], ..., [9, 9, 9] } if dimensions are [10, 10, 10]
        /// </summary>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public static int[][] GetCrossProductsOfDiscreteRanges(int[] dimensions)
        {
            List<int[]> crossProducts = new List<int[]>();
            int[] currentTracker = new int[dimensions.Length];
            do
            {
                crossProducts.Add(currentTracker.ToArray());
            }
            while (!AdvanceIndexTracker(dimensions, currentTracker));
            return crossProducts.ToArray();
        }

        public static int GetCrossProductIndex(int[] dimensions, List<int> values)
        {
            int index = 0;
            for (int v = 0; v < values.Count(); v++)
            {
                int productOfLaterDimensions = 1;
                for (int d = v + 1; d < dimensions.Length; d++)
                    productOfLaterDimensions *= dimensions[d];
                index += values[v] * productOfLaterDimensions;
            }
            return index;
        }

        /// <summary>
        /// Given a function to calculate the probability of an element in the range given an element in the domain, calculates a probability of each element of the cross product of the domain and range elements.
        /// </summary>
        /// <param name="calcProbOfElementInRangeGivenDomain">Calculates a probability based on a zero-based domain index and a zero-based range index</param>
        /// <param name="numRangeElements">The number of elements in the range</param>
        /// <param name="domainProbabilities">The prior distribution of elements in the domain </param>
        /// <returns></returns>
        public static double[] BuildProbabilityMap(Func<int, int, double> calcProbOfElementInRangeGivenDomain, int numRangeElements, double[] domainProbabilities = null)
        {
            int numDomainElements = domainProbabilities.Length;
            double[] probabilities = new double[numDomainElements * numRangeElements];
            double total = 0;
            int i = 0;
            for (int d = 0; d < numDomainElements; d++)
            {
                double domainElementProbability = domainProbabilities?[d] ?? 1.0 / numDomainElements;
                for (int r = 0; r < numRangeElements; r++)
                {
                    double rangeElementProbability = calcProbOfElementInRangeGivenDomain(d, r);
                    double combinedProbability = rangeElementProbability * domainElementProbability;
                    total += combinedProbability;
                    probabilities[i] = combinedProbability;
                    i++;
                }
            }
            if (Math.Abs(total - 1.0) > 0.000001)
                throw new Exception("Probabilities do not add up to 1.");
            probabilities[i - 1] -= total - 1.0; // Adjust total so that it is exactly 1.
            return probabilities;
        }

        /// <summary>
        /// Advances a tracker of indices of certain dimension -- e.g., from [0, 0, 0] to [0, 0, 1]] -- switching to the next dimension as necessary, and returning true with the last combination of indices.
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="indexTracker"></param>
        /// <returns></returns>
        private static bool AdvanceIndexTracker(int[] dimensions, int[] indexTracker)
        {
            for (int i = dimensions.Length - 1; i >= 0; i--)
            {
                indexTracker[i]++;
                if (indexTracker[i] == dimensions[i])
                {
                    indexTracker[i] = 0;
                    if (i == 0)
                        return true; // done
                }
                else
                    break;
            }
            return false;
        }


        /// <summary>
        /// Calculate the probability of all possible values in some variable (the distribution variable) given each possible value in some other variable (the fixed variable).
        /// </summary>
        /// <returns>The probabilities, grouped by the fixed variable, and then ordered by values in the distribution variable</returns>
        public static double[][] CalculateProbabilitiesFromMap(double[] flattenedProbabilities, int[] dimensions, int distributionVariableIndex, int fixedVariableIndex)
        {
            return Enumerable.Range(0, dimensions[fixedVariableIndex]).Select(x => CalculateProbabilitiesFromMap(flattenedProbabilities, dimensions, distributionVariableIndex, fixedVariableIndex, x)).ToArray();
        }

        /// <summary>
        /// Produces a list of calculators that can be used to calculate the probability distribution of a discrete variable given the realized values of all values for variables previously determined. This can be used where probability distributions are calculated sequentially, first one variable, then another, and so on, based on obfuscation of earlier variables. The calculators produced can be of any of the variables in any order.
        /// </summary>
        /// <param name="dimensions">The number of possible values for each variable</param>
        /// <param name="domainProbabilities">The exogenous probability distribution for the initial variable</param>
        /// <param name="producingExtraDimensions">A set of instructions for how to produce later variables from earlier variables</param>
        /// <param name="calculatorsToProduce">The set of variables for which to calculate distributions, along with any variables earlier in the list whose values should be taken into account in calculating these values</param>
        /// <returns></returns>
        public static List<Func<List<int>, double[]>> GetProbabilityMapCalculators(int[] dimensions, double[] domainProbabilities, List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)> producingExtraDimensions, List<(int distributionVariableIndex, List<int> fixedVariableIndices)> calculatorsToProduce)
        {
            double[] crossProductProbabilities = BuildProbabilityMapBasedOnDiscreteValueSignals(dimensions, domainProbabilities, producingExtraDimensions);
            List<Func<List<int>, double[]>> calculatorsList = new List<Func<List<int>, double[]>>();
            foreach (var calculatorToProduce in calculatorsToProduce)
            {
                calculatorsList.Add(GetProbabilityMapCalculator(dimensions, domainProbabilities, producingExtraDimensions, calculatorToProduce.distributionVariableIndex, calculatorToProduce.fixedVariableIndices));
            }
            return calculatorsList;
        }

        public static Func<List<int>, double[]> GetProbabilityMapCalculator(int[] dimensions, double[] domainProbabilities, List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)> producingExtraDimensions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            double[] crossProductProbabilities = BuildProbabilityMapBasedOnDiscreteValueSignals(dimensions, domainProbabilities, producingExtraDimensions);
            Func<List<int>, double[]> calculator = GetProbabilityMapCalculator(crossProductProbabilities, dimensions, distributionVariableIndex, fixedVariableIndices);
            return calculator;
        }

        /// <summary>
        /// Given the probabilities for all cross product permutations, returns a function that will translate particular values of the fixed variables into a probability distribution over the distribution variable.
        /// </summary>
        /// <param name="flattenedProbabilities"></param>
        /// <param name="dimensions"></param>
        /// <param name="distributionVariableIndex"></param>
        /// <param name="fixedVariableIndices"></param>
        /// <returns></returns>
        public static Func<List<int>, double[]> GetProbabilityMapCalculator(double[] flattenedProbabilities, int[] dimensions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            double[][] probabilitiesForEachPossibleValueOfFixedVariables = CalculateProbabilitiesFromMap(flattenedProbabilities, dimensions, distributionVariableIndex, fixedVariableIndices);
            int[] selectedDimensions = fixedVariableIndices.Select(x => dimensions[x]).ToArray();
            double[] GetValueFromFixedVariableValues(List<int> values)
            {
                int crossProductIndex = GetCrossProductIndex(selectedDimensions, values);
                return probabilitiesForEachPossibleValueOfFixedVariables[crossProductIndex];
            }
            return GetValueFromFixedVariableValues;
        }

        /// <summary>
        /// Calculates the probabilities of all values in the distribution variable index, ordered by all possible values of the fixed variable indices.
        /// The first indexer will be an index into the cross product of all of the fixed variable indices. The second indexer will be the different possible values of the distribution variable index.
        /// </summary>
        /// <param name="flattenedProbabilities"></param>
        /// <param name="dimensions"></param>
        /// <param name="distributionVariableIndex"></param>
        /// <param name="fixedVariableIndices"></param>
        /// <returns></returns>
        public static double[][] CalculateProbabilitiesFromMap(double[] flattenedProbabilities, int[] dimensions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            int[] dimensionsOfFixedVariables = fixedVariableIndices.Select(x => dimensions[x]).ToArray();
            int[][] crossProducts = GetCrossProductsOfDiscreteRanges(dimensionsOfFixedVariables);
            int fixedVariablesValuesPermutations = crossProducts.GetLength(0);
            double[][] results = new double[fixedVariablesValuesPermutations][];
            int r = 0;
            foreach (int[] fixedVariablesValues in crossProducts)
            {
                var indicesAndValues = new List<(int fixedVariableIndex, int fixedVariableValue)>();
                for (int i = 0; i < fixedVariableIndices.Count(); i++)
                    indicesAndValues.Add((fixedVariableIndices[i], fixedVariablesValues[i]));
                results[r] = CalculateProbabilitiesFromMap(flattenedProbabilities, dimensions, distributionVariableIndex, indicesAndValues);
                r++;
            }
            return results;
        }

        /// <summary>
        /// Calculate the probability of all possible values in some variable (the distribution variable) given a particular value in some other variable (the fixed variable).
        /// </summary>
        /// <returns>The probability distribution for possible values of the distribution variable</returns>
        public static double[] CalculateProbabilitiesFromMap(double[] flattenedProbabilities, int[] dimensions, int distributionVariableIndex, int fixedVariableIndex, int fixedVariableValue) => CalculateProbabilitiesFromMap(flattenedProbabilities, dimensions, distributionVariableIndex, new List<(int fixedVariableIndex, int fixedVariableValue)>() { (fixedVariableIndex, fixedVariableValue) });

        /// <summary>
        /// Calculate the probability of all possible values in some variable (the distribution variable) given some particular values in other variables (the fixed variables).
        /// </summary>
        public static double[] CalculateProbabilitiesFromMap(double[] flattenedProbabilities, int[] dimensions, int distributionVariableIndex, List<(int fixedVariableIndex, int fixedVariableValue)> fixedVariables = null)
        {
            int numValuesVariableVariable = dimensions[distributionVariableIndex];
            double[] results = new double[numValuesVariableVariable];
            int?[] numeratorIndices = new int?[dimensions.Length];
            int?[] denominatorIndices = new int?[dimensions.Length];
            if (fixedVariables != null)
                foreach (var (fixedVariableIndex, fixedVariableValue) in fixedVariables)
                    denominatorIndices[fixedVariableIndex] = fixedVariableValue;
            int[] currentIndexTracker = new int[dimensions.Length];
            for (int i = 0; i < numValuesVariableVariable; i++)
            {
                numeratorIndices[distributionVariableIndex] = i;
                results[i] = CalculateProbabilityFromMap(flattenedProbabilities, dimensions, numeratorIndices, denominatorIndices, currentIndexTracker);
            }
            return results;
        }

        /// <summary>
        /// Calculate the probability that the numerator indices match, given that the denominator indices match.
        /// </summary>
        /// <param name="flattenedProbabilities">An array of probabilities from building a probability map. The first probability corresponds to indices [0, 0, ..., 0], the second to indices [0, 0, ..., 1] etc. up to [dimensions[0] - 1, dimensions[1] - 1, ...]</param>
        /// <param name="dimensions">The dimensions of the map producing the flattened probabilities. If there are three possible values of the first variable, two possible values of the second variable, and one of the third, then this would be [3, 2, 1].</param>
        /// <param name="numeratorIndices">The non-null entries indicate the numerator dimensions that must match for an item to be included</param>
        /// <param name="denominatorIndices">The non-null entries indicate the denominator dimensions that must match for an item to be included</param>
        /// <param name="currentIndexTracker">An optional array of dimension dimensions.Length that is used to </param>
        /// <returns></returns>
        public static double CalculateProbabilityFromMap(double[] flattenedProbabilities, int[] dimensions, int?[] numeratorIndices, int?[] denominatorIndices, int[] currentIndexTracker = null)
        {
            if (currentIndexTracker == null)
                currentIndexTracker = new int[dimensions.Length];
            bool isMatch(int?[] indices)
            {
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i] is int index && currentIndexTracker[i] != index)
                        return false;
                return true;
            }
            double combinedNumeratorProbability = 0;
            double combinedDenominatorProbability = 0;
            int probabilitiesIndex = 0;
            bool done = false;
            while (!done)
            {
                bool denominatorMatches = isMatch(denominatorIndices);
                if (denominatorMatches)
                {
                    double probability = flattenedProbabilities[probabilitiesIndex];
                    combinedDenominatorProbability += probability;
                    bool numeratorMatches = isMatch(numeratorIndices);
                    if (numeratorMatches)
                        combinedNumeratorProbability += flattenedProbabilities[probabilitiesIndex];
                }
                done = AdvanceIndexTracker(dimensions, currentIndexTracker);
                probabilitiesIndex++;
            }
            return combinedNumeratorProbability / combinedDenominatorProbability;
        }

        public static double[] BuildProbabilityMapBasedOnDiscreteValueSignals(int[] dimensions, double[] domainProbabilities, List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)> producingExtraDimensions)
        {
            double[] crossProductProbabilities = null;
            for (int d = 1; d < dimensions.Length; d++)
            {
                int[] incomingDimensions = dimensions.Take(d).ToArray();
                int[][] incomingCrossProducts = GetCrossProductsOfDiscreteRanges(incomingDimensions);
                int[] outgoingDimensions = dimensions.Take(d + 1).ToArray();
                var instruction = producingExtraDimensions[d - 1];
                int numDomainElements = dimensions[instruction.sourceSignalIndex];
                int numRangeElements = dimensions[d];
                DiscreteValueSignalParameters dvsParams = new DiscreteValueSignalParameters()
                {
                    NumPointsInSourceUniformDistribution = numDomainElements,
                    NumSignals = numRangeElements,
                    SourcePointsIncludeExtremes = instruction.sourceIncludesExtremes,
                    StdevOfNormalDistribution = instruction.stdev
                };
                crossProductProbabilities = BuildProbabilityMap((int domainIndex, int rangeIndex) =>
                {
                    int discreteValueSignalIndex = incomingCrossProducts[domainIndex][instruction.sourceSignalIndex];
                    double probability = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(discreteValueSignalIndex + 1 /* DiscreteValueSignal expects 1-based action */, dvsParams)[rangeIndex];
                    return probability;
                }, dimensions[d], domainProbabilities);
                domainProbabilities = crossProductProbabilities;
            }
            return crossProductProbabilities;
        }
    }
}
