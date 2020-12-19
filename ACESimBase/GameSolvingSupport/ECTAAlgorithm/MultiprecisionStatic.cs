﻿using System;
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
		static string FORMAT = $"%4.4u"; // for printf
		const int MAXD = int.MaxValue;
		const int BASE = 10000;
		const int BASE_DIG = 4;
		

		public static bool positive(int[] a)
		{
			return (((a)[0] < 2 || ((a)[0] == 2 && (a)[1] == 0)) ? false : true);
		}
		public static bool negative(int[] a)
		{
			return (((a)[0] > -2 || ((a)[0] == -2 && (a)[1] == 0)) ? false : true);
		}
		public static int length(int[] a)
		{
			return (((a)[0] > 0) ? (a)[0] : -(a)[0]);
		}
		public static int sign(int[] a)
		{
			return (((a)[0] < 0) ? NEG : POS);
		}
		public static bool zero(int[] a)
		{
			return ((((a)[0] == 2 || (a)[0] == -2) && (a)[1] == 0) ? true : false);
		}
		public static bool one(int[] a)
		{
			return (((a)[0] == 2 && (a)[1] == 1) ? true : false);
		}
		public static void storesign(int[] a, int sa)
		{
			a[0] = ((a)[0] > 0) ? (sa) * ((a)[0]) : -(sa) * ((a)[0]);
		}
		public static void changesign(int[] a)
		{
			a[0] = -(a)[0];
		}
		public static void storelength(int[] a, int la)
		{
			a[0] = ((a)[0] > 0) ? (la) : -(la);
		}
		public static int DEC2DIGFunc(int d)
		{
			return ((d) % BASE_DIG != 0 ? (d) / BASE_DIG + 1 : (d) / BASE_DIG);
		}
		public static int Dig2Dec(int d)
		{
			return ((d) * BASE_DIG);
		}

/* convert  mp a  back to integer in *result,
 * bcomplain:     give warning to stdout if overflow in conversion.
 * return value:  set to 1 if overflow, o/w 0
 */
		public static bool mptoi(int[] a, ref int result, int bcomplain)
		{
			char[] smp = new char[((MAX_DIGITS) * BASE_DIG) + 2]; // string to print  mp  into

			mptoa(a, smp);
			try
			{
				decimal stringResult = Convert.ToDecimal(smp);
				if (result >= int.MaxValue || result <= int.MinValue)
				{
					if (bcomplain != 0)
					{
						throw new Exception($"Long integer overflow");
					}
					return true; // overflow
				}
				else
				{
					result = (int)stringResult;
					return false;
				}
			}
			catch
            {
				throw new Exception($"Long integer overflow");
            }
		}
		/* tests if a > b and returns (true=POS)                       */
		public static void itomp(int @in, int[] a)
		{
			int i;
			a[0] = 2; // initialize to zero
			for (i = 1; i < digits; i++)
			{
				a[i] = 0;
			}
			if (@in < 0)
			{
				storesign(a, NEG);
				@in = @in * (-1);
			}
			i = 0;
			while (@in != 0)
			{
				i++;
				a[i] = @in - BASE * (@in / BASE);
				@in = @in / BASE;
				storelength(a, i + 1);
			}
		}

		/* print the long precision integer a                          */
		public static void prat(char[] name, int[] Nt, int[] Dt)
		{
			int i;
			printf("%s", name);
			if (sign(Nt) == NEG)
			{
				printf("-");
			}
			printf("%u", Nt[length(Nt) - 1]);
			for (i = length(Nt) - 2; i >= 1; i--)
			{
				printf(FORMAT, Nt[i]);
			}
			if (!(Dt[0] == 2 && Dt[1] == 1)) // rational
			{
				printf("/");
				if (sign(Dt) == NEG)
				{
					printf("-");
				}
				printf("%u", Dt[length(Dt) - 1]);
				for (i = length(Dt) - 2; i >= 1; i--)
				{
					printf(FORMAT, Dt[i]);
				}
			}
			printf(" ");
		}

		/* normalize mp after computation                              */
		public static void pmp(char[] name, int[] a)
		{
			int i;
			printf("%s", name);
			if (sign(a) == NEG)
			{
				printf("-");
			}
			printf("%u", a[length(a) - 1]);
			for (i = length(a) - 2; i >= 1; i--)
			{
				printf(FORMAT, a[i]);
			}
		}

		/* a = least common multiple of a, b; b is preserved           */
		public static int mptoa(int[] x, char[] s)
		/* convert mp integer to string, returning length              */
		/* s  must be sufficiently long to contain result              */
		{
			int i;
			int pos = 0;
			if (sign(x) == NEG)
			{
				s[pos++] = '-';
			}
			pos = strcpy_formatted(s, pos, "%u", x[length(x) - 1]);
			for (i = length(x) - 2; i >= 1; i--)
			{
				pos = strcpy_formatted(s, pos, FORMAT, x[i]);
			}
			return pos;
		}

		public static int mptoa(int[] x, ref string s)
        {
			char[] smp = new char[MultiprecisionStatic.Dig2Dec(MAX_DIGITS) + 2];
			int result = mptoa(x, smp);
			s = new string(smp);
			return result;
		}

		internal static uint gcd_maxspval = MAXD;
		internal static int gcd_maxsplen;
		internal static bool gcd_firstime = true;

		public static void gcd(int[] u, int[] v)
		{
			int[] r = new int[MAX_DIGITS + 1];
			int ul;
			int vl;
			int i;

			if (gcd_firstime) // initialize constants
			{
				for (gcd_maxsplen = 2; gcd_maxspval >= BASE; gcd_maxsplen++)
				{
					gcd_maxspval /= BASE;
				}
				gcd_firstime = false;
			}
			if (greater(v, u))
			{
				goto bigv;
			}
			bigu:
			if (zero(v))
			{
				return;
			}
			if ((i = length(u)) < gcd_maxsplen || i == gcd_maxsplen && u[gcd_maxsplen - 1] < gcd_maxspval)
			{
				goto quickfinish;
			}
			divint(u, v, r);
			normalize(u);

			bigv:
			if (zero(u))
			{
				copy(u, v);
				return;
			}
			if ((i = length(v)) < gcd_maxsplen || i == gcd_maxsplen && v[gcd_maxsplen - 1] < gcd_maxspval)
			{
				goto quickfinish;
			}
			divint(v, u, r);
			normalize(v);
			goto bigu;

			/* Base 10000 only at the moment
			 * when u and v are small enough, transfer to single precision integers
			 * and finish with Euclid's algorithm, then transfer back to mp
			 */
			quickfinish:
			ul = vl = 0;
			for (i = length(u) - 1; i > 0; i--)
			{
				ul = BASE * ul + u[i];
			}
			for (i = length(v) - 1; i > 0; i--)
			{
				vl = BASE * vl + v[i];
			}
			if (ul > vl)
			{
				goto qv;
			}
			qu:
			if (vl == 0)
			{
				for (i = 1; ul != 0; i++)
				{
					u[i] = ul % BASE;
					ul = ul / BASE;
				}
				storelength(u, i);
				return;
			}
			ul %= vl;
			qv:
			if (ul == 0)
			{
				for (i = 1; vl != 0; i++)
				{
					u[i] = vl % BASE;
					vl = vl / BASE;
				}
				storelength(u, i);
				return;
			}
			vl %= ul;
			goto qu;
		}
		public static void reduce(int[] Na, int[] Da)
		{
			int[] Nb = new int[MAX_DIGITS + 1];
			int[] Db = new int[MAX_DIGITS + 1];
			int[] Nc = new int[MAX_DIGITS + 1];
			int[] Dc = new int[MAX_DIGITS + 1];
			copy(Nb, Na);
			copy(Db, Da);
			storesign(Nb, POS);
			storesign(Db, POS);
			copy(Nc, Na);
			copy(Dc, Da);
			gcd(Nb, Db); // Nb is the gcd(Na,Da)
			divint(Nc, Nb, Na);
			divint(Dc, Nb, Da);
		}

		/* compute a*ka+b*kb --> a                                     */
		public static void lcm(int[] a, int[] b)
		/* a = least common multiple of a, b; b is preserved */
		{
			int[] u = new int[MAX_DIGITS + 1];
			int[] v = new int[MAX_DIGITS + 1];
			copy(u, a);
			copy(v, b);
			gcd(u, v);
			divint(a, u, v); // v=a/u   a contains remainder = 0
			mulint(v, b, a);
		}
		public static bool greater(int[] a, int[] b)
		{
			int i;

			if (a[0] > b[0])
			{
				return (true);
			}
			if (a[0] < b[0])
			{
				return (false);
			}

			for (i = length(a) - 1; i >= 1; i--)
			{
				if (a[i] < b[i])
				{
					if (sign(a) == POS)
					{
						return false;
					}
					else
					{
						return true;
					}
				}
				if (a[i] > b[i])
				{
					if (sign(a) == NEG)
					{
						return false;
					}
					else
					{
						return true;
					}
				}
			}
			return false;
		}

		/* +1 if Na*Nb > Nc*Nd, -1 if Na*Nb < Nc*Nd else 0             */
		public static void copy(int[] a, int[] b)
		{
			int i;
			for (i = 0; i <= length(b); i++)
			{
				a[i] = b[i];
			}
		}

		/* convert integer i to multiple precision with base BASE      */
		public static void linint(int[] a, int ka, int[] b, int kb)
		{
			int i;
			int la;
			int lb;
			la = length(a);
			lb = length(b);
			for (i = 1; i < la; i++)
			{
				a[i] *= ka;
			}
			if (sign(a) != sign(b))
			{
				kb = -kb;
			}
			if (lb > la)
			{
				storelength(a, lb);
				for (i = la; i < lb; i++)
				{
					a[i] = 0;
				}
			}
			for (i = 1; i < lb; i++)
			{
				a[i] += kb * b[i];
			}
			normalize(a);
		} // end of linint

		/* multiply two integers a*b --> c                             */
		public static void normalize(int[] a)
		{
			int cy;
			int i;
			int la;
			la = length(a);
			start:
			cy = 0;
			for (i = 1; i < la; i++)
			{
				cy = (a[i] += cy) / BASE;
				a[i] -= cy * BASE;
				if (a[i] < 0)
				{
					a[i] += BASE;
					cy--;
				}
			}
			while (cy > 0)
			{
				a[i++] = cy % BASE;
				cy /= BASE;
			}
			if (cy < 0)
			{
				a[la - 1] += cy * BASE;
				for (i = 1; i < la; i++)
				{
					a[i] = -a[i];
				}
				storesign(a, sign(a) == POS ? NEG : POS);
				goto start;
			}
			while (a[i - 1] == 0 && i > 2)
			{
				i--;
			}
			if (i > record_digits)
			{
				if ((record_digits = i) > digits)
				{
					digits_overflow(la);
				}
			};
			storelength(a, i);
			if (i == 2 && a[1] == 0)
			{
				storesign(a, POS);
			}
		}

		public static void mulint(int[] a, int[] b, int[] c)
		/***Handbook of Algorithms and Data Structures, p239  ***/
		{
			int nlength;
			int i;
			int j;
			int la;
			int lb;
			/*** b and c may coincide ***/
			la = length(a);
			lb = length(b);
			nlength = la + lb - 2;
			if (nlength > digits)
			{
				digits_overflow(nlength);
			}

			for (i = 0; i < la - 2; i++)
			{
				c[lb + i] = 0;
			}
			for (i = lb - 1; i > 0; i--)
			{
				for (j = 2; j < la; j++)
				{
					if ((c[i + j - 1] += b[i] * a[j]) > MAXD - (BASE - 1) * (BASE - 1) - MAXD / BASE)
					{
						c[i + j - 1] -= (MAXD / BASE) * BASE;
						c[i + j] += MAXD / BASE;
					}
				}
				c[i] = b[i] * a[1];
			}
			storelength(c, nlength);
			storesign(c, sign(a) == sign(b) ? POS : NEG);
			normalize(c);
		}

		public static int comprod(int[] Na, int[] Nb, int[] Nc, int[] Nd)
		/* +1 if Na*Nb > Nc*Nd  */
		/* -1 if Na*Nb < Nc*Nd  */
		/*  0 if Na*Nb = Nc*Nd  */
		{
			int[] mc = new int[MAX_DIGITS + 1];
			int[] md = new int[MAX_DIGITS + 1];
			mulint(Na, Nb, mc);
			mulint(Nc, Nd, md);
			linint(mc, ONE, md, -ONE);
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
		public static void divint(int[] a, int[] b, int[] c)
		/* c=a/b, a contains remainder on return */
		{
			int cy;
			int la;
			int lb;
			int lc;
			int d1;
			int s;
			int t;
			int sig;
			int i;
			int j;
			int qh;

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
					printf("divide error");
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