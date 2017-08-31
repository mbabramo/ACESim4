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
        public const int CacheLength = 10; // the game and game definition can use the cache to store information. This is helpful when the game player is simulating the game without playing the underlying game. The game definition may, for example, need to be able to figure out which decision is next.
        public const int MaxHistoryLength = 200;
        public const int MaxInformationSetLength = 1000; // MUST equal MaxInformationSetLengthPerPlayer * MaxNumPlayers. 
        public const int MaxInformationSetLengthPerPlayer = 100; 
        public const int MaxNumPlayers = 10;
        public const int MaxNumActions = 30;
        const byte HistoryComplete = 254;
        const byte HistoryTerminator = 255;

        const byte InformationSetTerminator = 255;
        const byte RemoveItemFromInformationSet = 254;

        private const byte History_DecisionByteCode_Offset = 0;
        private const byte History_DecisionIndex_Offset = 1; // the decision index reflects the order of the decision in the decisions list. A decision with the same byte code could correspond to multiple decision indices.
        private const byte History_PlayerNumber_Offset = 2;
        private const byte History_Action_Offset = 3;
        private const byte History_NumPossibleActions_Offset = 4;
        private const byte History_NumPiecesOfInformation = 5; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public fixed byte Cache[CacheLength];

        public fixed byte History[MaxHistoryLength];
        public short LastIndexAddedToHistory;

        public fixed byte HistoryActionsOnly[MaxNumActions];
        public byte NextIndexInHistoryActionsOnly;

        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 
        public bool Initialized;
        public fixed byte InformationSets[MaxInformationSetLength]; // a buffer for each player, terminated by 255.
                                                                    // Implement this method to serialize data. The method is called 
                                                                    // on serialization.

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
            NextIndexInHistoryActionsOnly = 0;
            Initialized = (bool)info.GetValue("Initialized", typeof(bool));
        }

        #region Construction and adding information

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
            fixed (byte* ptr = InformationSets)
                for (int p = 0; p < MaxNumPlayers; p++)
                    *(ptr + MaxInformationSetLengthPerPlayer * p) = InformationSetTerminator;
            LastIndexAddedToHistory = 0;
            Initialized = true;
        }

        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerNumber, byte action, byte numPossibleActions, List<byte> playersToInform, bool skipAddToHistory, List<byte> cacheIndicesToIncrement)
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
                    *(historyPtr + i + History_PlayerNumber_Offset) = playerNumber;
                    *(historyPtr + i + History_Action_Offset) = action;
                    *(historyPtr + i + History_NumPossibleActions_Offset) = numPossibleActions;
                    *(historyPtr + i + History_NumPiecesOfInformation) = HistoryTerminator; // this is just one item at end of all history items
                }
                LastIndexAddedToHistory = (short)(i + History_NumPiecesOfInformation);
                if (LastIndexAddedToHistory >= MaxHistoryLength - 2) // must account for terminator characters
                    throw new Exception("Internal error. Must increase history length.");
            }
            if (playersToInform != null && playersToInform.Any())
                AddToInformationSet(action, decisionIndex, playerNumber, playersToInform);
            if (cacheIndicesToIncrement != null)
                foreach (byte cacheIndex in cacheIndicesToIncrement)
                    IncrementCacheIndex(cacheIndex);
        }

        public unsafe void IncrementCacheIndex(byte cacheIndexToIncrement)
        {
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte)cacheIndexToIncrement) = (byte)(*(cachePtr + (byte)cacheIndexToIncrement) + (byte)1);
        }

        public unsafe byte GetCacheIndex(byte cacheIndexToReset)
        {
            fixed (byte* cachePtr = Cache)
                return *(cachePtr + (byte)cacheIndexToReset);
        }

        public unsafe void SetCacheIndex(byte cacheIndexToReset, byte newValue)
        {
            fixed (byte* cachePtr = Cache)
                *(cachePtr + (byte)cacheIndexToReset) = newValue;
        }

        public void AddToSimpleActionsList(byte action)
        {
            fixed (byte* historyPtr = HistoryActionsOnly)
            {
                *(historyPtr + NextIndexInHistoryActionsOnly) = action;
                NextIndexInHistoryActionsOnly++;
                if (NextIndexInHistoryActionsOnly >= MaxNumActions)
                    throw new Exception("Internal error. Must increase MaxNumActions.");
            }
        }

        /// <summary>
        /// Gets an earlier version of the GameHistory, including everything up to but not including the specified decision.
        /// </summary>
        /// <param name="upToDecisionIndex"></param>
        /// <returns></returns>
        public GameHistory BackInTime(byte upToDecisionIndex)
        {
            if (!Initialized)
                Initialize();
            GameHistory next = this;
            if (LastIndexAddedToHistory != 0)
            {
                if (upToDecisionIndex == 0)
                    next.Reinitialize();
                else
                {
                    byte* historyPtr = next.History;
                    for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                    {
                        byte decisionIndex = *(historyPtr + i + History_DecisionIndex_Offset);
                        if (decisionIndex >= upToDecisionIndex)
                        {
                            *(historyPtr + i - 1) = HistoryTerminator;
                            break;
                        }
                    }
                    byte* informationSetsPtr = next.InformationSets;
                    for (byte p = 0; p < MaxNumPlayers; p++)
                    {
                        byte* playerPointer = informationSetsPtr + p * MaxInformationSetLengthPerPlayer;
                        while (*playerPointer != InformationSetTerminator)
                        {
                            if (*playerPointer >= upToDecisionIndex)
                            {
                                *playerPointer = InformationSetTerminator;
                                break;
                            }
                            playerPointer += 2;
                        }
                    }
                }
            }
            return next;
        }

        #endregion
        
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

        //public (byte mostRecentAction, byte actionBeforeThat) GetLastActionAndActionBeforeThat()
        //{
        //    if (LastIndexAddedToHistory < History_NumPiecesOfInformation * 2)
        //        throw new Exception("Internal error. Two actions have not occurred");
        //    fixed (byte* historyPtr = History)
        //    {
        //        byte* mostRecentPointer = (historyPtr + LastIndexAddedToHistory - History_NumPiecesOfInformation + History_Action_Offset);
        //        return (*mostRecentPointer, *(mostRecentPointer - History_NumPiecesOfInformation));
        //    }
        //}

        public (byte mostRecentAction, byte actionBeforeThat) GetLastActionAndActionBeforeThat()
        {
            // Note that we're using the simple actions list here. That means that when we have decisions with subdivisions, we ignore the subdivisions and count only the final decision.
            if (NextIndexInHistoryActionsOnly < 2)
                throw new Exception("Internal error. Two actions have not occurred");
            fixed (byte* historyPtr = HistoryActionsOnly)
            {
                return (*(historyPtr + NextIndexInHistoryActionsOnly - 1), *(historyPtr + NextIndexInHistoryActionsOnly - 2));
            }
        }

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems()
        {
            if (!Initialized)
                Initialize();
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                yield return GetInformationSetHistory(i);
            }
        }

        private InformationSetHistory GetInformationSetHistory(short index)
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
            GetPlayerInformation(playerIndex, decisionIndex, informationSetHistory.InformationSetForPlayer);
            return informationSetHistory;
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

        #region Player information sets

        public void AddToInformationSet(byte information, byte followingDecisionIndex, byte playerNumber, List<byte> playersToInform)
        {
            //Debug.WriteLine($"player {playerNumber} informing {String.Join(", ", playersToInform)} info {information} following {followingDecisionIndex}"); 
            fixed (byte* informationSetsPtr = InformationSets)
            {
                if (playersToInform != null)
                    foreach (byte playerIndex in playersToInform)
                    {
                        AddToInformationSet(information, followingDecisionIndex, playerIndex, informationSetsPtr);
                    }
            }
        }

        public void AddToInformationSet(byte information, byte followingDecisionIndex, byte playerIndex)
        {
            fixed (byte* informationSetsPtr = InformationSets)
                AddToInformationSet(information, followingDecisionIndex, playerIndex, informationSetsPtr);
        }

        private void AddToInformationSet(byte information, byte followingDecisionIndex, byte playerNumber, byte* informationSetsPtr)
        {
            if (!Initialized)
                Initialize();
            //Debug.WriteLine($"Adding information {information} following decision {followingDecisionIndex} for Player number {playerNumber}"); 
            if (playerNumber >= MaxNumPlayers)
                throw new Exception("Invalid player index. Must increase MaxNumPlayers.");
            byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
            byte* nextPlayerPointer = playerPointer + MaxInformationSetLengthPerPlayer;
            // advance to the end of the information set
            while (*playerPointer != InformationSetTerminator)
                playerPointer += 2;
            // now record the information
            *playerPointer = followingDecisionIndex; // we must record the decision
            playerPointer++;
            *playerPointer = information;
            playerPointer++;
            *playerPointer = InformationSetTerminator; // terminator
            if (playerPointer >= nextPlayerPointer)
                throw new Exception("Internal error. Must increase size of information set.");
        }

        public byte AggregateSubdividable(byte playerNumber, byte decisionIndex, byte numOptionsPerBranch, byte numLevels)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                // advance to the end of the information set
                while (*playerPointer != InformationSetTerminator)
                    playerPointer += 2;
                playerPointer--; // spot before terminator
                byte accumulator = (byte) (*playerPointer - 1); // one less than value pointed to (not changing the pointer itself)
                byte columnValue = 1; // this is the units column
                for (byte level = 1; level < numLevels; level++)
                {
                    columnValue = (byte) (columnValue * numOptionsPerBranch); // for example, when level = 1, if numOptionsPerBranch is 10, then an action of 1 is worth 0, an action of 2 is worth 10, etc.
                    playerPointer--; // go back to decision index
                    playerPointer--; // go back to previous decision
                    accumulator = (byte) (accumulator + columnValue * (*playerPointer - 1));
                }
                return (byte) (accumulator + 1);
            }
        }

        public unsafe byte GetPlayerInformationItem(int playerNumber, byte decisionIndex)
        {
            if (!Initialized)
                Initialize();
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*playerPointer != InformationSetTerminator)
                {
                    if (*playerPointer == decisionIndex)
                    {
                        playerPointer++;
                        if (*playerPointer != RemoveItemFromInformationSet)
                            return *playerPointer;
                        else
                            playerPointer++;
                    }
                    else if (*playerPointer == InformationSetTerminator)
                        break;
                    else
                    {
                        playerPointer++;
                        playerPointer++;
                    }
                }
                return 0;
            }
        }

        public unsafe void GetPlayerInformation(int playerNumber, byte? upToDecision, byte* playerInfoBuffer)
        {
            // TODO: We're keeping the player information in a log and then converting that log each time. We should also keep a running copy, so that if we want all the player information, we can just call a faster routine. // DEBUG
            if (!Initialized)
                Initialize();
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*playerPointer != InformationSetTerminator)
                {
                    if (*playerPointer >= upToDecision)
                        break;
                    playerPointer++;
                    if (*playerPointer == RemoveItemFromInformationSet)
                        playerInfoBuffer--; // delete an item
                    else
                    {
                        *playerInfoBuffer = *playerPointer;
                        playerInfoBuffer++;
                    }
                    playerPointer++;
                }
                *playerInfoBuffer = InformationSetTerminator;
            }
        }

        public unsafe string GetPlayerInformationString(int playerNumber, byte? upToDecision)
        {
            byte* playerInfoBuffer = stackalloc byte[MaxInformationSetLengthPerPlayer];
            GetPlayerInformation(playerNumber, upToDecision, playerInfoBuffer);
            List<byte> informationSetList = ListExtensions.GetPointerAsList_255Terminated(playerInfoBuffer);
            return String.Join(",", informationSetList);
        }

        public byte CountItemsInInformationSet(byte playerNumber)
        {
            if (!Initialized)
                Initialize();
            byte b = 0;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* ptr = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*ptr != InformationSetTerminator)
                {
                    ptr++; // skip the decision code
                    if (*ptr == RemoveItemFromInformationSet)
                        b--;
                    else
                        b++;
                    ptr++; // now move past the information
                }
            }
            return b;
        }

        public void RemoveItemsInInformationSet(byte playerIndex, byte followingDecisionIndex, byte numItemsToRemove)
        {
            // This takes the approach of keeping the information set as append-only storage. That is, we add a notation that we're removing an item from the information set. 
            if (!Initialized)
                Initialize();
            for (byte b = 0; b < numItemsToRemove; b++)
            {
                AddToInformationSet(RemoveItemFromInformationSet, followingDecisionIndex, playerIndex);
                // TODO: We could make this more efficient by going to the end of the information set and then adding all the removals. Otherwise, we're going back through our list for each item we're removing.
            }
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
                    int playerNumber = *(historyPtr + i + History_PlayerNumber_Offset);
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
