using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum LeducGamePlayers : byte
    {
        // NOTE: Order: Main players, the resolution player, chance players with small information sets, then other chance players (who don't need to be counted in the MaxNumPlayers with information sets).
        // NOTE2: When adding players, also add in GetPlayersList. Also may need to change values in InformationSetLog and GameHistory, including value for MaxNumPlayers and other values dependent on this.
        Player1,
        Player2,
        Resolution,
        Player2Chance,
        FlopChance,
        Player1Chance, // last because no information set
    }
}
