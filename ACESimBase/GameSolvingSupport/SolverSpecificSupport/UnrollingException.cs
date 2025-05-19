using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.SolverSpecificSupport
{
    public class UnrollingException : Exception
    {
        const string _Message = "Unrolling failed. This may be because the game tree was cached but doesn't fit.";

        public UnrollingException(Exception innerException) : base(_Message, innerException)
        {

        }
    }
}
