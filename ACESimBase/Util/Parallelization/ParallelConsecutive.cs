using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimBase.Util.Parallelization
{
    /// <summary>
    /// This supports parallelizing indexable operations where the results from earlier operations must be processed before results from later operations. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ParallelConsecutive<T>
    {
        ConcurrentDictionary<int, T> PendingConsumption = new ConcurrentDictionary<int, T>();
        int NextItemToStartProducing = -1;
        int NextItemToConsume = 0;
        bool Complete = false;
        Func<int, bool> IsCompleteFunc;
        Func<int, T> ProducerAction;
        Action<T> ConsumerAction;

        public ParallelConsecutive(Func<int, bool> isCompleteFunc, Func<int, T> producerAction, Action<T> consumerAction)
        {
            IsCompleteFunc = isCompleteFunc;
            ProducerAction = producerAction;
            ConsumerAction = consumerAction;
        }

        public async Task Run(bool parallel)
        {
            if (parallel)
            {
                Task production = Task.Factory.StartNew(() => ProduceInParallel());
                Task consumption = Task.Factory.StartNew(() => Consume());
                Task[] tasks = new Task[] { production, consumption };
                await Task.WhenAll(tasks);
            }
            else
            {
                ProduceSerially();
            }
        }

        public void ProduceSerially()
        {
            while (!Complete)
            {
                int itemToStart = GetNextItemToStart();
                T result = ProducerAction(itemToStart);
                ConsumerAction(result);
                NextItemToConsume = itemToStart + 1;
                Complete = IsCompleteFunc(NextItemToConsume);
            }
        }

        public void ProduceInParallel()
        {
            ParallelOptions parallelOptions = new ParallelOptions();
            Parallel.ForEach(new InfinitePartitioner(), parallelOptions,
                (ignored, loopState) =>
                {
                    if (!Complete)
                    {
                        int itemToStart = GetNextItemToStart();
                        StoreProductionResult(itemToStart);
                    }
                    else loopState.Stop();
                });
        }

        public void Consume()
        {
            while (!Complete)
            {
                int j = TryToConsumeMore(NextItemToConsume);
                if (j == NextItemToConsume)
                    Thread.Sleep(20);
                else
                    NextItemToConsume = j;
            }
        }

        public int GetNextItemToStart()
        {
            int result = Interlocked.Increment(ref NextItemToStartProducing);
            return result;
        }

        public void StoreProductionResult(int i)
        {
            T t = ProducerAction(i);
            PendingConsumption[i] = t;
        }

        private int TryToConsumeMore(int i)
        {
            while (!Complete && PendingConsumption.ContainsKey(i))
            {
                var consumable = PendingConsumption[i];
                ConsumerAction(consumable);
                i++;
                PendingConsumption.Remove(NextItemToConsume, out _);
                if (IsCompleteFunc(i))
                    Complete = true;
            }

            return i;
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
