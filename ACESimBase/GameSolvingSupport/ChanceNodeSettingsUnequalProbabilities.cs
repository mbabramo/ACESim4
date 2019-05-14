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
        public Dictionary<int, double[]> ProbabilitiesForNondistributedActions;
        bool DistributionComplete;

        /// <summary>
        /// This is used to calculate the uneven chance probabilities for a nondistributed action.
        /// </summary>
        /// <param name="piChance"></param>
        /// <param name="probabilityIncrements"></param>
        public void RegisterProbabilityForNondistributedAction(double piChance, int nondistributedActions, double[] probabilityIncrements)
        {
            if (ProbabilitiesForNondistributedActions == null)
                ProbabilitiesForNondistributedActions = new Dictionary<int, double[]>();
            if (!ProbabilitiesForNondistributedActions.ContainsKey(nondistributedActions))
                ProbabilitiesForNondistributedActions[nondistributedActions] = new double[Decision.NumPossibleActions];
            double[] currentValues = ProbabilitiesForNondistributedActions[nondistributedActions];
            for (int i = 0; i < currentValues.Length; i++)
                currentValues[i] += probabilityIncrements[i] * piChance;
        }

        public void NormalizeNondistributedActionProbabilities()
        {
            if (ProbabilitiesForNondistributedActions != null)
            {
                var keys = ProbabilitiesForNondistributedActions.Keys.ToList();
                foreach (int nondistributedActions in keys)
                {
                    double[] unnormalized = ProbabilitiesForNondistributedActions[nondistributedActions];
                    double sum = unnormalized.Sum();
                    if (sum > 0)
                    {
                        double[] normalized = unnormalized.Select(x => x / sum).ToArray();
                        ProbabilitiesForNondistributedActions[nondistributedActions] = normalized;
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

        public override double GetActionProbability(int action, int nondistributedActions = -1)
        {
            if (nondistributedActions != -1 && DistributionComplete && ProbabilitiesForNondistributedActions != null)
                return ProbabilitiesForNondistributedActions[nondistributedActions][action - 1];
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
