namespace ACESim
{
    public enum LeducPlayerChoice : byte
    {
        NotAvailable, // using one-based actions
        Fold,
        CallOrCheck,
        BetOrRaise01,
        BetOrRaise02,
        BetOrRaise04,
        BetOrRaise08,
        BetOrRaise16,
    }
}
