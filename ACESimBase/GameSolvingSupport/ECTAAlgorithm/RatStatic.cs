using System;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.MultiprecisionStatic;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public static class RatStatic
    {
        public static Rat ratadd(Rat a, Rat b)
        {
            /*
            a.num = a.num * b.den + a.den * b.num;
            a.den *= b.den;
            return ratreduce(a);
            */

            Multiprecision num, den, x, y;
            num = new Multiprecision();
            den = new Multiprecision();
            x = new Multiprecision();
            y = new Multiprecision();

            itomp(a.num, num);
            itomp(a.den, den);
            itomp(b.num, x);
            itomp(b.den, y);
            mulint(y, num, num);
            mulint(den, x, x);
            linint(num, 1, x, 1);
            mulint(y, den, den);
            reduce(num, den);
            int num2 = a.num;
            int den2 = a.den;
            mptoi(num, out num2, 1);
            mptoi(den, out den2, 1);
            a.num = num2;
            a.den = den2;
            return a;
        }

        public static Rat ratdiv(Rat a, Rat b)
        {
            return ratmult(a, ratinv(b));
        }

        public static Rat ratfromi(int i)
        {
            Rat tmp = new Rat();
            tmp.num = i;
            tmp.den = 1;
            return tmp;
        }

        public static int ratgcd(int a, int b)
        {
            int c;
            if (a < 0) a = -a;
            if (b < 0) b = -b;
            if (a < b) { c = a; a = b; b = c; }
            while (b != 0)
            {
                c = a % b;
                a = b;
                b = c;
            }
            return a;
        }

        public static Rat ratinv(Rat a)
        {
            int x;

            x = a.num;
            a.num = a.den;
            a.den = x;
            return a;
        }
        public static bool ratiseq(Rat a, Rat b)
        {
            return (a.num == b.num && a.den == b.den);
        }

        public static bool ratgreat(Rat a, Rat b)
        {
            Rat c = ratadd(a, ratneg(b));
            return (c.num > 0);
        }

        public static Rat ratmult(Rat a, Rat b)
        {
            int x;

            /* avoid overflow in intermediate product by cross-cancelling first
             */
            x = a.num;
            a.num = b.num;
            b.num = x;
            a = ratreduce(a);
            b = ratreduce(b);
            a.num *= b.num;
            a.den *= b.den;
            return ratreduce(a);        /* a  or  b  might be non-normalized    s*/
        }

        public static Rat ratneg(Rat a)
        /* returns -a                                           */
        {
            a.num = -a.num;
            return a;
        }

        public static Rat ratreduce(Rat a)
        {
            if (a.num == 0)
                a.den = 1;
            else
            {
                int div;
                if (a.den < 0)
                {
                    a.den = -a.den;
                    a.num = -a.num;
                }
                div = ratgcd(a.den, a.num);
                a.num = a.num / div;
                a.den = a.den / div;
            }
            return a;
        }

        public static int rattoa(Rat r, char[] s)
        {
            int l, a;
            l = strcpy_formatted(s, 0, "%d", r.num);
            if (r.den != 1)
            {
                a = strcpy_formatted(s, l, "/%d", r.den);
                l += a + 1;
            }
            return l;
        }
        public static int rattoa(Rat r, ref string s)
        {
            s = sprintf("%d", r.num);
            if (r.den != 1)
            {
                string s2 = sprintf("/%d", r.den);
                s += s2;
            }
            return s.Length;
        }

        public static double rattodouble(Rat a)
        {
            return (double)a.num / (double)a.den;
        }

        public static Rat contfract(double x, int accuracy)
        {
            int n0, n1, d0, d1;
            double xfl, nnext, dnext;
            Rat result = new Rat();

            xfl = Math.Floor(x);
            n0 = 1;
            d0 = 0;
            n1 = (int)xfl;
            d1 = 1;

            while (true)
            {
                if (x < xfl + 0.5 / int.MaxValue)    /* next inverse too large */
                    break;
                x = 1 / (x - xfl);
                xfl = Math.Floor(x);
                dnext = d1 * xfl + d0;
                nnext = n1 * xfl + n0;
                if (dnext > accuracy || nnext > int.MaxValue || nnext < int.MaxValue)
                    break;
                d0 = d1;
                d1 = (int)dnext;
                n0 = n1;
                n1 = (int)nnext;
            }
            result.num = n1;
            result.den = d1;
            return result;
        }
    }
}
