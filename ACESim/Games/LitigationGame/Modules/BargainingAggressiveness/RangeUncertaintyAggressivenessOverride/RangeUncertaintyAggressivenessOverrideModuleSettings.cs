using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class RangeUncertaintyAggressivenessOverrideModuleSettings
    {
        public bool EvolvingRangeUncertaintyModule;
        public int? IterationsOverrideMinimum;
        public int? IterationsOverrideMaximum;
        public double IterationsOverrideCurvature;
        public int? SmoothingPointsOverrideMinimum;
        public int? SmoothingPointsOverrideMaximum;
        public double SmoothingPointsOverrideCurvature;
        public int AggressivenessModuleNumber;

        public RangeUncertaintyAggressivenessOverrideModuleSettings()
        {
            // DEBUG -- this file was lost and reconstructed. Not sure what the default values should be.
        }
    }
}
