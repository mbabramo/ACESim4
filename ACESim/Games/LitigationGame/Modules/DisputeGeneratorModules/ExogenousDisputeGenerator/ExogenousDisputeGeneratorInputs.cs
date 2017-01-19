using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class ExogenousDisputeGeneratorInputs : DisputeGeneratorInputs
    {
        public double DamagesClaim;
        [FlipInputSeed]
        public double Damages;
        [FlipInputSeed]
        public double EvidentiaryStrengthLiability;
        public double ProbabilityCorrectGivenThreeQuartersAgreement;
        public double ProbabilityCorrectGivenUnanimity;
        [FlipInputSeed]
        public double RandSeedForDeterminingCorrectness;
        public double SocialLossWeightOnPCompensationInaccuracy;
        public double SocialLossWeightOnDDeterrenceInaccuracy;
    }
}
