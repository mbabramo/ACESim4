using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class RandomProducer : IRandomProducer
    {
        public double NextDouble()
        {
            return RandomGenerator.NextDouble();
        }
    }
}
