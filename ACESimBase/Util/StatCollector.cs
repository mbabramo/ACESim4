using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class StatCollectorArray
    {
        public bool Initialized = false;
        int ArraySize;
        public StatCollector[] StatCollectors;
        public object theLock = new object();
        public object theLock2 = new object();

        public void Initialize(int arraySize)
        {
            lock (theLock)
            {
                Initialized = true;
                ArraySize = arraySize;
                StatCollectors = new StatCollector[ArraySize];
                for (int s = 0; s < ArraySize; s++)
                    StatCollectors[s] = new StatCollector();
            }
        }

        public override string ToString()
        {
            return String.Join("; ", StatCollectors.Select(x => $"{x.Average().ToSignificantFigures(5)} (sd {x.StandardDeviation().ToSignificantFigures(5)})"));
        }

        public void Reset()
        {
            lock (theLock)
            {
                foreach (var sc in StatCollectors)
                    sc.Reset();
            }
        }

        public void Add(double[] items, double? weight = null)
        {
            if (!Initialized)
                Initialize(items.Length);
            lock (theLock)
            {
                for (int s = 0; s < ArraySize; s++)
                    StatCollectors[s].Add(items[s], weight);
            }
        }

        public List<double> Average()
        {
            lock (theLock2)
            {
                List<double> theList = new List<double>();
                if (!Initialized)
                    return theList;
                for (int s = 0; s < ArraySize; s++)
                    theList.Add(StatCollectors[s].Average());
                return theList;
            }
        }

        public List<double> StandardDeviation()
        {
            lock (theLock2)
            {
                List<double> theList = new List<double>();
                if (!Initialized)
                    return theList;
                for (int s = 0; s < ArraySize; s++)
                    theList.Add(StatCollectors[s].StandardDeviation());
                return theList;
            }
        }

        public StatCollectorArray DeepCopy()
        {
            return new StatCollectorArray() { ArraySize = ArraySize, Initialized = Initialized, StatCollectors = StatCollectors.Select(x => x.DeepCopy()).ToArray() };
        }
    }

    public class WeightedAverageCalculator
    {
        double totalNumerator = 0;
        double totalDenominator = 0;

        public void Add(double item, double weight)
        {
            totalNumerator += item * weight;
            totalDenominator += weight;
            if (double.IsNaN(totalNumerator) || double.IsInfinity(totalNumerator) || double.IsNaN(totalDenominator) || double.IsInfinity(totalDenominator))
                throw new Exception("Not a number overflow.");
        }

        public double Calculate()
        {
            double returnVal = totalNumerator / totalDenominator;
            if (double.IsNaN(returnVal) || double.IsInfinity(returnVal) || double.IsNaN(returnVal) || double.IsInfinity(returnVal))
                throw new Exception("Not a number overflow.");
            return returnVal;
        }
    }

    public static class NormalizeData
    {
        public static double[] Normalize(double[] unnormalizedData, out double avg, out double stdev, Func<int, bool> include = null)
        {
            if (include == null)
                include = (x => true);
            StatCollector sc = new StatCollector();
            for (int i = 0; i < unnormalizedData.Length; i++)
                if (include(i))
                    sc.Add(unnormalizedData[i]);
            avg = sc.Average();
            stdev = sc.StandardDeviation();
            double[] normalizedData = new double[unnormalizedData.Length];
            for (int i = 0; i < unnormalizedData.Length; i++)
                if (include(i))
                    normalizedData[i] = (unnormalizedData[i] - avg) / stdev;
                else
                    normalizedData[i] = 0;
            return normalizedData;
        }
    }

    [Serializable]
    public class StatCollectorFasterButNotThreadSafe : StatCollector
    {
        public override void Reset()
        {
            n = 0;
            // http://stackoverflow.com/questions/282600/computing-a-mean-confidence-interval-without-storing-all-the-data-points
            mean = 0;
            M2 = 0;
        }

        public override StatCollector DeepCopy()
        {
            return new StatCollectorFasterButNotThreadSafe() { n = n, mean = mean, M2 = M2 };
        }

        public override void Add(double item, double? weight = null)
        {
            //if (double.IsInfinity(item) || double.IsNaN(item))
            //    throw new OverflowException("Invalid addition to the stat collector.");
            double w = weight ?? 1.0;
            if (w == 0)
                return;
            double nextSumOfWeights = sumOfWeights + w;
            n++;
            // see http://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Weighted_incremental_algorithm
            double delta = item - mean;
            double R = delta * w / nextSumOfWeights;
            mean += R;
            //if (double.IsInfinity(mean))
            //    throw new OverflowException("The stat collector had an overflow error.");
            M2 += sumOfWeights * delta * R;
            sumOfWeights = nextSumOfWeights;
        }

        public override double Num()
        {
            return (double)n;
        }

        public override double Average()
        {
            if (n > 0)
                return mean;
            return 0;
        }

        public override double StandardDeviation()
        {
            double variance_n = M2/sumOfWeights; // population variance
            // sample standard deviation double variance = variance_n * (double)n / ((double)(n - 1));
            return Math.Sqrt(variance_n);
        }
    }

    [Serializable]
    public class StatCollector
    {
        public int n;
        public double sumOfWeights;
        public double mean;
        public double M2;
        public double Min, Max;
        object theLock = new object();

        public virtual void Reset()
        {
            lock (theLock)
            {
                n = 0;
                // http://stackoverflow.com/questions/282600/computing-a-mean-confidence-interval-without-storing-all-the-data-points
                mean = 0;
                M2 = 0;
                sumOfWeights = 0;
            }
        }

        public virtual StatCollector DeepCopy()
        {
            return new StatCollector() { n = n, mean = mean, M2 = M2, Min = Min, Max = Max, sumOfWeights = sumOfWeights };
        }

        public virtual void Aggregate(StatCollector anotherCollector)
        {
            lock (theLock)
            {
                n += anotherCollector.n;
                mean = (mean * sumOfWeights + anotherCollector.mean * anotherCollector.sumOfWeights) / (sumOfWeights + anotherCollector.sumOfWeights);
                sumOfWeights += anotherCollector.sumOfWeights;
                M2 += anotherCollector.M2; // I don't think this is right, but for now, we're only using this for averages, so it won't matter.
                Min = Math.Min(Min, anotherCollector.Min);
                Max = Math.Max(Max, anotherCollector.Max);
            }
        }

        public virtual void Add(double item, double? weight = null)
        {
            if (double.IsInfinity(item) || double.IsNaN(item))
                throw new OverflowException("Invalid addition to the stat collector.");
            double w = weight ?? 1.0;
            if (w == 0)
                return;
            lock (theLock)
            {
                double nextSumOfWeights = sumOfWeights + w;
                n++;
                if (n == 1)
                    Min = Max = item;
                else if (item < Min)
                    Min = item;
                else if (item > Max)
                    Max = item;
                // see http://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Weighted_incremental_algorithm
                double delta = item - mean;
                double R = delta * w / nextSumOfWeights;
                mean += R;
                //if (double.IsInfinity(mean))
                //    throw new OverflowException("The stat collector had an overflow error.");
                M2 += sumOfWeights * delta * R;
                sumOfWeights = nextSumOfWeights;
            }
        }

        public virtual double Num()
        {
            lock (theLock)
            {
                return (double)n;
            }
        }

        public virtual double Average()
        {
            lock (theLock)
            {
                if (n > 0)
                    return mean;
                return 0;
            }
        }

        public virtual double? AverageOrNull()
        {
            return (n > 0) ? (double?) Average() : null;
        }

        public virtual double StandardDeviation()
        {
            lock (theLock)
            {
                double variance_n = M2/sumOfWeights; // population variance
                // sample standard deviation double variance = variance_n * (double)n / ((double)(n - 1));
                return Math.Sqrt(variance_n);
            }
        }

        // The remainder is for calculating confidence intervals.
        // we throw in high values for n = 0 and n = 1. This takes us up to n = 100.
        static double[] gvals = new double[] { 99999999.0, 999999.0, 12.706204736174698, 4.302652729749464, 3.182446305283708, 2.7764451051977934, 2.570581835636314, 2.4469118511449666, 2.3646242515927853, 2.306004135204168, 2.262157162798205, 2.2281388519862735, 2.2009851600916384, 2.178812829667226, 2.1603686564627917, 2.1447866879178012, 2.131449545559774, 2.1199052992212533, 2.1098155778333156, 2.100922040241039, 2.093024054408307, 2.0859634472658626, 2.0796138447276835, 2.073873067904019, 2.0686576104190477, 2.0638985616280254, 2.0595385527532963, 2.05552943864287, 2.051830516480281, 2.048407141795243, 2.0452296421327034, 2.042272456301236, 2.039513446396408, 2.0369333434600976, 2.0345152974493392, 2.032244509317719, 2.030107928250338, 2.0280940009804462, 2.0261924630291066, 2.024394163911966, 2.022690920036762, 2.0210753903062715, 2.0195409704413745, 2.018081702818439, 2.016692199227822, 2.0153675744437627, 2.0141033888808457, 2.0128955989194246, 2.011740513729764, 2.0106347576242314, 2.0095752371292335, 2.0085591121007527, 2.007583770315835, 2.0066468050616857, 2.005745995317864, 2.0048792881880577, 2.004044783289136, 2.0032407188478696, 2.002465459291016, 2.001717484145232, 2.000995378088259, 2.0002978220142578, 1.9996235849949402, 1.998971517033376, 1.9983405425207483, 1.997729654317692, 1.9971379083920013, 1.9965644189523084, 1.996008354025304, 1.9954689314298386, 1.994945415107228, 1.9944371117711894, 1.9939433678456229, 1.993463566661884, 1.9929971258898527, 1.9925434951809258, 1.992102154002232, 1.9916726096446793, 1.9912543953883763, 1.9908470688116922, 1.9904502102301198, 1.990063421254452, 1.989686323456895, 1.9893185571365664, 1.9889597801751728, 1.9886096669757192, 1.9882679074772156, 1.9879342062390228, 1.9876082815890748, 1.9872898648311672, 1.9869786995062702, 1.986674540703777, 1.986377154418625, 1.9860863169510985, 1.9858018143458114, 1.9855234418666061, 1.9852510035054973, 1.9849843115224508, 1.9847231860139618, 1.98446745450849, 1.9842169515863888 };
        static double gasym = 1.959963984540054235524594430520551527955550;

        internal double get_g()
        {
            if (n <= 100)
                return gvals[n];
            return gasym;
        }

        public double ConfInterval()
        {
            lock (theLock)
            {
                double SE = Math.Sqrt((M2 / (double)(n - 1)) / (double)n);
                double c = SE * get_g();
                return c;
            }
        }
    }

}
