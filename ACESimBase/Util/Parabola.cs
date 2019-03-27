using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class Parabola
    {
        internal double denom, A, B, C;

        public Parabola(double x1_, double y1_, double x2_, double y2_, double x3_, double y3_)
        {
            denom = (x1_ - x2_)*(x1_ - x3_)*(x2_ - x3_);
            A = (x3_ * (y2_ - y1_) + x2_ * (y1_ - y3_) + x1_ * (y3_ - y2_)) / denom;
            B = (x3_*x3_ * (y1_ - y2_) + x2_*x2_ * (y3_ - y1_) + x1_*x1_ * (y2_ - y3_)) / denom;
            C = (x2_ * x3_ * (x2_ - x3_) * y1_ + x3_ * x1_ * (x3_ - x1_) * y2_ + x1_ * x2_ * (x1_ - x2_) * y3_) / denom;
        }

        public double GetYValueForX(double x)
        {
            return A * (x * x) + B * x + C;
        }

        public double GetXValueForY(double y)
        {
            // Ax^2 + Bx + C == y, so in the pythagorean formula, (C - y) is substituted in for C
            return PythagoreanFormula.GetResult(A, B, C - y, false);
        }
    }

    public static class PythagoreanFormula
    {
        public static double GetResult(double a, double b, double c, bool greaterResult)
        {
            if (greaterResult)
                return ((0 - b) + Math.Sqrt(b*b - 4.0 * a * c)) / 2 * a;
            else
                return ((0 - b) - Math.Sqrt(b * b - 4.0 * a * c)) / 2 * a;
        }
    }
}
