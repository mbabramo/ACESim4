#define SAFETYCHECKS


namespace ACESim
{
    public unsafe struct GameHistoryStorable
    {
        public bool Complete;
        public fixed byte ActionsHistory[GameFullHistory.MaxHistoryLength];
        public byte NextIndexInHistoryActionsOnly;
        public fixed byte Cache[GameHistory.CacheLength]; 
        public bool Initialized;
        public fixed byte InformationSets[GameHistory.MaxInformationSetLength]; 
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform;
        public byte LastDecisionIndexAdded;

        public GameHistory ToRefStruct()
        {
            var result = new GameHistory()
            {
                Complete = Complete,
                NextIndexInHistoryActionsOnly = NextIndexInHistoryActionsOnly,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform,
                LastDecisionIndexAdded = LastDecisionIndexAdded
            };
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                result.ActionsHistory[i] = ActionsHistory[i];
            for (int i = 0; i < GameHistory.CacheLength; i++)
                result.Cache[i] = Cache[i];
            for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                result.InformationSets[i] = InformationSets[i];
            return result;
        }
    }
}
