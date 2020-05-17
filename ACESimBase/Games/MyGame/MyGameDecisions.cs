using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum MyGameDecisions : byte
    {
        PrePrimaryActionChance,
        PrimaryAction,
        PostPrimaryActionChance,
        LiabilityStrength,
        DamagesStrength,
        PLiabilitySignal,
        DLiabilitySignal,
        PDamagesSignal,
        DDamagesSignal,
        PFile, // 1 = yes, 2 = no
        DAnswer, // 1 = yes, 2 = no
        PreBargainingRound, // dummy chance decision -- gives us chance to perform some processing 
        PAgreeToBargain, // 1 = yes (can do rest of bargaining for this round), 2 = no (no settlement reached this round)
        DAgreeToBargain, // 1 = yes (can do rest of bargaining for this round), 2 = no (no settlement reached this round)
        POffer,
        DOffer,
        PResponse, // 1 = yes, 2 = no
        DResponse,
        PChips, // how many extra chips to bet (simultaneous with defendant; maximum controls)
        DChips,
        PAbandon, // 1 = yes, 2 = no
        DDefault, // 1 = yes, 2 = no
        MutualGiveUp, // 1 = plaintiff gives up (defendant wins), 2 = defendant gives up (plaintiff wins)
        PostBargainingRound, // dummy chance decision -- gives us chance to perform some processing
        PPretrialAction,
        DPretrialAction,
        CourtDecisionLiability, // with processed signals: 1 = defendant wins, 2 = plaintiff wins
        CourtDecisionDamages,
        SubdividableOffer,
    }
}
