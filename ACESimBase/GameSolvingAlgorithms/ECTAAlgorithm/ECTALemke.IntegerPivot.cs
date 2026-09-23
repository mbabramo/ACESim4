using System;
using System.Numerics;
using ACESimBase.GameSolvingSupport.ExactValues;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public partial class ECTALemke<T> where T : IMaybeExact<T>, new()
    {
        // Column LCM scaling makes the initial tableau integral, with det = -1.
        // The fraction-free update is the signed determinantal (Sylvester)
        // identity. Its numerator is divisible by the previous determinant.
        // The absolute pivot becomes the next determinant; the negative-pivot
        // row/column conventions below are exactly those in Pivot.
        // Every division checks its remainder, including in Release builds.
        private bool TryPivotIntegers(int leave, int enter)
        {
            // Public callers can supply fractional tableaux. Fall back BEFORE
            // changing anything, so that their previous rational behavior stays.
            if (determinant.AsRational.Denominator != BigInteger.One) return false;
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= n + 1; j++)
                    if (Tableau[i][j].AsRational.Denominator != BigInteger.One) return false;

            int row = TableauRow(leave), col = TableauColumn(enter);
            BigInteger det = determinant.AsRational.Numerator;
            BigInteger signedPivot = Tableau[row][col].AsRational.Numerator;
            bool negative = signedPivot.Sign < 0;
            BigInteger pivot = BigInteger.Abs(signedPivot);
            var pivotRow = new BigInteger[n + 2];
            for (int j = 0; j <= n + 1; j++) pivotRow[j] = Tableau[row][j].AsRational.Numerator;

            for (int i = 0; i < n; i++)
            {
                if (i == row) continue;
                var values = Tableau[i];
                BigInteger inColumn = values[col].AsRational.Numerator;
                for (int j = 0; j <= n + 1; j++)
                {
                    if (j == col) continue;
                    BigInteger numerator = values[j].AsRational.Numerator * pivot;
                    if (!inColumn.IsZero)
                    {
                        BigInteger product = inColumn * pivotRow[j];
                        numerator = negative ? numerator + product : numerator - product;
                    }
                    values[j] = IMaybeExact<T>.FromRational(DivideExactly(numerator, det));
                }
                if (!inColumn.IsZero && !negative) ChangeSignInTableau(i, col);
            }
            SetValueInTableau(row, col, determinant);
            if (negative) NegateTableauRow(row);
            determinant = IMaybeExact<T>.FromRational(pivot);
            variableIndexToBasicCobasicIndex[leave] = col + n;
            basicCobasicIndexToVariable[col + n] = leave;
            variableIndexToBasicCobasicIndex[enter] = row;
            basicCobasicIndexToVariable[row] = enter;
            pivotnum++;
            return true;
        }

        private static BigInteger DivideExactly(BigInteger numerator, BigInteger divisor)
        {
            BigInteger quotient = BigInteger.DivRem(numerator, divisor, out BigInteger remainder);
            if (!remainder.IsZero)
                throw new ArithmeticException("ECTA fraction-free pivot produced a nonintegral quotient.");
            return quotient;
        }
    }
}
