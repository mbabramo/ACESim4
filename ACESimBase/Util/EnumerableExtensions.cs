using System;
using System.Collections.Generic;
using System.Linq;
using Tensorflow;

namespace ACESimBase.Util
{
    public static class EnumerableExtensions
    {

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
