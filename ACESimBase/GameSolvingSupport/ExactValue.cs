using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public readonly struct ExactValue
    {
		public static bool AbbreviateExactValues = false;

        private readonly Rational V;

		public ExactValue(Rational v)
		{
			V = v;
		}

		public static ExactValue Zero() => ExactValue.FromInteger(0);
		public static ExactValue One() => ExactValue.FromInteger(1);
		public static ExactValue FromInteger(int i) => new ExactValue((Rational) i);
		public static ExactValue FromRational(Rational r) => new ExactValue(r);

		public static implicit operator ExactValue(Rational b) => new ExactValue(b);

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

		public ExactValue CanonicalForm => V.CanonicalForm;

		public ExactValue Numerator => ExactValue.FromRational(V.Numerator);
		public ExactValue Denominator => ExactValue.FromRational(V.Denominator);
		public double AsDouble => (double)V;
		public Rational AsRational => V;
		public bool IsExact => true;

		public bool IsEqualTo(ExactValue b) => V == b.V;
		public bool IsNotEqualTo(ExactValue b) => V != b.V;

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

		public ExactValue LeastCommonMultiple(ExactValue b)
		/* a = least common multiple of a, b; b is preserved */
		{
			if (V.Denominator != 1 || b.V.Denominator != 1)
				throw new Exception("LeastCommonMultiple operation not available.");
			var result = Times(b).DividedBy((Rational) BigInteger.GreatestCommonDivisor(V.Numerator, b.V.Numerator));
			return result.CanonicalForm;
		}

		public bool IsGreaterThan(ExactValue b)
		{
			return V > b.V;
		}

		public bool IsLessThan(ExactValue b)
		{
			return V < b.V;
		}

		public ExactValue Plus(ExactValue b)
		{
			return (V + b.V).CanonicalForm;
		}

		public ExactValue Minus(ExactValue b)
		{
			return (V - b.V).CanonicalForm;
		}

		public ExactValue Negated() => Zero().Minus(this);

		public ExactValue Times(ExactValue b)
		{
			return (V * b.V).CanonicalForm;
		}

		public ExactValue DividedBy(ExactValue b)
		{
			return (V / b.V).CanonicalForm;
		}

        public override int GetHashCode()
        {
            return V.GetHashCode();
        }
	}
}
