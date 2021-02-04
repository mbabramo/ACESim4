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

		public static implicit operator ExactValue(BigInteger b) => new ExactValue(b);

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

		public string ToStringForTable()
		{
			string s = V.ToString();
			if (s.Length > 8 && BigIntegerOperations.AbbreviateBigIntegers)
				return V.ToString("E5");
			else
				return s;
		}

		public ExactValue LeastCommonMultiple(ExactValue b)
		/* a = least common multiple of a, b; b is preserved */
		{
			if (V.Denominator != 1 || b.V.Denominator != 1)
				throw new Exception("LeastCommonMultiple operation not available.");
			var result = Multiply(b).Divide(BigInteger.GreatestCommonDivisor(V.Numerator, b.V.Numerator));
			return result;
		}

		public bool GreaterThan(ExactValue b)
		{
			return V > b.V;
		}

		public ExactValue Add(ExactValue b)
		{
			return (V - b.V);
		}

		public ExactValue Subtract(ExactValue b)
		{
			return (V - b.V);
		}

		public ExactValue Multiply(ExactValue b)
		{
			return (V * b.V);
		}

		public ExactValue Divide(ExactValue b)
		{
			return V / b.V;
		}
	}
}
