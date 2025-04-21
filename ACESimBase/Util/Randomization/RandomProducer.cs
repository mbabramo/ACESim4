using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Randomization
{
    public class RandomProducer : IRandomProducer
    {
        public double NextDouble()
        {
            return RandomGenerator.NextDouble();
        }

        public double GetDoubleAtIndex(int index)
        {
            return RandomGenerator.NextDouble(); // does the same thing -- i.e., doesn't necessarily produce consistent random numbers.
        }
    }
}
