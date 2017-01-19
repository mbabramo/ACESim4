using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LitigationCostInputs : GameModuleInputs
    {
        public bool UseContingencyFees;
        public double ContingencyFeeRate;
        [SwapInputSeeds("Party")]
        public UtilityMaximizer Plaintiff;
        [SwapInputSeeds("Party")]
        public UtilityMaximizer Defendant;
    }
}
