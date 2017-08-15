using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{

    public static class RandomGenerator
    {
        public static bool ThrowExceptionIfCalled = false;

        /// <summary>
        /// Returns a non-negative random number.
        /// </summary>
        /// <returns></returns>
        public static int Next()
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return RandomGeneratorInstanceManager.Instance.Next();
        }

        public static double NextLogarithmic(double maxValue, double exp)
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            double lnMax = Math.Log(maxValue, exp);
            double rnd = NextDouble(0, lnMax);
            return Math.Pow(exp, rnd);
        }

        /// <summary>
        /// Returns a non-negative random number less than the specified maximum.
        /// </summary>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static int Next(int maxValue)
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return RandomGeneratorInstanceManager.Instance.Next(maxValue);
        }
        /// <summary>
        /// Returns a random number within a specified range. Note that the number returned is always less than the second value.
        /// </summary>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static int NextIntegerExclusiveOfSecondValue(int minValue, int maxValue)
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return RandomGeneratorInstanceManager.Instance.Next(minValue, maxValue);
        }

        public static float NextFloat()
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return (float)RandomGeneratorInstanceManager.Instance.NextDouble();
        }

        public static float NextFloat(float low, float high)
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return low + NextFloat() * (high - low);
        }

        /// <summary>
        /// Returns a random number between 0.0 and 1.0.
        /// </summary>
        /// <returns></returns>
        public static double NextDouble()
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return RandomGeneratorInstanceManager.Instance.NextDouble();
        }

        /// <summary>
        /// Returns a random number within a specified range.
        /// </summary>
        /// <param name="low"></param>
        /// <param name="high"></param>
        /// <returns></returns>
        public static double NextDouble(double low, double high)
        {
            if (ThrowExceptionIfCalled)
                throw new Exception("RandomGenerator called when it was not supposed to be called.");
            return low + NextDouble() * (high - low);
        }
    }
    
    public class RandomGeneratorInstanceManager
    {
        public static bool useDateTime = false; // if setting this to false to ensure consistent results, should also set EnsureConsistentIterationNumbers in Parallelizer to true
        static int seedFromDateTime = unchecked((int)DateTime.Now.Ticks);
        static int arbitrarySeed = 15; // use this to get the same result every time, as long as parallel processing is not enabled and we restart after each evolution
        private static Random _global = new Random(useDateTime ? seedFromDateTime : arbitrarySeed);
        [ThreadStatic]
        private static Random _local;

        public static void Reset(bool useDateTimeThisTime, bool moveToNextArbitrarySeed)
        {
            if (moveToNextArbitrarySeed)
                arbitrarySeed++;
            if (useDateTimeThisTime)
                seedFromDateTime = unchecked((int)DateTime.Now.Ticks);
            _global = new Random(useDateTimeThisTime ? seedFromDateTime : arbitrarySeed);
            _local = null;
        }

        public static void Reset()
        {
            _global = new Random(useDateTime ? seedFromDateTime : arbitrarySeed);
            _local = null;
        }

        public static Random Instance
        {
            get
            {
                Random inst = _local;
                if (inst == null)
                {
                    int seed;
                    lock (_global) seed = _global.Next();
                    _local = inst = new Random(seed);
                }
                return inst; 
            }
        }
    }
}
