using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util
{
    public static class ArrayUtilities
    {
        public static int ChooseIndex(double[] probabilities, double randomValue)
        {
            double total = 0;
            for (int i = 0; i < probabilities.Length; i++)
            {
                total += probabilities[i];
                if (total > randomValue)
                    return i;
            }
            return probabilities.Length - 1; // rounding error
        }
        public static byte ChooseIndex_OneBasedByte(double[] probabilities, double randomValue)
        {
            double total = 0;
            for (int i = 0; i < probabilities.Length; i++)
            {
                total += probabilities[i];
                if (total > randomValue)
                    return (byte) ( i + 1 );
            }
            return (byte) probabilities.Length; // rounding error
        }
    }
}
