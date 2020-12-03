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
        public byte PlayerIndex;
        public bool PlayerIsChance;
        /// <summary>
        /// True if the player optimizes to obtain the highest possible score (bowling) rather than the lowest (golf).
        /// </summary>
        public bool HighestIsBest;

        public PlayerInfo()
        {

        }

        public PlayerInfo(string playerName, byte playerNumber, bool playerIsChance, bool highestIsBest)
        {
            PlayerName = playerName;
            PlayerIndex = playerNumber;
            PlayerIsChance = playerIsChance;
            HighestIsBest = highestIsBest;
        }

        public override string ToString()
        {
            return $"Player {PlayerName} ({PlayerIndex}) {(PlayerIsChance ? "Chance" : "")}";
        }
    }
}
