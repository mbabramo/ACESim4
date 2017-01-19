using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class LitigationCostEndogenousEffortInputs : LitigationCostStandardInputs
    {
        public double MarginalCostForFirstPieceOfIndependentInformation; // how much does it cost to get first piece of extra information?
        public double MarginalCostMultiplierForSubsequentPiecesOfIndependentInformation; // multiply cost of previous piece of this to get new marginal cost, reflects declining marginal benefits of additional information
        public bool RelativeIntensityAffectsTrialOutcomes; // if false, then differences in intensity affect only information; if true, there is also a shift to the more intense party
        public double IntensityRatioAsymptoteForProbabilityShift; // magnitude of shift if there were an infinite effort ratio; a shift of 1.0 means that if probability of winning would have been 0.7, it will now be 1.0 or 0.4, depending on direction of shift
        public double DoubleIntensityEffectOnProbabilityShift; // magnitude of shift if the more intense party is twice as intense as the other party
        public double AssumedInvestigationIntensityOfOtherParty; // when not exploring equilibria, we assume one party's investigation intensity in deriving the other
        public double AssumedTrialIntensityOfOtherParty; // when not exploring equilibria, we assume one party's trial intensity in deriving the other
        public double IntensityContagion; // If > 0, then each party's intensity choice depends to this extent on other party's
        public double RandSeedForEvolvingBargaining;
        public double RandSeed2ForEvolvingBargaining;
    }
}
