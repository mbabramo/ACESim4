namespace ACESimBase.GameSolvingSupport
{
    /// <summary>
    ///  If a game is symmetric, then the strategy developer will look at this value for a decision to determine whether the decision output is symmetric (that is, whether player 0 will make the same decision as player 1 in the corresponding information set or the reverse decision)
    /// </summary>
    public enum SymmetryMapOutput : byte
    {
        /// <summary>
        /// This is a chance decision, so there is no decision by a non-chance player, and this is irrelevant. 
        /// </summary>
        ChanceDecision,
        /// <summary>
        /// The action chosen by player 0 will be chosen by player 1 in the corresponding information set.
        /// </summary>
        SameAction,
        /// <summary>
        /// Given that player 0 chooses action a for this decision of n possible actions, player 1 chooses n - a + 1 in player 1's corresponding information set.
        /// </summary>
        ReverseAction,
    }
}
