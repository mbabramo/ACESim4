﻿using ACESim.Util;
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

        public const int CacheLength = 15; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const int MaxHistoryLength = 200;

        public const int MaxInformationSetLength = 69; // MUST equal MaxInformationSetLengthPerFullPlayer * NumFullPlayers + MaxInformationSetLengthPerPartialPlayer * NumPartialPlayers. 
        public const int MaxInformationSetLengthPerFullPlayer = 20;
        public const int MaxInformationSetLengthPerPartialPlayer = 3;
        public const int NumFullPlayers = 3; // includes main players and resolution player and any chance players that need full size information set
        public const int MaxNumPlayers = 6; // includes chance players that need a very limited information set
        public int NumPartialPlayers => MaxNumPlayers - NumFullPlayers;
        public int InformationSetIndex(byte playerIndex) => playerIndex <= NumFullPlayers ? MaxInformationSetLengthPerFullPlayer * playerIndex : MaxInformationSetLengthPerFullPlayer * NumFullPlayers + (playerIndex - NumFullPlayers) * MaxInformationSetLengthPerPartialPlayer;
        public int MaxInformationSetLengthForPlayer(byte playerIndex) => playerIndex < NumFullPlayers ? MaxInformationSetLengthPerFullPlayer : MaxInformationSetLengthPerPartialPlayer;
        public const int MaxNumActions = 40;

        const byte HistoryComplete = 254;
        const byte HistoryTerminator = 255;

        public const byte InformationSetTerminator = 255;

        private const byte History_DecisionByteCode_Offset = 0;
        private const byte History_DecisionIndex_Offset = 1; // the decision index reflects the order of the decision in the decisions list. A decision with the same byte code could correspond to multiple decision indices.
        private const byte History_PlayerNumber_Offset = 2;
        private const byte History_Action_Offset = 3;
        private const byte History_NumPossibleActions_Offset = 4;
        private const byte History_NumPiecesOfInformation = 5; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public fixed byte Cache[CacheLength];

        public fixed byte History[MaxHistoryLength];
        public short LastIndexAddedToHistory;

        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 
        public bool Initialized;
        public fixed byte InformationSets[MaxInformationSetLength]; // a buffer for each player, terminated by 255.
                                                                    // Implement this method to serialize data. The method is called 
                                                                    // on serialization.

        // The following are used to defer adding information to a player information set.
        private bool PreviousNotificationDeferred;
        private byte DeferredAction;
        private byte DeferredPlayerNumber;
        private List<byte> DeferredPlayersToInform;

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] history = new byte[MaxHistoryLength];
            fixed (byte* ptr = History)
                for (int b = 0; b < MaxHistoryLength; b++)
                    history[b] = *(ptr + b);
            byte[] informationSets = new byte[MaxInformationSetLength];
            fixed (byte* ptr = InformationSets)
                for (int b = 0; b < MaxInformationSetLength; b++)
                    informationSets[b] = *(ptr + b);

            info.AddValue("history", history, typeof(byte[]));
            info.AddValue("informationSets", informationSets, typeof(byte[]));
            info.AddValue("LastIndexAddedToHistory", LastIndexAddedToHistory, typeof(short));
            info.AddValue("Initialized", Initialized, typeof(bool));

        }

        // The special constructor is used to deserialize values.
        public GameHistory(SerializationInfo info, StreamingContext context)
        {
            byte[] history = (byte[])info.GetValue("history", typeof(byte[]));
            byte[] informationSets = (byte[])info.GetValue("informationSets", typeof(byte[]));
            fixed (byte* ptr = History)
                for (int b = 0; b < MaxHistoryLength; b++)
                    *(ptr + b) = history[b];
            fixed (byte* ptr = InformationSets)
                for (int b = 0; b < MaxInformationSetLength; b++)
                    *(ptr + b) = informationSets[b];
            LastIndexAddedToHistory = (short)info.GetValue("LastIndexAddedToHistory", typeof(short));
            Initialized = (bool)info.GetValue("Initialized", typeof(bool));

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

        private void Initialize()
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
            fixed (byte* historyPtr = History)
                *(historyPtr + 0) = HistoryTerminator;
            fixed (byte* informationSetPtr = InformationSets)
                for (byte p = 0; p < MaxNumPlayers; p++)
                {
                    *(informationSetPtr + InformationSetIndex(p)) = InformationSetTerminator;
                }
            LastIndexAddedToHistory = 0;
            Initialized = true;
        }

        #endregion

        #region Cache

        public unsafe void IncrementItemAtCacheIndex(byte cacheIndexToIncrement)
        {
            // Console.WriteLine($"Increment cache for {cacheIndexToIncrement}");
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte)cacheIndexToIncrement) = (byte)(*(cachePtr + (byte)cacheIndexToIncrement) + (byte)1);
        }

        public unsafe byte GetCacheItemAtIndex(byte cacheIndexToReset)
        {
            fixed (byte* cachePtr = Cache)
                return *(cachePtr + (byte)cacheIndexToReset);
        }

        public unsafe void SetCacheItemAtIndex(byte cacheIndexToReset, byte newValue)
        {
            // Console.WriteLine($"Set cache for {cacheIndexToReset} to {newValue}"); 
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte)cacheIndexToReset) = newValue;
        }

        #endregion

        #region History

        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, List<byte> playersToInform, bool skipAddToHistory, List<byte> cacheIndicesToIncrement, byte? storeActionInCacheIndex, bool deferNotification, GameProgress gameProgress)
        {
            if (!Initialized)
                Initialize();
            if (!skipAddToHistory)
            {
                short i = LastIndexAddedToHistory;
                fixed (byte* historyPtr = History)
                {
                    if (*(historyPtr + i) == HistoryComplete)
                        throw new Exception("Cannot add to history of complete game.");
                    *(historyPtr + i + History_DecisionByteCode_Offset) = decisionByteCode;
                    *(historyPtr + i + History_DecisionIndex_Offset) = decisionIndex;
                    *(historyPtr + i + History_PlayerNumber_Offset) = playerIndex;
                    *(historyPtr + i + History_Action_Offset) = action;
                    *(historyPtr + i + History_NumPossibleActions_Offset) = numPossibleActions;
                    *(historyPtr + i + History_NumPiecesOfInformation) = HistoryTerminator; // this is just one item at end of all history items
                }
                LastIndexAddedToHistory = (short)(i + History_NumPiecesOfInformation);
                if (LastIndexAddedToHistory >= MaxHistoryLength - 2) // must account for terminator characters
                    throw new Exception("Internal error. Must increase history length.");
            }
            if (PreviousNotificationDeferred && DeferredPlayersToInform != null && DeferredPlayersToInform.Any())
                AddToInformationSetAndLog(DeferredAction, decisionIndex, DeferredPlayerNumber, DeferredPlayersToInform, gameProgress); /* we use the current decision index, not the decision from which it was deferred -- this is important in setting the information set correctly */
            PreviousNotificationDeferred = deferNotification;
            if (deferNotification)
            {
                DeferredAction = action;
                DeferredPlayerNumber = playerIndex;
                DeferredPlayersToInform = playersToInform;
            }
            else if (playersToInform != null && playersToInform.Any())
                AddToInformationSetAndLog(action, decisionIndex, playerIndex, playersToInform, gameProgress);
            if (cacheIndicesToIncrement != null)
                foreach (byte cacheIndex in cacheIndicesToIncrement)
                    IncrementItemAtCacheIndex(cacheIndex);
            if (storeActionInCacheIndex != null)
                SetCacheItemAtIndex((byte)storeActionInCacheIndex, action);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"Actions so far: {GetActionsAsListString()}");
        }

        

        /// <summary>
        /// Gets an earlier version of the GameHistory, including everything up to but not including the specified decision. Not tested.
        /// </summary>
        /// <returns></returns>        
        public byte? LastDecisionIndex()
        {
            if (!Initialized)
                Initialize();
            short i = LastIndexAddedToHistory;
            if (i == 0)
                return null; // no decisions processed yet
            fixed (byte* historyPtr = History)
            {
                return *(historyPtr + i - History_NumPiecesOfInformation + History_DecisionIndex_Offset);
            }
        }

        public unsafe void GetActions(byte* actions)
        {
            if (!Initialized)
                Initialize();
            GetItems(History_Action_Offset, actions);
        }

        public unsafe void GetActionsWithBlanksForSkippedDecisions(byte* actions)
        {
            if (!Initialized)
                Initialize();
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                {
                    byte decisionIndex = GetHistoryIndex(i + History_DecisionIndex_Offset);
                    while (d != decisionIndex)
                        actions[d++] = 0;
                    actions[d++] = GetHistoryIndex(i + History_Action_Offset);
                }
            actions[d] = HistoryTerminator;
        }

        public List<byte> GetActionsAsList()
        {
            byte* actions = stackalloc byte[MaxNumActions];
            GetActions(actions);
            return Util.ListExtensions.GetPointerAsList_255Terminated(actions);
        }

        public string GetActionsAsListString()
        {
            return String.Join(",", GetActionsAsList());
        }


        public unsafe void GetNumPossibleActions(byte* numPossibleActions)
        {
            GetItems(History_NumPossibleActions_Offset, numPossibleActions);
        }

        private unsafe void GetItems(int offset, byte* items)
        {
            if (!Initialized)
                Initialize();
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                    items[d++] = GetHistoryIndex(i + offset);
            items[d] = HistoryTerminator;
        }

        private byte GetHistoryIndex(int i)
        {
            // The following is useful in iterator blocks, which cannot directly contain unsafe code.
            fixed (byte* historyPtr = History)
                return *(historyPtr + i);
        }

        public void MarkComplete()
        {
            if (!Initialized)
                Initialize();
            short i = LastIndexAddedToHistory;
            fixed (byte* historyPtr = History)
            {
                if (*(historyPtr + i) == HistoryComplete)
                    throw new Exception("Game is already complete.");
                *(historyPtr + i) = HistoryComplete;
                *(historyPtr + i + 1) = HistoryTerminator;
            }
        }

        public bool IsComplete()
        {
            if (!Initialized)
                return false;
            fixed (byte* historyPtr = History)
                return (*(historyPtr + LastIndexAddedToHistory) == HistoryComplete);
        }

        #endregion

        #region Player information sets

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems(GameProgress gameProgress)
        {
            if (!Initialized)
                Initialize();
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                yield return GetInformationSetHistory(i, gameProgress);
            }
        }

        private InformationSetHistory GetInformationSetHistory(short index, GameProgress gameProgress)
        {
            if (!Initialized)
                Initialize();
            byte playerIndex = GetHistoryIndex(index + History_PlayerNumber_Offset);
            byte decisionByteCode = GetHistoryIndex(index + History_DecisionByteCode_Offset);
            byte decisionIndex = GetHistoryIndex(index + History_DecisionIndex_Offset);
            var informationSetHistory = new InformationSetHistory()
            {
                PlayerIndex = playerIndex,
                DecisionByteCode = decisionByteCode,
                DecisionIndex = decisionIndex,
                ActionChosen = GetHistoryIndex(index + History_Action_Offset),
                NumPossibleActions = GetHistoryIndex(index + History_NumPossibleActions_Offset),
                IsTerminalAction = GetHistoryIndex(index + History_NumPiecesOfInformation) == HistoryComplete
            };
            gameProgress.InformationSetLog.GetPlayerInformationAtPoint(playerIndex, decisionIndex, informationSetHistory.InformationSetForPlayer);
            return informationSetHistory;
        }

        public void AddToInformationSetAndLog(byte information, byte followingDecisionIndex, byte playerIndex, List<byte> playersToInform, GameProgress gameProgress)
        {
            GameProgressLogger.Log(() => $"player {playerIndex} informing {String.Join(", ", playersToInform)} info {information} following {followingDecisionIndex}");
            fixed (byte* informationSetsPtr = InformationSets)
            {
                if (playersToInform != null)
                    foreach (byte playerToInformIndex in playersToInform)
                    {
                        AddToInformationSet(information, playerToInformIndex, informationSetsPtr);
                        if (gameProgress != null)
                            gameProgress.InformationSetLog.AddToLog(information, followingDecisionIndex, playerToInformIndex);
                    }
            }
            if (GameProgressLogger.LoggingOn)
            {
                if (playersToInform != null)
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


        public byte AggregateSubdividable(byte playerIndex, byte decisionIndex, byte numOptionsPerBranch, byte numLevels)
        {
            throw new NotImplementedException(); // must implement without using logs
            //fixed (byte* informationSetsPtr = InformationSetLogs)
            //{
            //    byte* playerPointer = informationSetsPtr + InformationSetLoggingIndex(playerIndex);
            //    // advance to the end of the information set
            //    while (*playerPointer != InformationSetTerminator)
            //        playerPointer += 2;
            //    playerPointer--; // spot before terminator
            //    byte accumulator = (byte) (*playerPointer - 1); // one less than value pointed to (not changing the pointer itself)
            //    byte columnValue = 1; // this is the units column
            //    for (byte level = 1; level < numLevels; level++)
            //    {
            //        columnValue = (byte) (columnValue * numOptionsPerBranch); // for example, when level = 1, if numOptionsPerBranch is 10, then an action of 1 is worth 0, an action of 2 is worth 10, etc.
            //        playerPointer--; // go back to decision index
            //        playerPointer--; // go back to previous decision
            //        accumulator = (byte) (accumulator + columnValue * (*playerPointer - 1));
            //    }
            //    return (byte) (accumulator + 1);
            //}
        }

        

        public unsafe void GetPlayerInformationCurrent(byte playerIndex, byte* playerInfoBuffer)
        {
            if (!Initialized)
                Initialize();
            if (playerIndex >= MaxNumPlayers)
            {
                // player has no information
                *playerInfoBuffer = InformationSetTerminator;
                return;
            }
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + InformationSetIndex(playerIndex);
                while (*playerPointer != InformationSetTerminator)
                {
                    *playerInfoBuffer = *playerPointer;
                    playerPointer++;
                    playerInfoBuffer++;
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
            if (!Initialized)
                Initialize();
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


        public void RemoveItemsInInformationSet(byte playerIndex, byte followingDecisionIndex, byte numItemsToRemove, GameProgress gameProgress)
        {
            if (playerIndex >= MaxNumPlayers)
                throw new NotImplementedException();
            // This takes the approach of keeping the information set as append-only storage. That is, we add a notation that we're removing an item from the information set. 
            if (!Initialized)
                Initialize();
            if (gameProgress != null)
                for (byte b = 0; b < numItemsToRemove; b++)
                {
                    gameProgress.InformationSetLog.AddRemovalToInformationSetLog(followingDecisionIndex, playerIndex);
                }
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* ptr = informationSetsPtr + InformationSetIndex(playerIndex);
                while (*ptr != InformationSetTerminator)
                    ptr++; // now move past the information
                ptr -= (byte) numItemsToRemove;
                *ptr = InformationSetTerminator;
            }
            GameProgressLogger.Log($"Player {playerIndex} information (removed {numItemsToRemove}): {GetCurrentPlayerInformationString(playerIndex)}");
        }


        #endregion

        #region Decision paths

        /// <summary>
        /// When called on a complete game, this returns the next decision path to take. 
        /// For example, if there are three decisions with three actions each, then after (1, 1, 1), it would return (1, 1, 2), then (1, 1, 3), then (1, 2). 
        /// Note that in this example there may be further decisions after (1, 2). 
        /// If called on (3, 3, 3), it will throw an Exception.
        /// </summary>
        public unsafe void GetNextDecisionPath(GameDefinition gameDefinition, byte* nextDecisionPath)
        {
            if (!Initialized)
                Initialize();
            if (!IsComplete())
                throw new Exception("Can get next path to try only on a completed game.");
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionInNextPath = GetIndexOfLastDecisionWithAnotherAction(gameDefinition) ?? -1; // negative number symbolizes that there is nothing else to do
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            byte* currentActions = stackalloc byte[MaxNumActions];
            GetActionsWithBlanksForSkippedDecisions(currentActions);
            //var currentActionsList = Util.ListExtensions.GetPointerAsList_255Terminated(currentActions);
            while (indexInNewDecisionPath <= lastDecisionInNextPath)
            {
                byte currentAction = currentActions[indexInCurrentActions];
                if (currentAction == 0)
                {
                    indexInCurrentActions++;
                    lastDecisionInNextPath--;
                }
                else
                {
                    bool another = currentAction != InformationSetTerminator;
                    if (!another)
                        throw new Exception("Internal error. Expected another decision to exist.");
                    if (indexInNewDecisionPath == lastDecisionInNextPath)
                        nextDecisionPath[indexInNewDecisionPath] =
                            (byte) (currentAction +
                                    (byte) 1); // this is the decision where we need to try the next path
                    else
                        nextDecisionPath[indexInNewDecisionPath] = currentAction; // we're still on the same path

                    indexInCurrentActions++;
                    indexInNewDecisionPath++;
                }
            }
            nextDecisionPath[indexInNewDecisionPath] = InformationSetTerminator;
        }

        private int? GetIndexOfLastDecisionWithAnotherAction(GameDefinition gameDefinition)
        {
            if (!Initialized)
                Initialize();
            int? lastDecisionWithAnotherAction = null;

            fixed (byte* historyPtr = History)
                for (int i = LastIndexAddedToHistory - History_NumPiecesOfInformation; i >= 0; i -= History_NumPiecesOfInformation)
                {
                    int decisionByteCode = *(historyPtr + i + History_DecisionByteCode_Offset);
                    int decisionIndex = *(historyPtr + i + History_DecisionIndex_Offset);
                    int playerIndex = *(historyPtr + i + History_PlayerNumber_Offset);
                    int action = *(historyPtr + i + History_Action_Offset);
                    int numPossibleActions = *(historyPtr + i + History_NumPossibleActions_Offset);
                    if (gameDefinition.DecisionsExecutionOrder[decisionIndex].NumPossibleActions > action)
                    {
                        lastDecisionWithAnotherAction = decisionIndex;
                        break;
                    }
                }
            if (lastDecisionWithAnotherAction == null)
                throw new Exception("No more decision paths to take."); // indicates that there are no more decisions to take
            return lastDecisionWithAnotherAction;
        }

        #endregion
    }
}
