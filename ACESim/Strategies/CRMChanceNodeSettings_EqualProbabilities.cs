using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class CRMChanceNodeSettings_EqualProbabilities : CRMChanceNodeSettings
    {
        public double EachProbability;

        public override double GetActionProbability(int action)
        {
            return EachProbability;
        }

        public override bool AllProbabilitiesEqual()
        {
            return true;
        }

        public override string ToString()
        {
            return $"Chance player {PlayerNum} for decision {DecisionByteCode} => Equal probabilities {EachProbability}";
        }
    }
}
