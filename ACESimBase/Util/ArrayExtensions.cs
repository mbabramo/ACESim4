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
        public static string TableToString<T>(this T[][] arr)
        {
            int rowCount = arr.Length;
            int columnCount = arr[0].Length;
            int[] colLength = Enumerable.Range(0, columnCount).Select(c => arr.Max(r => r[c]?.ToString()?.Length ?? 0)).ToArray();
            StringBuilder b = new StringBuilder();
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < columnCount; c++)
                {
                    string s = arr[r][c].ToString();
                    b.Append(s);
                    int spacesNeeded = colLength[c] - s.Length;
                    for (int sp = 0; sp < spacesNeeded; sp++)
                        b.Append(" ");
                    if (c != columnCount - 1)
                        b.Append(" | ");
                }
                b.AppendLine();
            }
            return b.ToString();
        }

        public static T[][] TransposeRowsAndColumns<T>(this T[][] arr)
        {
            int rowCount = arr.Length;
            int columnCount = arr[0].Length;
            T[][] transposed = new T[columnCount][];
            if (rowCount == columnCount)
            {
                transposed = (T[][])arr.Clone();
                for (int i = 1; i < rowCount; i++)
        {
                    for (int j = 0; j < i; j++)
            {
                        T temp = transposed[i][j];
                        transposed[i][j] = transposed[j][i];
                        transposed[j][i] = temp;
                    }
                }
            }
            else
            {
                for (int column = 0; column < columnCount; column++)
        {
                    transposed[column] = new T[rowCount];
                    for (int row = 0; row < rowCount; row++)
            {
                        transposed[column][row] = arr[row][column];
                    }
                }
            }
            return transposed;
        }

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
