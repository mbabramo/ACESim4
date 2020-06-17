using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ACESimBase.Util
{
    public class ArrayComparer<T> : IComparer<T[]>
    {
        public int Compare([AllowNull] T[] first, [AllowNull] T[] second)
        {
            // If one of collection objects is null, use the default Comparer class
            // (null is considered to be less than any other object)
            if (first == null || second == null)
                return Comparer<T[]>.Default.Compare(first, second);

            var elementComparer = Comparer<T>.Default;
            int compareResult;
            System.Collections.IEnumerator firstEnum = first.GetEnumerator();
            var secondEnum = second.GetEnumerator();
            {
                do
                {
                    bool gotFirst = firstEnum.MoveNext();
                    bool gotSecond = secondEnum.MoveNext();

                    // Reached the end of collections => assume equal
                    if (!gotFirst && !gotSecond)
                        return 0;

                    // Different sizes => treat collection of larger size as "greater"
                    if (gotFirst != gotSecond)
                        return gotFirst ? 1 : -1;

                    compareResult = elementComparer.Compare((T) firstEnum.Current, (T) secondEnum.Current);
                } while (compareResult == 0);
            }

            return compareResult;
        }
    }
}
