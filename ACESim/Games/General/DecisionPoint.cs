using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class DecisionPoint : ActionPoint
    {
        public Decision Decision;
        public int? DecisionNumber;
        public int? DecisionNumberWithinActionGroup;
        public int? DecisionNumberWithinModule;
        public int? SubstituteDecisionNumberInsteadOfEvolving;

        public override ActionPoint DeepCopy(ActionGroup newActionGroup)
        {
            return new DecisionPoint() { ActionGroup = newActionGroup, Name = Name, Decision = Decision, DecisionNumber = DecisionNumber, DecisionNumberWithinActionGroup = DecisionNumberWithinActionGroup, DecisionNumberWithinModule = DecisionNumberWithinModule, SubstituteDecisionNumberInsteadOfEvolving = SubstituteDecisionNumberInsteadOfEvolving };
        }
    }
}
