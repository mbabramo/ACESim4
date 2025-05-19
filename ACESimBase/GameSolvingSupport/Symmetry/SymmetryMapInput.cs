using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport.Symmetry
{

    /// <summary>
    ///  If a game is symmetric, then the strategy developer will look at this value for a decision to determine whether, when this decision becomes an input in an information set, the information is the same for both players or reversed (that is, whether to find the player 1 information set corresponding to a player 0's information set, the information must be reversed)
    /// </summary>
    public enum SymmetryMapInput : byte
    {
        /// <summary>
        /// This action does not appear in either non-chance player's information set.
        /// </summary>
        NotInInformationSet,
        /// <summary>
        /// This decision maps directly. That is, if this information appears in player 0's information set, then the corresponding information set for player 1 has the exact same information.
        /// </summary>
        SameInfo,
        /// <summary>
        /// This decision maps in reverse. That is, if this information appears in player 0's information set as action a, and the number of possible actions is n, then the corresponding information set for player 1 has this information has action n - a + 1. 
        /// </summary>
        ReverseInfo,
        /// <summary>
        /// This decision cannot be used in a symmetric game, because as it is defined, play is not symmetric.
        /// </summary>
        NotCompatibleWithSymmetry,
    }
}
