using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim.Util
{
    /// <summary>
    /// A delegate to use with IList.Map.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="input"></param>
    /// <returns></returns>
    public delegate T MapDelegate<T>(T input);

    public delegate TAggregate ReduceDelegate<TAggregate, TInput>(TAggregate aggregate, TInput input);

    public delegate bool AllDelegate<T>(T input);
    public delegate bool AnyDelegate<T>(T input);

    public static class ListExtensions
    {
        public static int GetSequenceHashCode<TItem>(this IEnumerable<TItem> list)
        {
            if (list == null) return 0;
            const int seedValue = 0x2D2816FE;
            const int primeNumber = 397;
            return list.Aggregate(seedValue, (current, item) => (current * primeNumber) + (Equals(item, default(TItem)) ? 0 : item.GetHashCode()));
        }

        public static List<List<T>> Pivot<T>(this List<List<T>> inputLists, bool removeEmpty, T defaultVal = default(T))
        {
            if (inputLists == null) throw new ArgumentNullException("inputLists");
            if (removeEmpty && !object.Equals(defaultVal, default(T))) throw new ArgumentException("You cannot provide a default value and removeEmpty at the same time!", "removeEmpty");

            int maxCount = inputLists.Max(l => l.Count);
            List<List<T>> outputLists = new List<List<T>>(maxCount);
            for (int i = 0; i < maxCount; i++)
            {
                List<T> list = new List<T>();
                outputLists.Add(list);
                for (int index = 0; index < inputLists.Count; index++)
                {
                    List<T> inputList = inputLists[index];
                    bool listSmaller = inputList.Count <= i;
                    if (listSmaller)
                    {
                        if (!removeEmpty)
                            list.Add(defaultVal);
                    }
                    else
                        list.Add(inputList[i]);
                }
            }
            return outputLists;
        }

        public static int GetIndexOfDifference<T>(this List<T> x, List<T> y)
        {
            int xCount = x.Count(), yCount = y.Count();
            int smaller = Math.Min(xCount, yCount);
            for (int i = 0; i < smaller; i++)
                if (!x[i].Equals(y[i]))
                    return i;
            // No difference detected so far.
            if (xCount == yCount)
                return -1; // sequences are identical
            
            return smaller; // for example, if x is () and y is (1), this will return 0, since the difference is at the first index of y. If x is (2) and y is (2,6,7), then it will return 1, since the difference is at the second index of y
        }

        public static unsafe List<byte> GetPointerAsList_255Terminated(Span<byte> r)
        {
            var r2 = new List<byte>();
            int d = 0;
            while (r[d] != 255)
            {
                r2.Add(r[d]);
                d++;
            }
            return r2;
        }

        public static unsafe List<byte> GetPointerAsList_255Terminated(byte* r)
        {
            var r2 = new List<byte>();
            int d = 0;
            while (r[d] != 255)
            {
                r2.Add(r[d]);
                d++;
            }
            return r2;
        }

        public static List<double> GetSpanAsList(Span<double> r, int size)
        {
            var r2 = new List<double>();
            for (int i = 0; i < size; i++)
                r2.Add(r[i]);
            return r2;
        }

        public static unsafe List<double> GetPointerAsList(double* r, int size)
        {
            var r2 = new List<double>();
            for (int i = 0; i < size; i++)
                r2.Add(r[i]);
            return r2;
        }

        public static unsafe List<byte> GetPointerAsList_NumItemsPrefix(byte* r)
        {
            var r2 = new List<byte>();
            byte numItems = r[0];
            for (byte b = 0; b < numItems; b++)
                r2.Add(r[b]);
            return r2;
        }


        ///// <summary>
        ///// Returns a copy of list with function applied to each of its members.
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="list"></param>
        ///// <param name="function"></param>
        ///// <returns></returns>
        //public static IList<T> Map<T>(this IList<T> list, MapDelegate<T> function)
        //{
        //    IList<T> mappedList = list.ToList(); // How to get a new list of list's type?  Shouldn't be necessary to copy the entire list.
        //    for (int x = 0; x < mappedList.Count; ++x)
        //    {
        //        mappedList[x] = function(list[x]);
        //    }
        //    return mappedList;
        //}

        ///// <summary>
        ///// Reduces an enumerable to a single value of type TAggregate baseed up an initial value and a reducing function.  The function
        ///// takes an intermediate aggregated value and a value in the list and returns a combined value.
        ///// </summary>
        ///// <typeparam name="TAggregate"></typeparam>
        ///// <typeparam name="TInput"></typeparam>
        ///// <param name="list"></param>
        ///// <param name="initial"></param>
        ///// <param name="function"></param>
        ///// <returns></returns>
        //public static TAggregate Reduce<TAggregate, TInput>(this IEnumerable<TInput> list, TAggregate initial, ReduceDelegate<TAggregate, TInput> function)
        //{
        //    TAggregate aggregate = initial;
        //    foreach (TInput item in list)
        //        aggregate = function(aggregate, item);

        //    return aggregate;
        //}

        //////public delegate TAggregate ReduceDelegate<TAggregate, TInput>(TAggregate aggregate, TInput input);
        ////// delegate(int aggregate, int input) { return aggregate + input; }
        ////delegate bool allReduceDelegate(bool aggregate, bool input)
        ////{
        ////    return (aggregate && input);
        ////}

        //public static bool All<T>(this IEnumerable<T> enumerable, AllDelegate<T> function)
        //{
        //    foreach (T item in enumerable)
        //        if (!function(item))
        //            return false;
        //    return true;
        //}

        //public static bool Any<T>(this IEnumerable<T> enumerable, AnyDelegate<T> function)
        //{
        //    foreach (T item in enumerable)
        //        if (function(item))
        //            return true;
        //    return false;
        //}


        ///// <summary>
        ///// Shuffles the elements of the list in-place.
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="list">A list to shuffle.</param>
        //public static void Shuffle<T>(this IList<T> list)
        //{
        //    Random rng = new Random();
        //    int index = list.Count;
        //    while (index > 1)
        //    {
        //        index--;
        //        int shuffleIndex = rng.Next(index + 1);
        //        T value = list[shuffleIndex];
        //        list[shuffleIndex] = list[index];
        //        list[index] = value;
        //    }
        //}
    }
}
