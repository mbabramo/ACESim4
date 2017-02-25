using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class PlayerInfo
    {
        public string PlayerName;
        public byte PlayerNumberOverall;
        public byte NonChancePlayerIndex; // the index of the player excluding chance -- first nonchance player is 1.
        public bool PlayerIsChance;
        /// <summary>
        /// True if the player optimizes to obtain the highest possible score (bowling) rather than the lowest (golf).
        /// </summary>
        public bool HighestIsBest;

        public PlayerInfo()
        {

        }

        public PlayerInfo(string playerName, byte playerNumber, byte nonChancePlayerIndex, bool playerIsChance, bool highestIsBest)
        {
            PlayerName = playerName;
            PlayerNumberOverall = playerNumber;
            NonChancePlayerIndex = nonChancePlayerIndex;
            PlayerIsChance = playerIsChance;
            HighestIsBest = highestIsBest;
        }
    }
}
