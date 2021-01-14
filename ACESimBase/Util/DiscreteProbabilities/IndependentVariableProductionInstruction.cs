using System.Linq;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public class IndependentVariableProductionInstruction : VariableProductionInstruction
    {
        double[] Probabilities;

        public IndependentVariableProductionInstruction(int[] Dimensions, int TargetIndex, double[] probabilities) : base(Dimensions, TargetIndex)
        {
            Probabilities = probabilities ?? Enumerable.Range(0, NumRangeElements).Select(x => 1.0 / (double)NumRangeElements).ToArray();
        }

        public override double GetConditionalProbability(int domainIndex, int rangeIndex)
        {
            return Probabilities[rangeIndex];
        }
    }
}
