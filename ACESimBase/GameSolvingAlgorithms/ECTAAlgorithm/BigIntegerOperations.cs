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

		public static bool positive(BigInteger a)
		{
			return a > 0;
		}
		public static bool negative(BigInteger a)
		{
			return a < 0;
		}

		public static bool zero(BigInteger a)
		{
			return a == 0;
		}

		public static bool one(BigInteger a)
		{
			return a == 1;
		}

		public static void changesign(ref BigInteger a)
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

		public static void lcm(ref BigInteger a, BigInteger b)
		/* a = least common multiple of a, b; b is preserved */
		{
			a = (a * b) / BigInteger.GreatestCommonDivisor(a, b);
		}

		public static bool greater(BigInteger a, BigInteger b)
		{
			return a > b;
		}

		/* +1 if Na*Nb > Nc*Nd, -1 if Na*Nb < Nc*Nd else 0             */
		public static void copy(ref BigInteger a, BigInteger b)
		{
			a = b;
		}


		public static void mulint(BigInteger a, BigInteger b, ref BigInteger c)
		{
			c = (a * b);
		}

		// compare products
		public static int comprod(BigInteger Na, BigInteger Nb, BigInteger Nc, BigInteger Nd)
		{
			BigInteger mc = Na * Nb;
			BigInteger md = Nc * Nd;
			mc = mc - md;
			if (positive(mc))
			{
				return (1);
			}
			if (negative(mc))
			{
				return (-1);
			}
			return (0);
		}

		public static void divint(BigInteger a, BigInteger b, ref BigInteger c)
        {
			c = a / b;
        }
	}
}
