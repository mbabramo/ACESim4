namespace ACESimBase.GameSolvingSupport.DeepCFRSupport
{
    public enum DeepCFRMultiModelMode
    {
        /// <summary>
        /// One unified model for all decisions of both players
        /// </summary>
        Unified,
        /// <summary>
        /// A single model for each player, covering all decisions of that player
        /// </summary>
        PlayerSpecific,
        /// <summary>
        /// A separate model for each type of decision (but where a single decision type, such as plaintiff's offer, occurs more than once, a single model for all of those together)
        /// </summary>
        DecisionTypeSpecific,
        /// <summary>
        /// A separate model for each specific
        /// </summary>
        DecisionSpecific
    }
}
