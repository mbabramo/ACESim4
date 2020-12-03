using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ACESim.Util
{
    public static class Interlocking
    {
        public static double Add(ref double location1, double value)
        {
            // Note: There is no Interlocked.Add for doubles, but this accomplishes the same thing, without using a lock.
            double newCurrentValue = location1; // non-volatile read, so may be stale
            if (double.IsNaN(value))
                throw new Exception("Not a double");
            while (true)
            {
                double currentValue = newCurrentValue;
                double newValue = currentValue + value;
                newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
                if (newCurrentValue == currentValue)
                    return newValue;
            }
        }

        public static double Subtract(ref double location1, double value)
        {
            // Note: There is no Interlocked.Add for doubles, but this accomplishes the same thing, without using a lock.
            double newCurrentValue = location1; // non-volatile read, so may be stale
            if (double.IsNaN(value))
                throw new Exception("Not a double");
            while (true)
            {
                double currentValue = newCurrentValue;
                double newValue = currentValue - value;
                newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
                if (newCurrentValue == currentValue)
                    return newValue;
            }
        }

        public static double Multiply(ref double location1, double value)
        {
            // Note: There is no Interlocked.Add for doubles, but this accomplishes the same thing, without using a lock.
            double newCurrentValue = location1; // non-volatile read, so may be stale
            if (double.IsNaN(value))
                throw new Exception("Not a double");
            while (true)
            {
                double currentValue = newCurrentValue;
                double newValue = currentValue * value;
                newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
                if (newCurrentValue == currentValue)
                    return newValue;
            }
        }
    }
}
