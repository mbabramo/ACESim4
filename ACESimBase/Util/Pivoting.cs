using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using NumSharp;

namespace ACESimBase.Util
{
    public static class Pivoting
    {

        public static int[,] RotateMatrixCounterClockwise(this int[,] oldMatrix)
        {
            int[,] newMatrix = new int[oldMatrix.GetLength(1), oldMatrix.GetLength(0)];
            int newColumn, newRow = 0;
            for (int oldColumn = oldMatrix.GetLength(1) - 1; oldColumn >= 0; oldColumn--)
            {
                newColumn = 0;
                for (int oldRow = 0; oldRow < oldMatrix.GetLength(0); oldRow++)
                {
                    newMatrix[newRow, newColumn] = oldMatrix[oldRow, oldColumn];
                    newColumn++;
                }
                newRow++;
            }
            return newMatrix;
        }

        public static void Pivot(this double[,] A, int pivotColumn)
        {
            int pivotRow = A.MinimumRatioTest(pivotColumn);
            A.Pivot(pivotRow, pivotColumn);
        }

        private static void Pivot(this double[,] A, int pivotRow, int pivotColumn)
        {
            int rows = A.Rows();
            int cols = A.Columns();
            double pivotElement = A[pivotRow, pivotColumn];
            for (int r = 0; r < rows; r++)
            {
                if (r != pivotRow)
                {
                    double valueInPivotColumn = A[r, pivotColumn];
                    for (int c = 0; c < cols; c++)
                    {
                        A[r, c] = A[r, c] * pivotElement - A[pivotRow, c] * valueInPivotColumn;
                    }
                }
            }
        }

        private static int MinimumRatioTest(this double[,] A, int pivotColumn)
        {
            // choose the row to pivot on, by returning the row with the lowest ratio of 
            // the last column to the value in the pivot column. in fact, however, we 
            // do the maximum ratio test of the inverse, to avoid divide by zero errors
            int rows = A.Rows();
            int cols = A.Columns();
            int lastCol = cols - 1;
            int bestRow = 0;
            double maxRatio = A[0, pivotColumn] / A[0, lastCol];
            for (int r = 1; r < rows; r++)
            {
                double ratio = A[r, pivotColumn] / A[r, lastCol];
                if (ratio > maxRatio)
                {
                    bestRow = r;
                    maxRatio = ratio;
                }
            }
            return bestRow;
        }
    }
}
