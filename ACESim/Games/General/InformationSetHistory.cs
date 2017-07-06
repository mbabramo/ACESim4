using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public unsafe struct InformationSetHistory
    {
        public byte PlayerIndex;
        public byte DecisionIndex;
        public fixed byte InformationSet[GameHistory.MaxNumActions];
        public byte ActionChosen;
        public byte NumPossibleActions;
        public bool IsTerminalAction;

        public override string ToString()
        {
            StringBuilder infoSet = new StringBuilder();
            fixed (byte* ptr = InformationSet)
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
            return $"Player {PlayerIndex} Decision {DecisionIndex} Information {infoSet.ToString()} ActionChosen {ActionChosen} NumPossible {NumPossibleActions} IsTerminal {IsTerminalAction}";
        }
    }
}
