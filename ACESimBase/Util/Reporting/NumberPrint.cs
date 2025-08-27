using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util.Reporting
{
    public static class NumberPrint
    {
        public static string ToDecimalPlaces(this double d, int decimalPlaces = 2) => double.IsNaN(d) ? "N/A" : d.ToString($"F{decimalPlaces}"); // Decimal.Round((decimal)d, digits).ToString();

        public static double RoundToSignificantFigures(this double d, int digits = 4)
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

        public static string ToSignificantFigures(this IEnumerable<double?> nums, int numSignificantFigures = 4) => nums == null ? "" : string.Join(",", nums.Select(num => num.ToSignificantFigures(numSignificantFigures)));

        public static string ToSignificantFigures(this IEnumerable<double> nums, int numSignificantFigures = 4) => nums == null ? "" : string.Join(",", nums.Select(num => ((double?)num).ToSignificantFigures(numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this IEnumerable<double?> nums, int numSignificantFigures = 4) => nums == null ? "" : string.Join(",", nums.Select(num => num.ToSignificantFigures_WithSciNotationForVerySmall(numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this IEnumerable<double> nums, int numSignificantFigures = 4) => nums == null ? "" : string.Join(",", nums.Select(num => ((double?)num).ToSignificantFigures_WithSciNotationForVerySmall(numSignificantFigures)));

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this double num, int numSignificantFigures = 4) => ((double?)num).ToSignificantFigures_WithSciNotationForVerySmall(numSignificantFigures);

        public static string ToSignificantFigures_WithSciNotationForVerySmall(this double? num, int numSignificantFigures = 4)
        {
            if (-1 < num && num < 1)
            {
                if (num < 0)
                    return "-" + ((double)(0 - num)).ToSignificantFigures_WithSciNotationForVerySmall(numSignificantFigures);
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
            return num.ToSignificantFigures(numSignificantFigures);
        }

                // LaTeX variant: mirrors ToSignificantFigures_WithSciNotationForVerySmall but outputs "mantissa \\times 10^{exponent}"
        public static string ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(this double num, int numSignificantFigures = 4)
            => ((double?)num).ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(numSignificantFigures);

        public static string ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(this double? num, int numSignificantFigures = 4)
        {
            if (num == null || double.IsNaN(num.Value))
                return "--";
            if (num == 0)
                return "0";
            if (num < 0)
                return "-" + ((-num).ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(numSignificantFigures));

            double value = num.Value;

            // Use scientific form only if original logic would (|value| < 1 and < 1e-3)
            if (value < 1 && value < 0.001)
            {
                double rounded = RoundToSignificantFigures(value, numSignificantFigures);
                if (rounded == 0)
                    return "0";

                int exponent = (int)Math.Floor(Math.Log10(rounded));
                double mantissa = rounded / Math.Pow(10, exponent);

                // Handle case where rounding pushes mantissa to 10
                if (mantissa >= 10)
                {
                    mantissa /= 10;
                    exponent += 1;
                }

                int decimals = Math.Max(0, numSignificantFigures - 1);
                string mantissaStr = mantissa.ToString("0." + new string('0', decimals), System.Globalization.CultureInfo.InvariantCulture);

                return $"${mantissaStr} \\times 10^{{{exponent}}}$";
            }

            // Otherwise plain significant figures (no scientific notation)
            return RoundToSignificantFigures(value, numSignificantFigures)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ToSignificantFigures_MaxLength(this double? num, int numSignificantFigures, int maxLength)
        {
            string result = num.ToSignificantFigures(numSignificantFigures);
            if (result.Length <= maxLength)
                return result;
            result = num.ToSignificantFigures_WithSciNotationForVerySmall(numSignificantFigures);
            if (result.Length <= maxLength)
                return result;
            if (numSignificantFigures == 1)
                return result;
            result = num.ToSignificantFigures_MaxLength(Math.Max(numSignificantFigures - 1, 1), maxLength);
            return result; // not guaranteed to fit, but we did our best
        }
    }
}
