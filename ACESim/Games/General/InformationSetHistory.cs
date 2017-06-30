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
        public fixed byte InformationSet[GameHistory.MaxNumActions];
        public byte ActionChosen;
        public byte NumPossibleActions;
        public bool IsTerminalAction;
    }
}
