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
		public static int digits = 40;
		public static int record_digits;
		public const int DEFAULT_DIGITS = 100;
		public const int MAX_DIGITS = 40;


		// constants for 32-bit integers (can change later to 64-bit)
		static string FORMAT = $"%4.4u"; // for tabbedtextf
		const int MAXD = int.MaxValue;
		const int BASE = 10000;
		const int BASE_DIG = 4;
		


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
		public static void changesign(BigInteger a)
		{
			a = -a;
		}

		public static void itomp(long i, BigInteger a)
		{
			a = i;
		}

		public static int mptoa(BigInteger x, ref string s)
        {
			s = x.ToString();
			return s.Length;
		}

		/* compute a*ka+b*kb --> a                                     */
		public static void lcm(BigInteger a, BigInteger b)
		/* a = least common multiple of a, b; b is preserved */
		{
			BigInteger u = 0;
			BigInteger v = 0;
			copy(ref u, a);
			copy(ref v, b);
			gcd(u, v);
			divint(a, u, ref v); 
			mulint(v, b, ref a);
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
