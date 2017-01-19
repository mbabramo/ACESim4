using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class OversamplingInfo
    {
        public OversamplingPlan OversamplingPlan;
        public bool StoreInputSeedsForImprovementOfOversamplingPlan;
        public double[] ReturnedInputSeeds; // only 1 input seeds is returned -- a separate OversamplingInfo must be used for a single run when StoreInputSeedsForImprovementOfOversamplingPlan is true
        public bool StoreWeightsForAdjustmentOfScoreAverages;
        public List<double> ReturnedWeightsToApplyToObservation;

        public double GetWeightForObservation(int observation)
        {
            if (StoreWeightsForAdjustmentOfScoreAverages)
                return ReturnedWeightsToApplyToObservation[observation];
            return 1.0;
        }
    }
}
