namespace ACESim
{
    public struct HistoryPointStorable
    {
        public NWayTreeStorage<IGameState> TreePoint;
        public GameHistoryStorable HistoryToPointStorable;
        public GameProgress GameProgress;
        public IGameState GameState;

        public HistoryPoint ToRefStruct()
        {
            return new HistoryPoint()
            {
                TreePoint = TreePoint,
                HistoryToPoint = HistoryToPointStorable.ToRefStruct(),
                GameProgress = GameProgress,
                GameState = GameState
            };
        }
    }
}
