#define SAFETYCHECKS


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
    }
}
