using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class ForbiddenActDisputeGeneratorInputs : DisputeGeneratorInputs
    {
        public double PrivateBenefitOfAct;
        public double SocialCostOfAct;
        public double LiabilityMultiplier;
        public double UntruncatedEvidentiaryStrengthDidNotDoIt;
        public double AdditiveEvidentiaryStrengthDidIt;
        public bool DamagesEqualPrivateBenefit;
        public bool DamagesEqualSocialCost;
        public bool DamagesEqualLargerOfPrivateBenefitAndSocialCost;
        public double RandSeedForPreEvolution;
    }
}
