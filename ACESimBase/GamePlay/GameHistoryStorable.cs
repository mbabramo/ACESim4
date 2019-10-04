#define SAFETYCHECKS


using ACESimBase.Util;

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

        public void Initialize()
        {
            if (Initialized)
                return;
            if (GameHistory.MaxInformationSetLength != GameHistory.MaxInformationSetLengthPerFullPlayer * GameHistory.NumFullPlayers + GameHistory.MaxInformationSetLengthPerPartialPlayer * GameHistory.NumPartialPlayers)
                ThrowHelper.Throw("Lengths not set correctly.");
            Initialize_Helper();
        }

        public void Reinitialize()
        {
            Initialize_Helper();
        }

        private void Initialize_Helper()
        {
            fixed (byte* informationSetPtr = InformationSets)
                for (byte p = 0; p < GameHistory.MaxNumPlayers; p++)
                {
                    *(informationSetPtr + GameHistory.InformationSetIndex(p)) = GameHistory.InformationSetTerminator;
                }
            Initialized = true;
            LastDecisionIndexAdded = 255;
            NextIndexInHistoryActionsOnly = 0;
            fixed (byte* cachePtr = Cache)
                for (int i = 0; i < GameHistory.CacheLength; i++)
                    *(cachePtr + i) = 0;
        }
    }
}
