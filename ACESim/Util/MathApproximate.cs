using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class MathApproximate
    {
        // This doesn't seem to work all that well. There is a better function http://www.hxa.name/articles/content/fast-pow-adjustable_hxa7241_2007.html
        // but it needs to be translated to C#.
        public static double Power(double a, double b)
        {
            int tmp = (int)(BitConverter.DoubleToInt64Bits(a) >> 32);
            int tmp2 = (int)(b * (tmp - 1072632447) + 1072632447);
            return BitConverter.Int64BitsToDouble(((long)tmp2) << 32);
        }

        // This one's no good either -- maybe slightly faster but not much
        public static double Exp(double val)
        {
            long tmp = (long)(1512775 * val + (1072632447));
            return BitConverter.Int64BitsToDouble(tmp << 32);
        }

        public static double PerformanceCheck()
        {
            Stopwatch s = new Stopwatch();
            double approximationSpeed = Performance_approximation(s);
            double traditionalSpeed = Performance_traditional(s);

            return approximationSpeed / traditionalSpeed;
        }

        private static double Performance_approximation(Stopwatch s)
        {
            s.Start();
            for (int i = 1; i < 1000; i++)
            {
                double x = 0.01 * i;
                for (int z = 0; z < 100000; z++)
                {
                    double y = Exp(x);
                }
            }
            s.Stop();
            double approximationSpeed = s.ElapsedMilliseconds;
            return approximationSpeed;
        }

        private static double Performance_traditional(Stopwatch s)
        {
            s.Start();
            for (int i = 1; i < 1000; i++)
            {
                double x = 0.01 * i;
                for (int z = 0; z < 100000; z++)
                {
                    double y = Math.Exp(x);
                }
            }
            s.Stop();
            double traditionalSpeed = s.ElapsedMilliseconds;
            return traditionalSpeed;
        }
    }


}
