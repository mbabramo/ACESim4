using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum InformationSetLookupApproach : byte
    {
        /// <summary>
        /// The game tree will be used to find the currently applicable information set for either a chance or nonchance player. These information sets will also be stored in the individual players' strategies, but those will not be used for retrieval during cached game replay. This approach takes longer to initialize and requires more memory, but executes improvement iterations faster.
        /// </summary>
        GameTree,
        /// <summary>
        /// There will be no complete game tree. The information sets will be stored only in the individual players' strategies, including a resolution information set. This approach should be faster to initialize but take more time to execute. It is preferable when the game tree is too large to initialize efficiently.
        /// </summary>
        Strategies,
        /// <summary>
        /// The game tree will be used to find the applicable information sets, but this information will be cross-checked against the information sets stored in the players' strategies. This is useful only to verify that the algorithm is working correctly.
        /// </summary>
        Both
    }
}
