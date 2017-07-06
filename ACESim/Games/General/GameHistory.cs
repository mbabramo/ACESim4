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
        public const int MaxInformationSetLength = 220; // MUST equal MaxInformationSetLengthPerPlayer * MaxNumPlayers. 
        public const int MaxInformationSetLengthPerPlayer = 20;
        public const int MaxNumPlayers = 10;
        public const int MaxNumActions = 20;
        const byte Complete = 254;
        const byte Incomplete = 255;

        byte NumPlayers;

        public fixed byte History[MaxHistoryLength];
        public short LastIndexAddedToHistory;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;
        public fixed byte InformationSets[MaxInformationSetLength]; // a buffer for each player, terminated by 255

        private const byte History_DecisionNumber_Offset = 0;
        private const byte History_PlayerNumber_Offset = 1;
        private const byte History_Action_Offset = 2;
        private const byte History_NumPossibleActions_Offset = 3;
        private const byte History_NumPiecesOfInformation = 4; // the total number of pieces of information above (i.e., 0, 1, 2, and 3)

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
                *(historyPtr + 0) = Incomplete;
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
                if (*(historyPtr + i) == Complete)
                    throw new Exception("Cannot add to history of complete game.");
                *(historyPtr + i + History_DecisionNumber_Offset) = decisionNumber;
                *(historyPtr + i + History_PlayerNumber_Offset) = playerNumber;
                *(historyPtr + i + History_Action_Offset) = action;
                *(historyPtr + i + History_NumPossibleActions_Offset) = numPossibleActions;
                *(historyPtr + i + History_NumPiecesOfInformation) = Incomplete; // this is just one item at end of all history items
            }
            LastIndexAddedToHistory = (short) (i + History_NumPiecesOfInformation);
            AddToInformationSet(action, playersToInform);
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

        private InformationSetHistory GetInformationSetHistory(short i)
        {
            byte playerIndex = GetHistoryIndex(i + History_PlayerNumber_Offset);
            byte decisionIndex = GetHistoryIndex(i + History_DecisionNumber_Offset);
            var informationSetHistory = new InformationSetHistory()
            {
                PlayerIndex = playerIndex,
                DecisionIndex = decisionIndex,
                ActionChosen = GetHistoryIndex(i + History_Action_Offset),
                NumPossibleActions = GetHistoryIndex(i + History_NumPossibleActions_Offset),
                IsTerminalAction = GetHistoryIndex(i + History_NumPiecesOfInformation) == Complete
            };
            GetPlayerInformation(playerIndex, informationSetHistory.InformationSet);
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
                if (*(historyPtr + i) == Complete)
                    throw new Exception("Game is already complete.");
                *(historyPtr + i) = Complete;
            }
        }

        public bool IsComplete()
        {
            fixed (byte* historyPtr = History)
                return (*(historyPtr + LastIndexAddedToHistory) == Complete);
        }
        
        public void AddToInformationSet(byte information, List<byte> playersToInform)
        {
            foreach (byte playerIndex in playersToInform)
                AddToInformationSet(information, playerIndex);
        }

        public void AddToInformationSet(byte information, byte playerNumber)
        {
            if (playerNumber >= MaxNumPlayers)
                throw new Exception("Invalid player index. Must increase MaxNumPlayers.");
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                while (*playerPointer != 255)
                    playerPointer++;
                *playerPointer = information;
                playerPointer++;
                *playerPointer = 255; // terminator
            }
        }

        public unsafe void GetPlayerInformation(int playerNumber, byte* playerInfoBuffer)
        {
            fixed (byte* informationSetsPtr = InformationSets)
            {
                byte* playerPointer = informationSetsPtr + playerNumber * MaxInformationSetLengthPerPlayer;
                bool done = false;
                do
                {
                    *playerInfoBuffer = *playerPointer;
                    done = *playerPointer == 255;
                    playerInfoBuffer++;
                    playerPointer++;
                } while (!done);
            }
        }

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
    }
}
