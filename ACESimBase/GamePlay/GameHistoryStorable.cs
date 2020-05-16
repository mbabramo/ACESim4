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
        public Span<byte> ActionsHistory => new Span<byte>(Buffer, 0, GameFullHistory.MaxNumActions);
        public Span<byte> DecisionsHistory => new Span<byte>(Buffer, GameFullHistory.MaxNumActions, GameFullHistory.MaxNumActions);
        public Span<byte> Cache => new Span<byte>(Buffer, GameFullHistory.MaxNumActions + GameFullHistory.MaxNumActions, GameHistory.CacheLength);
        public Span<byte> InformationSets => new Span<byte>(Buffer, GameFullHistory.MaxNumActions + GameFullHistory.MaxNumActions + GameHistory.CacheLength, GameHistory.MaxInformationSetLength);
        public byte NextIndexInHistoryActionsOnly;
        public byte HighestCacheIndex;
        public bool Initialized;
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform;
        public byte[] DeferredDecisionIndices;
        public byte LastDecisionIndexAdded;
#if SAFETYCHECKS
        public int CreatingThreadID;
#endif

        public GameHistoryStorable(in GameHistory gameHistory)
        {
            Complete = gameHistory.Complete;
            NextIndexInHistoryActionsOnly = gameHistory.NextIndexInHistoryActionsOnly;
            HighestCacheIndex = gameHistory.HighestCacheIndex;
            Initialized = gameHistory.Initialized;
            PreviousNotificationDeferred = gameHistory.PreviousNotificationDeferred;
            DeferredAction = gameHistory.DeferredAction;
            DeferredPlayerNumber = gameHistory.DeferredPlayerNumber;
            DeferredPlayersToInform = gameHistory.DeferredPlayersToInform;
            LastDecisionIndexAdded = gameHistory.LastDecisionIndexAdded;
            Buffer = new byte[GameFullHistory.MaxNumActions + GameFullHistory.MaxNumActions + GameHistory.CacheLength + GameHistory.MaxInformationSetLength];
            DeferredDecisionIndices = gameHistory.DeferredDecisionIndices.Length > 0 ? new byte[GameHistory.MaxDeferredDecisionIndicesLength] : null;
#if SAFETYCHECKS
            // it doesn't matter what the CreatingThreadID is on this GameHistory; now that we've 
            // duplicated the entire object, this can be used on whatever the current thread is
            // (and then on some other thread if there is another deep copy).
            CreatingThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
            byte maxActions = Math.Min((byte)GameFullHistory.MaxNumActions, (byte)NextIndexInHistoryActionsOnly);
            if (ActionsHistory.Length > 0)
                for (byte i = 0; i < maxActions; i++)
                    ActionsHistory[i] = gameHistory.ActionsHistory[i];
            if (DecisionsHistory.Length > 0)
                for (byte i = 0; i < maxActions; i++)
                    DecisionsHistory[i] = gameHistory.DecisionsHistory[i];
            if (Cache.Length > 0)
                for (byte i = 0; i < GameHistory.CacheLength; i++)
                    Cache[i] = gameHistory.Cache[i];
            int informationSetsLength = InformationSets.Length;
            if (informationSetsLength > 0)
            {
                for (byte p = 0; p < GameHistory.NumFullPlayers; p++)
                {
                    int i = GameHistory.InformationSetIndex(p);
                    byte b = 0;
                    do
                    {
                        b = gameHistory.InformationSets[i];
                        InformationSets[i] = b;
                        i++;
                    }
                    while (b != GameHistory.InformationSetTerminator);
                }
                for (byte p = GameHistory.NumFullPlayers; p < GameHistory.MaxNumPlayers; p++)
                {
                    int i = GameHistory.InformationSetIndex(p);
                    for (int j = 0; j < GameHistory.MaxInformationSetLengthPerPartialPlayer; j++)
                    {
                        InformationSets[i] = gameHistory.InformationSets[i];
                        i++;
                    }
                }
                //Simpler, but slower, because it copies past the information set terminator
                //for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                //    InformationSets[i] = gameHistory.InformationSets[i];
            }
            if (DeferredDecisionIndices.Length > 0)
            {
                for (int i = 0; i < GameHistory.MaxDeferredDecisionIndicesLength; i++)
                    DeferredDecisionIndices[i] = gameHistory.DeferredDecisionIndices[i];
            }
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
                for (int i = 0; i < GameFullHistory.MaxNumActions; i++)
                    result.ActionsHistory[i] = ActionsHistory[i];
                for (int i = 0; i < GameFullHistory.MaxNumActions; i++)
                    result.DecisionsHistory[i] = DecisionsHistory[i];
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
                DecisionsHistory = DecisionsHistory,
                Cache = Cache,
                DeferredDecisionIndices = DeferredDecisionIndices,
                InformationSets = InformationSets,
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
            NextIndexInHistoryActionsOnly = mutationOfShallowCopy.NextIndexInHistoryActionsOnly;
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
