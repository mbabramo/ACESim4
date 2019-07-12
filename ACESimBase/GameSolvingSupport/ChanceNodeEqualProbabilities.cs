﻿using System;
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

        public override double GetActionProbability(int action, int distributorChanceInputs = -1) => EachProbability;

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
