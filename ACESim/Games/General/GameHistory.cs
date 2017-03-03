﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class GameHistory
    {
        const int MaxLength = 150;
        const byte Complete = 254;
        const byte Incomplete = 255;
        
        public byte[] History = new byte[MaxLength];
        public short LastIndexAddedToHistory = 0;
        public int NumberDecisions => (LastIndexAddedToHistory - 1) / 4;
        public byte[] InformationSets = new byte[MaxLength];
        public short LastIndexAddedToInformationSets = -1;

        public GameHistory()
        {
            Initialize();
        }

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

        public void Initialize()
        {
            History[0] = Incomplete;
            LastIndexAddedToHistory = 0;
            LastIndexAddedToInformationSets = -1;
        }

        public void AddToHistory(byte decisionNumber, byte playerNumber, byte action, byte numPossibleActions, List<byte> playersToInform)
        {
            short i = LastIndexAddedToHistory;
            if (i == 2 && decisionNumber == 6)
            {
                var DEBUG = 0;
            }
            if (History[2] == 1 && History[6] == 1 && History[10] == 2 && History[14] == 1 && decisionNumber == 6 && GetActions().Count() != 6)
            {
                var DEBUG = 0;
            }
            if (History[i] == Complete)
                throw new Exception("Cannot add to history of complete game.");
            History[i] = decisionNumber;
            History[i + 1] = playerNumber;
            History[i + 2] = action;
            History[i + 3] = numPossibleActions;
            History[i + 4] = Incomplete;
            LastIndexAddedToHistory = (short) (i + 4);
            AddToInformationSet(action, playersToInform, decisionNumber);
        }

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems()
        {
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += 4)
            {
                yield return new InformationSetHistory()
                {
                    PlayerMakingDecision = History[i + 1],
                    DecisionIndex = History[i],
                    InformationSet = GetPlayerInformation(History[i + 1], History[i]).ToList(),
                    ActionChosen = History[i + 2],
                    NumPossibleActions = History[i + 3],
                    IsTerminalAction = History[i + 4] == Complete
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
                if (i % 4 == offset)
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
                if (i % 4 == offset)
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
            InformationSets[++LastIndexAddedToInformationSets] = playerNumber;
            InformationSets[++LastIndexAddedToInformationSets] = information;
            InformationSets[++LastIndexAddedToInformationSets] = decisionIndexAdded;
        }

        public IEnumerable<byte> GetPlayerInformation(int playerNumber, byte? beforeDecisionIndex = null)
        {
            if (LastIndexAddedToInformationSets < 0)
                yield break;
            for (byte i = 0; i < LastIndexAddedToInformationSets; i += 3)
            {
                if (InformationSets[i] == playerNumber)
                {
                    if (beforeDecisionIndex == null || InformationSets[i + 2] < beforeDecisionIndex)
                        yield return InformationSets[i + 1];
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
            for (int i = LastIndexAddedToHistory - 4; i >= 0; i -= 4)
            {
                int decisionNumber = History[i];
                int playerNumber = History[i + 1];
                int action = History[i + 2];
                int numPossibleActions = History[i + 3];
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
