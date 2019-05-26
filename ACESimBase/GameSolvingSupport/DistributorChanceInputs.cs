using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    public readonly struct DistributorChanceInputs
    {
        public readonly int SingleScalarValue;
        public readonly List<(int value, double probability)> Distributed;

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

        public DistributorChanceInputs AddScalarDistributorChanceInput(ChanceNode chanceNode, byte playerBeingOptimized, byte action, bool distributeDistributableDistributorChanceInputs, out bool actionWasDistributed)
        {
            Decision decision = chanceNode.Decision;
            if (!decision.DistributorChanceInputDecision)
            {
                actionWasDistributed = false;
                return this;
            }
            if (distributeDistributableDistributorChanceInputs && decision.DistributableDistributorChanceInput && decision.PlayerNumber != playerBeingOptimized)
            {
                // distribute all possible actions so that we are passing forward an array
                var oldAccumulated = Distributed ?? new List<(int value, double probability)>() { (SingleScalarValue, 1.0) };
                var distributed = new List<(int value, double probability)>();
                for (action = 1; action <= decision.NumPossibleActions; action++)
                {
                    double probability = chanceNode.GetActionProbability(action);
                    foreach (var old in oldAccumulated)
                        Distributed.Add((old.value + action * decision.DistributorChanceInputDecisionMultiplier, old.probability * probability));
                }
                actionWasDistributed = true;
                return new DistributorChanceInputs(distributed);
            }
            else
            {
                actionWasDistributed = false;
                if (Distributed == null)
                {
                    int singleScalarValue = SingleScalarValue + action * decision.DistributorChanceInputDecisionMultiplier;
                    return new DistributorChanceInputs(singleScalarValue);
                }
                else
                {
                    int count = Distributed.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var d = Distributed[i];
                        Distributed[i] = (d.value + action * decision.DistributorChanceInputDecisionMultiplier, d.probability);
                    }
                    return new DistributorChanceInputs(Distributed);
                }
            }
        }
    }
}
