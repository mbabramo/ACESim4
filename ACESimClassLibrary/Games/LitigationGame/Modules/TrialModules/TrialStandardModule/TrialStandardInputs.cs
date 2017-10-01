using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class TrialStandardInputs : TrialInputs
    {
        [FlipInputSeed]
        public double DisputeResolutionRandomSeedLiability;
        [FlipInputSeed]
        public double DisputeResolutionRandomSeedDamages;
        public double JudgeNoiseLevelLiability;
        public double JudgeNoiseLevelDamages;
        public bool UseProportionalResultsWhenEvolvingEarlierDecisions;
        public bool CaseResolvedBasedOnExogenouslySpecifiedProbability;
    }
}
