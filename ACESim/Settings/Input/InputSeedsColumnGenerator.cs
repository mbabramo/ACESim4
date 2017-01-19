using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class InputSeedsColumnGenerator
    {
        long iterations;
        internal InputSeedsSortHelper[] theSortHelpers;
        public double this[int index]
        {
            get
            {
                return theSortHelpers[index].evenlySpacedValue;
            }
            set
            {
            }
        }

        public InputSeedsColumnGenerator(long numIterations)
        {
            iterations = numIterations;
            theSortHelpers = new InputSeedsSortHelper[numIterations];
            for (int i = 0; i < iterations; i++)
            {
                theSortHelpers[i] = new InputSeedsSortHelper();
                theSortHelpers[i].Fill(((double)(i + 1)) / ((double)(numIterations + 1)));
            }

        }
        internal void Sort()
        {
            theSortHelpers = theSortHelpers.OrderBy(x => x.randomValue).ToArray();
        }
        public void RandomizeAndSort()
        {
            for (int i = 0; i < iterations; i++)
            {
                theSortHelpers[i].Randomize();
            }
            Sort();
        }
    }
}
