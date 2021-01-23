using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{


    public class EFGFileChanceInformationSet : EFGFileInformationSet
    {
        public double[] ChanceProbabilities;
        public override int NumActions => ChanceProbabilities.Length;
    }
}
