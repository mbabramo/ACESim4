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
        Plaintiff,
        Defendant,

        // resolution player (full information set)
        Resolution,

        // chance players (small information sets)
        PostPrimaryChance,
        PLiabilitySignalChance,
        DLiabilitySignalChance,
        LiabilityStrengthChance,
        PDamagesSignalChance,
        DDamagesSignalChance,
        DamagesStrengthChance,
        CourtLiabilityChance,
        CourtDamagesChance,

        // chance players (no information sets)
        PrePrimaryChance,
        BothGiveUpChance,
        PreBargainingRoundChance, // no real chance / 1 possible action
        PostBargainingRoundChance, // no real chance / 1 possible action
    }
}
