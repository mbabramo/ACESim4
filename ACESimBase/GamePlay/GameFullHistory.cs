#define SAFETYCHECKS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;
using ACESimBase.Util;

namespace ACESim
{
    public unsafe struct GameFullHistory
    {

        const byte HistoryComplete = 254;
        const byte HistoryTerminator = 255;

        private const byte History_DecisionByteCode_Offset = 0;
        private const byte History_DecisionIndex_Offset = 1; // the decision index reflects the order of the decision in the decisions list. A decision with the same byte code could correspond to multiple decision indices.
        private const byte History_PlayerNumber_Offset = 2;
        private const byte History_Action_Offset = 3;
        private const byte History_NumPossibleActions_Offset = 4;
        private const byte History_NumPiecesOfInformation = 5; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public const int MaxNumActions = 100;
        public const int MaxHistoryLength = 300;
        public fixed byte History[MaxHistoryLength];
        public short LastIndexAddedToHistory;

        public bool Initialized;

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] history = new byte[MaxHistoryLength];
            fixed (byte* ptr = History)
                for (int b = 0; b < MaxHistoryLength; b++)
                    history[b] = *(ptr + b);

            info.AddValue("history", history, typeof(byte[]));
            info.AddValue("LastIndexAddedToHistory", LastIndexAddedToHistory, typeof(short));

        }

        // The special constructor is used to deserialize values.
        public GameFullHistory(SerializationInfo info, StreamingContext context)
        {
            byte[] history = (byte[])info.GetValue("history", typeof(byte[]));
            fixed (byte* ptr = History)
                for (int b = 0; b < MaxHistoryLength; b++)
                    *(ptr + b) = history[b];
            Initialized = true;
            LastIndexAddedToHistory = (short)info.GetValue("LastIndexAddedToHistory", typeof(short));
        }

        public void Initialize()
        {
            fixed (byte* historyPtr = History)
                *(historyPtr + 0) = HistoryTerminator;
            LastIndexAddedToHistory = 0;
            Initialized = true;
        }

        public void AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions, bool skipAddToHistory)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            if (!skipAddToHistory)
            {
                short i = LastIndexAddedToHistory;
                fixed (byte* historyPtr = History)
                {
#if (SAFETYCHECKS)
                    if (*(historyPtr + i) == HistoryComplete)
                        ThrowHelper.Throw("Cannot add to history of complete game.");
#endif
                    *(historyPtr + i + History_DecisionByteCode_Offset) = decisionByteCode;
                    *(historyPtr + i + History_DecisionIndex_Offset) = decisionIndex;
                    *(historyPtr + i + History_PlayerNumber_Offset) = playerIndex;
                    *(historyPtr + i + History_Action_Offset) = action;
                    *(historyPtr + i + History_NumPossibleActions_Offset) = numPossibleActions;
                    *(historyPtr + i + History_NumPiecesOfInformation) = HistoryTerminator; // this is just one item at end of all history items
                }
                LastIndexAddedToHistory = (short)(i + History_NumPiecesOfInformation);

#if (SAFETYCHECKS)
                if (LastIndexAddedToHistory >= MaxHistoryLength - 2) // must account for terminator characters
                   ThrowHelper.Throw("Internal error. Must increase history length.");
#endif
            }
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"Actions so far: {GetActionsAsListString()}");
        }



        /// <summary>
        /// Gets an earlier version of the GameHistory, including everything up to but not including the specified decision. 
        /// </summary>
        /// <returns></returns>        
        public byte? LastDecisionIndex()
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
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
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            GetItems(History_Action_Offset, actions);
        }

        public unsafe void GetActionsWithBlanksForSkippedDecisions(byte* actions)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                {
                    byte decisionIndex = GetHistoryIndex(i + History_DecisionIndex_Offset);
                    while (d != decisionIndex)
                        actions[d++] = 0;
                    byte historyIndex = GetHistoryIndex(i + History_Action_Offset);
                    actions[d++] = historyIndex;
                }
            actions[d] = HistoryTerminator;
        }

        public List<byte> GetActionsAsList()
        {
            byte* actions = stackalloc byte[MaxNumActions];
            GetActions(actions);
            return ListExtensions.GetPointerAsList_255Terminated(actions);
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
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
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
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            short i = LastIndexAddedToHistory;
            fixed (byte* historyPtr = History)
            {
#if (SAFETYCHECKS)
                if (*(historyPtr + i) == HistoryComplete)
                    ThrowHelper.Throw("Game is already complete.");
#endif
                *(historyPtr + i) = HistoryComplete;
                *(historyPtr + i + 1) = HistoryTerminator;
            }
        }

        public bool IsComplete()
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            fixed (byte* historyPtr = History)
                return (*(historyPtr + LastIndexAddedToHistory) == HistoryComplete);
        }

        public IEnumerable<InformationSetHistory> GetInformationSetHistoryItems(GameProgress gameProgress)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            if (LastIndexAddedToHistory == 0)
                yield break;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                yield return GetInformationSetHistory(i, gameProgress);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private InformationSetHistory GetInformationSetHistory(short index, GameProgress gameProgress)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
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

#region Decision paths

        /// <summary>
        /// When called on a complete game, this returns the next decision path to take. 
        /// For example, if there are three decisions with three actions each, then after (1, 1, 1), it would return (1, 1, 2), then (1, 1, 3), then (1, 2). 
        /// Note that in this example there may be further decisions after (1, 2). 
        /// If called on (3, 3, 3), it will throw an Exception.
        /// </summary>
        public unsafe void GetNextDecisionPath(GameDefinition gameDefinition, byte* nextDecisionPath)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
            if (!IsComplete())
                ThrowHelper.Throw("Can get next path to try only on a completed game.");
#endif
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionInNextPath = GetIndexOfLastDecisionWithAnotherAction(gameDefinition) ?? -1; // negative number symbolizes that there is nothing else to do
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            byte* currentActions = stackalloc byte[GameFullHistory.MaxNumActions];
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
#if (SAFETYCHECKS)
                    bool another = currentAction != HistoryTerminator;
                    if (!another)
                        ThrowHelper.Throw("Internal error. Expected another decision to exist.");
#endif
                    if (indexInNewDecisionPath == lastDecisionInNextPath)
                        nextDecisionPath[indexInNewDecisionPath] =
                            (byte)(currentAction +
                                   (byte)1); // this is the decision where we need to try the next path
                    else
                        nextDecisionPath[indexInNewDecisionPath] = currentAction; // we're still on the same path

                    indexInCurrentActions++;
                    indexInNewDecisionPath++;
                }
            }
            nextDecisionPath[indexInNewDecisionPath] = HistoryTerminator;
            //var nextDecisionPath2 = Util.ListExtensions.GetPointerAsList_255Terminated(nextDecisionPath);
        }

        private int? GetIndexOfLastDecisionWithAnotherAction(GameDefinition gameDefinition)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
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
#if (SAFETYCHECKS)
            if (lastDecisionWithAnotherAction == null)
                ThrowHelper.Throw("No more decision paths to take."); // indicates that there are no more decisions to take
#endif
            return lastDecisionWithAnotherAction;
        }

#endregion
    }
}
