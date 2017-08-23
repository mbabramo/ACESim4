using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum MyGameDecisions : byte
    {
        LitigationQuality,
        PSignal,
        DSignal,
        PFile, // 1 = yes, 2 = no
        DAnswer, // 1 = yes, 2 = no
        POffer,
        DOffer,
        PResponse, // 1 = yes, 2 = no
        DResponse,
        PAbandon, // 1 = yes, 2 = no
        DDefault, // 1 = yes, 2 = no
        MutualGiveUp, // 1 = plaintiff gives up (defendant wins), 2 = defendant gives up (plaintiff wins)
        CourtDecision, // with processed signals: 1 = defendant wins, 2 = plaintiff wins
        SubdividableOffer,
    }
}
