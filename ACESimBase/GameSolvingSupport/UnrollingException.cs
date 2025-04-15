using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class UnrollingException : Exception
    {
        public override string Message => "Unrolling failed. This may be because the game tree was cached but doesn't fit.";
    }
}
