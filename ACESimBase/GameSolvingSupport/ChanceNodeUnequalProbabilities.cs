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
        /// This is used to calculate the uneven chance probabilities given distributor chance inputs. This is called for a distributor chance input decision (e.g., a player's hidden signal) or a distributor chance decision (i.e., a chance decision whose values will differ depending on the value of that hidden signal). It will be called multiple times for the various distributor chance input values, so that the probability increments (for example, from different values of a card hidden to all players) can be added.
        /// </summary>
        /// <param name="piChance"></param>
        /// <param name="probabilityIncrements"></param>
        public void RegisterProbabilityForDistributorChanceInput(double piChance, int distributorChanceInputs, double[] probabilityIncrements)
        {
            if (ChanceNodeNumber == 14)
            {
                var DEBUG = 0;
            }
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
            if (distributorChanceInputs != -1 && DistributionComplete && ProbabilitiesForDistributorChanceInputs != null && (Decision.DistributorChanceDecision || Decision.DistributorChanceInputDecision))
                return ProbabilitiesForDistributorChanceInputs[distributorChanceInputs][action - 1];
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
