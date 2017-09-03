using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum MyGamePlayers : byte
    {
        // NOTE: Order: Main players, the resolution player, chance players that may have uneven chance actions (and thus have information sets), then other chance players.
        // NOTE2: When adding players, also add in GetPlayersList.
        Plaintiff,
        Defendant,
        Resolution,
        PNoiseOrSignalChance,
        DNoiseOrSignalChance,
        CourtChance,
        QualityChance,
        BothGiveUpChance,
        PreBargainingRoundChance, // no real chance / 1 possible action
        PostBargainingRoundChance, // no real chance / 1 possible action
    }
}
