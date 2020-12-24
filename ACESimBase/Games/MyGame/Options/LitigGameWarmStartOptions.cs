using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    public enum LitigGameWarmStartOptions
    {
        NoWarmStart,
        DiscourageSettlementByMakingOpponentGenerous, // if opponent is generous, then player will be stingy --> discourages settlement
        FacilitateSettlementByMakingOpponentStingy,
    }
}
