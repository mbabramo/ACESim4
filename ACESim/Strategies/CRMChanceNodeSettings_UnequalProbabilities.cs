using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class CRMChanceNodeSettings_UnequalProbabilities : CRMChanceNodeSettings
    {
        public double[] Probabilities;

        public override double GetActionProbability(int action)
        {
            return Probabilities[action - 1];
        }

        public override bool AllProbabilitiesEqual()
        {
            return false;
        }
    }
}
