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
        public ChanceNodeUnequalProbabilities(int chanceNodeNumber) : base(chanceNodeNumber)
        {
        }

        public double[] Probabilities;
        // The remaining settings are for distributed chance actions.
        public Dictionary<int, double[]> ProbabilitiesForDistributorChanceInputs;
        bool DistributionComplete;


        public override (int, int) GetActionProbabilityAsRational(int denominator, int action, int distributorChanceInputs = -1)
        {
            var unroundedActionProbability = GetActionProbability(action, distributorChanceInputs);
            int numerator = (int) Math.Round(unroundedActionProbability * (double)denominator);
            // simplify fraction
            for (int i = 2; i <= numerator; i++)
            {
                if (numerator % i == 0 && denominator % i == 0)
                {
                    numerator /= i;
                    denominator /= i;
                    i--; // check same factor again
                }
            }
            return (numerator, denominator);
        }

        /// <summary>
        /// This is used to calculate the uneven chance probabilities given distributor chance inputs. This is called for a distributor chance input decision (e.g., a player's hidden signal) or a distributor chance decision (i.e., a chance decision whose values will differ depending on the value of that hidden signal). It will be called multiple times for the various distributor chance input values, so that the probability increments (for example, from different values of a card hidden to all players) can be added.
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
            return $"{Decision.Abbreviation} (Info set {AltNodeNumber ?? ChanceNodeNumber}): Chance player {PlayerNum} for decision {DecisionByteCode} => probabilities {String.Join(",", Probabilities)}";
        }

    }
}
