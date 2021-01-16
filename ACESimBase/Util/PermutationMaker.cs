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
                if (setOfLists.Count() > 1000000)
                    throw new Exception("There may be a problem with the formatting of the settings file, leading to large numbers of permutations being generated. However, this may be because of many unstarred items which will be removed from the list later.");
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

        public static List<List<T>> GetPermutationsOfItems<T>(List<List<T>> lists)
        {
            List<int> counts = lists.Select(x => x.Count()).ToList();
            List<List<int>> indicesInCorrespondingLists = GetPermutations(counts); // one-based indices
            List<List<T>> result = new List<List<T>>();
            foreach (List<int> indexList in indicesInCorrespondingLists)
            {
                List<T> correspondingItems = new List<T>();
                for (int i = 0; i < indicesInCorrespondingLists.Count(); i++)
                {
                    List<T> listToPickFrom = lists[i];
                    int indexInList = indexList[i] - 1;
                    T item = listToPickFrom[indexInList];
                    correspondingItems.Add(item);
                }
                result.Add(correspondingItems);
            }
            return result;
        }
    }
}
