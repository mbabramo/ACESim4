using ACESimBase.Util.Randomization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Collections
{
    public class Reservoir<T> : IEnumerable<T>
    {
        ConsistentRandomSequenceProducer RandomProducer;
        public int CurrentSize, Capacity, OriginalCapacity;
        public long Seed;
        public int RemainingCapacity => Capacity - CurrentSize;
        public T[] Items;

        public Reservoir(int capacity, long seed)
        {
            Seed = seed;
            RandomProducer = new ConsistentRandomSequenceProducer(seed);
            CurrentSize = 0;
            OriginalCapacity = Capacity = capacity;
            Items = new T[Capacity];
        }

        public Reservoir<T> DeepCopy(Func<T, T> deepCopyItem)
        {
            var result = new Reservoir<T>(Capacity, Seed);
            foreach (T item in this)
                result.AddItem(deepCopyItem(item));
            return result;
        }

        public void ChangeCapacity(int revisedCapacity)
        {
            if (revisedCapacity != Capacity)
            {
                T[] revisedItems = new T[revisedCapacity];
                for (int i = 0; i < Math.Min(Capacity, revisedCapacity); i++)
                    revisedItems[i] = Items[i];
                Items = revisedItems;
                Capacity = revisedCapacity;
                if (CurrentSize > Capacity)
                    CurrentSize = Capacity;
            }
        }

        public void ReturnToOriginalCapacity() => ChangeCapacity(OriginalCapacity);


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < CurrentSize; i++)
                yield return Items[i];
        }

        public bool AtCapacity()
        {
            return CurrentSize == Capacity;
        }

        public void AddItem(T item)
        {
            if (AtCapacity())
                throw new Exception("Already at capacity");
            Items[CurrentSize++] = item;
        }

        public void AddPotentialReplacementsAtIteration(List<T> replacements, double discountRate, int iteration)
        {
            int numToDrop = CountNumToDropAtIteration(discountRate, iteration);
            if (numToDrop > 0)
            {
                DropItems(numToDrop);
            }
            replacements = replacements.Take(RemainingCapacity).ToList();
            AddReplacements(replacements);
        }

        public void AddReplacements(List<T> replacement)
        {
            DropItemsIfNecessary(replacement.Count());
            foreach (var r in replacement)
                AddItem(r);
        }

        /// <summary>
        /// We reduce the number of items by a discount rate and then by removing one of every
        /// i, where i is the one-based iteration number. With a discount rate of 1.0, only
        /// iteration replacement will take place, which assures that on average each iteration
        /// makes the same contribution to the reservoir. A discount rate of less than 1.0 ensures
        /// that later generations make a larger contribution to the reservoir.
        /// </summary>
        /// <param name="discountRate"></param>
        /// <param name="iteration"></param>
        public int CountNumToDropAtIteration(double discountRate, int iteration)
        {
            if (iteration == 0)
                throw new DivideByZeroException("There should be no iteration 0 for reservoir sampling.");
            // Suppose we have 1000 items and a discount rate of 0.99. Then,
            // we start by reducing our items to 990. Of those 990, we also
            // want to replace 1/iteration (where iterations start numbering at 1).
            int reducedSize = (int)(CurrentSize * discountRate);
            int targetSize = reducedSize - (int)(1.0 / iteration * reducedSize);
            if (targetSize >= CurrentSize)
                return 0;
            int numToDrop = CurrentSize - targetSize;
            return numToDrop;
        }

        public int CountTotalNumberToAddAtIteration(double discountRate, int iteration)
        {
            return RemainingCapacity + CountNumToDropAtIteration(discountRate, iteration);
        }

        public void DropItemsIfNecessary(int numToAdd)
        {
            if (numToAdd > RemainingCapacity)
                DropItems(numToAdd - RemainingCapacity);
        }

        public void DropItems(int numToDrop)
        {
            if (numToDrop > CurrentSize)
                throw new Exception("Reservoir not that full.");
            // each item must have the same probability of being dropped.
            // we make decision one item at a time, but revising the drop probability based on whether we have already done a drop.
            // when we don't drop, we copy the item to the new location, so all items in the reservoir are at the beginning
            int numDroppedSoFar = 0;
            for (int i = 0; i < CurrentSize; i++)
            {
                int numRemainingOpportunities = CurrentSize - i;
                int numRemainingToDrop = numToDrop - numDroppedSoFar;
                double dropProbability = numRemainingToDrop / (double)numRemainingOpportunities;
                bool drop = RandomProducer.NextDouble() < dropProbability;
                if (drop)
                {
                    numDroppedSoFar++;
                }
                else
                {
                    int targetIndex = i - numDroppedSoFar;
                    Items[targetIndex] = Items[i];
                }
            }
            if (numDroppedSoFar != numToDrop)
                throw new Exception();
            CurrentSize -= numDroppedSoFar;
        }

    }
}
