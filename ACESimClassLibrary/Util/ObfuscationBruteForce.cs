using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public class ObfuscationBruteForceCalculationForStdDev
    {
        double stdev;
        const int numBuckets = 1000; // should be a multiple of 10
        double[] buckets = new double[numBuckets];
        int[] numInBuckets = new int[numBuckets];
        double[] averageOfBuckets = new double[numBuckets];
        public ObfuscationBruteForceCalculationForStdDev(double theStdev)
        {
            stdev = theStdev;
            DoCalc();
        }
        public int GetBucketFromObfuscated(double obfuscated)
        {
            return (int)(Math.Floor(obfuscated * (double)numBuckets + 5.0 * (10.0 / (double)numBuckets))) - 1;
        }
        public double GetValue(double obfuscated)
        {
            int bucket = GetBucketFromObfuscated(obfuscated);
            if (bucket >= 0 && bucket < numBuckets)
            {
                return averageOfBuckets[bucket];
            }
            else if (bucket < 0)
                return 0;
            else
                return 1.0;
        }
        public void DoCalc()
        {
            for (int i = 0; i < 10000000; i++)
            {
                double actualNumber = RandomGenerator.NextDouble();
                double obfuscation = stdev * (double)InvNormal.Calculate(RandomGenerator.NextDouble());
                double obfuscated = actualNumber + obfuscation;
                int bucket = GetBucketFromObfuscated(obfuscated);
                if (bucket >= 0 && bucket < numBuckets)
                {
                    buckets[bucket] += actualNumber;
                    numInBuckets[bucket]++;
                }
            }
            for (int i = 0; i < numBuckets; i++)
                averageOfBuckets[i] = buckets[i] / numInBuckets[i];
        }
    }

    public static class BruteForceProbabilityProxyGreaterThan05AssessAccuracy
    {
        public static void DoTest()
        {
            const double stdev = 0.2;
            for (int zeros = 5; zeros <= 10; zeros++)
            {
                long numCalculations = 1;
                for (int z = 0; z < zeros; z++)
                    numCalculations *= 10;
                BruteForceProbabilityProxyGreaterThan05 b = new BruteForceProbabilityProxyGreaterThan05(stdev, numCalculations);
                //Debug.WriteLine("Zeros: " + zeros + " " + String.Format("Value for 0.1 {0} 0.25 {1} 0.3 {2} 0.69 {3} 0.74 {4} 0.89 {5}", b.GetValue(0.1), b.GetValue(0.25), b.GetValue(0.3), b.GetValue(0.69), b.GetValue(0.74), b.GetValue(0.89)));
                Func<double, double> getDiscrepancy = x => Math.Abs(b.GetValue(x) - (1.0 - b.GetValue(1 - x - 0.001)));
                StatCollector sc = new StatCollector();
                for (double d = 0.01; d <= 0.49; d += 0.01)
                    sc.Add(getDiscrepancy(d));
                Debug.WriteLine("Zeros: " + zeros + " average discrepancy: " + sc.Average() + " with standard deviation " + sc.StandardDeviation());
            }

        }
    }

    public static class CalculateProbabilityProxyWouldBeGreaterThan0Point5
    {
        static double lastStdev;
        static DateTime? lastTimeCalculated = null;
        static BruteForceProbabilityProxyGreaterThan05 b;
        static object lockObj = new object();

        static void Init(double stdev, long numCalculations = 100000000)
        {
            lock (lockObj)
            {
                if (lastStdev != stdev)
                {
                    if (lastTimeCalculated != null && lastTimeCalculated > DateTime.Now - new TimeSpan(0, 0, 2))
                        Debug.WriteLine("WARNING: Repeatedly using brute force calculation.");
                    b = new BruteForceProbabilityProxyGreaterThan05(stdev, numCalculations);
                    lastStdev = stdev;
                    lastTimeCalculated = DateTime.Now;
                }
            }
        }

        public static double GetProbability(double actualValue, double stdev, long numCalculations = 100000000)
        {
            if (lastStdev != stdev)
                Init(stdev, numCalculations);
            return b.GetValue(actualValue);
        }
    }

    public class BruteForceProbabilityProxyGreaterThan05
    {
        double stdev;
        long numCalculations;
        const int numBuckets = 1000; // should be a multiple of 10
        long[] bucketsNumProxyGreater05 = new long[numBuckets];
        long[] numInBuckets = new long[numBuckets];
        double[] averageOfBuckets = new double[numBuckets];
        public BruteForceProbabilityProxyGreaterThan05(double theStdev, long theNumCalculations)
        {
            stdev = theStdev;
            numCalculations = theNumCalculations;
            DoCalc();
        }

        public int GetBucketFromActual(double actual)
        {
            return (int)(Math.Floor(actual * (double)numBuckets));
        }

        public double GetValue(double actual)
        {
            int bucket = GetBucketFromActual(actual);
            if (bucket >= 0 && bucket < numBuckets)
            {
                return averageOfBuckets[bucket];
            }
            else if (bucket < 0)
                return 0;
            else
                return 1.0;
        }

        public void DoCalc()
        {
            //for (int i = 0; i < numCalculations; i++)
            Parallel.For((long)0, (long)numCalculations - 1, i =>
            {
                double actualNumber = FastPseudoRandom.GetRandom(i * 2, 0, 0);
                double obfuscation = stdev * (double)InvNormal.Calculate(FastPseudoRandom.GetRandom(i * 2 + 1, 0, 0));
                double obfuscated = actualNumber + obfuscation;
                int bucket = GetBucketFromActual(actualNumber);
                if (bucket >= 0 && bucket < numBuckets)
                {
                    if (obfuscated > 0.5)
                        Interlocked.Increment(ref bucketsNumProxyGreater05[bucket]);
                    Interlocked.Increment(ref numInBuckets[bucket]);
                }
            }
            );
            for (int i = 0; i < numBuckets; i++)
                averageOfBuckets[i] = ((double) bucketsNumProxyGreater05[i]) / (double) numInBuckets[i];
            //for (double x = 0.01; x <= 1.0; x += 0.01)
            //    Debug.WriteLine("BruteForce: " + x + " ==> " + GetValue(x) + " " + (1.0 - GetValue(1.0 - x)) + " ");
        }
    }
}
