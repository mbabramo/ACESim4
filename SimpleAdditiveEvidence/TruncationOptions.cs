namespace SimpleAdditiveEvidence
{
    public enum TruncationOptions
    {
        None,
        Automatic_BothSides, // truncate based on the strategies being used against each other
        Automatic_EssentialOnly, // same, truncating only lower part of plaintiff strategy and upper part of defendant strategy
        Endogenous // make the truncation part of the strategy
    }
}
