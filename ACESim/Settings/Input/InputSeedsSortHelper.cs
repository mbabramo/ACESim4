using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    internal class InputSeedsSortHelper
    {
        public double evenlySpacedValue;
        public double randomValue;
        public InputSeedsSortHelper()
        {
        }
        public void Fill(double theEvenlySpacedValue)
        {
            evenlySpacedValue = theEvenlySpacedValue;
        }
        public void Randomize()
        {
            randomValue = RandomGenerator.Next();
        }
    }
}
