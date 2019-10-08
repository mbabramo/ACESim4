#define SAFETYCHECKS

using ACESim.Util;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    [Serializable]
    public unsafe ref struct GameHistory
    {
        // Note: This is intended to be read-only except for the contents of the buffers. DEBUG TODO -- CAN'T DO CURRENTLY WITH FIXED


        #region Construction

        // We use a struct here because this makes a big difference in performance, allowing GameHistory to be allocated on the stack. A disadvantage is that we must set the number of players, maximum size of different players' information sets, etc. in the GameHistory (which means that we need to change the code whenever we change games). We distinguish between full and partial players because this also produces a significant performance boost.

        public const int CacheLength = 25; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const byte Cache_SubdivisionAggregationIndex = 0; // Use this cache entry to aggregate subdivision decisions. Thus, do NOT use it for any other purpose.

        public const byte InformationSetTerminator = 255;
        public const byte StartDetourMarker = 252; // when starting a subdivision detour, we need to put in a marker to delineate the subdivision action from subsequent actions by same player. Note that this will precede the second subdivision decision
        public const byte EndDetourMarker = 253; // when ending a subdivision detour, we need to put in a marker to delineate the subdivision action from other player's
        public const byte 
            DecisionHasOccurred = 251; // if reporting only that the decision has occurred, we do that here.

        public bool Complete;
        public Span<byte> ActionsHistory; // length GameFullHistory.MaxHistoryLength
        public byte NextIndexInHistoryActionsOnly;
        public Span<byte> Cache; // length CacheLength

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 
        public bool Initialized;

        // Must also change values in InformationSetLog.
        public Span<byte> InformationSets; // length MaxInformationSetLength
        public const int MaxInformationSetLength = MaxInformationSetLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLengthPerPartialPlayer * NumPartialPlayers;
        public const int MaxInformationSetLengthPerFullPlayer = 40;
        public const int MaxInformationSetLengthPerPartialPlayer = 3;
        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 13; // includes chance players that need a very limited information set
        public const int NumPartialPlayers = MaxNumPlayers - NumFullPlayers;
        public static int InformationSetIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLengthPerFullPlayer * playerIndex : MaxInformationSetLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLengthPerPartialPlayer;
        public static int MaxInformationSetLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLengthPerFullPlayer : MaxInformationSetLengthPerPartialPlayer;

        // The following are used to defer adding information to a player information set.
        public bool PreviousNotificationDeferred;

        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform; // DEBUG -- change to span also
        public byte LastDecisionIndexAdded;

        public const int TotalSpanLength = GameFullHistory.MaxHistoryLength + CacheLength + MaxInformationSetLength;

        public void Initialize()
        {
            if (Initialized)
                return;
            Initialize_Helper();
        }

        private void Initialize_Helper()
        {
            CreateArraysForSpans();
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


        public GameHistoryStorable DeepCopyToStorable()
        {
            var result = new GameHistoryStorable()
            {
                Complete = Complete,
                NextIndexInHistoryActionsOnly = NextIndexInHistoryActionsOnly,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform,
                LastDecisionIndexAdded = LastDecisionIndexAdded,
                ActionsHistory = new byte[GameFullHistory.MaxHistoryLength], // DEBUG 3
                Cache = new byte[GameHistory.CacheLength],
                InformationSets = new byte[GameHistory.MaxInformationSetLength]
            };
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                result.ActionsHistory[i] = ActionsHistory[i];
            for (int i = 0; i < GameHistory.CacheLength; i++)
                result.Cache[i] = Cache[i];
            for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                result.InformationSets[i] = InformationSets[i];
            return result;
        }

        public void CreateArraysForSpans()
        {
            // DEBUG this is inefficient
            if (ActionsHistory == null)
            {
                ActionsHistory = new byte[GameFullHistory.MaxHistoryLength];
                Cache = new byte[GameHistory.CacheLength];
                InformationSets = new byte[GameHistory.MaxInformationSetLength];
            }
        }

        public GameHistory DeepCopy()
        {
            // DEBUG the critical point for allocation of arrays for history
            GameHistory result = new GameHistory()
            {
                Complete = Complete,
                NextIndexInHistoryActionsOnly = NextIndexInHistoryActionsOnly,
                Initialized = Initialized,
                PreviousNotificationDeferred = PreviousNotificationDeferred,
                DeferredAction = DeferredAction,
                DeferredPlayerNumber = DeferredPlayerNumber,
                DeferredPlayersToInform = DeferredPlayersToInform?.ToArray(), // DEBUG -- change after this is Span
                LastDecisionIndexAdded = LastDecisionIndexAdded,
            };
            result.CreateArraysForSpans();
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                result.ActionsHistory[i] = ActionsHistory[i];
            for (int i = 0; i < GameHistory.CacheLength; i++)
                result.Cache[i] = Cache[i];
            for (int i = 0; i < GameHistory.MaxInformationSetLength; i++)
                result.InformationSets[i] = InformationSets[i];
            return result;
        }

        #endregion

        #region Strings

        public override string ToString()
        {
            return $"Actions {String.Join(",", GetActionsAsList())} cache {CacheString()} {GetInformationSetsString()} PreviousNotificationDeferred {PreviousNotificationDeferred} DeferredAction {DeferredAction} DeferredPlayerNumber {DeferredPlayerNumber}";
        }

        public string CacheString()
        {
            string cacheString = "";
            fixed (byte* cache = Cache)
                for (int i = 0; i < CacheLength; i++)
                    cacheString += cache[i] + ",";
            return cacheString;
        }

        public string GetInformationSetsString()
        {
            string informationSetsString = "";
            for (byte i = 0; i < MaxNumPlayers; i++)
                informationSetsString += $"Player {i} Information: {GetCurrentPlayerInformationString(i)} ";
            return informationSetsString;
        }

        #endregion

        #region Serialization

        //public void GetObjectData(SerializationInfo info, StreamingContext context)
        //{
        //    // Use the AddValue method to specify serialized values.
        //    byte[] informationSets = new byte[MaxInformationSetLength];
        //    fixed (byte* ptr = InformationSets)
        //        for (int b = 0; b < MaxInformationSetLength; b++)
        //            informationSets[b] = *(ptr + b);

        //    info.AddValue("informationSets", informationSets, typeof(byte[]));
        //    info.AddValue("Initialized", Initialized, typeof(bool));

        //}

        //// The special constructor is used to deserialize values.
        //public GameHistory(SerializationInfo info, StreamingContext context)
        //{
        //    byte[] history = (byte[]) info.GetValue("history", typeof(byte[]));
        //    byte[] informationSets = (byte[]) info.GetValue("informationSets", typeof(byte[]));
        //    fixed (byte* ptr = InformationSets)
        //        for (int b = 0; b < MaxInformationSetLength; b++)
        //            *(ptr + b) = informationSets[b];
        //    Initialized = (bool) info.GetValue("Initialized", typeof(bool));

        //    NextIndexInHistoryActionsOnly = 0;
        //    LastDecisionIndexAdded = 255;
        //    Complete = false;

        //    PreviousNotificationDeferred = false;
        //    DeferredAction = 0;
        //    DeferredPlayerNumber = 0;
        //    DeferredPlayersToInform = null;
        //}


        #endregion

        #region Cache

        public unsafe void IncrementItemAtCacheIndex(byte cacheIndexToIncrement, byte incrementBy = 1)
        {
            // TabbedText.WriteLine($"Increment cache for {cacheIndexToIncrement}");
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte) cacheIndexToIncrement) = (byte) (*(cachePtr + (byte) cacheIndexToIncrement) + incrementBy);
        }

        public unsafe void DecrementItemAtCacheIndex(byte cacheIndexToDecrement, byte decrementBy = 1)
        {
            // TabbedText.WriteLine($"Decrement cache for {cacheIndexToIncrement}");
            fixed (byte* cachePtr = Cache)
            {
                byte currentValue = *(cachePtr + (byte)cacheIndexToDecrement);
#if SAFETYCHECKS
                if (currentValue == 0)
                    ThrowHelper.Throw();
#endif
                *(cachePtr + (byte) cacheIndexToDecrement) = (byte) (currentValue - (byte)decrementBy);
            }
        }

        public unsafe byte GetCacheItemAtIndex(byte cacheIndexToReset)
        {
            fixed (byte* cachePtr = Cache)
                return *(cachePtr + (byte) cacheIndexToReset);
        }

        public unsafe void SetCacheItemAtIndex(byte cacheIndexToReset, byte newValue)
        {
            // TabbedText.WriteLine($"Set cache for {cacheIndexToReset} to {newValue}"); 
#if SAFETYCHECKS
            if (cacheIndexToReset >= CacheLength)
                ThrowHelper.Throw();
#endif
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte) cacheIndexToReset) = newValue;
        }

#endregion

#region History

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, byte[] playersToInform, byte[] playersToInformOfOccurrenceOnly, byte[] cacheIndicesToIncrement, byte? storeActionInCacheIndex, GameProgress gameProgress, bool skipAddToHistory, bool deferNotification, bool delayPreviousDeferredNotification)
        {
            // Debug.WriteLine($"Add to history {decisionByteCode} for player {playerIndex} action {action} of {numPossibleActions}");
            if (!skipAddToHistory)
                AddToSimpleActionsList(action);

            gameProgress?.GameFullHistoryStorable.AddToHistory(decisionByteCode, decisionIndex, playerIndex, action, numPossibleActions, skipAddToHistory);
            LastDecisionIndexAdded = decisionIndex;
            if (!delayPreviousDeferredNotification)
            {
                if (PreviousNotificationDeferred && DeferredPlayersToInform != null)
                    AddToInformationSetAndLog(DeferredAction, decisionIndex, DeferredPlayerNumber, DeferredPlayersToInform, gameProgress); /* we use the current decision index, not the decision from which it was deferred -- this is important in setting the information set correctly */
                PreviousNotificationDeferred = deferNotification;
            }
            if (deferNotification)
            {
                DeferredAction = action;
                DeferredPlayerNumber = playerIndex;
                DeferredPlayersToInform = playersToInform;
            }
            else if (playersToInform != null && playersToInform.Length > 0)
                AddToInformationSetAndLog(action, decisionIndex, playerIndex, playersToInform, gameProgress);
            if (playersToInformOfOccurrenceOnly != null && playersToInformOfOccurrenceOnly.Length > 0)
                AddToInformationSetAndLog(DecisionHasOccurred, decisionIndex, playerIndex, playersToInformOfOccurrenceOnly, gameProgress);
            if (cacheIndicesToIncrement != null && cacheIndicesToIncrement.Length > 0)
                foreach (byte cacheIndex in cacheIndicesToIncrement)
                    IncrementItemAtCacheIndex(cacheIndex);
            if (storeActionInCacheIndex != null)
                SetCacheItemAtIndex((byte) storeActionInCacheIndex, action);
        }


        private void AddToSimpleActionsList(byte action)
        {
#if SAFETYCHECKS
            if (action == 0)
                ThrowHelper.Throw("Invalid action.");
#endif
            fixed (byte* historyPtr = ActionsHistory)
            {
                *(historyPtr + NextIndexInHistoryActionsOnly) = action;
                NextIndexInHistoryActionsOnly++;
#if SAFETYCHECKS
                if (NextIndexInHistoryActionsOnly >= GameFullHistory.MaxNumActions)
                    ThrowHelper.Throw("Internal error. Must increase MaxNumActions.");
#endif
            }
        }

        public void RemoveLastActionFromSimpleActionsList()
        {
            NextIndexInHistoryActionsOnly--;
        }

        public List<byte> GetActionsAsList()
        {
            List<byte> actions = new List<byte>();
            fixed (byte* historyPtr = ActionsHistory)
            {
                for (int i = 0; i < NextIndexInHistoryActionsOnly; i++)
                    actions.Add(*(historyPtr + i));
            }
            return actions;
        }

        public void MarkComplete(GameProgress gameProgress = null)
        {
            Complete = true;
            if (gameProgress != null)
                gameProgress.GameFullHistoryStorable.MarkComplete();
        }

        public bool IsComplete()
        {
            return Complete;
        }

#endregion

#region Player information sets


        private void AddToInformationSetAndLog(byte information, byte followingDecisionIndex, byte playerIndex, byte[] playersToInform, GameProgress gameProgress)
        {
            if (playersToInform == null)
                return;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                foreach (byte playerToInformIndex in playersToInform)
                {
                    AddToInformationSet(information, playerToInformIndex, informationSetsPtr);
                    gameProgress?.InformationSetLog.AddToLog(information, followingDecisionIndex, playerToInformIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
                }
            }
            if (GameProgressLogger.LoggingOn && GameProgressLogger.DetailedLogging)
            {
                GameProgressLogger.Log($"player {playerIndex} informing {String.Join(", ", playersToInform)} info {information} following {followingDecisionIndex}");
                if (gameProgress != null)
                    foreach (byte playerToInformIndex in playersToInform)
                    {
                        GameProgressLogger.Log($"Player {playerToInformIndex} ({gameProgress.GameDefinition.PlayerNames[playerToInformIndex]}) information: {GetCurrentPlayerInformationString(playerToInformIndex)}");
                    }
            }
        }

        public void AddToInformationSetAndLog(byte information, byte followingDecisionIndex, byte playerIndex, GameProgress gameProgress)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                AddToInformationSet(information, playerIndex, informationSetsPtr);
                if (gameProgress != null)
                    gameProgress.InformationSetLog.AddToLog(information, followingDecisionIndex, playerIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
            }
        }

        private void AddToInformationSet(byte information, byte playerIndex, byte* informationSetsPtr)
        {
#if SAFETYCHECKS
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif
            byte* playerPointer = informationSetsPtr + InformationSetIndex(playerIndex);
            byte numItems = 0;
            while (*playerPointer != InformationSetTerminator)
            {
                playerPointer++;
                numItems++;
            }
            *playerPointer = information;
            playerPointer++;
            numItems++;
#if SAFETYCHECKS
            if (numItems >= MaxInformationSetLengthForPlayer(playerIndex))
                ThrowHelper.Throw("Must increase MaxInformationSetLengthPerPlayer");
#endif
            *playerPointer = InformationSetTerminator;
        }

        public unsafe void GetPlayerInformationCurrent(byte playerIndex, byte* playerInfoBuffer)
        {
            if (playerIndex >= MaxNumPlayers)
            {
                // player has no information
                *playerInfoBuffer = InformationSetTerminator;
                return;
            }
            int maxInformationSetLengthForPlayer = MaxInformationSetLengthForPlayer(playerIndex);
            byte size = 0;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + InformationSetIndex(playerIndex);
                while (*playerPointer != InformationSetTerminator)
                {
                    *playerInfoBuffer = *playerPointer;
                    playerPointer++;
                    playerInfoBuffer++;
                    size++;
#if SAFETYCHECKS
                    if (size == maxInformationSetLengthForPlayer)
                        ThrowHelper.Throw("Internal error.");
#endif
                }
                *playerInfoBuffer = InformationSetTerminator;
            }
        }


        public unsafe string GetCurrentPlayerInformationString(byte playerIndex)
        {
            byte* playerInfoBuffer = stackalloc byte[MaxInformationSetLengthPerFullPlayer];
            GetPlayerInformationCurrent(playerIndex, playerInfoBuffer);
            List<byte> informationSetList = ListExtensions.GetPointerAsList_255Terminated(playerInfoBuffer);
            return String.Join(",", informationSetList);
        }

        public byte CountItemsInInformationSet(byte playerIndex)
        {
#if SAFETYCHECKS
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif
            byte b = 0;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* ptr = informationSetsPtr + InformationSetIndex(playerIndex);
                while (*ptr != InformationSetTerminator)
                {
                    b++;
                    ptr++; // now move past the information
                }
            }
            return b;
        }


        public void RemoveItemsInInformationSetAndLog(byte playerIndex, byte followingDecisionIndex, byte numItemsToRemove, GameProgress gameProgress)
        {
#if SAFETYCHECKS
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
#endif
            // This takes the approach of keeping the information set log as append-only storage. That is, we add a notation that we're removing an item from the information set. 
            if (gameProgress != null)
                for (byte b = 0; b < numItemsToRemove; b++)
                {
                    gameProgress.InformationSetLog.AddRemovalToInformationSetLog(followingDecisionIndex, playerIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
                }
            // Now, we actually change the information set by removing the items
            RemoveItemsInInformationSet(playerIndex, numItemsToRemove);
            if (GameProgressLogger.LoggingOn && GameProgressLogger.DetailedLogging)
                GameProgressLogger.Log($"Player {playerIndex} information (removed {numItemsToRemove}): {GetCurrentPlayerInformationString(playerIndex)}");
        }

        public unsafe void ReverseAdditionsToInformationSet(byte playerIndex, byte numItemsToRemove, GameProgress gameProgress = null)
        {
            RemoveItemsInInformationSet(playerIndex, numItemsToRemove);
            if (gameProgress != null)
                gameProgress.InformationSetLog.RemoveLastItemInLog(playerIndex);
        }

    public unsafe void RemoveItemsInInformationSet(byte playerIndex, byte numItemsToRemove)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* ptr = informationSetsPtr + InformationSetIndex(playerIndex);
                while (*ptr != InformationSetTerminator)
                    ptr++; // now move past the information
                ptr -= (byte) numItemsToRemove;
                *ptr = InformationSetTerminator;
            }
        }

#endregion

    }
}
