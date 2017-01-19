using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class UtilityRangeBargainingModuleSettings
    {
        public bool CanDirectlyCalculateEquivalentWealth;
        public bool NegotiateSeparatelyOverProbabilityAndMagnitude;
        public bool NegotiateProbabilityBeforeMagnitude;
        public bool PartialSettlementEnforced;
        public bool RejectLowProbabilitySettlements;
        public bool RejectHighProbabilitySettlements;
        public double LowProbabilityThreshold;
        public double HighProbabilityThreshold;
        public bool DivideBargainingIntoMinirounds;
        public int NumBargainingMinirounds;
        public bool EvolvingRangeUncertaintySettings; 
        public bool ConstrainOffersBetween0And1;
        public bool InterpretAggressivenessRelativeToOwnUncertainty; // if true, then we move from the midpoint of the perceived bargaining range, and the distance moved equals the aggressiveness (positive or negative) times the party's own uncertainty, which is already measured as a portion of the damages claim; if false, then we start at the threat point and then move the aggressiveness times the bargaining range. The key difference is that when true, the "units" are uncertainty units (which probably makes more sense, because the parties are negotiating about damages)
    }
}
