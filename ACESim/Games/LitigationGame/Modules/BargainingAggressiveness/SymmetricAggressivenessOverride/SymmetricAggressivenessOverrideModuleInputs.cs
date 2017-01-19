using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SymmetricAggressivenessOverrideModuleInputs : AggressivenessModuleInputs
    {
        public double RandomlyDeterminedStrategy;
        [FlipInputSeed]
        public double RandSeedToDetermineWhoStrategyIsFor;
    }
}
