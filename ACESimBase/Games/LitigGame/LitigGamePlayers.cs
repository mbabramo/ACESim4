using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum LitigGamePlayers : byte
    {
        // NOTE: Order: Main players, the resolution player, chance players with small information sets, then other chance players (who don't need to be counted in the MaxNumPlayers with information sets).
        // NOTE2: When adding players, also add in GetPlayersList. Also may need to change values in InformationSetLog and GameHistory, including value for MaxNumPlayers and other values dependent on this.

        // main players (full information sets)
        Plaintiff = 0,
        Defendant = 1,

        // resolution player (full information set)
        Resolution = 2,

        // chance players (small information sets)
        PostPrimaryChance = 3,
        PLiabilitySignalChance = 4,
        DLiabilitySignalChance = 5,
        LiabilityStrengthChance = 6,
        PDamagesSignalChance = 7,
        DDamagesSignalChance = 8,
        DamagesStrengthChance = 9,
        CourtLiabilityChance = 10,
        CourtDamagesChance = 11,

        // chance players (no information sets)
        PrePrimaryChance = 12,
        BothGiveUpChance = 13,
        PreBargainingRoundChance = 14, // no real chance / 1 possible action
        PostBargainingRoundChance = 15, // no real chance / 1 possible action
    }
}
