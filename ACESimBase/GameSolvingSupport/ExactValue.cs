using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct ExactValue : MaybeExact<ExactValue>
    {
        public static bool AbbreviateExactValues = false;

        private readonly Rational V;

        public ExactValue(int i)
        {
            V = (Rational)i;
        }

        public MaybeExact<ExactValue> NewValueFromInteger(int i) => new ExactValue(i);
        public MaybeExact<ExactValue> NewValueFromRational(Rational r) => new ExactValue(r);

        public ExactValue(Rational v)
        {
            V = v;
        }

        public static MaybeExact<ExactValue> Zero() => ExactValue.FromInteger(0);
        public static MaybeExact<ExactValue> One() => ExactValue.FromInteger(1);
        public static MaybeExact<ExactValue> FromInteger(int i) => new ExactValue((Rational)i);
        public static MaybeExact<ExactValue> FromRational(Rational r) => new ExactValue(r);

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

        public MaybeExact<ExactValue> CanonicalForm => FromRational(V.CanonicalForm);

        public MaybeExact<ExactValue> Numerator => ExactValue.FromRational(V.Numerator);
        public MaybeExact<ExactValue> Denominator => ExactValue.FromRational(V.Denominator);
        public double AsDouble => (double)V;
        public Rational AsRational => V;
        public bool IsExact => true;

        public bool IsEqualTo(MaybeExact<ExactValue> b) => V == b.AsRational;
        public bool IsNotEqualTo(MaybeExact<ExactValue> b) => V != b.AsRational;

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

        public MaybeExact<ExactValue> LeastCommonMultiple(MaybeExact<ExactValue> b)
        /* a = least common multiple of a, b; b is preserved */
        {
            if (V.Denominator != 1 || b.AsRational.Denominator != 1)
                throw new Exception("LeastCommonMultiple operation not available.");
            var result = Times(b).DividedBy(MaybeExact<ExactValue>.FromRational((Rational)BigInteger.GreatestCommonDivisor(V.Numerator, b.AsRational.Numerator)));
            return result.CanonicalForm;
        }

        public bool IsGreaterThan(MaybeExact<ExactValue> b)
        {
            return V > b.AsRational;
        }

        public bool IsLessThan(MaybeExact<ExactValue> b)
        {
            return V < b.AsRational;
        }

        public MaybeExact<ExactValue> Plus(MaybeExact<ExactValue> b)
        {
            return MaybeExact<ExactValue>.FromRational((V + b.AsRational).CanonicalForm);
        }

        public MaybeExact<ExactValue> Minus(MaybeExact<ExactValue> b)
        {
            return MaybeExact<ExactValue>.FromRational((V - b.AsRational).CanonicalForm);
        }

        public MaybeExact<ExactValue> Negated() => Zero().Minus(this);

        public MaybeExact<ExactValue> Times(MaybeExact<ExactValue> b)
        {
            return MaybeExact<ExactValue>.FromRational((V * b.AsRational).CanonicalForm);
        }

        public MaybeExact<ExactValue> DividedBy(MaybeExact<ExactValue> b)
        {
            return MaybeExact<ExactValue>.FromRational((V / b.AsRational).CanonicalForm);
        }

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
    }
}
