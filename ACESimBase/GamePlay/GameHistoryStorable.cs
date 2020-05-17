#define SAFETYCHECKS


using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{

    public struct GameHistoryStorable
    {
        public bool Complete;
        public byte[] Buffer;
        public Span<byte> ActionsHistory => new Span<byte>(Buffer, 0, GameHistory.MaxNumActions);
        public Span<byte> DecisionsHistory => new Span<byte>(Buffer, GameHistory.MaxNumActions, GameHistory.MaxNumActions);
        public Span<byte> Cache => new Span<byte>(Buffer, GameHistory.MaxNumActions + GameHistory.MaxNumActions, GameHistory.CacheLength);
        public Span<byte> InformationSetMembership => new Span<byte>(Buffer, GameHistory.MaxNumActions + GameHistory.MaxNumActions + GameHistory.CacheLength, GameHistory.SizeInBytes_BitArrayForInformationSetMembership);
        public Span<byte> DecisionsDeferred => new Span<byte>(Buffer, GameHistory.MaxNumActions + GameHistory.MaxNumActions + GameHistory.CacheLength + GameHistory.SizeInBytes_BitArrayForInformationSetMembership, GameHistory.SizeInBytes_BitArrayForDecisionsDeferred);
        public byte NextIndexInHistoryActionsOnly;
        public byte HighestCacheIndex;
        public bool Initialized;
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform;
        public byte LastDecisionIndexAdded;
#if SAFETYCHECKS
        public int CreatingThreadID;
#endif

        public GameHistoryStorable(in GameHistory gameHistory)
        {
            Complete = gameHistory.Complete;
            NextIndexInHistoryActionsOnly = gameHistory.NextActionsAndDecisionsHistoryIndex;
            HighestCacheIndex = gameHistory.HighestCacheIndex;
            Initialized = gameHistory.Initialized;
            PreviousNotificationDeferred = gameHistory.PreviousNotificationDeferred;
            DeferredAction = gameHistory.DeferredAction;
            DeferredPlayerNumber = gameHistory.DeferredPlayerNumber;
            DeferredPlayersToInform = gameHistory.DeferredPlayersToInform;
            LastDecisionIndexAdded = gameHistory.LastDecisionIndexAdded;
            Buffer = new byte[GameHistory.TotalBufferSize];
#if SAFETYCHECKS
            // it doesn't matter what the CreatingThreadID is on this GameHistory; now that we've 
            // duplicated the entire object, this can be used on whatever the current thread is
            // (and then on some other thread if there is another deep copy).
            CreatingThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
            byte maxActions = Math.Min((byte)GameHistory.MaxNumActions, (byte)NextIndexInHistoryActionsOnly);
            if (ActionsHistory.Length > 0)
                for (byte i = 0; i < maxActions; i++)
                    ActionsHistory[i] = gameHistory.ActionsHistory[i];
            if (DecisionsHistory.Length > 0)
                for (byte i = 0; i < maxActions; i++)
                    DecisionsHistory[i] = gameHistory.DecisionIndicesHistory[i];
            if (Cache.Length > 0)
                for (byte i = 0; i < GameHistory.CacheLength; i++)
                    Cache[i] = gameHistory.Cache[i];
            
            if (InformationSetMembership.Length > 0)
                for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForInformationSetMembership; i++)
                    InformationSetMembership[i] = gameHistory.InformationSetMembership[i];
            if (DecisionsDeferred.Length > 0)
                for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForDecisionsDeferred; i++)
                    DecisionsDeferred[i] = gameHistory.DecisionsDeferred[i];
        }

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
                for (int i = 0; i < GameHistory.MaxNumActions; i++)
                    result.ActionsHistory[i] = ActionsHistory[i];
                for (int i = 0; i < GameHistory.MaxNumActions; i++)
                    result.DecisionIndicesHistory[i] = DecisionsHistory[i];
                for (int i = 0; i < GameHistory.CacheLength; i++)
                    result.Cache[i] = Cache[i];
                result.VerifyThread();
                for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForInformationSetMembership; i++)
                    result.InformationSetMembership[i] = InformationSetMembership[i];
               for (int i = 0; i < GameHistory.SizeInBytes_BitArrayForDecisionsDeferred; i++)
                    result.DecisionsDeferred[i] = DecisionsDeferred[i];
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
                NextActionsAndDecisionsHistoryIndex = NextIndexInHistoryActionsOnly,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform,  // this does not need to be duplicated because it is set in gamedefinition and not changed
                LastDecisionIndexAdded = LastDecisionIndexAdded,
                ActionsHistory = ActionsHistory,
                DecisionIndicesHistory = DecisionsHistory,
                Cache = Cache,
                InformationSetMembership = InformationSetMembership,
                DecisionsDeferred = DecisionsDeferred,
#if SAFETYCHECKS
                CreatingThreadID = CreatingThreadID
#endif
            };
            return result;
        }

        /// <summary>
        /// This updates from a shallow copy (which will be using the same buffers, which therefore don't need copying).
        /// </summary>
        /// <param name="mutationOfShallowCopy"></param>
        public void UpdateFromShallowCopy(GameHistory mutationOfShallowCopy)
        {
            Complete = mutationOfShallowCopy.Complete;
            NextIndexInHistoryActionsOnly = mutationOfShallowCopy.NextActionsAndDecisionsHistoryIndex;
            HighestCacheIndex = mutationOfShallowCopy.HighestCacheIndex;
            Initialized = mutationOfShallowCopy.Initialized;
            PreviousNotificationDeferred = mutationOfShallowCopy.PreviousNotificationDeferred;
            DeferredAction = mutationOfShallowCopy.DeferredAction;
            DeferredPlayerNumber = mutationOfShallowCopy.DeferredPlayerNumber;
            DeferredPlayersToInform = mutationOfShallowCopy.DeferredPlayersToInform;
            LastDecisionIndexAdded = mutationOfShallowCopy.LastDecisionIndexAdded;
        }
        public IEnumerable<byte> GetDecisionsEnumerable()
        {
            for (int i = 0; i < NextIndexInHistoryActionsOnly; i++)
                yield return DecisionsHistory[i];
        }
    }
}
