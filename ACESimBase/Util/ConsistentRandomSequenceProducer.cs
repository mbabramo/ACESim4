using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class ConsistentRandomSequenceProducer : IRandomProducer
    {
        long Seed = 0;
        int CurrentIndex = 0;

        public ConsistentRandomSequenceProducer(long seed)
        {
            Seed = seed;
        }

        public double NextDouble()
        {
            double v = GetDoubleAtIndex(CurrentIndex++);
            return v;
        }

        bool AlwaysUseNewRandomObject = true;

        public double GetDoubleAtIndex(int index)
        {
            // Combine multiple random numbers into 1. But averaging them won't work. Note that if one averaged a billion random numbers, we would converge to 0.5. This is a simple approach; we could probably do something more sophisticated (like bit interleaving).
            if (AlwaysUseNewRandomObject)
                return GetDoubleWithNewRandomObject(index);
            return GetDoubleAtIndex_Alt1(index) > 0.5 ? GetDoubleAtIndex_Alt2(index) : GetDoubleAtIndex_Alt3(index);
        }

        public double GetDoubleWithNewRandomObject(int index)
        {
            const long prime1 = 7594955549;
            const long prime2 = 8965095091;
            const long prime3 = 5336500537;
            long intermediateResult = ((index + Seed) * (index + Seed) * prime1 + (index + Seed) * prime2) % prime3;
            return new Random((int)intermediateResult).NextDouble();
        }

        public double GetDoubleAtIndex_Alt1(int index)
        {
            const long prime1 = 7594955549;
            const long prime2 = 8965095091;
            const long prime3 = 5336500537;
            long intermediateResult = ((index + Seed) * (index + Seed) * prime1 + (index + Seed) * prime2) % prime3;
            if (AlwaysUseNewRandomObject)
                return new Random((int)intermediateResult).NextDouble();
            double v = Math.Abs((double)intermediateResult / (double)prime3); // should scale it to 0 to 1
            return v;
        }

        public double GetDoubleAtIndex_Alt2(int index)
        {
            const long prime1 = 21798470266577;
            const long prime2 = 6682497317939;
            const long prime3 = 55571982217;
            long intermediateResult = ((index + Seed) * (index + Seed) * prime1 + (index + Seed) * prime2) % prime3;
            double v = Math.Abs((double)intermediateResult / (double)prime3); // should scale it to 0 to 1
            return v;
        }
        public double GetDoubleAtIndex_Alt3(int index)
        {
            const long prime1 = 634534536871;
            const long prime2 = 234534536161;
            const long prime3 = 7546456823;
            long intermediateResult = ((index + Seed) * (index + Seed) * prime1 + (index + Seed) * prime2) % prime3;
            double v = Math.Abs((double)intermediateResult / (double)prime3); // should scale it to 0 to 1
            return v;
        }

        public static void Test()
        {
            // The following test shows that we do get numbers that are pretty evenly distributed between 0 and 1. However, the variance of the randomness is different from what we get using the generic random.
            for (int seedToTest = 0; seedToTest < 100; seedToTest++)
            {
                //RandomProducer r = new RandomProducer();
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(seedToTest);
                Test_Random(r);
            }
        }

        private static void Test_Random(IRandomProducer r)
        {
            double[] x = new double[1000000];
            int[] counter = new int[100];
            int consecutiveMatches = 0;
            int lastCounter = 0;
            for (int i = 0; i < 1000000; i++)
            {
                x[i] = r.GetDoubleAtIndex(i);
                int counterIndex = (int)Math.Floor(x[i] * 100);
                if (counterIndex == lastCounter)
                    consecutiveMatches++;
                lastCounter = counterIndex;
                counter[counterIndex]++;
            }

            StatCollector c = new StatCollector();
            foreach (int y in counter)
                c.Add(y);
            System.Diagnostics.Debug.WriteLine($"Standard deviation of counters: {c.StandardDeviation()} Consecutive matches {consecutiveMatches}");
        }
    }
}
