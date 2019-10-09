#define SAFETYCHECKS


using ACESimBase.Util;
using System.Linq;

namespace ACESim
{

    public struct GameHistoryStorable
    {
        public bool Complete;
        public byte[] ActionsHistory;
        public byte NextIndexInHistoryActionsOnly;
        public byte[] Cache; 
        public bool Initialized;
        public byte[] InformationSets; 
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform;
        public byte LastDecisionIndexAdded;

        public static GameHistoryStorable NewInitialized()
        {
            GameHistory gameHistory = new GameHistory();
            gameHistory.Initialize();
            return gameHistory.DeepCopyToStorable();
        }

        public GameHistory DeepCopyToRefStruct()
        {
            GameHistory result = ShallowCopyToRefStruct();
            result.CreateArraysForSpans();
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                result.ActionsHistory[i] = ActionsHistory[i];
            for (int i = 0; i < GameHistory.CacheLength; i++)
                result.Cache[i] = Cache[i];
            for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                result.InformationSets[i] = InformationSets[i];
            return result;
        }

        public GameHistory ShallowCopyToRefStruct()
        {
            var result = new GameHistory()
            {
                Complete = Complete,
                NextIndexInHistoryActionsOnly = NextIndexInHistoryActionsOnly,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform,  // this does not need to be duplicated because it is set in gamedefinition and not changed
                LastDecisionIndexAdded = LastDecisionIndexAdded,
                ActionsHistory = ActionsHistory,
                Cache = Cache,
                InformationSets = InformationSets
            };
            return result;
        }
    }
}
