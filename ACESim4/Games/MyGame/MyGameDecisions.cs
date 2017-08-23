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
        PFile,
        DAnswer,
        POffer,
        DOffer,
        PResponse,
        DResponse,
        PAbandon,
        DDefault,
        MutualGiveUp,
        CourtDecision,
        SubdividableOffer,
    }
}
