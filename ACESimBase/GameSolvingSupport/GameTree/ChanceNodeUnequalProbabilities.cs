using ACESim;
using Rationals;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.GameTree
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
                AltNodeNumber = AltNodeNumber,
                Decision = Decision,
                DecisionIndex = DecisionIndex
            };
        }

        public double[] Probabilities;
        

        public double[] GetActionProbabilities(double weight = 1.0)
        {
            double[] probabilities = new double[Decision.NumPossibleActions];
            for (int action = 1; action <= Decision.NumPossibleActions; action++)
                probabilities[action - 1] = GetActionProbability(action) * weight;
            return probabilities;
        }

        public override double GetActionProbability(int action)
        {
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
            return $"{Decision.Abbreviation} (Info set {AltNodeNumber ?? ChanceNodeNumber}): Chance player {PlayerNum} for decision {DecisionByteCode} => probabilities {string.Join(",", Probabilities)}";
        }

    }
}
