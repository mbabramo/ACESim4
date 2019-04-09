namespace ACESim
{
    public enum LeducChoiceStage
    {
        P1Decision,
        P2Decision,
        P1Followup, // fold/check if P2 bets/raises
        P2Followup, // fold/check if P1 checks, then P2 bets, then P1 raises
    }
}
