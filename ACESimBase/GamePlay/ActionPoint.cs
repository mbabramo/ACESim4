using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ActionPoint
    {
        public ActionGroup ActionGroup;
        public int? ActionPointIndex;
        public string Name;
        public Decision Decision;
        public byte? DecisionNumber;
        public byte? DecisionNumberWithinActionGroup;
        public byte? DecisionNumberWithinModule;

        public ActionPoint DeepCopy(ActionGroup newActionGroup)
        {
            return new ActionPoint() { ActionGroup = newActionGroup, Name = Name, Decision = Decision, DecisionNumber = DecisionNumber, DecisionNumberWithinActionGroup = DecisionNumberWithinActionGroup, DecisionNumberWithinModule = DecisionNumberWithinModule };
        }
    }
}
