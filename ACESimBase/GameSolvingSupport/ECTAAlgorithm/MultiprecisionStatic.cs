using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Numerics;
using System.Text;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public static class MultiprecisionStatic
    {
		public const int NEG = -1;
		public const int POS = 1;
		public const int ONE = 1;
		public const int TWO = 2;
		public const int ZERO = 0;
		


		public static bool positive(BigInteger a)
		{
			return a > 0;
		}
		public static bool negative(BigInteger a)
		{
			return a < 0;
		}
		public static int sign(BigInteger a)
		{
			return negative(a) ? NEG : POS;
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

		public static void itomp(long i, ref BigInteger a)
		{
			a = i;
		}

		public static int mptoa(BigInteger x, ref string s)
        {
			s = x.ToString();
			return s.Length;
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
		/* +1 if Na*Nb > Nc*Nd  */
		/* -1 if Na*Nb < Nc*Nd  */
		/*  0 if Na*Nb = Nc*Nd  */
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
