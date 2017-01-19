using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ActionPoint
    {
        public ActionGroup ActionGroup;
        public int? ActionPointIndex;
        public string Name;

        public virtual ActionPoint DeepCopy(ActionGroup newActionGroup)
        {
            return new ActionPoint() { ActionGroup = newActionGroup, ActionPointIndex = ActionPointIndex, Name = Name };
        }
    }

}
