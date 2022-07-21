using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct ExactValue : IMaybeExact<ExactValue>
    {
        public static bool AbbreviateValues = false;

        private readonly Rational V;

        public ExactValue(int i)
        {
            V = (Rational)i;
        }

        public ExactValue(Rational r)
        {
            V = r;
        }

        public IMaybeExact<ExactValue> NewValueFromInteger(int i) => new ExactValue(i);
        public IMaybeExact<ExactValue> NewValueFromRational(Rational r) => new ExactValue(r);

        static IMaybeExact<ExactValue> _Zero = IMaybeExact<ExactValue>.FromInteger(0);
        static IMaybeExact<ExactValue> _One = IMaybeExact<ExactValue>.FromInteger(1);
        public static IMaybeExact<ExactValue> Zero() => _Zero;
        public static IMaybeExact<ExactValue> One() => _One;
        public static IMaybeExact<ExactValue> FromInteger(int i) => new ExactValue((Rational)i);
        public static IMaybeExact<ExactValue> FromRational(Rational r) => new ExactValue(r);

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

        public IMaybeExact<ExactValue> CanonicalForm => FromRational(V.CanonicalForm);

        public IMaybeExact<ExactValue> Numerator => ExactValue.FromRational(V.Numerator);
        public IMaybeExact<ExactValue> Denominator => ExactValue.FromRational(V.Denominator);
        public double AsDouble => (double)V;
        public Rational AsRational => V;
        public bool IsExact => true;

        public bool IsEqualTo(IMaybeExact<ExactValue> b) => V == b.AsRational;
        public bool IsNotEqualTo(IMaybeExact<ExactValue> b) => V != b.AsRational;

        public override string ToString()
        {
            string s;

            if (V.Denominator == 1)
            {
                s = V.ToString();
                if (s.Length > 8 && AbbreviateValues)
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

        public IMaybeExact<ExactValue> LeastCommonMultiple(IMaybeExact<ExactValue> b)
        /* a = least common multiple of a, b; b is preserved */
        {
            if (V.Denominator != 1 || b.AsRational.Denominator != 1)
                throw new Exception("LeastCommonMultiple operation not available.");
            var result = Times(b).DividedBy(IMaybeExact<ExactValue>.FromRational((Rational)BigInteger.GreatestCommonDivisor(V.Numerator, b.AsRational.Numerator)));
            return result;
        }

        public bool IsGreaterThan(IMaybeExact<ExactValue> b)
        {
            return V > b.AsRational;
        }

        public bool IsLessThan(IMaybeExact<ExactValue> b)
        {
            return V < b.AsRational;
        }

        public IMaybeExact<ExactValue> Plus(IMaybeExact<ExactValue> b)
        {
            return IMaybeExact<ExactValue>.FromRational((V + b.AsRational).CanonicalForm);
        }

        public IMaybeExact<ExactValue> Minus(IMaybeExact<ExactValue> b)
        {
            return IMaybeExact<ExactValue>.FromRational((V - b.AsRational).CanonicalForm);
        }

        public IMaybeExact<ExactValue> Negated() => Zero().Minus(this);

        public IMaybeExact<ExactValue> Times(IMaybeExact<ExactValue> b)
        {
            return IMaybeExact<ExactValue>.FromRational((V * b.AsRational).CanonicalForm);
        }

        public IMaybeExact<ExactValue> DividedBy(IMaybeExact<ExactValue> b)
        {
            return IMaybeExact<ExactValue>.FromRational((V / b.AsRational).CanonicalForm);
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
    }
}
