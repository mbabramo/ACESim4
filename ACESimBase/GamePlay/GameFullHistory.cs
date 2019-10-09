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

    public readonly ref struct GameFullHistory
    {
        public const byte HistoryComplete = 254;
        public const byte HistoryTerminator = 255;

        public const byte History_DecisionByteCode_Offset = 0;
        public const byte History_DecisionIndex_Offset = 1; // the decision index reflects the order of the decision in the decisions list. A decision with the same byte code could correspond to multiple decision indices.
        public const byte History_PlayerNumber_Offset = 2;
        public const byte History_Action_Offset = 3;
        public const byte History_NumPossibleActions_Offset = 4;
        public const byte History_NumPiecesOfInformation = 5; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public const int MaxNumActions = 100;
        public const int MaxHistoryLength = 300;

        public readonly Span<byte> History; // length is MaxHistoryLength
        public readonly short LastIndexAddedToHistory;

        public GameFullHistory(Span<byte> history, short lastIndexAddedToHistory)
        {
            History = history;
            LastIndexAddedToHistory = lastIndexAddedToHistory;
        }

        public GameFullHistoryStorable DeepCopyToStorable()
        {
            var result = new GameFullHistoryStorable(new byte[History.Length], LastIndexAddedToHistory);
            for (int i = 0; i < History.Length; i++)
                result.History[i] = History[i];
            return result;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Use the AddValue method to specify serialized values.
            byte[] history = new byte[MaxHistoryLength];
            for (int b = 0; b < MaxHistoryLength; b++)
                history[b] = History[b];

            info.AddValue("history", history, typeof(byte[]));
            info.AddValue("LastIndexAddedToHistory", LastIndexAddedToHistory, typeof(short));

        }

        // The special constructor is used to deserialize values.
        public GameFullHistory(SerializationInfo info, StreamingContext context)
        {
            History = new byte[MaxHistoryLength]; // rarely used so allocation not an issue
            byte[] history = (byte[])info.GetValue("history", typeof(byte[]));
            for (int b = 0; b < MaxHistoryLength; b++)
                History[b] = history[b];
            LastIndexAddedToHistory = (short)info.GetValue("LastIndexAddedToHistory", typeof(short));
        }



        /// <summary>
        /// Gets an earlier version of the GameHistory, including everything up to but not including the specified decision. 
        /// </summary>
        /// <returns></returns>       
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte? LastDecisionIndex()
        {
            short i = LastIndexAddedToHistory;
            if (i == 0)
                return null; // no decisions processed yet
            return History[i - History_NumPiecesOfInformation + History_DecisionIndex_Offset];
        }

        public unsafe void GetActions(byte* actions)
        {
            GetItems(History_Action_Offset, actions);
        }

        public unsafe void GetActionsWithBlanksForSkippedDecisions(byte* actions)
        {
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


        public string GetActionsAsListString()
        {
            return String.Join(",", GetActionsAsList());
        }

        public List<byte> GetActionsAsList()
        {
            Span<byte> actions = stackalloc byte[MaxNumActions];
            GetActions(actions);
            return ListExtensions.GetPointerAsList_255Terminated(actions);
        }

        public unsafe void GetActions(Span<byte> actions)
        {
            GetItems(History_Action_Offset, actions);
        }

        private unsafe void GetItems(int offset, Span<byte> items)
        {
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                    items[d++] = GetHistoryIndex(i + offset);
            items[d] = HistoryTerminator;
        }

        private unsafe void GetItems(int offset, byte* items)
        {
            int d = 0;
            if (LastIndexAddedToHistory != 0)
                for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
                    items[d++] = GetHistoryIndex(i + offset);
            items[d] = HistoryTerminator;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetHistoryIndex(int i)
        {
            // The following is useful in iterator blocks, which cannot directly contain unsafe code.
            return History[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsComplete()
        {
            return History[LastIndexAddedToHistory] == HistoryComplete;
        }

        public short GetInformationSetHistoryItems_Count(GameProgress gameProgress)
        {
            if (LastIndexAddedToHistory == 0)
                return 0;
            short overallIndex = 0;
            for (short i = 0; i < LastIndexAddedToHistory; i += History_NumPiecesOfInformation)
            {
                overallIndex++;
            }
            return overallIndex;
        }

        

        public InformationSetHistory GetInformationSetHistory_OverallIndex(short index, GameProgress gameProgress)
        {
            return GetInformationSetHistory((short) (index * History_NumPiecesOfInformation), gameProgress);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private InformationSetHistory GetInformationSetHistory(short index, GameProgress gameProgress)
        {
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
            int? lastDecisionWithAnotherAction = null;

            for (int i = LastIndexAddedToHistory - History_NumPiecesOfInformation; i >= 0; i -= History_NumPiecesOfInformation)
            {
                int decisionByteCode = History[i + History_DecisionByteCode_Offset];
                int decisionIndex = History[i + History_DecisionIndex_Offset];
                int playerIndex = History[i + History_PlayerNumber_Offset];
                int action = History[i + History_Action_Offset];
                int numPossibleActions = History[i + History_NumPossibleActions_Offset];
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
