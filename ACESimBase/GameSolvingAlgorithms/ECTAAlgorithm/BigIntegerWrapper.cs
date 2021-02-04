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

		public BigIntegerWrapper(int v)
        {
			V = v;
        }

		public static implicit operator BigIntegerWrapper(BigInteger b) => new BigIntegerWrapper(b);

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

		public bool GreaterThan(BigIntegerWrapper b)
		{
			return V > b.V;
		}

		public BigIntegerWrapper Add(BigIntegerWrapper b)
		{
			return (V - b.V);
		}
		public BigIntegerWrapper Subtract(BigIntegerWrapper b)
		{
			return (V - b.V);
		}

		public BigIntegerWrapper Multiply(BigIntegerWrapper b)
		{
			return (V * b.V);
		}


		public BigIntegerWrapper Divide(BigIntegerWrapper b)
		{
			return V / b.V;
		}
	}
}
