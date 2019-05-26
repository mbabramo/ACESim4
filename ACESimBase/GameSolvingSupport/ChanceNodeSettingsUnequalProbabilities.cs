using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ChanceNodeSettingsUnequalProbabilities : ChanceNodeSettings
    {
        public double[] Probabilities;
        // The remaining settings are for distributed chance actions.
        public Dictionary<int, double[]> ProbabilitiesFordistributorChanceInputs;
        bool DistributionComplete;

        /// <summary>
        /// This is used to calculate the uneven chance probabilities for a nondistributed action.
        /// </summary>
        /// <param name="piChance"></param>
        /// <param name="probabilityIncrements"></param>
        public void RegisterProbabilityFordistributorChanceInput(double piChance, int distributorChanceInputs, double[] probabilityIncrements)
        {
            if (ProbabilitiesFordistributorChanceInputs == null)
                ProbabilitiesFordistributorChanceInputs = new Dictionary<int, double[]>();
            if (!ProbabilitiesFordistributorChanceInputs.ContainsKey(distributorChanceInputs))
                ProbabilitiesFordistributorChanceInputs[distributorChanceInputs] = new double[Decision.NumPossibleActions];
            double[] currentValues = ProbabilitiesFordistributorChanceInputs[distributorChanceInputs];
            for (int i = 0; i < currentValues.Length; i++)
                currentValues[i] += probabilityIncrements[i] * piChance;
        }

        public void NormalizedistributorChanceInputProbabilities()
        {
            if (ProbabilitiesFordistributorChanceInputs != null)
            {
                var keys = ProbabilitiesFordistributorChanceInputs.Keys.ToList();
                foreach (int distributorChanceInputs in keys)
                {
                    double[] unnormalized = ProbabilitiesFordistributorChanceInputs[distributorChanceInputs];
                    double sum = unnormalized.Sum();
                    if (sum > 0)
                    {
                        double[] normalized = unnormalized.Select(x => x / sum).ToArray();
                        ProbabilitiesFordistributorChanceInputs[distributorChanceInputs] = normalized;
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
            if (distributorChanceInputs != -1 && DistributionComplete && ProbabilitiesFordistributorChanceInputs != null)
                return ProbabilitiesFordistributorChanceInputs[distributorChanceInputs][action - 1];
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
