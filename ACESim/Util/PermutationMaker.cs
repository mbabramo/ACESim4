using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class PermutationMaker
    {
        public static List<List<int>> GetPermutations(List<int> listOfNumbersToPickFrom, bool resultsZeroBased = false)
        {
            if (listOfNumbersToPickFrom.Any(x => x == 0))
                throw new Exception("Cannot generate permutations where one of sets is empty.");
            bool done = false;
            List<List<int>> setOfLists = new List<List<int>>();
            List<int> currentList = new List<int>();
            for (int i = 0; i < listOfNumbersToPickFrom.Count; i++)
            {
                currentList.Add(1);
            }
            do
            {
                if (setOfLists.Count() > 10000)
                    throw new Exception("There is a problem with the formatting of the settings file, leading to large numbers of permutations being generated.");
                setOfLists.Add(currentList.ToList());
                done = currentList.SequenceEqual<int>(listOfNumbersToPickFrom);
                if (!done)
                {
                    bool doneLevel2 = false;
                    for (int i = listOfNumbersToPickFrom.Count - 1; i >= 0 && !doneLevel2; i--)
                    {
                        currentList[i]++;
                        if (currentList[i] > listOfNumbersToPickFrom[i])
                        {
                            currentList[i] = 1; // reset to beginning and increment next one over
                            doneLevel2 = false;
                        }
                        else
                            doneLevel2 = true; // we've found the next number.
                    }
                }
            }
            while (!done);
            if (resultsZeroBased)
                foreach (var l in setOfLists)
                    for (int i = 0; i < l.Count; i++)
                        l[i]--;
            return setOfLists;
        }
    }
}
