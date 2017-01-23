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
        public double InventionValue; // standard deviation of distribution for all inventors; the actual noise is in AllInventorsInfo
        public double InventionValueCourtNoise;
        public double InventionValueNoiseStdev;
        public double SpilloverMultiplier;
        public double SuccessProbabilityMinimumInvestment;
        public double SuccessProbabilityDoubleInvestment;
        public double SuccessProbabilityTenTimesInvestment;
        public double SuccessIndependence;
        public double CommonSuccessRandomSeed;
        public double PickWinnerRandomSeed;
        public double InadvertentInfringementProbability;
        public AllInventorsInfo AllInventorsInfo;
        
    }
}
