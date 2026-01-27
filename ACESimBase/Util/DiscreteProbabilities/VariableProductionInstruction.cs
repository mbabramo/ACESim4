using System;
using System.Linq;
using static ACESimBase.Util.DiscreteProbabilities.DiscreteProbabilityDistribution;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public abstract class VariableProductionInstruction
    {
        public int[] Dimensions;
        public int TargetIndex;
        public int[][] IncomingPermutations;

        public int NumRangeElements => Dimensions[TargetIndex];
        public int NumDomainPermutations => IncomingPermutations.Length;

        public VariableProductionInstruction(int[] Dimensions, int TargetIndex)
        {
            this.Dimensions = Dimensions;
            this.TargetIndex = TargetIndex;
            IncomingPermutations = DiscreteProbabilityDistribution.GetPermutations(Dimensions.Take(TargetIndex).ToArray());
        }

        public abstract double GetConditionalProbability(int domainIndex, int rangeIndex);
    }

    public sealed class ConditionalProbabilityTableVariableProductionInstruction : VariableProductionInstruction
    {
        private readonly int _sourceVariableIndexInDomainPermutation;
        private readonly double[][] _conditionalProbabilitiesGivenSourceValue;

        public ConditionalProbabilityTableVariableProductionInstruction(
            int[] dimensions,
            int targetIndex,
            int sourceVariableIndex,
            double[][] conditionalProbabilitiesGivenSourceValue)
            : base(dimensions, targetIndex)
        {
            if (dimensions == null)
                throw new ArgumentNullException(nameof(dimensions));
            if (conditionalProbabilitiesGivenSourceValue == null)
                throw new ArgumentNullException(nameof(conditionalProbabilitiesGivenSourceValue));
            if (targetIndex <= 0 || targetIndex >= dimensions.Length)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));
            if (sourceVariableIndex < 0 || sourceVariableIndex >= targetIndex)
                throw new ArgumentOutOfRangeException(nameof(sourceVariableIndex));

            int expectedSourceCount = dimensions[sourceVariableIndex];
            int expectedTargetCount = dimensions[targetIndex];

            if (conditionalProbabilitiesGivenSourceValue.Length != expectedSourceCount)
                throw new ArgumentException("Conditional probability table outer dimension does not match the source variable cardinality.", nameof(conditionalProbabilitiesGivenSourceValue));

            for (int i = 0; i < conditionalProbabilitiesGivenSourceValue.Length; i++)
            {
                double[] row = conditionalProbabilitiesGivenSourceValue[i];
                if (row == null)
                    throw new ArgumentException("Conditional probability table contains a null row.", nameof(conditionalProbabilitiesGivenSourceValue));
                if (row.Length != expectedTargetCount)
                    throw new ArgumentException("Conditional probability table row length does not match the target variable cardinality.", nameof(conditionalProbabilitiesGivenSourceValue));
            }

            _sourceVariableIndexInDomainPermutation = sourceVariableIndex;
            _conditionalProbabilitiesGivenSourceValue = conditionalProbabilitiesGivenSourceValue;
        }

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            int sourceValue = IncomingPermutations[domainIndex][_sourceVariableIndexInDomainPermutation];
            return _conditionalProbabilitiesGivenSourceValue[sourceValue][rangeIndex];
        }
    }
}
