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
    public ref struct GameHistory
    {
        #region Construction

        // We use a struct here because this makes a big difference in performance, allowing GameHistory to be allocated on the stack. Currently, we fix the number of players, maximum size of different players' information sets, etc. in the GameHistory (which means that we need to change the code whenever we change games). We distinguish between full and partial players because this also produces a significant performance boost. DEBUG TODO: Make it so that the size can be specified by the game. Also, make it so that we can have a large space for history, most of which is blank space for the next history. Then, we could make it so that we don't have to keep copying all the earlier steps, but include the index of the previous and current steps. 

        public const int CacheLength = 25; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const byte Cache_SubdivisionAggregationIndex = 0; // Use this cache entry to aggregate subdivision decisions. Thus, do NOT use it for any other purpose.

        public const byte InformationSetTerminator = 255;
        public const byte DecisionHasOccurred = 251; // if reporting only that the decision has occurred, we do that here.

        public const int MaxInformationSetLength = MaxInformationSetLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLengthPerPartialPlayer * NumPartialPlayers;
        public const int MaxInformationSetLengthPerFullPlayer = 40;
        public const int MaxInformationSetLengthPerPartialPlayer = 3;
        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 13; // includes chance players that need a very limited information set
        public const int NumPartialPlayers = MaxNumPlayers - NumFullPlayers;
        public const int TotalSpanLength = GameFullHistory.MaxHistoryLength + CacheLength + MaxInformationSetLength;

        public bool Initialized;
        public bool Complete;
        public byte NextIndexInHistoryActionsOnly;
        public byte LastDecisionIndexAdded;

        // The following are used to defer adding information to a player information set.
        public bool PreviousNotificationDeferred;
        public byte DeferredAction;
        public byte DeferredPlayerNumber;
        public byte[] DeferredPlayersToInform; // NOTE: We can leave this as an array because it is set in game definition and not changed.

        public Span<byte> ActionsHistory; // length GameFullHistory.MaxHistoryLength
        public Span<byte> Cache; // length CacheLength
        public Span<byte> InformationSets; // length MaxInformationSetLength

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 

        // Must also change values in InformationSetLog.
        public static int InformationSetIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLengthPerFullPlayer * playerIndex : MaxInformationSetLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLengthPerPartialPlayer;
        public static int MaxInformationSetLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLengthPerFullPlayer : MaxInformationSetLengthPerPartialPlayer;

        public bool Matches(GameHistory other)
        {
            var basics = Initialized == other.Initialized && Complete == other.Complete && NextIndexInHistoryActionsOnly == other.NextIndexInHistoryActionsOnly && LastDecisionIndexAdded == other.LastDecisionIndexAdded && PreviousNotificationDeferred == other.PreviousNotificationDeferred && DeferredAction == other.DeferredAction && DeferredPlayerNumber == other.DeferredPlayerNumber && ((DeferredPlayersToInform == null && other.DeferredPlayersToInform == null) || DeferredPlayersToInform.SequenceEqual(other.DeferredPlayersToInform));
            if (!basics)
                return false;
            if (!GetActionsAsList().SequenceEqual(other.GetActionsAsList())) // will ignore info after items in span
                return false;
            if (!Cache.SequenceEqual(other.Cache))
                return false;
            if (GetInformationSetsString() != other.GetInformationSetsString())
                return false;
            return true;
        }

        public void Initialize()
        {
            if (Initialized)
                return;
            Initialize_Helper();
        }

        private void Initialize_Helper()
        {
            CreateArraysForSpans();
            for (byte p = 0; p < GameHistory.MaxNumPlayers; p++)
            {
                InformationSets[GameHistory.InformationSetIndex(p)] = GameHistory.InformationSetTerminator;
            }
            Initialized = true;
            LastDecisionIndexAdded = 255;
            NextIndexInHistoryActionsOnly = 0;
            for (int i = 0; i < GameHistory.CacheLength; i++)
                Cache[i] = 0;
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
            for (int i = 0; i < GameFullHistory.MaxHistoryLength && i < NextIndexInHistoryActionsOnly; i++)
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
                DeferredPlayersToInform = DeferredPlayersToInform, // this does not need to be duplicated because it is set in gamedefinition and not changed
                LastDecisionIndexAdded = LastDecisionIndexAdded,
            };
            result.CreateArraysForSpans();
            for (int i = 0; i < GameFullHistory.MaxHistoryLength && i < NextIndexInHistoryActionsOnly; i++)
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
            for (int i = 0; i < CacheLength; i++)
                cacheString += Cache[i] + ",";
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

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] informationSets = new byte[MaxInformationSetLength];
            for (int b = 0; b < MaxInformationSetLength; b++)
                informationSets[b] = InformationSets[b];

            info.AddValue("informationSets", informationSets, typeof(byte[]));
            info.AddValue("Initialized", Initialized, typeof(bool));

        }

        // The special constructor is used to deserialize values.
        public GameHistory(SerializationInfo info, StreamingContext context)
        {
            ActionsHistory = new byte[GameFullHistory.MaxHistoryLength];
            Cache = new byte[GameHistory.CacheLength];
            InformationSets = new byte[GameHistory.MaxInformationSetLength];

            byte[] history = (byte[])info.GetValue("history", typeof(byte[]));
            byte[] informationSets = (byte[])info.GetValue("informationSets", typeof(byte[]));
            for (int b = 0; b < MaxInformationSetLength; b++)
                InformationSets[b] = informationSets[b];
            Initialized = (bool)info.GetValue("Initialized", typeof(bool));

            NextIndexInHistoryActionsOnly = 0;
            LastDecisionIndexAdded = 255;
            Complete = false;

            PreviousNotificationDeferred = false;
            DeferredAction = 0;
            DeferredPlayerNumber = 0;
            DeferredPlayersToInform = null;
        }


        #endregion

        #region Cache

        public void IncrementItemAtCacheIndex(byte cacheIndexToIncrement, byte incrementBy = 1)
        {
            // TabbedText.WriteLine($"Increment cache for {cacheIndexToIncrement}");
            Cache[(byte) cacheIndexToIncrement] += incrementBy;
        }

        public void DecrementItemAtCacheIndex(byte cacheIndexToDecrement, byte decrementBy = 1)
        {
            // TabbedText.WriteLine($"Decrement cache for {cacheIndexToIncrement}");
            byte currentValue = Cache[(byte)cacheIndexToDecrement];
#if SAFETYCHECKS
            if (currentValue == 0)
                ThrowHelper.Throw();
#endif
            Cache[(byte) cacheIndexToDecrement] = (byte) (currentValue - (byte)decrementBy);
        }

        public byte GetCacheItemAtIndex(byte cacheIndexToReset)
        {
            return Cache[(byte) cacheIndexToReset];
        }

        public void SetCacheItemAtIndex(byte cacheIndexToReset, byte newValue)
        {
            // TabbedText.WriteLine($"Set cache for {cacheIndexToReset} to {newValue}"); 
#if SAFETYCHECKS
            if (cacheIndexToReset >= CacheLength)
                ThrowHelper.Throw();
#endif
            Cache[(byte) cacheIndexToReset] = newValue;
        }

#endregion

#region History

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, byte[] playersToInform, byte[] playersToInformOfOccurrenceOnly, byte[] cacheIndicesToIncrement, byte? storeActionInCacheIndex, GameProgress gameProgress, bool skipAddToHistory, bool deferNotification, bool delayPreviousDeferredNotification)
        {
            // Debug.WriteLine($"Add to history {decisionByteCode} for player {playerIndex} action {action} of {numPossibleActions}");
            if (!skipAddToHistory)
                AddToSimpleActionsList(action);

            if (gameProgress != null)
                gameProgress.GameFullHistoryStorable = gameProgress.GameFullHistoryStorable.AddToHistory(decisionByteCode, decisionIndex, playerIndex, action, numPossibleActions, skipAddToHistory);
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
            ActionsHistory[NextIndexInHistoryActionsOnly] = action;
            NextIndexInHistoryActionsOnly++;
#if SAFETYCHECKS
            if (NextIndexInHistoryActionsOnly >= GameFullHistory.MaxNumActions)
                ThrowHelper.Throw("Internal error. Must increase MaxNumActions.");
#endif
        }

        public void RemoveLastActionFromSimpleActionsList()
        {
            NextIndexInHistoryActionsOnly--;
        }

        public List<byte> GetActionsAsList()
        {
            List<byte> actions = new List<byte>();
            for (int i = 0; i < NextIndexInHistoryActionsOnly; i++)
                actions.Add(ActionsHistory[i]);
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
            foreach (byte playerToInformIndex in playersToInform)
            {
                AddToInformationSet(information, playerToInformIndex, InformationSets);
                gameProgress?.InformationSetLog.AddToLog(information, followingDecisionIndex, playerToInformIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
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
            AddToInformationSet(information, playerIndex, InformationSets);
            if (gameProgress != null)
                gameProgress.InformationSetLog.AddToLog(information, followingDecisionIndex, playerIndex, gameProgress.GameDefinition.PlayerNames, gameProgress.GameDefinition.DecisionPointsExecutionOrder);
        }

        private void AddToInformationSet(byte information, byte playerIndex, Span<byte> informationSets)
        {
#if SAFETYCHECKS
            if (playerIndex >= MaxNumPlayers)
                ThrowHelper.Throw();
#endif
            int playerPointer = InformationSetIndex(playerIndex);
            byte numItems = 0;
            while (informationSets[playerPointer] != InformationSetTerminator)
            {
                playerPointer++;
                numItems++;
            }
            informationSets[playerPointer] = information;
            playerPointer++;
            numItems++;
#if SAFETYCHECKS
            if (numItems >= MaxInformationSetLengthForPlayer(playerIndex))
                ThrowHelper.Throw("Must increase MaxInformationSetLengthPerPlayer");
#endif
            informationSets[playerPointer] = InformationSetTerminator;
        }

        public void GetPlayerInformationCurrent(byte playerIndex, Span<byte> playerInfo)
        {
            GetPlayerInformationCurrent(playerIndex, InformationSets, playerInfo);
        }


        // Note: This static method is made available so that we can call this when playerInfo Span<byte> is stack-allocated. 
        public static void GetPlayerInformationCurrent(byte playerIndex, Span<byte> informationSets, Span<byte> playerInfo)
        {
            int playerInfoBufferIndex = 0;
            if (playerIndex >= MaxNumPlayers)
            {
                // player has no information
                playerInfo[playerInfoBufferIndex] = InformationSetTerminator;
                return;
            }
            int maxInformationSetLengthForPlayer = MaxInformationSetLengthForPlayer(playerIndex);
            byte size = 0;
            int playerPointer = InformationSetIndex(playerIndex);
            while (informationSets[playerPointer] != InformationSetTerminator)
            {
                playerInfo[playerInfoBufferIndex] = informationSets[playerPointer];
                playerPointer++;
                playerInfoBufferIndex++;
                size++;
#if SAFETYCHECKS
                if (size == maxInformationSetLengthForPlayer)
                    ThrowHelper.Throw("Internal error.");
#endif
            }
            playerInfo[playerInfoBufferIndex] = InformationSetTerminator;
        }


        public string GetCurrentPlayerInformationString(byte playerIndex)
        {
            Span<byte> playerInfoBuffer = stackalloc byte[MaxInformationSetLengthPerFullPlayer]; 
            GetPlayerInformationCurrent(playerIndex, InformationSets, playerInfoBuffer);
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
            int ptr = InformationSetIndex(playerIndex);
            while (InformationSets[ptr] != InformationSetTerminator)
            {
                b++;
                ptr++; // now move past the information
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

        public void ReverseAdditionsToInformationSet(byte playerIndex, byte numItemsToRemove, GameProgress gameProgress = null)
        {
            RemoveItemsInInformationSet(playerIndex, numItemsToRemove);
            if (gameProgress != null)
                gameProgress.InformationSetLog.RemoveLastItemInLog(playerIndex);
        }

        public void RemoveItemsInInformationSet(byte playerIndex, byte numItemsToRemove)
        {
            int informationSetIndex = InformationSetIndex(playerIndex);
            while (InformationSets[informationSetIndex] != InformationSetTerminator)
                informationSetIndex++; // now move past the information
            informationSetIndex -= (byte) numItemsToRemove;
            InformationSets[informationSetIndex] = InformationSetTerminator;
        }

#endregion

    }
}
