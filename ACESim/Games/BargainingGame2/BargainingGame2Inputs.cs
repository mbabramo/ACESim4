using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class BargainingGame2Inputs : GameInputs
    {
        public double selfPctOfPie; // oppPctOfPie = 1 - selfPctOfPie
        public double selfFailCost; // Assume for now that fail costs are known
        public double oppFailCost;
        public double selfNoiseLevel;
        public double oppNoiseLevel;
        public double selfNoiseRealized;
        public double oppNoiseRealized;
    }
}
