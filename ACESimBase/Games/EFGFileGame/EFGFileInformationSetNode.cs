using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{


    public class EFGFileInformationSetNode : EFGFileNode
    {
        public EFGFileInformationSet InformationSet;
        public override EFGFileInformationSet GetInformationSet() => InformationSet;
        public override int NumChildNodes => InformationSet.NumActions;

    }
}
