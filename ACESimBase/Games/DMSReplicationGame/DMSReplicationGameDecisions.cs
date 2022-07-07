using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    internal enum DMSReplicationGameDecisions : byte
    {
        C_Dummy, // must have at least one chance decision
        P_Slope,
        P_MinValue,
        P_TruncationPortion,
        D_Slope,
        D_MinValue,
        D_TruncationPortion,
    }
}
