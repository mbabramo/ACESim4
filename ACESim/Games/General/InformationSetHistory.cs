using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public unsafe struct InformationSetHistory
    {
        public fixed byte InformationSetForPlayer[GameHistory.MaxInformationSetLengthPerPlayer];
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
                byte* ptr2 = ptr;
                byte numItems = *ptr2;
                ptr2++;
                for (byte b = 0; b < numItems; b++)
                {
                    if (b != 0)
                        infoSet.Append(",");
                    infoSet.Append(*ptr2);
                    ptr2++; // move to next information -- note that decision indices are not included
                }
            }
            return $"Player {PlayerIndex} Decision {DecisionByteCode} (index {DecisionIndex}) Information {infoSet.ToString()} ActionChosen {ActionChosen} NumPossible {NumPossibleActions} IsTerminal {IsTerminalAction}";
        }
    }
}
