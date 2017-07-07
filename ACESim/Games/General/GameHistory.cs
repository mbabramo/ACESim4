using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public unsafe struct GameHistory
    {
        public const int MaxHistoryLength = 100;
        public const int MaxInformationSetLength = 500; // MUST equal MaxInformationSetLengthPerPlayer * MaxNumPlayers. 
        public const int MaxInformationSetLengthPerPlayer = 50; // DEBUG -- too high
        public const int MaxNumPlayers = 10;
        public const int MaxNumActions = 20;
        const byte HistoryComplete = 254;
        const byte HistoryIncomplete = 255;

        byte NumPlayers;

        public fixed byte History[MaxHistoryLength];
        public short LastIndexAddedToHistory;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;

        // Information set structure. We have an information set buffer for each player. We need to be able to remove information from the information set for a player, but still to remember that it was there as of a particular point in time, so that we can figure out what the information set was as of a particular decision. (This is needed for reconstructing the game play.) We thus store information in pairs. The first byte consists of the decision byte code after which we are making changes. The second byte either consists of an item to add, or 254, indicating that we are removing an item from the information set. All of this is internal. When we get the information set, we get it as of a certain point, and thus we skip decision byte codes and automatically process deletions. 
        public fixed byte InformationSets[MaxInformationSetLength]; // a buffer for each player, terminated by 255.
        const byte RemoveItemFromInformationSet = 254;

        private const byte History_DecisionNumber_Offset = 0;
        private const byte History_PlayerNumber_Offset = 1;
        private const byte History_Action_Offset = 2;
        private const byte History_NumPossibleActions_Offset = 3;
        private const byte History_NumPiecesOfInformation = 4; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public GameHistory DeepCopy()
        {
            // This works only because we have a fixed buffer. If we had a reference type, we would get a shallow copy.
            GameHistory b = this;
            return b;
        }

        public GameHistory Initialize(byte numPlayers)
        {
            NumPlayers = numPlayers;
            fixed (byte* historyPtr = History)
                *(historyPtr + 0) = HistoryIncomplete;
            fixed (byte* ptr = InformationSets)
                for (int p = 0; p < NumPlayers; p++)
                    *(ptr + MaxInformationSetLengthPerPlayer * p) = 255;
            LastIndexAddedToHistory = 0;
            return this;
        }

        public void AddToHistory(byte decisionNumber, byte playerNumber, byte action, byte numPossibleActions, List<byte> playersToInform)
        {
            short i = LastIndexAddedToHistory;
            fixed (byte* historyPtr = History)
            {
                if (*(historyPtr + i) == HistoryComplete)
                    throw new Exception("Cannot add to history of complete game.");
                *(historyPtr + i + History_DecisionNumber_Offset) = decisionNumber;
                *(historyPtr + i + History_PlayerNumber_Offset) = playerNumber;
                *(historyPtr + i + History_Action_Offset) = action;
                *(historyPtr + i + History_NumPossibleActions_Offset) = numPossibleActions;
                *(historyPtr + i + History_NumPiecesOfInformation) = HistoryIncomplete; // this is just one item at end of all history items
            }
            LastIndexAddedToHistory = (short) (i + History_NumPiecesOfInformation);
            AddToInformationSet(action, decisionNumber, playersToInform);
        }

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems()
        {
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                yield return GetInformationSetHistory(i);
            }
        }

        private InformationSetHistory GetInformationSetHistory(short index)
        {
            byte playerIndex = GetHistoryIndex(index + History_PlayerNumber_Offset);
            byte decisionIndex = GetHistoryIndex(index + History_DecisionNumber_Offset);
            var informationSetHistory = new InformationSetHistory()
            {
                PlayerIndex = playerIndex,
                DecisionIndex = decisionIndex,
                ActionChosen = GetHistoryIndex(index + History_Action_Offset),
                NumPossibleActions = GetHistoryIndex(index + History_NumPossibleActions_Offset),
                IsTerminalAction = GetHistoryIndex(index + History_NumPiecesOfInformation) == HistoryComplete
            };
            GetPlayerInformation(playerIndex, decisionIndex, informationSetHistory.InformationSet);
            return informationSetHistory;
        }

        public unsafe void GetActions(byte* actions)
        {
            GetItems(History_Action_Offset, actions);
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
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                    items[d++] = GetHistoryIndex(i + offset);
            items[d] = 255;
        }

        private byte GetHistoryIndex(int i)
        {
            // The following is useful in iterator blocks, which cannot directly contain unsafe code.
            fixed (byte* historyPtr = History)
                return *(historyPtr + i);
        }

        public void MarkComplete()
        {
            short i = LastIndexAddedToHistory;
            fixed (byte* historyPtr = History)
            {
                if (*(historyPtr + i) == HistoryComplete)
                    throw new Exception("Game is already complete.");
                *(historyPtr + i) = HistoryComplete;
            }
        }

        public bool IsComplete()
        {
            fixed (byte* historyPtr = History)
                return (*(historyPtr + LastIndexAddedToHistory) == HistoryComplete);
        }

        #region Player information sets

        public void AddToInformationSet(byte information, byte followingDecision, List<byte> playersToInform)
        {
            fixed (byte* informationSetsPtr = InformationSets)
                foreach (byte playerIndex in playersToInform)
                    AddToInformationSet(information, followingDecision, playerIndex, informationSetsPtr);
        }

        public void AddToInformationSet(byte information, byte followingDecision, byte playerIndex)
        {
            fixed (byte* informationSetsPtr = InformationSets)
                AddToInformationSet(information, followingDecision, playerIndex, informationSetsPtr);
        }

        private void AddToInformationSet(byte information, byte followingDecision, byte playerNumber, byte* informationSetsPtr)
        {
            //Debug.WriteLine($"Adding information {information} following decision {followingDecision} for Player number {playerNumber}");
            if (playerNumber >= MaxNumPlayers)
                throw new Exception("Invalid player index. Must increase MaxNumPlayers.");
            byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
            var DEBUG = informationSetsPtr;
            // advance to the end of the information set
            while (*playerPointer != 255)
                playerPointer += 2;
            // now record the information
            *playerPointer = followingDecision; // we must record the decision
            playerPointer++;
            *playerPointer = information;
            playerPointer++;
            *playerPointer = 255; // terminator
        }

        public unsafe void GetPlayerInformation(int playerNumber, byte? upToDecision, byte* playerInfoBuffer)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*playerPointer != 255)
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
                *playerInfoBuffer = 255;
            }
        }

        public byte CountItemsInInformationSet(byte playerNumber)
        {
            byte b = 0;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* ptr = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*ptr != 255)
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

        public void ReduceItemsInInformationSet(byte playerIndex, byte followingDecision, byte numItems)
        {
            for (byte b = 0; b < numItems; b++)
            {
                AddToInformationSet(RemoveItemFromInformationSet, followingDecision, playerIndex);
                // We could make this more efficient by going to the end of the information ste and then adding all the removals. 
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
            if (!IsComplete())
                throw new Exception("Can get next path to try only on a completed game.");
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionInNextPath = GetLastDecisionWithAnotherAction(gameDefinition) ?? -1; // negative number symbolizes that there is nothing else to do
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            byte* currentActions = stackalloc byte[MaxNumActions];
            GetActions(currentActions);
            while (indexInNewDecisionPath <= lastDecisionInNextPath)
            {
                bool another = currentActions[indexInCurrentActions] != 255;
                if (!another)
                    throw new Exception("Internal error. Expected another decision to exist.");
                if (indexInNewDecisionPath == lastDecisionInNextPath)
                    nextDecisionPath[indexInNewDecisionPath] = (byte)(currentActions[indexInCurrentActions] + (byte)1); // this is the decision where we need to try the next path
                else
                    nextDecisionPath[indexInNewDecisionPath] = currentActions[indexInCurrentActions]; // we're still on the same path
                
                indexInCurrentActions++;
                indexInNewDecisionPath++;
            }
            nextDecisionPath[indexInNewDecisionPath] = 255;
        }

        private int? GetLastDecisionWithAnotherAction(GameDefinition gameDefinition)
        {
            int? lastDecisionWithAnotherAction = null;

            fixed (byte* historyPtr = History)
                for (int i = LastIndexAddedToHistory - History_NumPiecesOfInformation; i >= 0; i -= History_NumPiecesOfInformation)
                {
                    int decisionNumber = *(historyPtr + i + History_DecisionNumber_Offset);
                    int playerNumber = *(historyPtr + i + History_PlayerNumber_Offset);
                    int action = *(historyPtr + i + History_Action_Offset);
                    int numPossibleActions = *(historyPtr + i + History_NumPossibleActions_Offset);
                    if (gameDefinition.DecisionsExecutionOrder[decisionNumber].NumPossibleActions > action)
                    {
                        lastDecisionWithAnotherAction = decisionNumber;
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
