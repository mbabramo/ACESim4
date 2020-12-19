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
        public bool bdocupivot;   /* Y/N  document pivot step                     */
        public bool binitabl;    /* Y/N  output entire tableau at beginning/end  */
        public bool bouttabl;   /* Y/N  output entire tableau at each step      */
        public bool boutsol;   /* Y/N  output solution                         */
        public bool blexstats;   /* Y/N  statistics on lexminratio tests         */
    }
}
