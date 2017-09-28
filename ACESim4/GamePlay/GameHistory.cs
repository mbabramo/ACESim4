using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public unsafe struct GameHistory : ISerializable
    {

        #region Construction

        // We use a struct here because this makes a big difference in performance, allowing GameHistory to be allocated on the stack. A disadvantage is that we must set the number of players, maximum size of different players' information sets, etc. in the GameHistory (which means that we need to change the code whenever we change games). We distinguish between full and partial players because this also produces a significant performance boost.

        public const int CacheLength = 19; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const byte Cache_SubdivisionAggregationIndex = 0; // Use this cache entry to aggregate subdivision decisions. Thus, do NOT use it for any other purpose.

        public const byte InformationSetTerminator = 255;
        public const byte StartDetourMarker = 252; // when starting a subdivision detour, we need to put in a marker to delineate the subdivision action from subsequent actions by same player. Note that this will precede the second subdivision decision
        public const byte EndDetourMarker = 253; // when ending a subdivision detour, we need to put in a marker to delineate the subdivision action from other player's 

        public bool Complete;
        public fixed byte ActionsHistory[GameFullHistory.MaxHistoryLength];
        public byte NextIndexInHistoryActionsOnly;

        public fixed byte Cache[CacheLength];

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 
        public bool Initialized;

        // Must also change values in InformationSetLog.
        public fixed byte InformationSets[MaxInformationSetLength];
        public const int MaxInformationSetLength = 90; // MUST equal MaxInformationSetLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLengthPerPartialPlayer * NumPartialPlayers. 
        public const int MaxInformationSetLengthPerFullPlayer = 25;
        public const int MaxInformationSetLengthPerPartialPlayer = 3;
        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 8; // includes chance players that need a very limited information set
        public int NumPartialPlayers => MaxNumPlayers - NumFullPlayers;
        public int InformationSetIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLengthPerFullPlayer * playerIndex : MaxInformationSetLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLengthPerPartialPlayer;
        public int MaxInformationSetLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLengthPerFullPlayer : MaxInformationSetLengthPerPartialPlayer;

        // The following are used to defer adding information to a player information set.
        private bool PreviousNotificationDeferred;

        private byte DeferredAction;
        private byte DeferredPlayerNumber;
        private byte[] DeferredPlayersToInform;

        public byte LastDecisionIndexAdded;


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

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] informationSets = new byte[MaxInformationSetLength];
            fixed (byte* ptr = InformationSets)
                for (int b = 0; b < MaxInformationSetLength; b++)
                    informationSets[b] = *(ptr + b);

            info.AddValue("informationSets", informationSets, typeof(byte[]));
            info.AddValue("Initialized", Initialized, typeof(bool));

        }

        // The special constructor is used to deserialize values.
        public GameHistory(SerializationInfo info, StreamingContext context)
        {
            byte[] history = (byte[]) info.GetValue("history", typeof(byte[]));
            byte[] informationSets = (byte[]) info.GetValue("informationSets", typeof(byte[]));
            fixed (byte* ptr = InformationSets)
                for (int b = 0; b < MaxInformationSetLength; b++)
                    *(ptr + b) = informationSets[b];
            Initialized = (bool) info.GetValue("Initialized", typeof(bool));

            NextIndexInHistoryActionsOnly = 0;
            LastDecisionIndexAdded = 255;
            Complete = false;

            PreviousNotificationDeferred = false;
            DeferredAction = 0;
            DeferredPlayerNumber = 0;
            DeferredPlayersToInform = null;
        }

        public GameHistory DeepCopy()
        {
            // This works only because we have a fixed buffer. If we had a reference type, we would get a shallow copy.
            GameHistory b = this;
            return b;
        }

        public void Initialize()
        {
            if (Initialized)
                return;
            if (MaxInformationSetLength != MaxInformationSetLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLengthPerPartialPlayer * NumPartialPlayers)
                throw new Exception("Lengths not set correctly.");
            Initialize_Helper();
        }

        public void Reinitialize()
        {
            Initialize_Helper();
        }

        private void Initialize_Helper()
        {
            fixed (byte* informationSetPtr = InformationSets)
                for (byte p = 0; p < MaxNumPlayers; p++)
                {
                    *(informationSetPtr + InformationSetIndex(p)) = InformationSetTerminator;
                }
            Initialized = true;
            LastDecisionIndexAdded = 255;
            NextIndexInHistoryActionsOnly = 0;
            fixed (byte* cachePtr = Cache)
                for (int i = 0; i < CacheLength; i++)
                    *(cachePtr + i) = 0;
        }

        #endregion

        #region Cache

        public unsafe void IncrementItemAtCacheIndex(byte cacheIndexToIncrement, byte incrementBy = 1)
        {
            // Console.WriteLine($"Increment cache for {cacheIndexToIncrement}");
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte) cacheIndexToIncrement) = (byte) (*(cachePtr + (byte) cacheIndexToIncrement) + incrementBy);
        }

        public unsafe void DecrementItemAtCacheIndex(byte cacheIndexToDecrement)
        {
            // Console.WriteLine($"Decrement cache for {cacheIndexToIncrement}");
            fixed (byte* cachePtr = Cache)
            {
                byte currentValue = *(cachePtr + (byte)cacheIndexToDecrement);
                if (currentValue == 0)
                    throw new Exception();
                *(cachePtr + (byte) cacheIndexToDecrement) = (byte) (currentValue - (byte) 1);
            }
        }

        public unsafe byte GetCacheItemAtIndex(byte cacheIndexToReset)
        {
            fixed (byte* cachePtr = Cache)
                return *(cachePtr + (byte) cacheIndexToReset);
        }

        public unsafe void SetCacheItemAtIndex(byte cacheIndexToReset, byte newValue)
        {
            // Console.WriteLine($"Set cache for {cacheIndexToReset} to {newValue}"); 
            if (cacheIndexToReset >= CacheLength)
                throw new NotImplementedException();
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte) cacheIndexToReset) = newValue;
        }

        #endregion

        #region History

        private static int DEBUGX = 0;

        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, byte[] playersToInform, byte[] cacheIndicesToIncrement, byte? storeActionInCacheIndex, GameProgress gameProgress, bool skipAddToHistory, bool deferNotification, bool delayPreviousDeferredNotification)
        {
            Debug.WriteLine($"Add to history {decisionByteCode} for player {playerIndex} action {action} of {numPossibleActions}"); // DEBUG
            if (decisionByteCode == 15 || decisionByteCode == 16 || decisionByteCode == 17 || decisionIndex == 13)
            {
                var DEBUG_PInfo1 = GetCurrentPlayerInformationString(0);
                var DEBUG_DInfo1 = GetCurrentPlayerInformationString(1);
                if (gameProgress != null)
                { // DEBUG
                    var DEBUG21 = gameProgress.GameFullHistory.GetInformationSetHistoryItems(gameProgress).ToList();
                }
                var DEBUG = 0;
                DEBUGX++;
            }
            if (!skipAddToHistory)
                AddToSimpleActionsList(action);
            gameProgress?.GameFullHistory.AddToHistory(decisionByteCode, decisionIndex, playerIndex, action, numPossibleActions, playersToInform, skipAddToHistory, cacheIndicesToIncrement, storeActionInCacheIndex, deferNotification, gameProgress);
            if (gameProgress != null)
            { // DEBUG
                var DEBUG2 = gameProgress.GameFullHistory.GetInformationSetHistoryItems(gameProgress).ToList();
            }
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
            if (cacheIndicesToIncrement != null && cacheIndicesToIncrement.Length > 0)
                foreach (byte cacheIndex in cacheIndicesToIncrement)
                    IncrementItemAtCacheIndex(cacheIndex);
            if (storeActionInCacheIndex != null)
                SetCacheItemAtIndex((byte) storeActionInCacheIndex, action);
        }


        public void AddToSimpleActionsList(byte action)
        {
            fixed (byte* historyPtr = ActionsHistory)
            {
                *(historyPtr + NextIndexInHistoryActionsOnly) = action;
                NextIndexInHistoryActionsOnly++;
                if (NextIndexInHistoryActionsOnly >= GameFullHistory.MaxNumActions)
                    throw new Exception("Internal error. Must increase MaxNumActions.");
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
                gameProgress.GameFullHistory.MarkComplete();
        }

        public bool IsComplete()
        {
            return Complete;
        }

        #endregion

        #region Player information sets


        public void AddToInformationSetAndLog(byte information, byte followingDecisionIndex, byte playerIndex, byte[] playersToInform, GameProgress gameProgress)
        {
            if (playersToInform == null)
                return;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                foreach (byte playerToInformIndex in playersToInform)
                {
                    AddToInformationSet(information, playerToInformIndex, informationSetsPtr);
                    gameProgress?.InformationSetLog.AddToLog(information, followingDecisionIndex, playerToInformIndex);
                }
            }
            if (GameProgressLogger.LoggingOn)
            {
                GameProgressLogger.Log($"player {playerIndex} informing {String.Join(", ", playersToInform)} info {information} following {followingDecisionIndex}");
                foreach (byte playerToInformIndex in playersToInform)
                {
                    GameProgressLogger.Log($"Player {playerToInformIndex} information: {GetCurrentPlayerInformationString(playerToInformIndex)}");
                }
            }
        }

        public void AddToInformationSetAndLog(byte information, byte followingDecisionIndex, byte playerIndex, GameProgress gameProgress)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                AddToInformationSet(information, playerIndex, informationSetsPtr);
                if (gameProgress != null)
                    gameProgress.InformationSetLog.AddToLog(information, followingDecisionIndex, playerIndex);
            }
        }

        private void AddToInformationSet(byte information, byte playerIndex, byte* informationSetsPtr)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
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
            if (numItems >= MaxInformationSetLengthForPlayer(playerIndex))
                throw new Exception("Must increase MaxInformationSetLengthPerPlayer");
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
                    if (size == maxInformationSetLengthForPlayer)
                        throw new Exception("Internal error.");
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
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
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
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            // This takes the approach of keeping the information set as append-only storage. That is, we add a notation that we're removing an item from the information set. 
            if (gameProgress != null)
                for (byte b = 0; b < numItemsToRemove; b++)
                {
                    gameProgress.InformationSetLog.AddRemovalToInformationSetLog(followingDecisionIndex, playerIndex);
                }
            RemoveItemsInInformationSet(playerIndex, numItemsToRemove);
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
