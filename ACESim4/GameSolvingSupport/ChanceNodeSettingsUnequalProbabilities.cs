using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ChanceNodeSettingsUnequalProbabilities : ChanceNodeSettings
    {
        public double[] Probabilities;

        public override double GetActionProbability(int action)
        {
            if (DecisionByteCode == 12)
            {
                var DEBUG = 0;
            }
            return Probabilities[action - 1];
        }

        public override bool AllProbabilitiesEqual()
        {
            return false;
        }

        public override string ToString()
        {
            return $"Chance player {PlayerNum} for decision {DecisionByteCode} => probabilities {String.Join(",", Probabilities)}";
        }

    }
}
