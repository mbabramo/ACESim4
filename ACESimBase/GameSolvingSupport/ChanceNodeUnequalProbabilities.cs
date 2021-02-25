using Rationals;
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


        public override ChanceNode DeepCopy()
        {
            return new ChanceNodeUnequalProbabilities(ChanceNodeNumber)
            {
                Probabilities = Probabilities.ToArray(),
                ProbabilitiesForDistributorChanceInputs = new Dictionary<int, double[]>(ProbabilitiesForDistributorChanceInputs),
                DistributionComplete = DistributionComplete,
                AltNodeNumber = AltNodeNumber,
                Decision = Decision,
                DecisionIndex = DecisionIndex
            };
        }

        public double[] Probabilities;
        // The remaining settings are for distributed chance actions.
        public Dictionary<int, double[]> ProbabilitiesForDistributorChanceInputs
        {
            get;
            set;
        }
        bool DistributionComplete;

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

        public override Rational[] GetProbabilitiesAsRationals(bool makeAllProbabilitiesPositive, int maxIntegralUtility)
        {
            Rational minProbability = (Rational)1 / (Rational)maxIntegralUtility; // TODO -- better approach would be to trim the game tree.
            var results = GetActionProbabilities().Select(x => (int)Math.Round(x * maxIntegralUtility)).Select(x => (Rational)x / (Rational)maxIntegralUtility).Select(x => x < minProbability && makeAllProbabilitiesPositive ? minProbability : x).ToArray(); // NOTE: We set a minimium probability level of 1 / MaxIntegralUtility.
                                                                                                                                                                                                                                // make numbers add up to exactly 1
            Rational total = 0;
            for (int i = 0; i < results.Length; i++)
            {
                if (i < results.Length - 1)
                {
                    results[i] = results[i].CanonicalForm;
                    total += results[i];
                    total = total.CanonicalForm;
                }
                else
                {
                    results[i] = ((Rational)1 - total).CanonicalForm;
                    if (results[i].IsZero && makeAllProbabilitiesPositive)
                    {
                        int largestIndex = results.Select((item, index) => (item, index)).OrderByDescending(x => x.item).First().index;
                        results[largestIndex] -= minProbability;
                        results[i] = minProbability;
                    }
                }
            }
            // adjust the chance node probabilities so that they exactly match the rational numbers
            for (int i = 0; i < results.Length; i++)
            {
                Probabilities[i] = (double)results[i];
            }
            return results;
        }

        public override string ToString()
        {
            return $"{Decision.Abbreviation} (Info set {AltNodeNumber ?? ChanceNodeNumber}): Chance player {PlayerNum} for decision {DecisionByteCode} => probabilities {String.Join(",", Probabilities)}";
        }

    }
}
