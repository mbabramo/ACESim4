using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class IndependentEstimatesInputs : ValueAndErrorForecastingInputs
    {
        [SwapInputSeeds("NoiseLevel")]
        public double PNoiseLevel;
        [SwapInputSeeds("NoiseLevel")]
        public double DNoiseLevel;
        [SwapInputSeeds("EstimatingOtherNoiseLevel")]
        [FlipInputSeed]
        public double PEstimateDNoiseLevel;
        [SwapInputSeeds("EstimatingOtherNoiseLevel")]
        [FlipInputSeed]
        public double DEstimatePNoiseLevel;
        [SwapInputSeeds("NoiseRealized")]
        [FlipInputSeed]
        public double PNoiseRealized;
        [SwapInputSeeds("NoiseRealized")]
        [FlipInputSeed]
        public double DNoiseRealized;
    }
}
