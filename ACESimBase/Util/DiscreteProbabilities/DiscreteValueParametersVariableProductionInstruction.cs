using ACESim.Util.DiscreteProbabilities;
using System;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public class DiscreteValueParametersVariableProductionInstruction : VariableProductionInstruction
    {
        DiscreteValueSignalParameters DVSParams;
        int SourceSignalIndex;

        public DiscreteValueParametersVariableProductionInstruction(int[] Dimensions, int sourceSignalIndex, bool sourceIncludesExtremes, double stdev, int targetIndex) : base(Dimensions, targetIndex)
        {
            SourceSignalIndex = sourceSignalIndex;
            DVSParams = new DiscreteValueSignalParameters()
            {
                SourcePointsIncludeExtremes = sourceIncludesExtremes,
                NumSignals = NumRangeElements,
                NumPointsInSourceUniformDistribution = Dimensions[sourceSignalIndex],
                StdevOfNormalDistribution = stdev
            };
        }

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(IncomingPermutations[domainIndex][SourceSignalIndex] + 1, DVSParams)[rangeIndex];
        }
    }

    public class PrecomputedConditionalProbabilitiesVariableProductionInstruction : VariableProductionInstruction
    {
        private readonly double[][] ConditionalProbabilitiesGivenSourceValue;
        private readonly int SourceSignalIndex;

        public PrecomputedConditionalProbabilitiesVariableProductionInstruction(
            int[] Dimensions,
            int sourceSignalIndex,
            int targetIndex,
            double[][] conditionalProbabilitiesGivenSourceValue) : base(Dimensions, targetIndex)
        {
            if (Dimensions == null)
                throw new ArgumentNullException(nameof(Dimensions));
            if (conditionalProbabilitiesGivenSourceValue == null)
                throw new ArgumentNullException(nameof(conditionalProbabilitiesGivenSourceValue));
            if (sourceSignalIndex < 0 || sourceSignalIndex >= Dimensions.Length)
                throw new ArgumentOutOfRangeException(nameof(sourceSignalIndex));
            if (targetIndex < 0 || targetIndex >= Dimensions.Length)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));
            if (sourceSignalIndex >= targetIndex)
                throw new ArgumentException("Source index must be less than target index so that it is included in the domain permutations.");

            SourceSignalIndex = sourceSignalIndex;

            int expectedSourceValueCount = Dimensions[sourceSignalIndex];
            if (conditionalProbabilitiesGivenSourceValue.Length != expectedSourceValueCount)
                throw new ArgumentException("Source value dimension mismatch.", nameof(conditionalProbabilitiesGivenSourceValue));

            for (int s = 0; s < expectedSourceValueCount; s++)
            {
                if (conditionalProbabilitiesGivenSourceValue[s] == null)
                    throw new ArgumentException("Null conditional row.", nameof(conditionalProbabilitiesGivenSourceValue));
                if (conditionalProbabilitiesGivenSourceValue[s].Length != NumRangeElements)
                    throw new ArgumentException("Target value dimension mismatch.", nameof(conditionalProbabilitiesGivenSourceValue));
            }

            ConditionalProbabilitiesGivenSourceValue = conditionalProbabilitiesGivenSourceValue;
        }

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            int sourceValueIndex = IncomingPermutations[domainIndex][SourceSignalIndex];
            return ConditionalProbabilitiesGivenSourceValue[sourceValueIndex][rangeIndex];
        }
    }
}
