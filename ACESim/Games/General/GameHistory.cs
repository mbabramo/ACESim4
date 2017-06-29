using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct GameHistory
    {
        const int MaxLength = 150;
        const byte Complete = 254;
        const byte Incomplete = 255;
        
        public byte[] History;
        public short LastIndexAddedToHistory;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;
        public byte[] InformationSets;
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
            return new ACESim.GameHistory()
            {
                History = History.ToArray(),
                LastIndexAddedToHistory = LastIndexAddedToHistory,
                InformationSets = InformationSets.ToArray(),
                LastIndexAddedToInformationSets = LastIndexAddedToInformationSets
            };
        }

        public GameHistory Initialize()
        {
            History = new byte[MaxLength];
            InformationSets = new byte[MaxLength];
            History[0] = Incomplete;
            LastIndexAddedToHistory = 0;
            LastIndexAddedToInformationSets = -1;
            return this;
        }

        public void AddToHistory(byte decisionNumber, byte playerNumber, byte action, byte numPossibleActions, List<byte> playersToInform)
        {
            short i = LastIndexAddedToHistory;
            if (History[i] == Complete)
                throw new Exception("Cannot add to history of complete game.");
            History[i + History_DecisionNumber_Offset] = decisionNumber;
            History[i + History_PlayerNumber_Offset] = playerNumber;
            History[i + History_Action_Offset] = action;
            History[i + History_NumPossibleActions_Offset] = numPossibleActions;
            History[i + History_NumPiecesOfInformation] = Incomplete; // this is just one item at end of all history items
            LastIndexAddedToHistory = (short) (i + History_NumPiecesOfInformation);
            AddToInformationSet(action, playersToInform, decisionNumber);
        }

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems()
        {
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                yield return new InformationSetHistory()
                {
                    PlayerMakingDecision = History[i + History_PlayerNumber_Offset],
                    DecisionIndex = History[i + History_DecisionNumber_Offset],
                    InformationSet = GetPlayerInformation(History[i + History_PlayerNumber_Offset], History[i + History_DecisionNumber_Offset]).ToList(),
                    ActionChosen = History[i + History_Action_Offset],
                    NumPossibleActions = History[i + History_NumPossibleActions_Offset],
                    IsTerminalAction = History[i + History_NumPiecesOfInformation] == Complete
                };
            }
        }

        public IEnumerable<byte> GetActions()
        {
            int offset = 2;
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i++)
            {
                if (i % History_NumPiecesOfInformation == offset)
                    yield return History[i];
            }
        }

        public IEnumerable<byte> GetNumPossibleActions()
        {
            int offset = 3;
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i++)
            {
                if (i % History_NumPiecesOfInformation == offset)
                    yield return History[i];
            }
        }

        public void MarkComplete()
        {
            short i = LastIndexAddedToHistory;
            if (History[i] == Complete)
                throw new Exception("Game is already complete.");
            History[i] = Complete;
        }

        public bool IsComplete()
        {
            return (History[LastIndexAddedToHistory] == Complete);
        }
        
        public void AddToInformationSet(byte information, List<byte> playersToInform, byte decisionIndexAdded)
        {
            foreach (byte playerIndex in playersToInform)
                AddToInformationSet(information, playerIndex, decisionIndexAdded);
        }

        public void AddToInformationSet(byte information, byte playerNumber, byte decisionIndexAdded)
        {
            short firstIndex = (short) (LastIndexAddedToInformationSets + 1);
            InformationSets[firstIndex + InformationSet_PlayerNumber_Offset] = playerNumber;
            InformationSets[firstIndex + InformationSet_Information_Offset] = information;
            InformationSets[firstIndex + InformationSet_DecisionIndex_Offset] = decisionIndexAdded;
            LastIndexAddedToInformationSets += InformationSet_NumPiecesOfInformation;

        }

        public IEnumerable<byte> GetPlayerInformation(int playerNumber, byte? beforeDecisionIndex = null)
        {
            if (LastIndexAddedToInformationSets < 0)
                yield break;
            for (byte i = 0; i < LastIndexAddedToInformationSets; i += InformationSet_NumPiecesOfInformation)
            {
                if (InformationSets[i] == playerNumber)
                {
                    if (beforeDecisionIndex == null || InformationSets[i + InformationSet_DecisionIndex_Offset] < beforeDecisionIndex)
                        yield return InformationSets[i + InformationSet_Information_Offset];
                    else
                        yield break;
                }
            }
        }

        /// <summary>
        /// When called on a complete game, this returns the next decision path to take. For example, if there are three decisions with three actions each, then after (1, 1, 1), it would return (1, 1, 2), then (1, 1, 3), then (1, 2). If called on (3, 3, 3), it will throw an Exception.
        /// </summary>
        public IEnumerable<byte> GetNextDecisionPath(GameDefinition gameDefinition)
        {
            if (!IsComplete())
                throw new Exception("Can get next path to try only on a completed game.");
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionWithAnotherAction = GetLastDecisionWithAnotherAction(gameDefinition);
            int d = 0;
            List<byte> decisions = new List<byte>();
            IEnumerator<byte> decisionsEnumerator = GetActions().GetEnumerator();
            while (d <= lastDecisionWithAnotherAction)
            {
                bool another = decisionsEnumerator.MoveNext();
                if (!another)
                    throw new Exception("Internal error. Expected another decision to exist.");
                if (d == lastDecisionWithAnotherAction)
                    yield return (byte)(decisionsEnumerator.Current + (byte)1); // this is the decision where we need to try the next path
                else
                    yield return decisionsEnumerator.Current; // we're still on the same path
                d++;
            }
        }

        private int? GetLastDecisionWithAnotherAction(GameDefinition gameDefinition)
        {
            int? lastDecisionWithAnotherAction = null;
            for (int i = LastIndexAddedToHistory - History_NumPiecesOfInformation; i >= 0; i -= History_NumPiecesOfInformation)
            {
                int decisionNumber = History[i + History_DecisionNumber_Offset];
                int playerNumber = History[i + History_PlayerNumber_Offset];
                int action = History[i + History_Action_Offset];
                int numPossibleActions = History[i + History_NumPossibleActions_Offset];
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
