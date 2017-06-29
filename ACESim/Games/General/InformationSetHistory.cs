using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public unsafe struct InformationSetHistory
    {
        public byte PlayerMakingDecision;
        public byte DecisionIndex;
        public byte* InformationSet;
        public byte ActionChosen;
        public byte NumPossibleActions;
        public bool IsTerminalAction;
    }
}
