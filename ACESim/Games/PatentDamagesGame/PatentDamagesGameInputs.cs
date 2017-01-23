using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameInputs : GameInputs
    {
        public double MarketRateOfReturn;
        public double PermittedRateOfReturn;
        public double CostOfEntry;
        public double InventionValue;
        public double InventionValueCourtNoise;
        public double SpilloverMultiplier;
        public double SuccessProbabilityMinimumInvestment;
        public double SuccessProbabilityDoubleInvestment;
        public double SuccessIndependence;
        public double CommonSuccessRandomSeed;
        public double InadvertentInfringementProbability;
        public AllInventorsInfo AllInventorsInfo;
        
    }
}
