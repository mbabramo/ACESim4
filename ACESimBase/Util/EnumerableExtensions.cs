using System;
using System.Collections.Generic;
using System.Linq;
using Tensorflow;

namespace ACESimBase.Util
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> GetNth<T>(this IEnumerable<T> list, int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), n, null);
            int i = n;
            foreach (var e in list)
            {
                if (++i < n)
                { //save Division
                    continue;
                }
                i = 0;
                yield return e;
            }
        }

        public static IEnumerable<T> GetNth<T>(this IReadOnlyList<T> list, int n
            , int offset = 0)
        { //use IReadOnlyList<T>
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), n, null);
            for (var i = offset; i < list.Count; i += n)
            {
                yield return list[i];
            }
        }
        
        public static IEnumerable<(T first, T last)> Pairs<T>(this IEnumerable<T> sequence)
        {
            if (!sequence.Any())
                yield break;
            T previous = sequence.First();
            foreach (T item in sequence.Skip(1))
            {
                yield return (previous, item);
                previous = item;
            }
        }

        public static IEnumerable<T> Then<T>(this IEnumerable<T> sequence, IEnumerable<T> secondSequence)
        {
            foreach (T t in sequence)
                yield return t;
            foreach (T t in secondSequence)
                yield return t;
        }

        public static IEnumerable<double> CumulativeSum(this IEnumerable<double> sequence)
        {
            double sum = 0;
            foreach (var item in sequence)
            {
                sum += item;
                yield return sum;
            }
        }

        public static IEnumerable<double> Proportionally(this IEnumerable<double> sequence, double sumTo)
        {
            double sequenceSum = sequence.Sum();
            double multiplier = sequenceSum == 0 ? 1.0 : sumTo / sequenceSum;
            foreach (var item in sequence)
            {
                yield return item * multiplier;
            }
        }
    }
}
