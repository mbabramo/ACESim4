using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESimBase.Util
{
    public static class Matrix
    {
        public static double[,] FromNested(this IEnumerable<IEnumerable<double>> A)
        {
            int rows = A.Count();
            int cols = A.First().Count();
            var result = new double[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                var list = A.Skip(r).First().ToList();
                for (int c = 0; c < cols; c++)
                    result[r, c] = list[c];
            }
            return result;
        }

        public static int Rows(this double[,] A)
        {
            return A.GetLength(0);
        }
        public static int Columns(this double[,] A)
        {
            return A.GetLength(1);
        }

        public static bool ColumnIsBasic(this double[,] A, int col)
        {
            int rows = A.Rows();
            int numNonZero = 0;
            for (int r = 0; r < rows; r++)
            {
                if (A[r, col] != 0)
                    numNonZero++;
            }
            return numNonZero == 1;
        }

        public static string GetColumnLabelsString(this double[,] A, int widthEachElement)
        {
            int cols = A.Columns() - 1; // exclude last column
            StringBuilder s = new StringBuilder();
            for (int c = 0; c < cols; c++)
            {
                string itemString = "";
                if (!A.ColumnIsBasic(c))
                {
                    itemString = c.ToString();
                    s.Append(itemString);
                }
                for (int k = itemString.Length; k < widthEachElement; k++)
                    s.Append(" ");
            }
            return s.ToString();
        }

        public static double[,] KeepColumns(this double[,] A, bool[] columnsToKeep)
        {
            int rows = A.Rows();
            int initialColumns = A.Columns();
            int numColumnsToKeep = columnsToKeep.Count(x => x == true);
            double[,] result = new double[rows, numColumnsToKeep];
            for (int r = 0; r < rows; r++)
            {
                int targetColumn = 0;
                for (int c = 0; c < initialColumns; c++)
                {
                    if (columnsToKeep[c])
                    {
                        result[r, targetColumn] = A[r, c];
                        targetColumn++;
                    }
                }
            }
            return result;
        }

        public static double[,] RemoveLabeledColumns(this double[,] A)
        {
            int columns = A.Columns();
            bool[] labeled = Enumerable.Range(0, columns).Select(col => col == columns - 1 ? false : !A.ColumnIsBasic(col)).ToArray();
            bool[] columnsToKeep = labeled.Select(x => !x).ToArray();
            double[,] result = A.KeepColumns(columnsToKeep);
            return result;
        }


        public static void MakePositive(this double[,] A)
        {
            int rowsA = A.GetLength(0);
            int colsA = A.GetLength(1);
            double min = A[0, 0];
            for (int r = 0; r < rowsA; r++)
                for (int c = 0; c < colsA; c++)
                {
                    if (A[r, c] < min)
                        min = A[r, c];
                }
            if (min <= 0)
            {
                for (int r = 0; r < rowsA; r++)
                    for (int c = 0; c < colsA; c++)
                        A[r, c] -= min - 1.0;
            }
        }


        public static double[,] Append(this double[,] A, double[,] B)
        {
            int rowsA = A.GetLength(0);
            int colsA = A.GetLength(1);
            int rowsB = B.GetLength(0);
            int colsB = B.GetLength(1);
            if (rowsA != rowsB)
                throw new ArgumentException();
            double[,] result = new double[rowsA, colsA + colsB];
            for (int r = 0; r < rowsA; r++)
                for (int c = 0; c < colsA + colsB; c++)
                {
                    if (c < colsA)
                        result[r, c] = A[r, c];
                    else
                        result[r, c] = B[r, c - colsA];
                }
            return result;
        }

        public static double[] SolveLinearSystem(this double[,] x)
        {
            var rows = x.GetLength(0);
            var cols = x.GetLength(1);
            if (rows != cols - 1)
                throw new ArgumentException();
            var B = x.GetColumn(cols - 1);
            var A = x.KeepColumns(Enumerable.Range(0, cols).Select(x => x == cols - 1 ? false : true).ToArray());
            alglib.rmatrixsolvefast(A, rows, ref B, out int info);
            if (info != 1)
                throw new Exception("Linear system does not have unique solutions.");
            return B; // B is overwritten with the answer.
        }

        public static double[,] Identity(int size)
        {
            double[,] result = new double[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    result[r, c] = (r == c) ? 1.0 : 0;
                }
            return result;
        }

        public static double[,] One(int rows)
        {
            double[,] result = new double[rows, 1];
            for (int r = 0; r < rows; r++)
                result[r, 0] = 1.0;
            return result;
        }

        public static double[] Mean(this double[,] A)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[] M = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                    M[c] += A[r, c];
                M[c] /= (double)rows;
            }
            return M;
        }

        public static double[] Stdev(this double[,] A)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[] result = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                ACESim.StatCollector s = new ACESim.StatCollector();
                for (int r = 0; r < rows; r++)
                    s.Add(A[r, c]);
                result[c] = s.StandardDeviation();
            }
            return result;
        }

        public static (double[,] centered, double[] mean) MeanCentered(this double[,] A)
        {
            var mean = A.Mean();
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = A[r, c] - mean[c];
            return (C, mean);
        }

        public static (double[,] zscored, double[] mean, double[] stdev) ZScored(this double[,] A)
        {
            var mean = A.Mean();
            var stdev = A.Stdev();
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = stdev[c] == 0 ? 0 : (A[r, c] - mean[c]) / stdev[c];
            return (C, mean, stdev);
        }

        public static double[,] ReverseMeanCentered(this double[,] A, double[] mean)
        {

            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = A[r, c] + mean[c];
            return C;
        }

        public static double[,] ReverseZScored(this double[,] A, double[] mean, double[] stdev)
        {

            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = stdev[c] * A[r, c] + mean[c];
            return C;
        }

        public static double[] ReverseZScored(this double[] A, double[] mean, double[] stdev)
        {

            int rows = 1;
            int cols = A.GetLength(0);
            double[] C = new double[cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[c] = stdev[c] * A[c] + mean[c];
            return C;
        }

        public static double[,] Scaled(this double[,] A)
        {
            double maxAbs = A.Scale();
            double[,] scaled = A.Multiply(1.0 / maxAbs);
            return scaled;
        }

        public static double Scale(this double[,] A)
        {
            double max = A[0, 0];
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double value = Math.Abs(A[r, c]);
                    if (value > max)
                        max = value;
                }
            return max;
        }

        public static IEnumerable<double[]> GetColumns(this double[,] A)
        {
            int cols = A.GetLength(1);
            for (int c = 0; c < cols; c++)
                yield return GetColumn(A, c);
        }

        public static double[] GetColumn(this double[,] A, int index)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[] result = new double[rows];
            for (int r = 0; r < rows; r++)
                result[r] = A[r, index];
            return result;
        }

        public static IEnumerable<double[]> GetRows(this double[,] A)
        {
            int rows = A.GetLength(0);
            for (int r = 0; r < rows; r++)
                yield return GetRow(A, r);
        }

        public static double[] GetRow(this double[,] A, int index)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[] result = new double[cols];
            for (int c = 0; c < cols; c++)
                result[c] = A[index, c];
            return result;
        }

        public static double[,] Plus(this double[,] A, double[,] B)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = A[r, c] + B[r, c];
            return C;
        }

        public static double[,] Multiply(this double[,] A, double factor)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            double[,] C = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    C[r, c] = A[r, c] + factor;
            return C;
        }

        public static double[,] Transpose(this double[,] A)
        {
            int rowsInTranspose = A.GetLength(1);
            int colsInTranspose = A.GetLength(0);
            double[,] T = new double[rowsInTranspose, colsInTranspose];
            for (int r = 0; r < rowsInTranspose; r++)
                for (int c = 0; c < colsInTranspose; c++)
                    T[r, c] = A[c, r];
            return T;
        }

        public static double[,] Multiply(this double[,] A, double[,] B)
        {
            int rA = A.GetLength(0);
            int cA = A.GetLength(1);
            int rB = B.GetLength(0);
            int cB = B.GetLength(1);
            double temp = 0;
            double[,] kHasil = new double[rA, cB];
            if (cA != rB)
            {
                throw new Exception("matrik can't be multiplied !!");
            }
            else
            {
                for (int i = 0; i < rA; i++)
                {
                    for (int j = 0; j < cB; j++)
                    {
                        temp = 0;
                        for (int k = 0; k < cA; k++)
                        {
                            temp += A[i, k] * B[k, j];
                        }
                        kHasil[i, j] = temp;
                    }
                }
                return kHasil;
            }
        }

        public static double[] Multiply(this double[] A, double[,] B)
        {
            int rA = 1;
            int cA = A.GetLength(0);
            int rB = B.GetLength(0);
            int cB = B.GetLength(1);
            double temp = 0;
            double[] kHasil = new double[cB];
            if (cA != rB)
            {
                throw new Exception("matrix can't be multiplied !!");
            }
            else
            {
                for (int i = 0; i < rA; i++)
                {
                    for (int j = 0; j < cB; j++)
                    {
                        temp = 0;
                        for (int k = 0; k < cA; k++)
                        {
                            temp += A[k] * B[k, j];
                        }
                        kHasil[j] = temp;
                    }
                }
                return kHasil;
            }
        }

        public static bool ConfirmNash(this double[,] A, double[,] B, double[] A_MixedStrategy, double[] B_MixedStrategy, double precision = 1E-10)
        {
            int aStrategies = A_MixedStrategy.Length;
            int bStrategies = B_MixedStrategy.Length;
            var mixedResult = ExpectedUtility(A, B, A_MixedStrategy, B_MixedStrategy);
            for (int aPure = 0; aPure < aStrategies; aPure++)
            {
                var pureResult = ExpectedUtility(A, B, aPure, B_MixedStrategy);
                if (pureResult.a > mixedResult.a + precision)
                    return false;
            }
            for (int bPure = 0; bPure < bStrategies; bPure++)
            {
                var pureResult = ExpectedUtility(A, B, A_MixedStrategy, bPure);
                if (pureResult.b > mixedResult.b + precision)
                    return false;
            }
            return true;
        }

        public static (double a, double b) ExpectedUtility(this double[,] A, double[,] B, int A_PureIndex, double[] B_MixedStrategy)
        {
            double[] aMixed = Enumerable.Range(0, A.GetLength(0)).Select(x => x == A_PureIndex ? 1.0 : 0).ToArray();
            return ExpectedUtility(A, B, aMixed, B_MixedStrategy);
        }
        public static (double a, double b) ExpectedUtility(this double[,] A, double[,] B, double[] A_MixedStrategy, int B_PureIndex)
        {
            double[] bMixed = Enumerable.Range(0, A.GetLength(1)).Select(x => x == B_PureIndex ? 1.0 : 0).ToArray();
            return ExpectedUtility(A, B, A_MixedStrategy, bMixed);
        }

        public static (double a, double b) ExpectedUtility(this double[,] A,  double[,] B, double[] A_MixedStrategy, double[] B_MixedStrategy)
        {
            double totalA = 0, totalB = 0;
            for (int i = 0; i < A_MixedStrategy.Length; i++)
            {
                double aProbability = A_MixedStrategy[i];
                if (aProbability > 0)
                {
                    for (int j = 0; j < B_MixedStrategy.Length; j++)
                    {
                        double bProbability = B_MixedStrategy[j];
                        if (bProbability > 0)
                        {
                            totalA += aProbability * bProbability * A[i, j];
                            totalB += aProbability * bProbability * B[i, j];
                        }
                    }
                }
            }
            return (totalA, totalB);
        }

        public static string ToString(this double?[,] matrix, int significantFigures = 4, int widthEachElement = 10)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                for (int j = 0; j < matrix.GetLength(1); j++)
                {
                    string itemString = matrix[i, j]?.ToSignificantFigures(significantFigures) ?? "";
                    if (itemString.Length > widthEachElement - 1)
                        itemString = itemString.Substring(0, widthEachElement - 1);
                    s.Append(itemString);
                    for (int k = itemString.Length; k < widthEachElement; k++)
                        s.Append(" ");
                }
                s.AppendLine();
            }
            return s.ToString();
        }

        public static string ToString(this double[,] matrix, int significantFigures = 4, int widthEachElement = 10)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                for (int j = 0; j < matrix.GetLength(1); j++)
                {
                    string itemString = matrix[i, j].ToSignificantFigures(significantFigures);
                    if (itemString.Length > widthEachElement - 1)
                        itemString = itemString.Substring(0, widthEachElement - 1);
                    s.Append(itemString);
                    for (int k = itemString.Length; k < widthEachElement; k++)
                        s.Append(" ");
                }
                s.AppendLine();
            }
            return s.ToString();
        }

        public static string ToCodeStringPython(this double[,] matrix, int significantFigures = 4) => ToCodeString(matrix, significantFigures, '[', ']');

        public static string ToCodeStringSpaces(this double[,] matrix, int significantFigures = 4) => ToCodeString(matrix, significantFigures, ' ', ' ', ' ', '\n');

        public static string ToCodeString(this double[,] matrix, int significantFigures = 4, char leftDelimeter = '{', char rightDelimeter = '}', char betweenItems = ',', char betweenRows = ',')
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            StringBuilder s = new StringBuilder();
            s.Append($"{leftDelimeter}");
            for (int i = 0; i < rows; i++)
            {
                s.Append($"{leftDelimeter} ");
                for (int j = 0; j < cols; j++)
                {
                    string itemString = matrix[i, j].ToSignificantFigures(significantFigures);
                    s.Append(itemString);
                    if (j < cols - 1)
                        s.Append($"{betweenItems} ");
                }
                s.Append($" {rightDelimeter}");
                if (i < rows - 1)
                    s.Append($"{betweenRows} ");
            }
            s.AppendLine($"{rightDelimeter}");
            return s.ToString();
        }
    }
}
