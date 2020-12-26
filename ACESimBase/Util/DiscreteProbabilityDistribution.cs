using ACESim.Util;
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

        // DEBUG
        ///// <summary>
        ///// Adds a dimension to the probability map. Given the probabilities of each item along all the dimensions but the last, and a function to calculate the probability of each index in the last dimension, calculates probabilities of the revised cross product of dimensions. 
        ///// </summary>
        ///// <param name="initialCrossProductProbabilities">The </param>
        ///// <param name="dimensions"></param>
        ///// <param name="calcProbOfElementInRangeGivenDomain"></param>
        ///// <param name="numRangeElements"></param>
        ///// <returns></returns>
        //public static double[] AddDimensionToProbabilityMap(double[] initialCrossProductProbabilities, int[] dimensions, Func<int[], int, double> calcProbOfElementInRangeGivenDomain, int numRangeElements)
        //{
        //    var revisedCrossProductIndices = GetCrossProductsOfDiscreteRanges(dimensions);
        //    int numDomainElements = initialCrossProductProbabilities.Length;
        //    if (numDomainElements != revisedCrossProductIndices.GetLength(0))
        //        throw new ArgumentException();
        //    var initialResults = BuildProbabilityMap((int domainIndex, int rangeIndex) => calcProbOfElementInRangeGivenDomain(revisedCrossProductIndices[domainIndex], rangeIndex), numDomainElements, numRangeElements, initialCrossProductProbabilities);
        //    int numAugmentedCrossProducts = initialResults.crossProductProbabilities.Length;
        //    int[][] augmentedCrossProductIndices = new int[numAugmentedCrossProducts][];
        //    int i = 0;
        //    for (int d = 0; d < numDomainElements; d++)
        //    {
        //        int[] originalCrossProductIndices = revisedCrossProductIndices[d];
        //        for (int r = 0; r < numRangeElements; r++)
        //        {
        //            augmentedCrossProductIndices[i] = new int[originalCrossProductIndices.Length + 1];
        //            for (int j = 0; j < originalCrossProductIndices.Length; j++)
        //                augmentedCrossProductIndices[i][j] = originalCrossProductIndices[j];
        //            augmentedCrossProductIndices[i][originalCrossProductIndices.Length] = r;
        //            i++;
        //        }
        //    }
        //    return initialResults.crossProductProbabilities;
        //}

        /// <summary>
        /// Advances a tracker of indices of certain dimension -- e.g., from [0, 0, 0] to [0, 0, 1]] -- switching to the next dimension as necessary, and returning true with the last combination of indices.
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="indexTracker"></param>
        /// <returns></returns>
        private static bool AdvanceIndexTracker(int[] dimensions, int[] indexTracker)
        {

            for (int i = dimensions.Length - 1; i >= 0; i++)
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
            }
            return combinedNumeratorProbability / combinedDenominatorProbability;
        }

        public static double[] BuildProbabilityMapBasedOnDiscreteValueSignals(int[] dimensions, double[] domainProbabilities, int startIndex, List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)> producingExtraDimensions)
        {
            if (startIndex + producingExtraDimensions.Count() + 1 != dimensions.Length)
                throw new ArgumentException();
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
                    double probability = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(discreteValueSignalIndex, dvsParams)[rangeIndex];
                    return probability;
                }, dimensions[d], domainProbabilities);
                domainProbabilities = crossProductProbabilities;
            }
            return crossProductProbabilities;
        }
    }
}
