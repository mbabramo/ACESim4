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
        // The remaining settings are for distributed chance actions.
        public double[] ProbabilitiesForNondistributedActions;
        public Dictionary<int, double[]> ProbabilitiesGivenNondistributedActions;
        bool DistributionComplete;

        /// <summary>
        /// This is used to calculate the uneven chance probabilities for a nondistributed action.
        /// </summary>
        /// <param name="piChance"></param>
        /// <param name="probabilityIncrements"></param>
        public void RegisterProbabilityForNondistributedAction(double piChance, double[] probabilityIncrements)
        {
            if (ProbabilitiesForNondistributedActions == null)
                ProbabilitiesForNondistributedActions = new double[probabilityIncrements.Length];
            for (int i = 0; i < probabilityIncrements.Length; i++)
                ProbabilitiesForNondistributedActions[i] += piChance * probabilityIncrements[i];
        }

        /// <summary>
        /// This is used to calculate the distributed uneven chance probabilities, given particular nondistributed actions.
        /// </summary>
        /// <param name="piChance">The probability that chance would play up to this decision</param>
        /// <param name="nondistributedActions">An integer distinctly representing all nondistributed decisions differentiating the probabilities at this chance node, given the distribution of distributed decisions.</param>
        /// <param name="probabilityIncrements">The unequal chance probabilities that would obtain given the distributed and nondistributed actions.</param>
        public void RegisterProbabilityIncrementsGivenNondistributedActions(double piChance, int nondistributedActions, double[] probabilityIncrements)
        {
            if (ProbabilitiesGivenNondistributedActions == null)
                ProbabilitiesGivenNondistributedActions = new Dictionary<int, double[]>();
            if (!ProbabilitiesGivenNondistributedActions.ContainsKey(nondistributedActions))
                ProbabilitiesGivenNondistributedActions[nondistributedActions] = new double[Decision.NumPossibleActions];
            double[] currentValues = ProbabilitiesGivenNondistributedActions[nondistributedActions];
            for (int i = 0; i < currentValues.Length; i++)
                currentValues[i] += probabilityIncrements[i] * piChance;
        }

        public void NormalizeNondistributedActionProbabilities()
        {
            if (ProbabilitiesForNondistributedActions != null)
            {
                double[] unnormalized = ProbabilitiesForNondistributedActions;
                double sum = unnormalized.Sum();
                if (sum > 0)
                {
                    double[] normalized = unnormalized.Select(x => x / sum).ToArray();
                    ProbabilitiesForNondistributedActions = normalized;
                }
            }
            if (ProbabilitiesGivenNondistributedActions != null)
            {
                var keys = ProbabilitiesGivenNondistributedActions.Keys.ToList();
                foreach (int nondistributedActions in keys)
                {
                    double[] unnormalized = ProbabilitiesGivenNondistributedActions[nondistributedActions];
                    double sum = unnormalized.Sum();
                    if (sum > 0)
                    {
                        double[] normalized = unnormalized.Select(x => x / sum).ToArray();
                        ProbabilitiesGivenNondistributedActions[nondistributedActions] = normalized;
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
            if (DistributionComplete)
            {
                if (ProbabilitiesForNondistributedActions != null)
                    return ProbabilitiesForNondistributedActions[action - 1];
                if (ProbabilitiesGivenNondistributedActions != null)
                    return ProbabilitiesGivenNondistributedActions[nondistributedActions][action - 1];
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
