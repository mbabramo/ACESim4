using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PresetAggressivenessOverrideModuleInputs : AggressivenessModuleInputs
    {
        [SwapInputSeeds("PresetAggressiveness")]
        public double PPresetAggressiveness;
        [SwapInputSeeds("PresetAggressiveness")]
        public double DPresetAggressiveness;
    }
}
