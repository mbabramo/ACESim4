#define SAFETYCHECKS


using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ACESim
{
    public readonly struct GameFullHistoryStorable
    {
        public readonly byte[] History; // length is GameFullHistory.MaxHistoryLength;
        public readonly short NextIndexToAddToHistory;

        public GameFullHistoryStorable(byte[] history, short lastIndexAddedToHistory)
        {
            History = history;
            NextIndexToAddToHistory = lastIndexAddedToHistory;
        }

        public GameFullHistory DeepCopyToRefStruct()
        {
            var history = new byte[GameFullHistory.MaxHistoryLength];
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                history[i] = History[i];
            return new GameFullHistory(history, NextIndexToAddToHistory);
        }
        public GameFullHistory ShallowCopyToRefStruct()
        {
            var result = new GameFullHistory(History, NextIndexToAddToHistory);
            return result;
        }

        public GameFullHistoryStorable Initialize()
        {
            var history = History;
            if (history == null)
                history = new byte[GameFullHistory.MaxHistoryLength];
            history[0] = GameFullHistory.HistoryTerminator;
            const short lastIndexAddedToHistory = 0;
            return new GameFullHistoryStorable(history, lastIndexAddedToHistory);
        }

        public GameFullHistoryStorable AddToHistory(byte decisionByteCode, byte decisionIndex, byte playerIndex, byte action, byte numPossibleActions)
        {
            var history = History;
            short nextIndexToAddToHistory = NextIndexToAddToHistory;
            short i = nextIndexToAddToHistory;
#if (SAFETYCHECKS)
            if (history[i] == GameFullHistory.HistoryComplete)
                ThrowHelper.Throw("Cannot add to history of complete game.");
#endif
            history[i + GameFullHistory.History_DecisionByteCode_Offset] = decisionByteCode;
            history[i + GameFullHistory.History_DecisionIndex_Offset] = decisionIndex;
            history[i + GameFullHistory.History_PlayerNumber_Offset] = playerIndex;
            history[i + GameFullHistory.History_Action_Offset] = action;
            history[i + GameFullHistory.History_NumPossibleActions_Offset] = numPossibleActions;
            history[i + GameFullHistory.History_NumPiecesOfInformation] = GameFullHistory.HistoryTerminator; // this is just one item at end of all history items
            nextIndexToAddToHistory = (short)(i + GameFullHistory.History_NumPiecesOfInformation);

#if (SAFETYCHECKS)
            if (nextIndexToAddToHistory >= GameFullHistory.MaxHistoryLength - 2) // must account for terminator characters
                ThrowHelper.Throw("Internal error. Must increase history length.");
#endif
            var result = new GameFullHistoryStorable(history, nextIndexToAddToHistory);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"Actions so far: {result.ShallowCopyToRefStruct().GetActionsAsListString()}");
            return result;
        }

        public bool IsComplete()
        {
            short i = NextIndexToAddToHistory;
            return (History[i] == GameFullHistory.HistoryComplete);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkComplete()
        {
            // NOTE: Because this doesn't change LastIndexAddedToHistory, we just change the History bytespan, and thus we don't need to return a new GameFullHistoryStorable.
            short i = NextIndexToAddToHistory;
#if (SAFETYCHECKS)
            if (History[i] == GameFullHistory.HistoryComplete)
                ThrowHelper.Throw("Game is already complete.");
#endif
            History[i] = GameFullHistory.HistoryComplete;
            History[i + 1] = GameFullHistory.HistoryTerminator;
        }

        public void MarkIncomplete()
        {
            History[NextIndexToAddToHistory] = GameFullHistory.HistoryTerminator; // overwrite GameFullHistory.HistoryComplete
        }


        public string GetInformationSetHistoryItemsString(GameProgress gameProgress) => String.Join(",", GetInformationSetHistoryItemsStrings(gameProgress));

        public IEnumerable<string> GetInformationSetHistoryItemsStrings(GameProgress gameProgress)
        {
            short numItems = ShallowCopyToRefStruct().GetInformationSetHistoryItems_Count(gameProgress);
            for (short i = 0; i < numItems; i++)
            {
                string s = ShallowCopyToRefStruct().GetInformationSetHistory_OverallIndex(i, gameProgress).ToString();
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
            GameFullHistory gameFullHistory = ShallowCopyToRefStruct();
            short piecesOfInfo = GameFullHistory.History_NumPiecesOfInformation;
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
            GameFullHistory gameFullHistory = ShallowCopyToRefStruct();
            short piecesOfInfo = GameFullHistory.History_NumPiecesOfInformation;
            for (short i = 0; i < NextIndexToAddToHistory; i += piecesOfInfo)
            {
                decisionIndicesCompleted.Add(History[i + GameFullHistory.History_DecisionIndex_Offset]);
            }
            return decisionIndicesCompleted;
        }
    }
}
