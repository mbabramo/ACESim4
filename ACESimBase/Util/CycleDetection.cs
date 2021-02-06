using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public static class CycleDetection
    {
        /// <summary>
        /// Finds whether a cycle of at least minRepetitions exists at the end of a cycle of n elements.
        /// We could 0, 0, 1, 2, 1, 2, 1, 2 as being three repetitions, because the cycle 1, 2 occurs three times.
        /// Note that a cycle of 1 repetition always trivially exists.
        /// </summary>
        /// <param name="equalityFunction"></param>
        /// <param name="n"></param>
        /// <param name="minRepetitions"></param>
        /// <returns></returns>
        public static bool CycleExists(Func<int,int, bool> equalityFunction, int n, int minRepetitions)
        {
            if (minRepetitions == 1 && n > 0)
                return true;
            int i = n - 1; // a cycle would include the last element
            for (int j = i - 1; j >= 0; j--) // looking for last time the same element appeared
            {
                int numElements = i - j; // how long would cycle be
                if (numElements * minRepetitions > n)
                    return false; // not enough room for all repetitions
                if (equalityFunction(i, j))
                { // match of length 1 -- does everything intervening match, and does cycle repeat before that?
                    bool matches = true;
                    for (int numRepetitions = 0; numRepetitions < minRepetitions - 1; numRepetitions++)
                    {
                        for (int k = 0; k < numElements; k++)
                        {
                            int numElementsToRepetition = numElements * numRepetitions;
                            int beforei = i - numElementsToRepetition - k;
                            int beforej = j - numElementsToRepetition - k;
                            if (!equalityFunction(beforei, beforej))
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (!matches)
                            break;
                    }
                    if (matches)
                        return true;
                }
            }
            return false;
        }

    }
}
