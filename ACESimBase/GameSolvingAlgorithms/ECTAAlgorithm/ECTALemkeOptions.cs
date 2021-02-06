using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public struct ECTALemkeOptions
    {
        public int maxPivotSteps;   /* max no. of iterations, infinity if 0         */
        public bool outputPivotingSteps;   /* Y/N  document pivot step                     */
        public bool outputInitialAndFinalTableaux;    /* Y/N  output entire tableau at beginning/end  */
        public bool outputTableauxAfterPivots;   /* Y/N  output entire tableau at each step      */
        public bool outputSolution;   /* Y/N  output solution                         */
        public bool outputLexStats;   /* Y/N  statistics on lexminratio tests         */
        public bool abortIfCycling; /* if Y, we abort prematurely if a cycle is observed */
        public int minRepetitionsForCycling; /* must be greater than 1, or cycling will always be detected */
    }
}
