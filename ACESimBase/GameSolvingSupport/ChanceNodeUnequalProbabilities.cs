using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ChanceNodeUnequalProbabilities : ChanceNode
    {
        public double[] Probabilities;
        // The remaining settings are for distributed chance actions.
        public Dictionary<int, double[]> ProbabilitiesForDistributorChanceInputs;
        bool DistributionComplete;

        /// <summary>
        /// This is used to calculate the uneven chance probabilities for a nondistributed action.
        /// </summary>
        /// <param name="piChance"></param>
        /// <param name="probabilityIncrements"></param>
        public void RegisterProbabilityForDistributorChanceInput(double piChance, int distributorChanceInputs, double[] probabilityIncrements)
        {
            if (ProbabilitiesForDistributorChanceInputs == null)
                ProbabilitiesForDistributorChanceInputs = new Dictionary<int, double[]>();
            if (!ProbabilitiesForDistributorChanceInputs.ContainsKey(distributorChanceInputs))
                ProbabilitiesForDistributorChanceInputs[distributorChanceInputs] = new double[Decision.NumPossibleActions];
            double[] currentValues = ProbabilitiesForDistributorChanceInputs[distributorChanceInputs];
            for (int i = 0; i < currentValues.Length; i++)
                currentValues[i] += probabilityIncrements[i] * piChance;
        }

        public void NormalizeDistributorChanceInputProbabilities()
        {
            if (ProbabilitiesForDistributorChanceInputs != null)
            {
                var keys = ProbabilitiesForDistributorChanceInputs.Keys.ToList();
                foreach (int distributorChanceInputs in keys)
                {
                    double[] unnormalized = ProbabilitiesForDistributorChanceInputs[distributorChanceInputs];
                    double sum = unnormalized.Sum();
                    if (sum > 0)
                    {
                        double[] normalized = unnormalized.Select(x => x / sum).ToArray();
                        ProbabilitiesForDistributorChanceInputs[distributorChanceInputs] = normalized;
                    }
                }
            }
            DistributionComplete = true;
        }

        public double[] GetActionProbabilities(double weight = 1.0)
        {
            double[] probabilities = new double[Decision.NumPossibleActions];
            for (int action = 1; action <= Decision.NumPossibleActions; action++)
                probabilities[action - 1] = GetActionProbability(action) * weight;
            return probabilities;
        }

        public override double GetActionProbability(int action, int distributorChanceInputs = -1)
        {
            if (distributorChanceInputs != -1 && DistributionComplete && ProbabilitiesForDistributorChanceInputs != null)
                return ProbabilitiesForDistributorChanceInputs[distributorChanceInputs][action - 1];
            return Probabilities[action - 1];
        }


        public override double GetActionProbability(int action, DistributorChanceInputs distributorChanceInputs)
        {
            if (DistributionComplete && ProbabilitiesForDistributorChanceInputs != null && distributorChanceInputs.ContainsAccumulatedValue)
            {
                double sumProbabilityProducts = 0;
                if (distributorChanceInputs.Distributed != null)
                {
                    foreach (var input in distributorChanceInputs.Distributed)
                    {
                        // We have probabilities for specific distributor chance input values. But now we have multiple values, each with another probability. So, we need to calculate a weighted average.
                        double probabilityForDistributorChanceInputValue = ProbabilitiesForDistributorChanceInputs[input.value][action - 1];
                        sumProbabilityProducts += probabilityForDistributorChanceInputValue * input.probability;
                    }
                    return sumProbabilityProducts;
                }
                else
                    return ProbabilitiesForDistributorChanceInputs[distributorChanceInputs.SingleScalarValue][action - 1];
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
