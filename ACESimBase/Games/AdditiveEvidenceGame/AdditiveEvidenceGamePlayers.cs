using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public enum AdditiveEvidenceGamePlayers : byte
    {
        // NOTE: Order: Main players, the resolution player, chance players with small information sets, then other chance players (who don't need to be counted in the MaxNumPlayers with information sets).
        // NOTE2: When adding players, also add in GetPlayersList. Also may need to change values in InformationSetLog and GameHistory, including value for MaxNumPlayers and other values dependent on this.

        // main players (full information sets)
        Plaintiff,
        Defendant,

        // resolution player (full information set)
        Resolution,

        // chance players (small information sets)
        Chance_Plaintiff_Quality,
        Chance_Defendant_Quality,
        Chance_Plaintiff_Bias,
        Chance_Defendant_Bias,
        Chance_Neither_Quality, // will be drawn after settlement fails
        Chance_Neither_Bias, // will be drawn after settlement fails
        // Note that we omit Both_Quality and Both_Bias because we can just separate those as options. We do want the players recognizing the distribution of possibilities in information that neither knows about.
    }
}
