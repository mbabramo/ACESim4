#define SAFETYCHECKS


using ACESimBase.Util;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{

    public struct GameHistoryStorable : IDisposable
    {
        public bool Complete;
        public byte[] Buffer;
        public Span<byte> ActionsHistory => new Span<byte>(Buffer, 0, GameHistory.MaxNumActions);
        public Span<byte> DecisionIndicesHistory => new Span<byte>(Buffer, GameHistory.MaxNumActions, GameHistory.MaxNumActions);
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
            Buffer = ArrayPool<byte>.Shared.Rent(GameHistory.TotalBufferSize);
#if SAFETYCHECKS
            // it doesn't matter what the CreatingThreadID is on this GameHistory; now that we've 
            // duplicated the entire object, this can be used on whatever the current thread is
            // (and then on some other thread if there is another deep copy).
            CreatingThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
            for (int i = 0; i < GameHistory.TotalBufferSize; i++)
                Buffer[i] = gameHistory.Buffer[i];
            _disposed = false;
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
                result.CreateArrayForSpans(false);

                for (int i = 0; i < GameHistory.TotalBufferSize; i++)
                    result.Buffer[i] = Buffer[i];
                result.VerifyThread();
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
                Buffer = Buffer,
#if SAFETYCHECKS
                CreatingThreadID = CreatingThreadID
#endif
            };
            result.SliceBuffer();
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
                yield return DecisionIndicesHistory[i];
        }

        // To detect redundant calls
        private bool _disposed;

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose() => Dispose(true);

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _disposed = true;
                // Dispose managed state (managed objects).
                ArrayPool<byte>.Shared.Return(Buffer);
            }

        }
    }
}
