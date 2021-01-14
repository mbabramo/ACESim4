using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.DiscreteProbabilities.DiscreteProbabilityDistribution;

namespace ACESimBase.Util.DiscreteProbabilities
{
    public abstract class VariableProductionInstruction
    {
        public int[] Dimensions;
        public int TargetIndex;
        public int[][] IncomingPermutations;
        public int NumRangeElements;
        public VariableProductionInstruction(int[] dimensions, int targetIndex)
        {
            Dimensions = dimensions;
            TargetIndex = targetIndex;
            NumRangeElements = Dimensions[TargetIndex];
            int[] incomingDimensions = Dimensions.Take(TargetIndex).ToArray();
            if (incomingDimensions.Length != 0)
                IncomingPermutations = GetAllPermutations(incomingDimensions);
        }

        public abstract double GetConditionalProbability(int domainIndex, int rangeIndex);
    }
}
