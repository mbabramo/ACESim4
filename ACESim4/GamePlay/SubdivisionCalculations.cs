using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class SubdivisionCalculations
    {
        public static byte GetAggregatedDecision(byte zeroBasedAggregatedSoFar, byte oneBasedActionToAggregate, byte numOptionsPerLevel, bool isLast)
        {
            byte increasePreviousValue = (byte)(zeroBasedAggregatedSoFar * numOptionsPerLevel); // e.g., if there are 2 options per branch, we now see that there is another branch, so we multiple what is aggregated so far by 2.
            byte addingThisAction = (byte)(increasePreviousValue + oneBasedActionToAggregate - 1); // our actions are 1-based, but we need to do our arithmetic initially as if the actions were zero-based, since the first action means "don't add anything else.
            byte finalAction = isLast ? (byte)(addingThisAction + 1) : addingThisAction; // convert last decision back to a 1-based action 
            return finalAction;
        }

        public static byte GetOneBasedDisaggregatedAction(byte oneBasedAggregateValue, byte oneBasedDisaggregatedLevel, byte numLevels, byte numOptionsPerLevel)
        {
            byte zeroBasedAggregateValue = (byte) (oneBasedAggregateValue - 1); // make it zero based
            string convertedToBase = DecimalToArbitrarySystem(zeroBasedAggregateValue, numOptionsPerLevel);
            int stringLength = convertedToBase.Length;
            // Suppose that the oneBasedDisaggregatedLevel is 2 and there are five levels. That means that we want the 2nd most important digit of five. So, we want the fourth digit from the right. If the string is five digits long, this would be the second digit from the left (index 1). If the string is four digits long, this would be index 0.
            // Suppose that the oneBasedDisaggregatedLevel is 1 and there is one level. That means we want the only digit. 
            byte digitsFromRight = (byte) (numLevels - oneBasedDisaggregatedLevel + 1);
            int stringIndex = stringLength - digitsFromRight;
            if (stringIndex < 0 || convertedToBase == "0")
                return 1; // i.e., zero-based value of 0 converted to being a one-based action
            char character = convertedToBase[stringIndex];
            int zeroBasedValue = character - '0';
            return (byte) (zeroBasedValue + 1);
        }

        public static string DecimalToArbitrarySystem(long decimalNumber, int radix)
        {
            const int BitsInLong = 64;
            const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            if (radix < 2 || radix > Digits.Length)
                throw new ArgumentException("The radix must be >= 2 and <= " + Digits.Length.ToString());

            if (decimalNumber == 0)
                return "0";

            int index = BitsInLong - 1;
            long currentNumber = Math.Abs(decimalNumber);
            char[] charArray = new char[BitsInLong];

            while (currentNumber != 0)
            {
                int remainder = (int)(currentNumber % radix);
                charArray[index--] = Digits[remainder];
                currentNumber = currentNumber / radix;
            }

            string result = new String(charArray, index + 1, BitsInLong - index - 1);
            if (decimalNumber < 0)
            {
                result = "-" + result;
            }

            return result;
        }
    }
}
