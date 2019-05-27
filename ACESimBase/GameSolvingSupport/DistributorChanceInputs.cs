using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Maintains either a single value representing an accumulation of actions from one or more decisions or an array of action-probability tuples, resulting from at least one decision that is a DistributorChanceInputDecision decision and moreover a DistributableDistributorChanceInput for purposes of accelerated best-response calculation, for which an array of possible values is passed forward in the algorithm instead of a single value.
    /// </summary>
    public readonly struct DistributorChanceInputs
    {
        public readonly int SingleScalarValue;
        public readonly List<(int value, double probability)> Distributed;

        public bool ContainsAccumulatedValue => SingleScalarValue != 0 || Distributed != null;

        public DistributorChanceInputs(int singleScalarValue)
        {
            SingleScalarValue = singleScalarValue;
            Distributed = null;
        }
        public DistributorChanceInputs(List<(int value, double probability)> distributed)
        {
            SingleScalarValue = 0;
            Distributed = distributed;
        }

        public DistributorChanceInputs AddDistributorChanceInput(ChanceNode chanceNode, byte action, bool distributeDistributableDistributorChanceInputs, out bool actionWasDistributed)
        {
            Decision decision = chanceNode.Decision;
            if (!decision.DistributorChanceInputDecision)
            {
                actionWasDistributed = false;
                return this;
            }
            int increment = action * decision.DistributorChanceInputDecisionMultiplier;
            if (decision.DistributableDistributorChanceInput && distributeDistributableDistributorChanceInputs)
            {
                // distribute all possible actions so that we are passing forward an array
                var oldAccumulated = Distributed ?? new List<(int value, double probability)>() { (SingleScalarValue, 1.0) };
                var distributed = new List<(int value, double probability)>();
                for (action = 1; action <= decision.NumPossibleActions; action++)
                {
                    double probability = chanceNode.GetActionProbability(action);
                    foreach (var old in oldAccumulated)
                        distributed.Add((old.value + increment, old.probability * probability));
                }
                actionWasDistributed = true;
                return new DistributorChanceInputs(distributed);
            }
            else
            {
                actionWasDistributed = false;
                if (Distributed == null)
                {
                    int singleScalarValue = SingleScalarValue + increment;
                    return new DistributorChanceInputs(singleScalarValue);
                }
                else
                {
                    int count = Distributed.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var d = Distributed[i];
                        Distributed[i] = (d.value + increment, d.probability);
                    }
                    return new DistributorChanceInputs(Distributed);
                }
            }
        }
    }
}
