using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public struct InformationSetHistory
    {
        public byte PlayerMakingDecision;
        public byte DecisionIndex;
        public List<byte> InformationSet;
        public byte ActionChosen;
        public byte NumPossibleActions;
        public bool IsTerminalAction;
    }
}
