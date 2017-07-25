using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum SimpleGamePlayers : byte
    {
        // NOTE: Chance players must be listed after real players, and Resolution player must be listed last.
        Plaintiff,
        Defendant,
        QualityChance,
        PSignalChance,
        DSignalChance,
        CourtChance,
        Resolution
    }
}
