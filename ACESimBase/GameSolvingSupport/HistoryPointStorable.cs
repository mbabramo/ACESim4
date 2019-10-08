namespace ACESim
{
    public struct HistoryPointStorable
    {
        public NWayTreeStorage<IGameState> TreePoint;
        public GameHistoryStorable HistoryToPointStorable;
        public GameProgress GameProgress;
        public IGameState GameState;

        public HistoryPoint ShallowCopyToRefStruct()
        {
            return new HistoryPoint()
            {
                TreePoint = TreePoint,
                HistoryToPoint = HistoryToPointStorable.ShallowCopyToRefStruct(),
                GameProgress = GameProgress,
                GameState = GameState
            };
        }

        public HistoryPoint DeepCopyToRefStruct()
        {
            return new HistoryPoint()
            {
                TreePoint = TreePoint,
                HistoryToPoint = HistoryToPointStorable.DeepCopyToRefStruct(),
                GameProgress = GameProgress,
                GameState = GameState
            };
        }
    }
}
