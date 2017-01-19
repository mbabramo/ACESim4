using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SideBetAdjustmentsModuleInputs : AdjustmentsModuleInputs
    {
        public double PctOfDamagesClaimChallengedPartyWouldReceive;
        public double PctOfDamagesChallengerWouldReceive;
        public bool DoubleChallengeCountsAsSingleChallenge;
    }
}
