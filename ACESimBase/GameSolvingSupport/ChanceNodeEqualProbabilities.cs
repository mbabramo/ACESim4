using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ChanceNodeEqualProbabilities : ChanceNode
    {
        public double EachProbability;
        public ChanceNodeEqualProbabilities(int chanceNodeNumber) : base(chanceNodeNumber)
        {
        }

        public override ChanceNode DeepCopy()
        {
            return new ChanceNodeEqualProbabilities(ChanceNodeNumber)
            {
                EachProbability = EachProbability,
                AltNodeNumber = AltNodeNumber,
                Decision = Decision,
                DecisionIndex = DecisionIndex
            };
        }

        public override double GetActionProbability(int action, int distributorChanceInputs = -1) => EachProbability;

        public override bool AllProbabilitiesEqual()
        {
            return true;
        }

        public override string ToString()
        {
            return $"{Decision.Abbreviation} (Info set {AltNodeNumber ?? ChanceNodeNumber}): Chance player {PlayerNum} for decision {DecisionByteCode} => Equal probabilities {EachProbability}";
        }

        public override Rational[] GetProbabilitiesAsRationals(bool makeAllProbabilitiesPositive, int maxIntegralUtility)
        {
            int numPossibilities = GetNumPossibleActions();
            Rational eachProbability = (Rational)1 / (Rational)numPossibilities;
            return Enumerable.Range(0, numPossibilities).Select(x => eachProbability).ToArray();
        }
    }
}
