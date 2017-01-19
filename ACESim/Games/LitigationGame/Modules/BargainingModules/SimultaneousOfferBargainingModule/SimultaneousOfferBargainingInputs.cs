using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class SimultaneousOfferBargainingInputs : BargainingInputs
    {
        public double RandomSeedForSimultaneousAcceptanceOfOtherOffers;
        public double RandomSeedToDetermineWhetherAcceptanceOfOtherOffersAllowed;
        public double ChanceLastMomentAcceptanceAllowed;
    }
}
