using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct MyGameRunningSideBetsActions
    {
        public List<(byte PAction, byte DAction)> ActionsEachBargainingRound; // an action is 1 more than the number of chips bet

        public byte GetTotalChipsThatCount(byte? roundOfAbandonment, bool playerAbandoningIsPlaintiff)
        {
            byte chipsPreviousRounds = 0;
            if (ActionsEachBargainingRound != null)
                chipsPreviousRounds = (byte) ActionsEachBargainingRound.Take(roundOfAbandonment == null ? int.MaxValue : (byte) (roundOfAbandonment - 1)).Sum(x => Math.Max(x.PAction - 1, x.DAction - 1));
            byte chipsAbandoningPlayerAgreedTo = 0;
            if (roundOfAbandonment != null)
            {
                var chipsBetLastRound = ActionsEachBargainingRound.Last();
                // The abandoning player is on the hook for the chips bet in this round, if that player bet at least as many as the other player. That would then be the abandoning player's own bet. If the player bet fewer chips than the other player, the abandoning player is on the hook only for the abandoning player's own bet. Again, this is the abandoning player's own bet. Thus, either way, the bet in the last round is the abandoning player's bet.
                if (playerAbandoningIsPlaintiff)
                    chipsAbandoningPlayerAgreedTo = (byte) (chipsBetLastRound.PAction - 1);
                else
                    chipsAbandoningPlayerAgreedTo = (byte) (chipsBetLastRound.DAction - 1);
            }
            return (byte) (chipsPreviousRounds + chipsAbandoningPlayerAgreedTo);
        }
    }
}
