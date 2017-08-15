using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationSideBet
    {
        public double PPctOfDamagesClaimIfPWins;
        public double DPctOfDamagesClaimIfDWins;

        public double NetPaymentFromDToP(bool pWins, double damagesClaim)
        {
            if (pWins)
                return PPctOfDamagesClaimIfPWins * damagesClaim;
            else
                return 0 - DPctOfDamagesClaimIfDWins * damagesClaim;
        }
    }
}
