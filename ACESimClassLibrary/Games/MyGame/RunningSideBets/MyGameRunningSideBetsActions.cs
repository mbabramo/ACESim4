using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public struct MyGameRunningSideBetsActions
    {
        public List<(byte PAction, byte DAction)> ActionsEachBargainingRound; // an action is 1 more than the number of chips bet

        public double GetFirstChipsRecommended(bool plaintiff)
        {
            if (ActionsEachBargainingRound == null)
                return 0;
            return (plaintiff ? ActionsEachBargainingRound.First().PAction : ActionsEachBargainingRound.First().DAction) - 1;
        }

        public double GetTotalChipsRecommended(bool plaintiff)
        {
            if (ActionsEachBargainingRound == null)
                return 0;
            return ActionsEachBargainingRound.Sum(x => (plaintiff ? x.PAction : x.DAction) - 1);
        }

        public byte GetTotalChipsThatCount(byte? roundOfAbandonment, bool playerAbandoningIsPlaintiff, bool countAllChipsInAbandoningRound)
        {
            byte chipsPreviousRounds = 0;
            if (ActionsEachBargainingRound != null)
            {
                if (countAllChipsInAbandoningRound)
                    return (byte)ActionsEachBargainingRound.Sum(x => Math.Max(x.PAction - 1, x.DAction - 1));
                // otherwise, we hold the player to the chips he/she bet in last round, but not to other player's chips
                chipsPreviousRounds = (byte) ActionsEachBargainingRound.Take(roundOfAbandonment == null ? int.MaxValue : (byte) (roundOfAbandonment - 1)).Sum(x => Math.Max(x.PAction - 1, x.DAction - 1));
            }
            else
                return 0;
            byte chipsInLastRound = 0;
            if (roundOfAbandonment != null)
            {
                var chipsBetLastRound = ActionsEachBargainingRound.Last();
                // The abandoning player is on the hook for the chips bet in this round, if that player bet at least as many as the other player. That would then be the abandoning player's own bet. If the player bet fewer chips than the other player, the abandoning player is on the hook only for the abandoning player's own bet. Again, this is the abandoning player's own bet. Thus, either way, the bet in the last round is the abandoning player's bet.
                if (playerAbandoningIsPlaintiff)
                    chipsInLastRound = (byte) (chipsBetLastRound.PAction - 1);
                else
                    chipsInLastRound = (byte) (chipsBetLastRound.DAction - 1);
            }
            return (byte) (chipsPreviousRounds + chipsInLastRound);
        }
    }
}
