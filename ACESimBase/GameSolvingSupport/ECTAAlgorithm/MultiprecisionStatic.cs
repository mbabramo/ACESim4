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
			BigInteger u = new long[MAX_DIGITS + 1];
			BigInteger v = new long[MAX_DIGITS + 1];
			copy(u, a);
			copy(v, b);
			gcd(u, v);
			divint(a, u, v); // v=a/u   a contains remainder = 0
			mulint(v, b, a);
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
		/* c=a/b, a contains remainder on return */
		{
			long cy;
			int la;
			int lb;
			int lc;
			long d1;
			long s;
			long t;
			int sig;
			int i;
			int j;
			long qh;

			/*  figure out and save sign, do everything with positive numbers*/
			sig = sign(a) * sign(b);

			la = length(a);
			lb = length(b);
			lc = la - lb + 2;
			if (la < lb)
			{
				storelength(c, TWO);
				storesign(c, POS);
				c[1] = 0;
				normalize(c);
				return;
			}
			for (i = 1; i < lc; i++)
			{
				c[i] = 0;
			}
			storelength(c, lc);
			storesign(c, (sign(a) == sign(b)) ? POS : NEG);

			/******************************/
			/* division by a single word: */
			/*  do it directly            */
			/******************************/

			if (lb == 2)
			{
				cy = 0;
				t = b[1];
				for (i = la - 1; i > 0; i--)
				{
					cy = cy * BASE + a[i];
					a[i] = 0;
					cy -= (c[i] = cy / t) * t;
				}
				a[1] = cy;
				storesign(a, (cy == 0) ? POS : sign(a));
				storelength(a, TWO);
				/*      set sign of c to sig  (**mod**)            */
				storesign(c, sig);
				normalize(c);
				return;
			}
			else
			{
				/* mp's are actually DIGITS+1 in length, so if length of a or b = */
				/* DIGITS, there will still be room after normalization. */
				/****************************************************/
				/* Step D1 - normalize numbers so b > floor(BASE/2) */
				d1 = BASE / (b[lb - 1] + 1);
				if (d1 > 1)
				{
					cy = 0;
					for (i = 1; i < la; i++)
					{
						cy = (a[i] = a[i] * d1 + cy) / BASE;
						a[i] %= BASE;
					}
					a[i] = cy;
					cy = 0;
					for (i = 1; i < lb; i++)
					{
						cy = (b[i] = b[i] * d1 + cy) / BASE;
						b[i] %= BASE;
					}
					b[i] = cy;
				}
				else
				{
					a[la] = 0; // if la or lb = DIGITS this won't work
					b[lb] = 0;
				}
				/*********************************************/
				/* Steps D2 & D7 - start and end of the loop */
				for (j = 0; j <= la - lb; j++)
				{
					/*************************************/
					/* Step D3 - determine trial divisor */
					if (a[la - j] == b[lb - 1])
					{
						qh = BASE - 1;
					}
					else
					{
						s = (a[la - j] * BASE + a[la - j - 1]);
						qh = s / b[lb - 1];
						while (qh * b[lb - 2] > (s - qh * b[lb - 1]) * BASE + a[la - j - 2])
						{
							qh--;
						}
					}
					/*******************************************************/
					/* Step D4 - divide through using qh as quotient digit */
					cy = 0;
					for (i = 1; i <= lb; i++)
					{
						s = qh * b[i] + cy;
						a[la - j - lb + i] -= s % BASE;
						cy = s / BASE;
						if (a[la - j - lb + i] < 0)
						{
							a[la - j - lb + i] += BASE;
							cy++;
						}
					}
					/*****************************************************/
					/* Step D6 - adjust previous step if qh is 1 too big */
					if (cy != 0)
					{
						qh--;
						cy = 0;
						for (i = 1; i <= lb; i++) // add a back in
						{
							a[la - j - lb + i] += b[i] + cy;
							cy = a[la - j - lb + i] / BASE;
							a[la - j - lb + i] %= BASE;
						}
					}
					/***********************************************************************/
					/* Step D5 - write final value of qh.  Saves calculating array indices */
					/* to do it here instead of before D6 */

					c[la - lb - j + 1] = qh;

				}
				/**********************************************************************/
				/* Step D8 - unnormalize a and b to get correct remainder and divisor */

				for (i = lc; c[i - 1] == 0 && i > 2; i--)
				{
					; // strip excess 0's from quotient
				}
				storelength(c, i);
				if (i == 2 && c[1] == 0)
				{
					storesign(c, POS);
				}
				cy = 0;
				for (i = lb - 1; i >= 1; i--)
				{
					cy = (a[i] += cy * BASE) % d1;
					a[i] /= d1;
				}
				for (i = la; a[i - 1] == 0 && i > 2; i--)
				{
					; // strip excess 0's from quotient
				}
				storelength(a, i);
				if (i == 2 && a[1] == 0)
				{
					storesign(a, POS);
				}
				if (cy != 0)
				{
					tabbedtextf("divide error");
				}
				for (i = lb - 1; i >= 1; i--)
				{
					cy = (b[i] += cy * BASE) % d1;
					b[i] /= d1;
				}
			}
		}

		static void digits_overflow(int numdigits)
		{
			throw new Exception("Overflow at digits " + Dig2Dec(digits));
		}


	}
}
