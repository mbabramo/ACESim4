using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    /// <summary>
    /// An ECTAException is a type of exception that may occur when using inexact arithmetic to find an equilibrium. 
    /// </summary>
    public class ECTAException : Exception
    {
        public ECTAException(string message) : base(message)
        {

        }
    }
}
