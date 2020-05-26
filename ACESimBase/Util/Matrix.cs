using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util
{
    public static class Matrix
    {

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
    }
}
