using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class ConstrainToRange
    {
        public static double Constrain(double number, double min, double max)
        {
            if (number < min)
                return min;
            else if (number > max)
                return max;
            else
                return number;
        }
    }
}
