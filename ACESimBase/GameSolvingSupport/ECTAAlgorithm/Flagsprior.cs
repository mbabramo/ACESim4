using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public struct Flagsprior
    {
        public int seed;   /* 0: centroid,
                         * >0:  random seed for prior, will be added to
			 * FIRSTPRIORSEED
			 */
        public int accuracy; 	/* largest denominator for random prior,
			 * possibly smaller when only two probabilities
			 * via continued fractions generation.
    			 * default DEFAULTACCURACY
			 */
    }
}
