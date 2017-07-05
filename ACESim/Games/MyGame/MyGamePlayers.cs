using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum MyGamePlayers : byte
    {
        // NOTE: Chance players must be listed last
        Plaintiff,
        Defendant,
        QualityChance,
        SignalChance,
        CourtChance
    }
}
