using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum LeducGameDecisions : byte
    {
        P1Chance,
        P2Chance,
        FlopChance,
        P1Decision,
        P2Decision,
        P2DecisionFoldExcluded, // when player 2 makes its initial decision but isn't allowed to fold; in p1's initial decision, fold is always excluded
        P1Response,
        P1ResponseBetsExcluded, // when player 1 responds to p2's bet but isn't allowed to bet (since they both bet); in player 2's response, bets are always excluded
        P2Response,
    }
}
