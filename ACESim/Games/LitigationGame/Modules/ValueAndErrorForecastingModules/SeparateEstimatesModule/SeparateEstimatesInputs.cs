using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class SeparateEstimatesInputs : ValueAndErrorForecastingInputs
    {
        public double GenericNoiseLevel;
        public double GenericNoiseLevel2;
        [SwapInputSeeds("NoiseLevel")]
        public double PNoiseLevel;
        [SwapInputSeeds("NoiseLevel")]
        public double DNoiseLevel;
        public double GenericNoiseRealized;
        public double GenericNoiseRealized2;
        [SwapInputSeeds("NoiseRealized")]
        public double PNoiseRealized;
        [SwapInputSeeds("NoiseRealized")]
        public double DNoiseRealized;
    }
}
