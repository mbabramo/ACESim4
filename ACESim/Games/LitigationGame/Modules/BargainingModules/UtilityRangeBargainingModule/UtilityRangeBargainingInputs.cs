using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class UtilityRangeBargainingInputs : BargainingInputs
    {
        public bool PartiesWillAcceptIncrementalBenefitIfNoUtilityRangePerceived;
        public double AggressivenessContagion; // to what extent does one party's aggressiveness affect other's? Should not generally be greater than 0.5; only applies when bargaining aggressiveness is used
        [FlipInputSeed]
        public double RandomMultiplierForEvolvingEquivalentWealth;
        public double RandomChangeToPOffer;
        public double RandomChangeToDOffer;
    }
}
