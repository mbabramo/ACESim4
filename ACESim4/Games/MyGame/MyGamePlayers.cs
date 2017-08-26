using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum MyGamePlayers : byte
    {
        // NOTE: Chance players must be listed after real players, and Resolution player must be listed last.
        // NOTE2: When adding players, also add in GetPlayersList.
        Plaintiff,
        Defendant,
        QualityChance,
        PNoiseOrSignalChance,
        DNoiseOrSignalChance,
        BothGiveUpChance,
        PostBargainingRoundChance, // no real chance / 1 possible action
        CourtChance,
        Resolution, // keep this last
    }
}
