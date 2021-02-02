using Rationals;
using System;
using System.Linq;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.BigIntegerOperations;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public static class RationalOperations
    {
        public static Rational Add(Rational a, Rational b)
        {
            return (a + b).CanonicalForm;
        }

        public static Rational Divide(Rational a, Rational b)
        {
            return (a / b).CanonicalForm;
        }

        public static Rational FromInteger(int i)
        {
            return i;
        }

        public static Rational Invert(Rational a)
        {
            
            return Rational.Invert(a).CanonicalForm;
        }
        public static bool Equality(Rational a, Rational b)
        {
            return a == b;
        }

        public static bool GreaterThan(Rational a, Rational b)
        {
            return a > b;
        }

        public static Rational Multiply(Rational a, Rational b)
        {
            return (a * b).CanonicalForm;
        }

        public static Rational Negate(Rational a)
        {
            return -a;
        }

        public static Rational Reduce(Rational a)
        {
            return a.CanonicalForm;
        }

        public static string ToNumericString(Rational r)
        {
            string s;
            if (r.Denominator == 1)
                s = r.Numerator.ToString();
            else if (r.Denominator == 0)
                s = $"{r.Numerator}/{r.Denominator}"; // show illegal div by 0
            else
                s = r.ToString();
            return s;
        }
    }
}
