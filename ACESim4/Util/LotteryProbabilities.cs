using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class LotteryProbabilities
    {
        /// <summary>
        /// If various people have some chance of achieving a result, and we know that if someone achieves a result then anyone with a higher probability also achieves the result, and there is just one winner randomly selected if the result is achieved, what is the probability of achieving the result and being selected the winner?
        /// </summary>
        /// <param name="dependentProbabilities"></param>
        /// <returns></returns>
        public static double[] GetProbabilityOfBeingUltimateWinner_DependentProbabilities(double[] dependentProbabilities)
        {
            double[] ordered = dependentProbabilities.OrderBy(x => x).ToArray();
            double[] returnValues = new double[dependentProbabilities.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                // Say we have 0.15, 0.20, 0.20, 0.30, and 0.40, and we're looking at the chance for the party with 0.30. Then, we have (0.15 / 5) + ((0.20 - 0.15) / 4) + (0.30 - 0.20) / 2 = 0.0925.
                double total = 0;
                double last = 0;
                for (int j = 0; j < ordered.Length; j++)
                {
                    if (j > i)
                        break;
                    double current = ordered[j];
                    int numberHereOrHigher = ordered.Length - j;
                    total += (current - last) / (double)numberHereOrHigher;
                    last = current;
                }
                // now we must put the total in the proper place(s) in the array
                for (int j = 0; j < dependentProbabilities.Length; j++)
                    if (dependentProbabilities[j] == ordered[i])
                        returnValues[j] = total;
            }
            return returnValues;
        }

        /// <summary>
        /// If we have a set of independent probabilities of achieving a result, but only one winner is selected in the event multiple people achieve the result, then what is the probability that each achieves the result and is selected winner?
        /// </summary>
        /// <param name="independentProbabilities"></param>
        /// <returns></returns>
        public static double[] GetProbabilityOfBeingUltimateWinner_IndependentProbabilities(double[] independentProbabilities)
        {
            bool[] lockedInAnswers = new bool[independentProbabilities.Length];
            double[] returnValues = new double[independentProbabilities.Length];
            GetProbabilityWinning(independentProbabilities, returnValues, lockedInAnswers, 0);
            return returnValues;
        }

        private static void GetProbabilityWinning(double[] independentProbabilities, double[] returnValues, bool[] lockedInAnswers, int numLockedIn)
        {
            if (numLockedIn == independentProbabilities.Length)
            {
                double p = 1.0;
                for (int i = 0; i < independentProbabilities.Length; i++)
                    if (lockedInAnswers[i])
                        p *= independentProbabilities[i];
                    else
                        p *= 1.0 - independentProbabilities[i];
                int numLockedInAsTrue = lockedInAnswers.Count(x => x == true);
                double incrementEachTrueBy = p / (double)numLockedInAsTrue;
                if (numLockedInAsTrue > 0)
                    for (int i = 0; i < independentProbabilities.Length; i++)
                        if (lockedInAnswers[i])
                            returnValues[i] += incrementEachTrueBy;
            }
            else
            {
                lockedInAnswers[numLockedIn] = true;
                GetProbabilityWinning(independentProbabilities, returnValues, lockedInAnswers, numLockedIn + 1);
                lockedInAnswers[numLockedIn] = false;
                GetProbabilityWinning(independentProbabilities, returnValues, lockedInAnswers, numLockedIn + 1);
            }
        }
    }
}
