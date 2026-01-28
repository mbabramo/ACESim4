using ACESim.Util.DiscreteProbabilities;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public class DiscreteValueParametersVariableProductionInstruction : VariableProductionInstruction
    {
        private DiscreteValueSignalParameters DVSParams;
        private double[][] ConditionalProbabilitiesGivenSource;
        public int SourceSignalIndex;
        public bool SourceIncludesExtremes;
        public double Stdev;

        public DiscreteValueParametersVariableProductionInstruction(int[] dimensions, int sourceSignalIndex, bool sourceIncludesExtremes, double stdev, int targetIndex) : base(dimensions, targetIndex)
        {
            SourceSignalIndex = sourceSignalIndex;
            SourceIncludesExtremes = sourceIncludesExtremes;
            Stdev = stdev;
            int numSourceElements = Dimensions[SourceSignalIndex]; // this is the number of items in the source signal, but note that the domainIndex in GetConditionalProbability refers to the permutation index, and this will be more if there are other variables too.
            DVSParams = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = numSourceElements,
                NumSignals = NumRangeElements,
                SourcePointsIncludeExtremes = SourceIncludesExtremes,
                StdevOfNormalDistribution = Stdev
            };
        }

        public DiscreteValueParametersVariableProductionInstruction(int[] dimensions, int sourceSignalIndex, double[][] conditionalProbabilitiesGivenSource, int targetIndex) : base(dimensions, targetIndex)
        {
            if (conditionalProbabilitiesGivenSource == null)
                throw new System.ArgumentNullException(nameof(conditionalProbabilitiesGivenSource));

            SourceSignalIndex = sourceSignalIndex;
            SourceIncludesExtremes = false;
            Stdev = 0.0;

            int numSourceElements = Dimensions[SourceSignalIndex];
            if (conditionalProbabilitiesGivenSource.Length != numSourceElements)
                throw new System.ArgumentException("Source dimension mismatch.", nameof(conditionalProbabilitiesGivenSource));

            for (int i = 0; i < conditionalProbabilitiesGivenSource.Length; i++)
            {
                double[] row = conditionalProbabilitiesGivenSource[i];
                if (row == null)
                    throw new System.ArgumentException("Conditional table contains a null row.", nameof(conditionalProbabilitiesGivenSource));
                if (row.Length != NumRangeElements)
                    throw new System.ArgumentException("Range dimension mismatch.", nameof(conditionalProbabilitiesGivenSource));
            }

            ConditionalProbabilitiesGivenSource = conditionalProbabilitiesGivenSource;
            DVSParams = default;
        }

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            int discreteValueSignalIndex = IncomingPermutations[domainIndex][SourceSignalIndex];

            if (ConditionalProbabilitiesGivenSource != null)
                return ConditionalProbabilitiesGivenSource[discreteValueSignalIndex][rangeIndex];

            double probability = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(discreteValueSignalIndex + 1 /* DiscreteValueSignal expects 1-based action */, DVSParams)[rangeIndex];
            return probability;
        }
    }

}
