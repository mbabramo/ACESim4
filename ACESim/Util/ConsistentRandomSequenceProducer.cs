using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class ConsistentRandomSequenceProducer : IRandomProducer
    {
        long CurrentIteration = 0;

        public ConsistentRandomSequenceProducer(long startingIteration)
        {
            CurrentIteration = startingIteration;
        }

        public double NextDouble()
        {
            double v = FastPseudoRandom.GetRandom(CurrentIteration++, 2);
            return v;
        }
    }
}
