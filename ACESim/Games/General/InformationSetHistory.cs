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

        public override string ToString()
        {
            StringBuilder infoSet = new StringBuilder();
            int i = 0;
            fixed (byte* ptr = InformationSet)
                while (*(ptr+i) != 255)
                {
                    if (i != 0)
                        infoSet.Append(",");
                    infoSet.Append(*(ptr + i));
                    i++;
                }
            return $"Player {PlayerMakingDecision} Decision {DecisionIndex} Information {infoSet.ToString()} ActionChosen {ActionChosen} NumPossible {NumPossibleActions} IsTerminal {IsTerminalAction}";
        }
    }
}
