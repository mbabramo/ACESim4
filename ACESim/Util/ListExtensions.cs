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

    static class ListExtensions
    {

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
