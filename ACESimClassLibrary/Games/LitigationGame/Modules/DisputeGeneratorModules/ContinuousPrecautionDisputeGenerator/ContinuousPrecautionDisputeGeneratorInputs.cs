using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class ContinuousPrecautionDisputeGeneratorInputs : DisputeGeneratorInputs
    {
        public double UndetectedCasesForEachDetectedCase; // undetected cases count toward the effect on the defendant and the social loss
        public double DamagesClaimMultiplier; // what plaintiff requests in damage, as a multiple of actual injury
        public double ProbabilityInjuryWithZeroPrecaution; // if no precaution is taken, what would probability of injury be?
        public double ProbabilityInjuryWithInfinitePrecaution; // the asymptote of injury probability, as precaution becomes infinite; could be zero or another number
        public double PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution; // at this precaution level, probability is average of two above
        public double ProbabilityInjuryRandomSeed;
        public double MagnitudeOfInjury;
        public double BaseProbabilityLiabilityMinimum; // the probability of liability when the marginal cost of the precaution vastly exceeds the marginal benefits
        public double BaseProbabilityLiabilityMaximum; // the probability of liability when the marginal benefit of the precaution vastly exceeds the marginal costs
        public double BaseProbabilityLiabilityWhenMarginalCostsAreTwiceMarginalBenefits; // must be between minimum amount and 0.5
        public double BaseProbabilityLiabilityWhenMarginalCostsAreHalfMarginalBenefits; // must be between maximum amount and 0.5

        public double RandSeedForPreEvolution;
    }
}
