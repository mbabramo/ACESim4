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
        public byte? DecisionNumber;
        public byte? DecisionNumberWithinActionGroup;
        public byte? DecisionNumberWithinModule;
        public byte? SubstituteDecisionNumberInsteadOfEvolving;

        public override ActionPoint DeepCopy(ActionGroup newActionGroup)
        {
            return new DecisionPoint() { ActionGroup = newActionGroup, Name = Name, Decision = Decision, DecisionNumber = DecisionNumber, DecisionNumberWithinActionGroup = DecisionNumberWithinActionGroup, DecisionNumberWithinModule = DecisionNumberWithinModule, SubstituteDecisionNumberInsteadOfEvolving = SubstituteDecisionNumberInsteadOfEvolving };
        }
    }
}
