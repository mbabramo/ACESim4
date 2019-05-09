using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class RandomSubset
    {
        public static List<int> GetRandomIntegersInRandomOrder(int min, int max, int count)
        {
            List<int> theList = Enumerable.Range(min, max - min + 1).ToList();
            Shuffle(theList);
            return theList.Take(count).ToList();
        }

        public static void Shuffle<T>(IList<T> list, int seed = 0)
        {
            Random rng = seed == 0 ? new Random() : new Random(seed);
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }


    }
}
