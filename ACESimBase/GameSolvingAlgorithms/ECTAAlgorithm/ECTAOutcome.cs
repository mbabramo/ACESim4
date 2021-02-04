using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTAOutcome
    {
        public ExactValue[] pay = new ExactValue[] { new ExactValue(), new ExactValue() };
        public int nodeIndex;
    }
}
