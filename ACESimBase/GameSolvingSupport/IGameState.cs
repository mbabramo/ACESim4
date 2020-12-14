using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IGameState
    {
        GameStateTypeEnum GetGameStateType();
        int GetNodeNumber();
        int? AltNodeNumber { get; set; } // used by SequenceForm to align node numbers with external code
        int GetNumPossibleActions();

    }
}
