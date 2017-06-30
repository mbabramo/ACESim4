using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public unsafe struct GameHistory
    {
        public const int MaxLength = 150;
        public const int MaxNumActions = 20;
        const byte Complete = 254;
        const byte Incomplete = 255;
        
        public fixed byte History[MaxLength];
        public short LastIndexAddedToHistory;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;
        public fixed byte InformationSets[MaxLength];
        public short LastIndexAddedToInformationSets;

        private const byte History_DecisionNumber_Offset = 0;
        private const byte History_PlayerNumber_Offset = 1;
        private const byte History_Action_Offset = 2;
        private const byte History_NumPossibleActions_Offset = 3;
        private const byte History_NumPiecesOfInformation = 4;

        private const byte InformationSet_PlayerNumber_Offset = 0;
        private const byte InformationSet_Information_Offset = 1;
        private const byte InformationSet_DecisionIndex_Offset = 2;
        private const byte InformationSet_NumPiecesOfInformation = 3;

        public GameHistory DeepCopy()
        {
            // This works only because we have a fixed buffer. If we had a reference type, we would get a shallow copy.
            GameHistory b = this;
            return b;
        }

        public GameHistory Initialize()
        {
            fixed (byte* historyPtr = History)
                *(historyPtr + 0) = Incomplete;
            LastIndexAddedToHistory = 0;
            LastIndexAddedToInformationSets = -1;
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
            AddToInformationSet(action, playersToInform, decisionNumber);
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

        public InformationSetHistory GetInformationSetHistory(short i)
        {
            var informationSetHistory = new InformationSetHistory()
            {
                PlayerMakingDecision = GetHistoryIndex(i + History_PlayerNumber_Offset),
                DecisionIndex = GetHistoryIndex(i + History_DecisionNumber_Offset),
                ActionChosen = GetHistoryIndex(i + History_Action_Offset),
                NumPossibleActions = GetHistoryIndex(i + History_NumPossibleActions_Offset),
                IsTerminalAction = GetHistoryIndex(i + History_NumPiecesOfInformation) == Complete
            };
            GetPlayerInformation(GetHistoryIndex(i + History_PlayerNumber_Offset), GetHistoryIndex(i + History_DecisionNumber_Offset), informationSetHistory.InformationSet);
            return informationSetHistory;
        }

        public unsafe void GetActions(byte* actions)
        {
            GetItems(History_Action_Offset, actions);
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
        
        public void AddToInformationSet(byte information, List<byte> playersToInform, byte decisionIndexAdded)
        {
            foreach (byte playerIndex in playersToInform)
                AddToInformationSet(information, playerIndex, decisionIndexAdded);
        }

        public void AddToInformationSet(byte information, byte playerNumber, byte decisionIndexAdded)
        {
            short firstIndex = (short) (LastIndexAddedToInformationSets + 1);
            fixed (byte* informationSetsPtr = InformationSets)
            {
                *(informationSetsPtr + firstIndex + InformationSet_PlayerNumber_Offset) = playerNumber;
                *(informationSetsPtr + firstIndex + InformationSet_Information_Offset) = information;
                *(informationSetsPtr + firstIndex + InformationSet_DecisionIndex_Offset) = decisionIndexAdded;
            }
            LastIndexAddedToInformationSets += InformationSet_NumPiecesOfInformation;

        }

        public unsafe void GetPlayerInformation(int playerNumber, byte? beforeDecisionIndex, byte* playerInfoBuffer)
        {
            int d = 0;
            if (LastIndexAddedToInformationSets > 0)
                for (byte i = 0; i < LastIndexAddedToInformationSets; i += InformationSet_NumPiecesOfInformation)
                {
                    (byte playerNumberInInformationSet, byte decisionIndex, byte informationIndex) = GetInformationSetsInfo(i);

                    if (playerNumberInInformationSet == playerNumber)
                    {
                        if (beforeDecisionIndex == null || decisionIndex < beforeDecisionIndex)
                            playerInfoBuffer[d++] = informationIndex;
                        else
                            break;
                    }
                }
            playerInfoBuffer[d] = 255;
        }

        public (byte playerNumberInInformationSet, byte decisionIndex, byte informationIndex) GetInformationSetsInfo(int i)
        {
            byte playerNumberInInformationSet;
            byte decisionIndex;
            byte informationIndex;
            fixed (byte* informationSetsPtr = InformationSets)
            {
                playerNumberInInformationSet = *(informationSetsPtr + i);
                decisionIndex = *(informationSetsPtr + i + InformationSet_DecisionIndex_Offset);
                informationIndex = *(informationSetsPtr + i + InformationSet_Information_Offset);
            }
            return (playerNumberInInformationSet, decisionIndex, informationIndex);
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
            int? lastDecisionWithAnotherAction = GetLastDecisionWithAnotherAction(gameDefinition);
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            byte* currentActions = stackalloc byte[MaxNumActions];
            GetActions(currentActions);
            while (indexInNewDecisionPath <= lastDecisionWithAnotherAction)
            {
                bool another = currentActions[indexInCurrentActions] != 255;
                if (!another)
                    throw new Exception("Internal error. Expected another decision to exist.");
                if (indexInNewDecisionPath == lastDecisionWithAnotherAction)
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
