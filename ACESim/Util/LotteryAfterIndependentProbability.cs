using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class LotteryAfterIndependentProbability
    {
        public static double[] GetProbabilityWinning(double[] independentProbabilitiesOfInvention)
        {
            bool[] lockedInAnswers = new bool[independentProbabilitiesOfInvention.Length];
            double[] returnValues = new double[independentProbabilitiesOfInvention.Length];
            GetProbabilityWinning(independentProbabilitiesOfInvention, returnValues, lockedInAnswers, 0);
            return returnValues;
        }

        private static void GetProbabilityWinning(double[] independentProbabilitiesOfInvention, double[] returnValues, bool[] lockedInAnswers, int numLockedIn)
        {
            if (numLockedIn == independentProbabilitiesOfInvention.Length)
            {
                double p = 1.0;
                for (int i = 0; i < independentProbabilitiesOfInvention.Length; i++)
                    if (lockedInAnswers[i])
                        p *= independentProbabilitiesOfInvention[i];
                    else
                        p *= 1.0 - independentProbabilitiesOfInvention[i];
                int numLockedInAsTrue = lockedInAnswers.Count(x => x == true);
                double incrementEachTrueBy = p / (double)numLockedInAsTrue;
                if (numLockedInAsTrue > 0)
                    for (int i = 0; i < independentProbabilitiesOfInvention.Length; i++)
                        if (lockedInAnswers[i])
                            returnValues[i] += incrementEachTrueBy;
            }
            else
            {
                lockedInAnswers[numLockedIn] = true;
                GetProbabilityWinning(independentProbabilitiesOfInvention, returnValues, lockedInAnswers, numLockedIn + 1);
                lockedInAnswers[numLockedIn] = false;
                GetProbabilityWinning(independentProbabilitiesOfInvention, returnValues, lockedInAnswers, numLockedIn + 1);
            }
        }
    }
}
