using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public unsafe struct InformationSetHistory
    {
        public fixed byte InformationSetForPlayer[GameHistory.MaxInformationSetLoggingLengthPerFullPlayer];
        public byte PlayerIndex;
        public byte DecisionByteCode;
        public byte DecisionIndex;
        public byte ActionChosen;
        public byte NumPossibleActions;
        public bool IsTerminalAction;

        public override string ToString()
        {
            StringBuilder infoSet = new StringBuilder();
            fixed (byte* ptr = InformationSetForPlayer)
            {
                bool first = true;
                byte* ptr2 = ptr;
                while (*ptr2 != 255)
                {
                    if (first)
                        first = false;
                    else
                        infoSet.Append(",");
                    infoSet.Append(*ptr2);
                    ptr2++; // move to next information -- note that decision indices are not included
                }
            }
            return $"Player {PlayerIndex} Decision {DecisionByteCode} (index {DecisionIndex}) Information {infoSet.ToString()} ActionChosen {ActionChosen} NumPossible {NumPossibleActions} IsTerminal {IsTerminalAction}";
        }
    }
}
