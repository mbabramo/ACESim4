using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class NegligenceDisputeGeneratorInputs : DisputeGeneratorInputs
    {
        public double UndetectedCasesForEachDetectedCase; // undetected cases count toward the effect on the defendant and the social loss
        public double DamagesClaimMultiplier;
        public double CostOfPrecaution;
        public double ProbabilityInjuryWithoutPrecaution;
        public double ProbabilityInjuryWithoutPrecautionNoise;
        public double ProbabilityInjuryWithPrecaution;
        public double ProbabilityInjuryRandomSeed;
        public double MagnitudeOfInjury;
        public double BaseProbabilityLiabilityWithPrecaution;
        public double BaseProbabilityLiabilityWithoutPrecautionMinimum; // the probability of liability when the cost of the precaution vastly exceeds the benefits
        public double BaseProbabilityLiabilityWithoutPrecautionMaximum; // the probability of liability when the benefit of the precaution vastly exceeds the costs
        public double BaseProbabilityLiabilityWithoutPrecautionWhenCostsAreTwiceBenefits; // must be between minimum amount and 0.5
        public double BaseProbabilityLiabilityWithoutPrecautionWhenCostsAreHalfBenefits; // must be between maximum amount and 0.5
        public bool StrictLiability; // P should always when if there is an injury when this is set; the previous 5 settings are then ignored

        public double RandSeedForPreEvolution;
    }
}
