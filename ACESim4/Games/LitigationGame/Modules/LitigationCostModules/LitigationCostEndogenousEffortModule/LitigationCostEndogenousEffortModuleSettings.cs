using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostEndogenousEffortModuleSettings
    {
        public bool OptimizeInvestigationIntensity;
        public bool OptimizeTrialPrep;
        public bool UseSimpleEquilibria;
        public bool ExploreInvestigationEquilibria;
        public bool ExploreTrialEquilibria;
    }
}
