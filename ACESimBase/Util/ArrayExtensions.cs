using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public static class ArrayExtensions
    {
        public static T[] GetRowWithBlockCopy<T>(this T[,] array, int row)
        {
            if (!typeof(T).IsPrimitive)
                throw new InvalidOperationException("Not supported for managed types.");

            if (array == null)
                throw new ArgumentNullException("array");

            int cols = array.GetUpperBound(1) + 1;
            T[] result = new T[cols];

            int size;

            if (typeof(T) == typeof(bool))
                size = 1;
            else if (typeof(T) == typeof(char))
                size = 2;
            else
                size = Marshal.SizeOf<T>();

            Buffer.BlockCopy(array, row * cols * size, result, 0, cols * size);

            return result;
        }
        public static T[] GetColumn<T>(this T[,] multidimArray, int wanted_column)
        {
            int l = multidimArray.GetLength(0);
            T[] columnArray = new T[l];
            for (int i = 0; i < l; i++)
            {
                columnArray[i] = multidimArray[i, wanted_column];
            }
            return columnArray;
        }

        public static T[] GetRow<T>(this T[,] multidimArray, int wanted_row)
        {
            int l = multidimArray.GetLength(1);
            T[] rowArray = new T[l];
            for (int i = 0; i < l; i++)
            {
                rowArray[i] = multidimArray[wanted_row, i];
            }
            return rowArray;
        }
    }
}
