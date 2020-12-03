using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim.Util
{
    public class CombinationMaker
    {
        /// <summary>
        /// A front-end to CalculateCombinations
        /// </summary>
        public static List<List<int>> CalculateCombinations(int maxComboSum, int numPerCombo, bool allowNegativeExponents)
        {
            List<List<int>> combinations = new List<List<int>>(); // Initalize the object that the helper function will fill
            CalculateCombinationsBackend(maxComboSum, numPerCombo, 0, new List<int>(), combinations, allowNegativeExponents);
            return combinations;
        }
        /// <summary>
        /// Recursively calculates all the possible lists of 'numPerCombo' number of integers the absolute sum of which is less than or equal to maxComboSum.
        /// </summary>
        /// <param name="maxComboSum"></param>
        /// <param name="numPerCombo"></param>
        /// <param name="comboSum">The current absolute sum of the integers in 'currentCombo'.</param>
        /// <param name="currentCombo">The current combination being tested.  Each recursion has a unique version of this.</param>
        /// <param name="combinations">A running collection of all acceptable combinations.  Passed to each recursion so that if a combination is acceptable and finished, the method adds the combination to this object</param>
        static void CalculateCombinationsBackend(
            int maxComboSum, 
            int numPerCombo, 
            int comboSum, 
            List<int> currentCombo, 
            List<List<int>> combinations,
            bool allowNegativeExponents)
        {
            // If the current combination has the required number of members (inputs) then it is ready to be added to the running collection.
            if (currentCombo.Count >= numPerCombo)
            {
                combinations.Add(currentCombo);
                return;
            }

            // Otherwise recurse upon diffs
            for (int possibleNum = 0; possibleNum <= maxComboSum; possibleNum++)
            {
                // Check whether the current possibleDiff will stay within the required maxDiffSum
                int newSum = possibleNum + comboSum;
                if (newSum <= maxComboSum)
                {
                    // If so, then recurse by adding it to diffs as both a positive and negative integer
                    List<int> newPosDiffs = new List<int>(currentCombo)
                    {
                        possibleNum
                    };
                    CalculateCombinationsBackend(maxComboSum, numPerCombo, newSum, newPosDiffs, combinations, allowNegativeExponents);

                    if (
                        possibleNum > 0 &&// don't bother recursing on positive zero AND negative zero.
                        allowNegativeExponents
                        ) 
                    {
                        // (Here's the negative version)
                        List<int> newNegDiffs = new List<int>(currentCombo)
                        {
                            -1 * possibleNum
                        };
                        CalculateCombinationsBackend(maxComboSum, numPerCombo, newSum, newNegDiffs, combinations, allowNegativeExponents);
                    }
                }
            }
        }
    }
}
