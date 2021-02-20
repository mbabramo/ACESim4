using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class NumberPrint
    {
        public static string ToDecimalPlaces(this double d, int decimalPlaces = 2) => double.IsNaN(d) ? "N/A" : d.ToString($"F{decimalPlaces}"); // Decimal.Round((decimal)d, digits).ToString();

        public static double RoundToSignificantFigures(double d, int digits = 4) 
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                return d;
            if (d == 0.0)
            {
                return 0.0;
            }
            else
            {
                bool negative = d < 0;
                double negMultiplier = negative ? -1 : 1;
                if (negative)
                {
                    d = -d;
                }
                double leftSideNumbers = Math.Floor(Math.Log10(Math.Abs(d))) + 1;
                double scale = Math.Pow(10, leftSideNumbers);
                double result = scale * Math.Round(d / scale, digits, MidpointRounding.AwayFromZero);

                // Clean possible precision error.
                if ((int)leftSideNumbers >= digits)
                {
                    return negMultiplier * Math.Round(result, 0, MidpointRounding.AwayFromZero);
                }
                else
                {
                    if (digits - (int)leftSideNumbers > 15) // can't round more than 15 digits
                        return negMultiplier * RoundToSignificantFigures(result / scale, digits) * scale;
                    return negMultiplier * Math.Round(result, digits - (int)leftSideNumbers, MidpointRounding.AwayFromZero);
                }
            }
        }

        public static string ToSignificantFigures(this float num, int numSignificantFigures = 4)
        {
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }

        public static string ToSignificantFigures(this float? num, int numSignificantFigures = 4)
        {
            if (num == null || double.IsNaN((double)num))
                return "--";
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }

        public static string ToSignificantFigures(this double num, int numSignificantFigures = 4)
        {
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }

        public static string ToSignificantFigures(this double? num, int numSignificantFigures = 4)
        {
            if (num == null || double.IsNaN((double)num))
                return "--";
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }

        public static string ToSignificantFigures(this IEnumerable<double?> nums, int numSignificantFigures = 4) => nums == null ? "" : String.Join(",", nums.Select(num => ToSignificantFigures((double?)num, numSignificantFigures)));

        public static string ToSignificantFigures(this IEnumerable<double> nums, int numSignificantFigures = 4) => nums == null ? "" : String.Join(",", nums.Select(num => ToSignificantFigures((double?)num, numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this IEnumerable<double?> nums, int numSignificantFigures = 4) => nums == null ? "" : String.Join(",", nums.Select(num => ToSignificantFigures_WithSciNotationForVerySmall((double?)num, numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this IEnumerable<double> nums, int numSignificantFigures = 4) => nums == null ? "" : String.Join(",",nums.Select(num => ToSignificantFigures_WithSciNotationForVerySmall((double?)num, numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this double num, int numSignificantFigures = 4) => ToSignificantFigures_WithSciNotationForVerySmall((double?)num, numSignificantFigures);

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this double? num, int numSignificantFigures = 4)
        {
            if (-1 < num && num < 1)
            {
                if (num < 0)
                    return "-" + ToSignificantFigures_WithSciNotationForVerySmall((double)(0 - num), numSignificantFigures);
                double rounded = RoundToSignificantFigures((double)num, numSignificantFigures);
                if (rounded == 0)
                    return "0";
                if (num < 0.0001)
                {
                    string exponentString;
                    if (num <= 1E-100)
                        exponentString = "E000";
                    else if (num <= 1E-10)
                        exponentString = "E00";
                    else
                        exponentString = "E0";
                    string sigFiguresAfterDecimal = new string('0', numSignificantFigures - 1);
                    return rounded.ToString("0." + sigFiguresAfterDecimal + exponentString);
                }
            }
            return ToSignificantFigures(num, numSignificantFigures);
        }

        public static string ToSignificantFigures_MaxLength(this double? num, int numSignificantFigures, int maxLength)
        {
            string result = ToSignificantFigures(num, numSignificantFigures);
            if (result.Length <= maxLength)
                return result;
            result = ToSignificantFigures_WithSciNotationForVerySmall(num, numSignificantFigures);
            if (result.Length <= maxLength)
                return result;
            if (numSignificantFigures == 1)
                return result;
            result = ToSignificantFigures_MaxLength(num, Math.Max(numSignificantFigures - 1, 1), maxLength);
            return result; // not guaranteed to fit, but we did our best
        }
    }
}
