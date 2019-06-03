using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Maintains a single value representing an accumulation of actions from zero or more decisions that is a DistributorChanceInputDecision decision
    /// </summary>
    public readonly struct DistributorChanceInputs
    {
        public readonly int SingleScalarValue;

        public bool ContainsAccumulatedValue => SingleScalarValue != 0;

        public DistributorChanceInputs(int singleScalarValue)
        {
            SingleScalarValue = singleScalarValue;
        }

        public DistributorChanceInputs AddDistributorChanceInput(ChanceNode chanceNode, byte action)
        {
            Decision decision = chanceNode.Decision;
            if (!decision.DistributorChanceInputDecision)
            {
                return this;
            }
            int increment = action * decision.DistributorChanceInputDecisionMultiplier;
            int singleScalarValue = SingleScalarValue + increment;
            return new DistributorChanceInputs(singleScalarValue);
        }
    }
}
