using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Numerics;
using System.Text;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public static class BigIntegerOperations
    {
		public static bool AbbreviateBigIntegers = true;

		public static bool IsPositive(BigInteger a)
		{
			return a > 0;
		}
		public static bool IsNegative(BigInteger a)
		{
			return a < 0;
		}

		public static bool IsZero(BigInteger a)
		{
			return a.IsZero;
		}

		public static bool IsOne(BigInteger a)
		{
			return a.IsOne;
		}

		public static void ChangeSign(ref BigInteger a)
		{
			a = -a;
		}

		public static string ToStringForTable(this BigInteger x)
        {
			string s = x.ToString();
			if (s.Length > 8 && AbbreviateBigIntegers)
				return x.ToString("E5");
			else
				return s;
        }

		public static BigInteger LeastCommonMultiple(BigInteger a, BigInteger b)
		/* a = least common multiple of a, b; b is preserved */
		{
			var result = (a * b) / BigInteger.GreatestCommonDivisor(a, b);
			return result;
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
