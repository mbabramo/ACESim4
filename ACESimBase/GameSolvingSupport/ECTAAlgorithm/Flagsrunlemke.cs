using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public struct Flagsrunlemke
    {
        public int maxcount;   /* max no. of iterations, infinity if 0         */
        public bool outputPivotingSteps;   /* Y/N  document pivot step                     */
        public bool outputInitialTableau;    /* Y/N  output entire tableau at beginning/end  */
        public bool outputTableaux;   /* Y/N  output entire tableau at each step      */
        public bool outputSolution;   /* Y/N  output solution                         */
        public bool outputLexStats;   /* Y/N  statistics on lexminratio tests         */
    }
}
