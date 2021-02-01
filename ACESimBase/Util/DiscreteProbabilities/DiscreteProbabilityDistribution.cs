using ACESim.Util;
using JetBrains.Annotations;
using MathNet.Numerics.Integration;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.DiscreteProbabilities
{

    /// <summary>
    /// Builds probability distributions over discrete indices. For example, given a function that calculates probabilities of indices in the range given indices in the domain, the probabilities of the combination of the domain and range variables are returned. These can then be used to calculate probabilities of some third discrete variable, building a multivariate probability map. Given these probability maps, one can then calculate the probability of 
    /// </summary>
    public static class DiscreteProbabilityDistribution
    {

        /// <summary>
        /// Returns a list of index permutations corresponding to various dimensions -- e.g., { [0, 0, 0], [0, 0, 1], ..., [9, 9, 9] } if dimensions are [10, 10, 10]
        /// </summary>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public static int[][] GetAllPermutations(int[] dimensions)
        {
            if (dimensions.Length == 0)
                return null;
            List<int[]> crossProducts = new List<int[]>();
            int[] currentTracker = new int[dimensions.Length];
            do
            {
                crossProducts.Add(currentTracker.ToArray());
            }
            while (!AdvanceToNextPermutation(dimensions, currentTracker));
            return crossProducts.ToArray();
        }

        /// <summary>
        /// Returns an index of a particular list of dimensions -- e.g., [5, 6, 4] -- in the list of index permutations returned by GetCrossProductsOfDiscreteRanges
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="crossProductIndices"></param>
        /// <returns></returns>
        public static int GetIndexOfPermutation(int[] dimensions, List<int> crossProductIndices)
        {
            int index = 0;
            for (int v = 0; v < crossProductIndices.Count(); v++)
            {
                int productOfLaterDimensions = 1;
                for (int d = v + 1; d < dimensions.Length; d++)
                    productOfLaterDimensions *= dimensions[d];
                index += crossProductIndices[v] * productOfLaterDimensions;
            }
            return index;
        }

        /// <summary>
        /// Advances a tracker of indices of certain dimension -- e.g., from [0, 0, 0] to [0, 0, 1]] -- switching to the next dimension as necessary, and returning true with the last combination of indices.
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="indexTracker"></param>
        /// <returns></returns>
        private static bool AdvanceToNextPermutation(int[] dimensions, int[] indexTracker)
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
        /// Given a function to calculate the probability of an element in the range given an element in the domain, calculates a probability of each element of the cross product of the domain and range elements.
        /// The order of these probabilities corresponds to the order of index permutations returned by GetCrossProductsOfDiscreteRanges.
        /// </summary>
        /// <param name="calcProbOfElementInRangeGivenDomain">Calculates a probability based on a zero-based domain index and a zero-based range index</param>
        /// <param name="numRangeElements">The number of elements in the range</param>
        /// <param name="domainProbabilities">The prior distribution of elements in the domain </param>
        /// <returns></returns>
        public static double[] BuildProbabilityMap(Func<int, int, double> calcProbOfElementInRangeGivenDomain, int numRangeElements, double[] domainProbabilities)
        {
            int numDomainElements = domainProbabilities?.Length ?? 1;
            double[] probabilities = new double[numDomainElements * numRangeElements];
            double total = 0;
            int i = 0;
            for (int d = 0; d < numDomainElements; d++)
            {
                double domainElementProbability = domainProbabilities?[d] ?? 1.0;
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
        /// Calculate the probability of all possible values in some variable (the distribution variable) given each possible value in some other variable (the fixed variable).
        /// </summary>
        /// <returns>The probabilities, grouped by the fixed variable, and then ordered by values in the distribution variable</returns>
        public static double[][] CalculateConditionalProbabilities(double[] probabilityMap, int[] dimensions, int distributionVariableIndex, int fixedVariableIndex)
        {
            return Enumerable.Range(0, dimensions[fixedVariableIndex]).Select(x => CalculateProbabilitiesFromMap(probabilityMap, dimensions, distributionVariableIndex, fixedVariableIndex, x)).ToArray();
        }

        /// <summary>
        /// Produces a list of calculators each of which can be used to calculate the probability distribution of a discrete variable given the realized values of all values for specified variables previously determined. This can be used where probability distributions are calculated sequentially, first one variable, then another, and so on, based on obfuscation of earlier variables. The calculators produced can be of any of the variables in any order.
        /// </summary>
        /// <param name="dimensions">The number of possible values for each variable</param>
        /// <param name="domainProbabilities">The exogenous probability distribution for the initial variable</param>
        /// <param name="variableProductionInstructions">A set of instructions for how to produce later variables from earlier variables</param>
        /// <param name="calculatorsToProduce">The set of variables for which to calculate distributions, along with any variables earlier in the list whose values should be taken into account in calculating these values. The indices are all indices within dimensions.</param>
        /// <returns></returns>
        public static List<Func<List<int>, double[]>> GetProbabilityMapCalculators(int[] dimensions, List<VariableProductionInstruction> variableProductionInstructions, List<(int distributionVariableIndex, List<int> fixedVariableIndices)> calculatorsToProduce)
        {
            double[] crossProductProbabilities = BuildProbabilityMap(dimensions, variableProductionInstructions);
            List<Func<List<int>, double[]>> calculatorsList = new List<Func<List<int>, double[]>>();
            foreach (var calculatorToProduce in calculatorsToProduce)
            {
                calculatorsList.Add(GetProbabilityMapCalculator(dimensions, variableProductionInstructions, calculatorToProduce.distributionVariableIndex, calculatorToProduce.fixedVariableIndices));
            }
            return calculatorsList;
        }

        /// <summary>
        /// Produces a single calculator for calculating the probability distribution of a discrete variable (the distribution variable) given specification of a set of other variables.
        /// </summary>
        /// <param name="dimensions">The number of possible values for each variable</param>
        /// <param name="variableProductionInstructions">A set of instructions for how to produce later variables from earlier variables. The source signal index is 0 for the initial variable (the one specified in domain probabilities) and 1 or greater for the variables specified in variableProductionInstructions. This should thus have one fewer element than dimensions.</param>
        /// <param name="distributionVariableIndex">The index of the distribution variable within dimensions</param>
        /// <param name="fixedVariableIndices">The indices of the fixed variable within dimensions</param>
        /// <returns>A calculator that transforms realized values of the variables specified in fixedVariablesIndices into probabilities of different values of the distribution variable</returns>
        public static Func<List<int>, double[]> GetProbabilityMapCalculator(int[] dimensions, List<VariableProductionInstruction> variableProductionInstructions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            double[] crossProductProbabilities = BuildProbabilityMap(dimensions, variableProductionInstructions);
            Func<List<int>, double[]> calculator = GetProbabilityMapCalculator(crossProductProbabilities, dimensions, distributionVariableIndex, fixedVariableIndices);
            return calculator;
        }

        /// <summary>
        /// Returns unconditional probabilities of the distribution variable
        /// </summary>
        /// <param name="dimensions">The number of possible values for each variable</param>
        /// <param name="domainProbabilities">The exogenous probability distribution for the initial variable</param>
        /// <param name="variableProductionInstructions">A set of instructions for how to produce later variables from earlier variables. The source signal index is 0 for the initial variable (the one specified in domain probabilities) and 1 or greater for the variables specified in variableProductionInstructions. This should thus have one fewer element than dimensions.</param>
        /// <param name="distributionVariableIndex">The index within dimensions of the variable for which unconditional probabilities are sought. </param>
        /// <returns></returns>
        public static double[] GetUnconditionalProbabilities(int[] dimensions, List<VariableProductionInstruction> variableProductionInstructions, int distributionVariableIndex)
        {
            double[] probabilityMap = BuildProbabilityMap(dimensions, variableProductionInstructions);
            double[] results = CalculateProbabilitiesFromMap(probabilityMap, dimensions, distributionVariableIndex);
            return results;
        }

        public static double[] BuildProbabilityMap(int[] dimensions, List<VariableProductionInstruction> variableProductionInstructions)
        {
            double[] permutationProbabilities = null;
            for (int d = 0; d < dimensions.Length; d++)
            {
                int[] incomingDimensions = dimensions.Take(d).ToArray();
                int[][] incomingPermutations = GetAllPermutations(incomingDimensions);
                int[] outgoingDimensions = dimensions.Take(d + 1).ToArray();
                var instruction = variableProductionInstructions[d];

                var nextPermutationProbabilities = BuildProbabilityMap((int domainIndex, int rangeIndex) => instruction.GetConditionalProbability(domainIndex, rangeIndex), dimensions[d], permutationProbabilities);
                permutationProbabilities = nextPermutationProbabilities;
            }
            return permutationProbabilities;
        }

        /// <summary>
        /// Given the probabilities for all cross product permutations, returns a function that will translate particular values of the fixed variables into a probability distribution over the distribution variable.
        /// </summary>
        /// <param name="probabilityMap"></param>
        /// <param name="dimensions"></param>
        /// <param name="distributionVariableIndex"></param>
        /// <param name="fixedVariableIndices"></param>
        /// <returns></returns>
        public static Func<List<int>, double[]> GetProbabilityMapCalculator(double[] probabilityMap, int[] dimensions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            double[][] probabilitiesForEachPossibleValueOfFixedVariables = CalculateProbabilitiesFromMap(probabilityMap, dimensions, distributionVariableIndex, fixedVariableIndices);
            int[] selectedDimensions = fixedVariableIndices.Select(x => dimensions[x]).ToArray();
            double[] GetValueFromFixedVariableValues(List<int> values)
            {
                int crossProductIndex = GetIndexOfPermutation(selectedDimensions, values);
                var result = probabilitiesForEachPossibleValueOfFixedVariables[crossProductIndex];
                if (result.All(x => x == 0))
                {
                    // This can occur if the combination of the fixed variables is so unlikely that every probability estimate
                    // is less than the smallest value that can be represented with a double. Ideally in this circumstance,
                    // we would redo the calculations with arbitrary precision arithmetic to determine the relative probabilities.
                    // But in practice, this will have effectively 0 influence on any meaningful calculations (e.g., this scenario
                    // will be buried on an effectively unreachable part of the game tree), so we take a shortcut here of just
                    // setting each probability to the same value. 
                    double eachVal = 1.0 / (double) result.Length;
                    for (int i = 0; i < result.Length; i++)
                        result[i] = eachVal;
                }
                return result;
            }
            return GetValueFromFixedVariableValues;
        }

        /// <summary>
        /// Calculates the probabilities of all values in the distribution variable index, ordered by all possible values of the fixed variable indices.
        /// The first indexer will be an index into the cross product of all of the fixed variable indices. The second indexer will be the different possible values of the distribution variable index.
        /// </summary>
        /// <param name="probabilityMap"></param>
        /// <param name="dimensions"></param>
        /// <param name="distributionVariableIndex"></param>
        /// <param name="fixedVariableIndices"></param>
        /// <returns></returns>
        public static double[][] CalculateProbabilitiesFromMap(double[] probabilityMap, int[] dimensions, int distributionVariableIndex, List<int> fixedVariableIndices)
        {
            int[] dimensionsOfFixedVariables = fixedVariableIndices.Select(x => dimensions[x]).ToArray();
            int[][] crossProducts = GetAllPermutations(dimensionsOfFixedVariables);
            int fixedVariablesValuesPermutations = crossProducts.GetLength(0);
            double[][] results = new double[fixedVariablesValuesPermutations][];
            int r = 0;
            foreach (int[] fixedVariablesValues in crossProducts)
            {
                var indicesAndValues = new List<(int fixedVariableIndex, int fixedVariableValue)>();
                for (int i = 0; i < fixedVariableIndices.Count(); i++)
                    indicesAndValues.Add((fixedVariableIndices[i], fixedVariablesValues[i]));
                results[r] = CalculateProbabilitiesFromMap(probabilityMap, dimensions, distributionVariableIndex, indicesAndValues);
                r++;
            }
            return results;
        }

        /// <summary>
        /// Calculate the probability of all possible values in some variable (the distribution variable) given a particular value in some other variable (the fixed variable).
        /// </summary>
        /// <returns>The probability distribution for possible values of the distribution variable</returns>
        public static double[] CalculateProbabilitiesFromMap(double[] probabilityMap, int[] dimensions, int distributionVariableIndex, int fixedVariableIndex, int fixedVariableValue) => CalculateProbabilitiesFromMap(probabilityMap, dimensions, distributionVariableIndex, new List<(int fixedVariableIndex, int fixedVariableValue)>() { (fixedVariableIndex, fixedVariableValue) });

        /// <summary>
        /// Calculate the probability of all possible values in some variable (the distribution variable) given some particular values in other variables (the fixed variables).
        /// </summary>
        public static double[] CalculateProbabilitiesFromMap(double[] probabilityMap, int[] dimensions, int distributionVariableIndex, List<(int fixedVariableIndex, int fixedVariableValue)> fixedVariables = null)
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
                results[i] = CalculateProbabilityFromMap(probabilityMap, dimensions, numeratorIndices, denominatorIndices, currentIndexTracker);
            }
            return results;
        }

        /// <summary>
        /// Calculate the probability that the numerator indices match, given that the denominator indices match.
        /// </summary>
        /// <param name="probabilityMap">An array of probabilities from building a probability map. The first probability corresponds to indices [0, 0, ..., 0], the second to indices [0, 0, ..., 1] etc. up to [dimensions[0] - 1, dimensions[1] - 1, ...]</param>
        /// <param name="dimensions">The dimensions of the map producing the flattened probabilities. If there are three possible values of the first variable, two possible values of the second variable, and one of the third, then this would be [3, 2, 1].</param>
        /// <param name="numeratorIndices">The non-null entries indicate the numerator dimensions that must match for an item to be included</param>
        /// <param name="denominatorIndices">The non-null entries indicate the denominator dimensions that must match for an item to be included</param>
        /// <param name="currentIndexTracker">An optional array of dimension dimensions.Length that is used to </param>
        /// <returns></returns>
        public static double CalculateProbabilityFromMap(double[] probabilityMap, int[] dimensions, int?[] numeratorIndices, int?[] denominatorIndices, int[] currentIndexTracker = null)
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
                    double probability = probabilityMap[probabilitiesIndex];
                    combinedDenominatorProbability += probability;
                    bool numeratorMatches = isMatch(numeratorIndices);
                    if (numeratorMatches)
                        combinedNumeratorProbability += probabilityMap[probabilitiesIndex];
                }
                done = AdvanceToNextPermutation(dimensions, currentIndexTracker);
                probabilitiesIndex++;
            }
            if (combinedNumeratorProbability == 0)
                return 0;
            var result = combinedNumeratorProbability / combinedDenominatorProbability;
            return result;
        }

    }
}
