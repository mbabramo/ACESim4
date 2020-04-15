using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    /// <summary>
    /// This supports parallelizing indexable operations where the results from earlier operations must be processed before results from later operations. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ParallelConsecutive<T>
    {
        SortedList<int, T> SortedItems = new SortedList<int, T>();
        int NextItemToStartProducing = -1;
        int NextItemToConsume = 0;
        bool Complete = false;
        Func<int, bool> IsCompleteFunc;
        Func<int, T> ProducerAction;
        Action<T> ConsumerAction;

        public ParallelConsecutive(bool parallel, Func<int, bool> isCompleteFunc, Func<int, T> producerAction, Action<T> consumerAction)
        {
            IsCompleteFunc = isCompleteFunc;
            ProducerAction = producerAction;
            ConsumerAction = consumerAction;
            Go(parallel);
        }

        public int GetNextItemToStart()
        {
            int result = Interlocked.Increment(ref NextItemToStartProducing);
            return result;
        }

        public void ProduceAndInitiateConsumption(int i)
        {
            T t = ProducerAction(i);
            StartConsumption(i, t);
        }

        public void StartConsumption(int i, T item)
        {
            lock(SortedItems)
            {
                if (Complete)
                    return;
                SortedItems.Add(i, item);
                bool itemProcessed;
                do
                {
                    var nextItem = SortedItems.FirstOrDefault();
                    if (nextItem.Key == NextItemToConsume)
                    {
                        ConsumerAction(nextItem.Value);
                        SortedItems.Remove(NextItemToConsume);
                        if (IsCompleteFunc(NextItemToConsume + 1))
                            Complete = true;
                        NextItemToConsume++;
                        itemProcessed = true;
                    }
                    else
                        itemProcessed = false;
                }
                while (itemProcessed && !Complete);
            }
        }

        public void Go(bool parallel)
        {
            if (parallel)
            {
                ParallelOptions parallelOptions = new ParallelOptions();
                Parallel.ForEach(new InfinitePartitioner(), parallelOptions,
                    (ignored, loopState) =>
                    {
                        if (!Complete)
                        {
                            int itemToStart = GetNextItemToStart();
                            ProduceAndInitiateConsumption(itemToStart);
                        }
                        else loopState.Stop();
                    });
            }
            else
            {
                while (!Complete)
                {
                    int itemToStart = GetNextItemToStart();
                    ProduceAndInitiateConsumption(itemToStart);
                }
            }
        }

        private static void While(
            ParallelOptions parallelOptions, 
            Func<bool> condition,
            Action<ParallelLoopState> body)
        {
            Parallel.ForEach(new InfinitePartitioner(), parallelOptions,
                (ignored, loopState) =>
                {
                    if (condition()) body(loopState);
                    else loopState.Stop();
                });
        }
    }

    public class InfinitePartitioner : Partitioner<bool>
    {
        public override IList<IEnumerator<bool>> GetPartitions(int partitionCount)
        {
            if (partitionCount < 1)
                throw new ArgumentOutOfRangeException("partitionCount");
            return (from i in Enumerable.Range(0, partitionCount)
                    select InfiniteEnumerator()).ToArray();
        }

        public override bool SupportsDynamicPartitions { get { return true; } }

        public override IEnumerable<bool> GetDynamicPartitions()
        {
            return new InfiniteEnumerators();
        }

        private static IEnumerator<bool> InfiniteEnumerator()
        {
            while (true) yield return true;
        }

        private class InfiniteEnumerators : IEnumerable<bool>
        {
            public IEnumerator<bool> GetEnumerator()
            {
                return InfiniteEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
