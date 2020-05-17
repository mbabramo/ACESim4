#define SAFETYCHECKS


using ACESim.Util;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ACESim
{
    public readonly struct GameFullHistory
    {
        public const byte HistoryComplete = 254;
        public const byte HistoryTerminator = 255;

        public const byte History_DecisionByteCode_Offset = 0;
        public const byte History_DecisionIndex_Offset = 1; // the decision index reflects the order of the decision in the decisions list. A decision with the same byte code could correspond to multiple decision indices.
        public const byte History_PlayerNumber_Offset = 2;
        public const byte History_Action_Offset = 3;
        public const byte History_NumPossibleActions_Offset = 4;
        public const byte History_NumPiecesOfInformation = 5; // the total number of pieces of information above, so that we know how much to skip (i.e., 0, 1, 2, and 3)

        public const int MaxHistoryLength = GameHistory.MaxNumActions * History_NumPiecesOfInformation;

        public readonly byte[] History; // length is MaxHistoryLength;
        public readonly short NextIndexToAddToHistory;

        public GameFullHistory(byte[] history, short lastIndexAddedToHistory)
        {
            History = history;
            NextIndexToAddToHistory = lastIndexAddedToHistory;
        }

        public GameFullHistory DeepCopy()
        {
            var history = new byte[MaxHistoryLength];
            for (int i = 0; i < MaxHistoryLength; i++)
                history[i] = History[i];
            return new GameFullHistory(history, NextIndexToAddToHistory);
        }

        public GameFullHistory ShallowCopy()
        {
            var result = new GameFullHistory(History, NextIndexToAddToHistory);
            return result;
        }

        public static GameFullHistory Initialize()
        {
            var history = new byte[MaxHistoryLength];
            history[0] = HistoryTerminator;
            const short lastIndexAddedToHistory = 0;
            return new GameFullHistory(history, lastIndexAddedToHistory);
        }

        public GameFullHistory AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions)
        {
            var history = History;
            short nextIndexToAddToHistory = NextIndexToAddToHistory;
            short i = nextIndexToAddToHistory;
#if (SAFETYCHECKS)
            if (history[i] == HistoryComplete)
                ThrowHelper.Throw("Cannot add to history of complete game.");
#endif
            history[i + History_DecisionByteCode_Offset] = decisionByteCode;
            history[i + History_DecisionIndex_Offset] = decisionIndex;
            history[i + History_PlayerNumber_Offset] = playerIndex;
            history[i + History_Action_Offset] = action;
            history[i + History_NumPossibleActions_Offset] = numPossibleActions;
            history[i + History_NumPiecesOfInformation] = HistoryTerminator; // this is just one item at end of all history items
            nextIndexToAddToHistory = (short)(i + History_NumPiecesOfInformation);

#if (SAFETYCHECKS)
            if (nextIndexToAddToHistory >= MaxHistoryLength - 2) // must account for terminator characters
                ThrowHelper.Throw("Internal error. Must increase history length.");
#endif
            var result = new GameFullHistory(history, nextIndexToAddToHistory);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"Actions so far: {result.GetActionsAsListString()}");
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkComplete()
        {
            // NOTE: Because this doesn't change LastIndexAddedToHistory, we just change the History bytespan, and thus we don't need to return a new GameFullHistoryStorable.
            short i = NextIndexToAddToHistory;
#if (SAFETYCHECKS)
            if (History[i] == HistoryComplete)
                ThrowHelper.Throw("Game is already complete.");
#endif
            History[i] = HistoryComplete;
            History[i + 1] = HistoryTerminator;
        }

        public void MarkIncomplete()
        {
            History[NextIndexToAddToHistory] = HistoryTerminator; // overwrite HistoryComplete
        }


        public string GetInformationSetHistoryItemsString(GameProgress gameProgress) => String.Join(",", GetInformationSetHistoryItemsStrings(gameProgress));

        public IEnumerable<string> GetInformationSetHistoryItemsStrings(GameProgress gameProgress)
        {
            short numItems = GetInformationSetHistoryItems_Count(gameProgress);
            for (short i = 0; i < numItems; i++)
            {
                string s = GetInformationSetHistory_OverallIndex(i, gameProgress).ToString();
                yield return s;
            }
        }


        // NOTE: InformationSetHistory is ref struct, so we can't enumerate it directly. We can enumerate the indices, and the caller can then
        // access each InformationSetHistory one at a time.

        public IEnumerable<short> GetInformationSetHistoryItems_OverallIndices(GameProgress gameProgress)
        {
            if (NextIndexToAddToHistory == 0)
                yield break;
            short overallIndex = 0;
            short piecesOfInfo = History_NumPiecesOfInformation;
            for (short i = 0; i < NextIndexToAddToHistory; i += piecesOfInfo)
            {
                yield return overallIndex++;
            }
        }

        public List<byte> GetDecisionIndicesCompleted(GameProgress gameProgress)
        {
            List<byte> decisionIndicesCompleted = new List<byte>();
            if (NextIndexToAddToHistory == 0)
                return decisionIndicesCompleted;
            short piecesOfInfo = History_NumPiecesOfInformation;
            for (short i = 0; i < NextIndexToAddToHistory; i += piecesOfInfo)
            {
                decisionIndicesCompleted.Add(History[i + History_DecisionIndex_Offset]);
            }
            return decisionIndicesCompleted;
        }


        /// <summary>
        /// Gets an earlier version of the GameHistory, including everything up to but not including the specified decision. 
        /// </summary>
        /// <returns></returns>       
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte? LastDecisionIndex()
        {
            short i = NextIndexToAddToHistory;
            if (i == 0)
                return null; // no decisions processed yet
            return History[i - History_NumPiecesOfInformation + History_DecisionIndex_Offset];
        }

        public void GetActionsWithBlanksForSkippedDecisions(Span<byte> actions)
        {
            int d = 0;
            if (NextIndexToAddToHistory != 0)
                for (short i = 0; i < NextIndexToAddToHistory; i += History_NumPiecesOfInformation)
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
            Span<byte> actions = stackalloc byte[GameHistory.MaxNumActions];
            GetActions(actions);
            return ListExtensions.GetSpan255TerminatedAsList(actions);
        }

        public void GetActions(Span<byte> actions)
        {
            GetItems(History_Action_Offset, actions);
        }

        private void GetItems(int offset, Span<byte> items)
        {
            int d = 0;
            if (NextIndexToAddToHistory != 0)
                for (short i = 0; i < NextIndexToAddToHistory; i += History_NumPiecesOfInformation)
                    items[d++] = GetHistoryIndex(i + offset);
            items[d] = HistoryTerminator;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte GetHistoryIndex(int i)
        {
            return History[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsComplete()
        {
            return History[NextIndexToAddToHistory] == HistoryComplete;
        }

        public short GetInformationSetHistoryItems_Count(GameProgress gameProgress)
        {
            if (NextIndexToAddToHistory == 0)
                return 0;
            short overallIndex = 0;
            for (short i = 0; i < NextIndexToAddToHistory; i += History_NumPiecesOfInformation)
            {
                overallIndex++;
            }
            return overallIndex;
        }



        public InformationSetHistory GetInformationSetHistory_OverallIndex(short index, GameProgress gameProgress)
        {
            return GetInformationSetHistory((short)(index * History_NumPiecesOfInformation), gameProgress);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private InformationSetHistory GetInformationSetHistory(short index, GameProgress gameProgress)
        {
            byte playerIndex = GetHistoryIndex(index + History_PlayerNumber_Offset);
            byte decisionByteCode = GetHistoryIndex(index + History_DecisionByteCode_Offset);
            byte decisionIndex = GetHistoryIndex(index + History_DecisionIndex_Offset);
            Span<byte> informationSetForPlayer = new byte[InformationSetLog.MaxInformationSetLoggingLengthPerFullPlayer];
            byte actionChosen = GetHistoryIndex(index + History_Action_Offset);
            byte numPossibleActions = GetHistoryIndex(index + History_NumPossibleActions_Offset);
            bool isTerminalAction = GetHistoryIndex(index + History_NumPiecesOfInformation) == HistoryComplete;
            var informationSetHistory = new InformationSetHistory(informationSetForPlayer, playerIndex, decisionByteCode, decisionIndex, actionChosen, numPossibleActions, isTerminalAction);
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
        public void GetNextDecisionPath(GameDefinition gameDefinition, Span<byte> nextDecisionPath)
        {
#if (SAFETYCHECKS)
            if (!IsComplete())
                ThrowHelper.Throw("Can get next path to try only on a completed game.");
#endif
            // We need to find the last decision made where there was another action that could have been taken.
            int? lastDecisionInNextPath = GetIndexOfLastDecisionWithAnotherAction(gameDefinition) ?? -1; // negative number symbolizes that there is nothing else to do
            int indexInNewDecisionPath = 0, indexInCurrentActions = 0;
            Span<byte> currentActions = stackalloc byte[GameHistory.MaxNumActions];
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

            for (int i = NextIndexToAddToHistory - History_NumPiecesOfInformation; i >= 0; i -= History_NumPiecesOfInformation)
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
