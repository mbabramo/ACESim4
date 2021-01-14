using ACESim.Util.DiscreteProbabilities;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public class DiscreteValueParametersVariableProductionInstruction : VariableProductionInstruction
    {
        private DiscreteValueSignalParameters DVSParams;
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

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            int discreteValueSignalIndex = IncomingPermutations[domainIndex][SourceSignalIndex];
            double probability = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(discreteValueSignalIndex + 1 /* DiscreteValueSignal expects 1-based action */, DVSParams)[rangeIndex];
            return probability;
        }
    }
}
