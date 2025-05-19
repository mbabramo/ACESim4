using ACESim;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public struct HistoryPointStorable
    {
        public NWayTreeStorage<IGameState> TreePoint;
        public GameHistoryStorable HistoryToPointStorable;
        public GameProgress GameProgress;
        public IGameState GameState;

        public HistoryPoint ShallowCopyToRefStruct()
        {
            return new HistoryPoint(TreePoint, HistoryToPointStorable.ShallowCopyToRefStruct(), GameProgress, GameState);
        }

        public HistoryPoint DeepCopyToRefStruct()
        {
            return new HistoryPoint(TreePoint, HistoryToPointStorable.DeepCopyToRefStruct(), GameProgress?.DeepCopy(), GameState);
        }
    }
}
