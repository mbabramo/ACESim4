using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class ObfuscationGameModule
    {
        public static double DeobfuscateBasedOnUniformUnderlyingDistributionAndNormalNoise(Strategy obfuscationGameStrategy, double numberWithObfuscation, double minRange, double maxRange, double stdevOfObfuscation)
        {
            double normalizedStdev = (stdevOfObfuscation - minRange) / (maxRange - minRange);
            if (normalizedStdev < 0 || normalizedStdev > 1.0)
                throw new Exception("ObfuscationGame was not trained on this value.");
            double normalizedObfuscated = (numberWithObfuscation - minRange) / (maxRange - minRange); // could be less than 0 or greater than 1
            double normalizedObfuscationGameResult = obfuscationGameStrategy.InterpolateOutputForPoint(new List<double> { normalizedObfuscated, normalizedStdev });
            double returnVal = minRange + normalizedObfuscationGameResult * (maxRange - minRange);
            return returnVal;
        }
    }
}
