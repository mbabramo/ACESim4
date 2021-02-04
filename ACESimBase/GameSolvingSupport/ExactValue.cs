using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public struct ExactValue
    {
		public static bool AbbreviateExactValues = false;

        private Rational V;

		public ExactValue(Rational v)
		{
			V = v;
		}

		public ExactValue(BigInteger v)
        {
			V = v;
        }

		public ExactValue(int v)
        {
			V = v;
        }

		public static ExactValue Zero() => new ExactValue(0);
		public static ExactValue One() => new ExactValue(1);
		public static ExactValue FromInteger(int i) => new ExactValue(i);

		public static implicit operator ExactValue(BigInteger b) => new ExactValue(b);
		public static implicit operator ExactValue(int b) => new ExactValue(b);

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

		public void ChangeSign()
		{
			V = -V;
		}

		public void ChangeToCanonicalForm()
        {
			V = V.CanonicalForm;
		}

		public ExactValue CanonicalForm => V.CanonicalForm;

		public ExactValue Numerator => V.Numerator;
		public ExactValue Denominator => V.Denominator;
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
			var result = Times(b).DividedBy(BigInteger.GreatestCommonDivisor(V.Numerator, b.V.Numerator));
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
