using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ShootoutAdjustmentsModuleInputs : AdjustmentsModuleInputs
    {
        public double DiscountFactorForShootouts;
        public double MultiplierForShootouts;
        public bool EnforceWhenCaseDroppedImmediatelyAfterward;
        public double TrialExpensesMultiplierWithDoubleDamages;
    }
}
