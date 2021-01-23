using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{
    public record EFGFileGameMove(int informationSetNumber, int playerNumber, int oneBasedAction)
    {
        public EFGFileInformationSetID InformationSetID => new EFGFileInformationSetID(informationSetNumber, playerNumber);
    }
}
