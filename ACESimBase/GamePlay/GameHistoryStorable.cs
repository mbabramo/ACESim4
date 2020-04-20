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
        public byte[] DeferredDecisionIndices;
        public byte LastDecisionIndexAdded;
#if SAFETYCHECKS
        public int CreatingThreadID;
#endif

        public static GameHistoryStorable NewInitialized()
        {
            GameHistory gameHistory = new GameHistory();
            gameHistory.Initialize();
            return gameHistory.DeepCopyToStorable();
        }

        /// <summary>
        /// Copies to a temporary game history object. The result can be used in a different thread from the thread used to store the GameHistoryStorable.
        /// </summary>
        /// <returns></returns>
        public GameHistory DeepCopyToRefStruct()
        {
            GameHistory result = ShallowCopyToRefStruct();
            result.CreatingThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId; // we can specify a new thread, since we're copying all of the information.
            if (ActionsHistory != null)
            {
                result.CreateArraysForSpans(false);
                for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                    result.ActionsHistory[i] = ActionsHistory[i];
                for (int i = 0; i < GameHistory.CacheLength; i++)
                    result.Cache[i] = Cache[i];
                result.VerifyThread();
                for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                    result.InformationSets[i] = InformationSets[i];
                for (int i = 0; i < GameHistory.MaxDeferredDecisionIndicesLength; i++)
                    result.DeferredDecisionIndices[i] = DeferredDecisionIndices[i];
            }
            return result;
        }

        /// <summary>
        /// Creates a shallow copy, which should be used only on the creating thread.
        /// </summary>
        /// <returns></returns>
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
                DeferredDecisionIndices = DeferredDecisionIndices,
                InformationSets = InformationSets,
#if SAFETYCHECKS
                CreatingThreadID = CreatingThreadID
#endif
            };
            return result;
        }

        public void UpdateFromShallowCopy(GameHistory mutationOfShallowCopy)
        {
            Complete = mutationOfShallowCopy.Complete;
            NextIndexInHistoryActionsOnly = mutationOfShallowCopy.NextIndexInHistoryActionsOnly;
            Initialized = mutationOfShallowCopy.Initialized;
            PreviousNotificationDeferred = mutationOfShallowCopy.PreviousNotificationDeferred;
            DeferredAction = mutationOfShallowCopy.DeferredAction;
            DeferredPlayerNumber = mutationOfShallowCopy.DeferredPlayerNumber;
            DeferredPlayersToInform = mutationOfShallowCopy.DeferredPlayersToInform;
            LastDecisionIndexAdded = mutationOfShallowCopy.LastDecisionIndexAdded;
        }
    }
}
