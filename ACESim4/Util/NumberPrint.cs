using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class NumberPrint
    {
        private static double roundToSignificantFigures_old(double num, int n)
        {
            if (num == 0)
            {
                return 0;
            }

            double d = Math.Ceiling(Math.Log10(num < 0 ? -num : num));
            int power = n - (int)d;

            double magnitude = Math.Pow(10, power);
            long shifted = (long)Math.Round(num * magnitude);
            return shifted / magnitude;
        }


        public static double RoundToSignificantFigures(double d, int digits = 6) // DEBUG -- return to 4
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                return d;
            if (d == 0.0)
            {
                return 0.0;
            }
            else
            {
                double leftSideNumbers = Math.Floor(Math.Log10(Math.Abs(d))) + 1;
                double scale = Math.Pow(10, leftSideNumbers);
                double result = scale * Math.Round(d / scale, digits, MidpointRounding.AwayFromZero);

                // Clean possible precision error.
                if ((int)leftSideNumbers >= digits)
                {
                    return Math.Round(result, 0, MidpointRounding.AwayFromZero);
                }
                else
                {
                    if (digits - (int)leftSideNumbers > 15) // can't round more than 15 digits
                        return RoundToSignificantFigures(result / scale, digits) * scale;
                    return Math.Round(result, digits - (int)leftSideNumbers, MidpointRounding.AwayFromZero);
                }
            }
        }

        public static string ToSignificantFigures(this double num, int numSignificantFigures = 4)
        {
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }

        public static string ToSignificantFigures(this double? num, int numSignificantFigures = 6) // DEBUG -- return to 4
        {
            if (num == null || double.IsNaN((double)num))
                return "--";
            return RoundToSignificantFigures((double)num, numSignificantFigures).ToString();
        }
    }
}
