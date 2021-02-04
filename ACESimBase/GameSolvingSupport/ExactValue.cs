using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct ExactValue : IPotentiallyExactValue<ExactValue>
    {
        public static bool AbbreviateExactValues = false;

        private readonly Rational V;

        public ExactValue(Rational v)
        {
            V = v;
        }

        public static IPotentiallyExactValue<ExactValue> Zero() => ExactValue.FromInteger(0);
        public static IPotentiallyExactValue<ExactValue> One() => ExactValue.FromInteger(1);
        public static IPotentiallyExactValue<ExactValue> FromInteger(int i) => new ExactValue((Rational)i);
        public static IPotentiallyExactValue<ExactValue> FromRational(Rational r) => new ExactValue(r);

        public bool IsPositive()
        {
            return V > 0;
        }
        public bool IsNegative()
        {
            return V < 0;
        }

        public bool IsZero()
        {
            return V.IsZero;
        }

        public bool IsOne()
        {
            return V.IsOne;
        }

        public IPotentiallyExactValue<ExactValue> CanonicalForm => FromRational(V.CanonicalForm);

        public IPotentiallyExactValue<ExactValue> Numerator => ExactValue.FromRational(V.Numerator);
        public IPotentiallyExactValue<ExactValue> Denominator => ExactValue.FromRational(V.Denominator);
        public double AsDouble => (double)V;
        public Rational AsRational => V;
        public bool IsExact => true;

        public bool IsEqualTo(IPotentiallyExactValue<ExactValue> b) => V == b.AsRational;
        public bool IsNotEqualTo(IPotentiallyExactValue<ExactValue> b) => V != b.AsRational;

        public override string ToString()
        {
            string s;

            if (V.Denominator == 1)
            {
                s = V.ToString();
                if (s.Length > 8 && AbbreviateExactValues)
                    return V.ToString("E5");
                else
                    return s;
            }

            if (V.Denominator == 1)
                s = V.Numerator.ToString();
            else if (V.Denominator == 0)
                s = $"{V.Numerator}/{V.Denominator}"; // show illegal div by 0
            else
                s = V.ToString();
            return s;
        }

        public IPotentiallyExactValue<ExactValue> LeastCommonMultiple(IPotentiallyExactValue<ExactValue> b)
        /* a = least common multiple of a, b; b is preserved */
        {
            if (V.Denominator != 1 || b.AsRational.Denominator != 1)
                throw new Exception("LeastCommonMultiple operation not available.");
            var result = Times(b).DividedBy(IPotentiallyExactValue<ExactValue>.FromRational((Rational)BigInteger.GreatestCommonDivisor(V.Numerator, b.AsRational.Numerator)));
            return result.CanonicalForm;
        }

        public bool IsGreaterThan(IPotentiallyExactValue<ExactValue> b)
        {
            return V > b.AsRational;
        }

        public bool IsLessThan(IPotentiallyExactValue<ExactValue> b)
        {
            return V < b.AsRational;
        }

        public IPotentiallyExactValue<ExactValue> Plus(IPotentiallyExactValue<ExactValue> b)
        {
            return IPotentiallyExactValue<ExactValue>.FromRational((V + b.AsRational).CanonicalForm);
        }

        public IPotentiallyExactValue<ExactValue> Minus(IPotentiallyExactValue<ExactValue> b)
        {
            return IPotentiallyExactValue<ExactValue>.FromRational((V - b.AsRational).CanonicalForm);
        }

        public IPotentiallyExactValue<ExactValue> Negated() => Zero().Minus(this);

        public IPotentiallyExactValue<ExactValue> Times(IPotentiallyExactValue<ExactValue> b)
        {
            return IPotentiallyExactValue<ExactValue>.FromRational((V * b.AsRational).CanonicalForm);
        }

        public IPotentiallyExactValue<ExactValue> DividedBy(IPotentiallyExactValue<ExactValue> b)
        {
            return IPotentiallyExactValue<ExactValue>.FromRational((V / b.AsRational).CanonicalForm);
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
    }
}
