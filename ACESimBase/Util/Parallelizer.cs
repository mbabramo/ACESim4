using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;

namespace ACESim
{
    public static class Parallelizer
    {
        public static int ParallelDepth = 0;
        public static int? MaxDegreeOfParallelism = (int?) null;
        const int maxParallelDepth = 50;
        static object[] lockObj = new object[maxParallelDepth];
        internal static bool lockObjInitialized = false;
        internal static object GetLockObj()
        {
            if (!lockObjInitialized)
            {
                lock (lockObj) // lock on the whole thing
                {
                    if (!lockObjInitialized)
                    {
                        for (int i = 0; i < maxParallelDepth; i++)
                            lockObj[i] = new object();
                        lockObjInitialized = true;
                    }
                }
            }
            return lockObj[ParallelDepth];
        }

        public static ParallelOptions GetParallelOptions()
        {
            if (MaxDegreeOfParallelism == null)
                return null;
            else
                return new ParallelOptions() {MaxDegreeOfParallelism = (int) MaxDegreeOfParallelism};
        }

        public static bool DisableParallel;

        public static bool EnsureConsistentIterationNumbers = true; // if true, then when we use GoForSpecifiedNumberOfSuccesses (int or long version), we make sure that we complete this with the minimum iteration numbers. As long as nothing within the parallel loop depends on order of execution other than iteration numbers, we should then have consistent results.
        public static bool VerifyConsistentResults = false; 

        public static void Go(bool doParallel, int start, int stopBeforeThis, Action<int> action)
        {
            Go(doParallel, (long)start, (long)stopBeforeThis, x => { action((int)x); });
            //if (ParallelDepth >= 1 || DisableParallel)
            //    doParallel = false;
            //if (doParallel)
            //{
            //    IncrementParallelDepth();
            //    Parallel.ForEach(Partitioner.Create(start, stopBeforeThis),
            //        (range) =>
            //        {
            //            for (int i = range.Item1; i < range.Item2; i++)
            //            {
            //                action(i);
            //            }
            //        });
            //    DecrementParallelDepth(); 
            //}
            //else
            //{
            //    for (int i = start; i < stopBeforeThis; i++)
            //    {
            //        action(i);
            //    }
            //}
        }

        public static void GoForSpecifiedNumberOfSuccesses(bool doParallel, int numSuccessesRequired, Func<int, int, bool> actionReturningTrueUponSuccess, int firstIterationNumberToTry = 0, double? minSuccessRate = null)
        {
            GoForSpecifiedNumberOfSuccesses(doParallel, (long)numSuccessesRequired, (x, y) => actionReturningTrueUponSuccess((int)x, (int)y), (long)firstIterationNumberToTry, minSuccessRate);
            //if (ParallelDepth > 1)
            //    doParallel = false;
            //int nextIterationNumberToTry = firstIterationNumberToTry - 1;
            //int successCount = 0, failureCount = 0;
            //if (doParallel)
            //{
            //    IncrementParallelDepth();
            //    Parallel.ForEach(Partitioner.Create(0, numSuccessesRequired),
            //        (range) =>
            //        {
            //            for (int successNumber = range.Item1; successNumber < range.Item2; successNumber++)
            //            {
            //                DoUntilSuccessful(actionReturningTrueUponSuccess, successNumber, ref nextIterationNumberToTry, ref successCount, ref failureCount, maxNumberAttempts, returnIfNoSuccessAfterAttempts);
            //                if (maxNumberAttempts != null && successCount + failureCount > maxNumberAttempts)
            //                    break;
            //                if (returnIfNoSuccessAfterAttempts != null && successCount == 0 && failureCount >= (int)returnIfNoSuccessAfterAttempts)
            //                    break;
            //            }
            //        });
            //    DecrementParallelDepth();
            //}
            //else
            //{
            //    for (int successNumber = 0; successNumber < numSuccessesRequired; successNumber++)
            //    {
            //        DoUntilSuccessful(actionReturningTrueUponSuccess, successNumber, ref nextIterationNumberToTry, ref successCount, ref failureCount, maxNumberAttempts, returnIfNoSuccessAfterAttempts);
            //        if (maxNumberAttempts != null && successCount + failureCount > maxNumberAttempts)
            //            break;
            //        if (returnIfNoSuccessAfterAttempts != null && successCount == 0 && failureCount >= (int)returnIfNoSuccessAfterAttempts)
            //            break;
            //    }
            //}
        }

        private static void DoUntilSuccessful(Func<int, int, bool> actionReturningTrueUponSuccess, int successNumber, ref int nextIterationNumberToTry, ref int successCount, ref int failureCount, int? maxNumberAttempts = null, int? returnIfNoSuccessAfterAttempts = null)
        {
            long nextIter = nextIterationNumberToTry;
            DoUntilSuccessful((x, y) => actionReturningTrueUponSuccess((int)x, (int)y), (long)successNumber, ref nextIter);
            nextIterationNumberToTry = (int)nextIter;
            //bool success;
            //do
            //{
            //    int iterationNumberToTry = Interlocked.Increment(ref nextIterationNumberToTry); // must get returned value since if we look again at the nextIterationNumberToTry value, the value may have been incremented again
            //    success = actionReturningTrueUponSuccess(successNumber, iterationNumberToTry);
            //    if (success)
            //        Interlocked.Increment(ref successCount);
            //    else
            //        Interlocked.Increment(ref failureCount);
            //    if (maxNumberAttempts != null && successCount + failureCount > maxNumberAttempts)
            //        break;
            //    if (returnIfNoSuccessAfterAttempts != null && successCount == 0 && failureCount >= (int)returnIfNoSuccessAfterAttempts)
            //        break;
            //} while (!success);
        }

        public static void GoByte(bool doParallel, byte start, byte stopBeforeThis, Action<byte> action)
        {
            if (ParallelDepth >= maxParallelDepth || DisableParallel)
                doParallel = false;
            if (doParallel)
            {
                int initialParallelDepth = ParallelDepth;
                IncrementParallelDepth();
                Parallel.ForEach(Partitioner.Create(start, stopBeforeThis),
                    GetParallelOptions(),
                    (range) =>
                    {
                        for (byte i = (byte) range.Item1; i < range.Item2; i++)
                        {
                            action(i);
                        }
                    });
                DecrementParallelDepth();
            }
            else
            {
                for (byte i = start; i < stopBeforeThis; i++)
                {
                    action(i);
                }
            }
        }

        public static void Go(bool doParallel, long start, long stopBeforeThis, Action<long> action)
        {
            if (ParallelDepth > 0 || DisableParallel)
                doParallel = false;
            if (doParallel)
            {
                IncrementParallelDepth();
                Parallel.ForEach(Partitioner.Create(start, stopBeforeThis),
                    GetParallelOptions(),
                    (range) =>
                    {
                        for (long i = range.Item1; i < range.Item2; i++)
                        {
                            action(i);
                        }
                    });
                DecrementParallelDepth();
            }
            else
            {
                for (long i = start; i < stopBeforeThis; i++)
                {
                    action(i);
                }
            }
        }

        public static async Task GoAsync(bool doParallel, long start, long stopBeforeThis, Func<long, Task> action)
        {
            if (ParallelDepth > 0 || DisableParallel)
                doParallel = false;
            if (doParallel)
            {
                IncrementParallelDepth();
                await ForAsync(start, stopBeforeThis, action);
                DecrementParallelDepth();
            }
            else
            {
                for (long i = start; i < stopBeforeThis; i++)
                {
                    await action(i);
                }
            }
        }

        public static Task ForAsync(long start, long stopBeforeThis, Func<long, Task> body)
        {

            return Task.WhenAll(
                from partition in Partitioner.Create(start, stopBeforeThis).GetPartitions(MaxDegreeOfParallelism ?? Environment.ProcessorCount)
                select Task.Run(async delegate {
                    using (partition)
                        while (partition.MoveNext())
                        {
                            for (long i = partition.Current.Item1; i < partition.Current.Item2; i++)
                                await body(i);
                        }
                }));
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(MaxDegreeOfParallelism ?? Environment.ProcessorCount)
                select Task.Run(async delegate {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current);
                }));
        }


        public static void GoForSpecifiedNumberOfSuccessesButNoMore(bool doParallel, long numSuccessesRequired, Func<long, long, bool> actionReturningTrueUponSuccess, long firstIterationNumberToTry = 0, double? minSuccessRate = null)
        {
            long checkMinimumSuccessRateAfter = 10000;

            long numStarted = 0;
            long numCompleted = 0;
            long numSuccessesSoFar = 0;
            bool abort = false;
            bool checkpointCompleted = false;

            if (doParallel)
                IncrementParallelDepth();
            Queue<long> successNumbersToRetry = new Queue<long>(); // each successfully completed operation should have a different success number assigned to it before the task launches
            long highestSuccessNumberPending = -1;
            long nextIterationToTry = firstIterationNumberToTry - 1;

            object lockObj = new object();
            object lockObj2 = new object();
            bool createChildTask = false;
            do
            { // keep creating child tasks as long as we haven't reached the number of successes
                long iterationToTry;
                long successNumberToTry = 0;
                lock (lockObj)
                {
                    long numberPending = numStarted - numCompleted;
                    bool notCloseToCheckpoint = minSuccessRate == null || checkpointCompleted || numStarted < (int)checkMinimumSuccessRateAfter;
                    bool notYetCloseToFinishing = numSuccessesSoFar + numberPending < numSuccessesRequired;
                    bool notTooManyTasks = numberPending < 1000;
                    bool notParallelDisableWithTaskPending = !(!doParallel && numberPending > 0);
                    if (notCloseToCheckpoint && notYetCloseToFinishing && notTooManyTasks && notParallelDisableWithTaskPending)
                    {
                        createChildTask = true;
                        numStarted++; // must note that we've started this within this lock portion so that once we've made the decision to create a child task, we won't create another one if that's unnecessary
                        if (successNumbersToRetry.Any())
                            successNumberToTry = successNumbersToRetry.Dequeue();
                        else
                        {
                            highestSuccessNumberPending++;
                            successNumberToTry = highestSuccessNumberPending;
                            if (successNumberToTry >= numSuccessesRequired)
                                throw new Exception("Internal error.");
                        }
                    }
                    else // don't create a child task now (maybe later), because we either may have enough or have too many pending.
                        createChildTask = false;
                }
                if (createChildTask)
                {
                    iterationToTry = Interlocked.Increment(ref nextIterationToTry); // the goal here is to assure that this never goes higher than necessary
                    lock (lockObj2)
                    {
                        Task childTask = Task.Factory.StartNew(() =>
                            {
                                bool success = actionReturningTrueUponSuccess(successNumberToTry, iterationToTry);
                                lock (lockObj)
                                {
                                    numCompleted++;
                                    if (minSuccessRate != null && numCompleted == checkMinimumSuccessRateAfter)
                                    { // checkpoint
                                        if ((double)numSuccessesSoFar / (double)numCompleted < minSuccessRate)
                                            abort = true;
                                        else
                                            checkpointCompleted = true;
                                    }
                                    if (success)
                                        numSuccessesSoFar++;
                                    else
                                        successNumbersToRetry.Enqueue(successNumberToTry);

                                }
                            });
                    }
                }
                else if (numSuccessesSoFar < numSuccessesRequired) // not necessarily done -- may need to add more tasks once currently pending ones are completed
                    Thread.Sleep(1);
            } while (numSuccessesSoFar < numSuccessesRequired && !abort);

            if (doParallel)
                DecrementParallelDepth();
        }

        public static void GoForSpecifiedNumberOfSuccesses(bool doParallel, long numSuccessesRequired, Func<long, long, bool> actionReturningTrueUponSuccess, long firstIterationNumberToTry = 0, double? minSuccessRate = null, CancellationToken? ct = null)
        {
            if (ParallelDepth > 0 || DisableParallel)
                doParallel = false;
            if (EnsureConsistentIterationNumbers || minSuccessRate != null)
            {
                GoForSpecifiedNumberOfSuccessesButNoMore(doParallel, numSuccessesRequired, actionReturningTrueUponSuccess, firstIterationNumberToTry, minSuccessRate);
                return;
            }
            long nextIterationNumberToTry = firstIterationNumberToTry - 1;
            if (doParallel)
            {
                IncrementParallelDepth();
                RandomGenerator.ThrowExceptionIfCalled = true;
                Parallel.ForEach(Partitioner.Create((long)0, numSuccessesRequired),
                    GetParallelOptions(),
                    (range) =>
                    {
                        
                        for (long successNumber = range.Item1; successNumber < range.Item2; successNumber++)
                        {
                            bool cancel = false;
                            if (ct != null && ((CancellationToken)ct).IsCancellationRequested)
                                cancel = true;
                            if (!cancel)
                                DoUntilSuccessful(actionReturningTrueUponSuccess, successNumber, ref nextIterationNumberToTry);
                        }
                    });
                RandomGenerator.ThrowExceptionIfCalled = false;
                DecrementParallelDepth();
            }
            else
            {
                for (long successNumber = 0; successNumber < numSuccessesRequired; successNumber++)
                {
                    DoUntilSuccessful(actionReturningTrueUponSuccess, successNumber, ref nextIterationNumberToTry);
                }
            }
        }

        public static void TestConsistentParallelism()
        {
            EnsureConsistentIterationNumbers = true;
            ConcurrentBag<long> first500NumbersOver1MillionDivisibleBy3 = new ConcurrentBag<long>();
            ConcurrentBag<long> allSuccesses = new ConcurrentBag<long>();
            GoForSpecifiedNumberOfSuccesses(true, 500, (successNumber, iterationNumber) =>
                {
                    bool success = iterationNumber > 1000000 && iterationNumber % 3 == 0;
                    if (success)
                    {
                        first500NumbersOver1MillionDivisibleBy3.Add(iterationNumber);
                        allSuccesses.Add(successNumber);
                    }
                    return success;
                }, 1);
            List<long> result = first500NumbersOver1MillionDivisibleBy3.ToList().OrderBy(x => x).ToList();
            if (result.Last() != 1001499)
                throw new Exception("Internal error.");
            List<long> allSuccessesList = allSuccesses.ToList().OrderBy(x => x).ToList();
            if (allSuccessesList.Any(x => x >= 500))
                throw new Exception("Internal error.");
            if (allSuccessesList.Distinct().Count() != 500)
                throw new Exception("Internal error.");

            allSuccesses = new ConcurrentBag<long>();
            ConcurrentBag<long> numbersDivisibleBy300 = new ConcurrentBag<long>();
            GoForSpecifiedNumberOfSuccesses(true, 500, (successNumber, iterationNumber) =>
            {
                bool success = iterationNumber % 300 == 0 || iterationNumber > 1000;
                if (success)
                {
                    numbersDivisibleBy300.Add(iterationNumber);
                    allSuccesses.Add(successNumber);
                }
                return success;
            }, 1, 0.10);
            if (allSuccesses.Count != 3) // only iteration 300, 600, and 900 should be successful -- we should never get to 1000
                throw new Exception("Internal error.");
        }

        private static void DoUntilSuccessful(Func<long, long, bool> actionReturningTrueUponSuccess, long successNumber, ref long nextIterationNumberToTry)
        {
            bool success;
            do
            {
                long iterationNumberToTry = Interlocked.Increment(ref nextIterationNumberToTry);
                success = actionReturningTrueUponSuccess(successNumber, iterationNumberToTry);
            } while (!success);
        }

        public static void GoUndifferentiatedIterations(bool doParallel, int totalIterations, Action action)
        {
            if (ParallelDepth > 0 || DisableParallel)
                doParallel = false;
            if (doParallel)
            {
                IncrementParallelDepth();
                Parallel.ForEach(Partitioner.Create(0, totalIterations),
                    GetParallelOptions(),
                    (range) =>
                    {
                        for (long i = range.Item1; i < range.Item2; i++)
                        {
                            action();
                        }
                    });
                DecrementParallelDepth();
            }
            else
            {
                for (long i = 0; i < totalIterations; i++)
                {
                    action();
                }
            }
        }

        public static void DecrementParallelDepth()
        {
            lock (GetLockObj())
                ParallelDepth--;
        }

        public static void IncrementParallelDepth()
        {
            lock (GetLockObj())
                ParallelDepth++;
        }
    }
}
