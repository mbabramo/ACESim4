using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public struct BigIntegerWrapper
    {
        private BigInteger V;

		public BigIntegerWrapper(BigInteger v)
        {
			V = v;
        }

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

		public BigIntegerWrapper LeastCommonMultiple(BigInteger b)
		/* a = least common multiple of a, b; b is preserved */
		{
			var result = (V * b) / BigInteger.GreatestCommonDivisor(V, b);
			return new BigIntegerWrapper(result);
		}

		public static bool GreaterThan(BigInteger a, BigInteger b)
		{
			return a > b;
		}


		public static BigInteger Multiply(BigInteger a, BigInteger b)
		{
			return (a * b);
		}


		public static BigInteger Divide(BigInteger a, BigInteger b)
		{
			return a / b;
		}
	}
}
