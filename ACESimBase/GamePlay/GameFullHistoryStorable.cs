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
