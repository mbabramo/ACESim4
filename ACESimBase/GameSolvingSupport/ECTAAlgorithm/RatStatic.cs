using Rationals;
using System;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.MultiprecisionStatic;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public static class RatStatic
    {
        public static Rational ratadd(Rational a, Rational b)
        {
            return a + b;
        }

        public static Rational ratdiv(Rational a, Rational b)
        {
            return a / b;
        }

        public static Rational ratfromi(int i)
        {
            return i;
        }

        public static Rational ratinv(Rational a)
        {
            
            return 1 / a;
        }
        public static bool ratiseq(Rational a, Rational b)
        {
            return a == b;
        }

        public static bool ratgreat(Rational a, Rational b)
        {
            return a > b;
        }

        public static Rational ratmult(Rational a, Rational b)
        {
            return (a * b).CanonicalForm;
        }

        public static Rational ratneg(Rational a)
        {
            return -a;
        }

        public static Rational ratreduce(Rational a)
        {
            return a.CanonicalForm;
        }

        public static int rattoa(Rational r, char[] s)
        {
            s = r.ToString().ToCharArray();
            return s.Length;
        }

        public static int rattoa(Rational r, ref string s)
        {
            s = r.ToString();
            return s.Length;
        }

        public static Rational contfract(double x, int accuracy)
        {
            return Rational.Approximate(x, 1.0 / (double)accuracy);
        }
    }
}
