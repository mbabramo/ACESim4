#define SAFETYCHECKS


using ACESimBase.Util;
using System;
using System.Collections.Generic;

namespace ACESim
{
    public unsafe struct GameFullHistoryStorable
    {

        public fixed byte History[GameFullHistory.MaxHistoryLength];
        public short LastIndexAddedToHistory;
        public bool Initialized;

        public GameFullHistory ToRefStruct()
        {
            var result = new GameFullHistory()
            {
                LastIndexAddedToHistory = LastIndexAddedToHistory,
                Initialized = Initialized
            };
            for (int i = 0; i < GameFullHistory.MaxHistoryLength; i++)
                result.History[i] = History[i];
            return result;
        }

        public void Initialize()
        {
            fixed (byte* historyPtr = History)
                *(historyPtr + 0) = GameFullHistory.HistoryTerminator;
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
                    if (*(historyPtr + i) == GameFullHistory.HistoryComplete)
                        ThrowHelper.Throw("Cannot add to history of complete game.");
#endif
                    *(historyPtr + i + GameFullHistory.History_DecisionByteCode_Offset) = decisionByteCode;
                    *(historyPtr + i + GameFullHistory.History_DecisionIndex_Offset) = decisionIndex;
                    *(historyPtr + i + GameFullHistory.History_PlayerNumber_Offset) = playerIndex;
                    *(historyPtr + i + GameFullHistory.History_Action_Offset) = action;
                    *(historyPtr + i + GameFullHistory.History_NumPossibleActions_Offset) = numPossibleActions;
                    *(historyPtr + i + GameFullHistory.History_NumPiecesOfInformation) = GameFullHistory.HistoryTerminator; // this is just one item at end of all history items
                }
                LastIndexAddedToHistory = (short)(i + GameFullHistory.History_NumPiecesOfInformation);

#if (SAFETYCHECKS)
                if (LastIndexAddedToHistory >= GameFullHistory.MaxHistoryLength - 2) // must account for terminator characters
                    ThrowHelper.Throw("Internal error. Must increase history length.");
#endif
            }
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"Actions so far: {ToRefStruct().GetActionsAsListString()}");
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
                if (*(historyPtr + i) == GameFullHistory.HistoryComplete)
                    ThrowHelper.Throw("Game is already complete.");
#endif
                *(historyPtr + i) = GameFullHistory.HistoryComplete;
                *(historyPtr + i + 1) = GameFullHistory.HistoryTerminator;
            }
        }


        public string GetInformationSetHistoryItemsString(GameProgress gameProgress) => String.Join(",", GetInformationSetHistoryItemsStrings(gameProgress));

        public IEnumerable<string> GetInformationSetHistoryItemsStrings(GameProgress gameProgress)
        {
            short numItems = ToRefStruct().GetInformationSetHistoryItems_Count(gameProgress);
            for (short i = 0; i < numItems; i++)
            {
                string s = ToRefStruct().GetInformationSetHistory_OverallIndex(i, gameProgress).ToString();
                yield return s;
            }
        }


        // NOTE: InformationSetHistory is ref struct, so we can't enumerate it directly. We can enumerate the indices, and the caller can then
        // access each InformationSetHistory one at a time.

        public IEnumerable<short> GetInformationSetHistoryItems_OverallIndices(GameProgress gameProgress)
        {
#if (SAFETYCHECKS)
            if (!Initialized)
                ThrowHelper.Throw();
#endif
            if (LastIndexAddedToHistory == 0)
                yield break;
            short overallIndex = 0;
            GameFullHistory gameFullHistory = ToRefStruct();
            short piecesOfInfo = GameFullHistory.History_NumPiecesOfInformation;
            for (short i = 0; i < LastIndexAddedToHistory; i += piecesOfInfo)
            {
                yield return overallIndex++;
            }
        }
    }
}
