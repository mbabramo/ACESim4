using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public struct HistoryNavigationInfo
    {
        public InformationSetLookupApproach LookupApproach;
        public List<Strategy> Strategies;
        public GameDefinition GameDefinition;
        [NonSerialized]
        private Func<HistoryPoint, HistoryNavigationInfo?, ICRMGameState> GetGameStateFn;

        public HistoryNavigationInfo(InformationSetLookupApproach lookupApproach, List<Strategy> strategies, GameDefinition gameDefinition, Func<HistoryPoint, HistoryNavigationInfo?, ICRMGameState> getGameStateFn)
        {
            LookupApproach = lookupApproach;
            Strategies = strategies;
            GameDefinition = gameDefinition;
            GetGameStateFn = getGameStateFn;
        }

        public void SetGameStateFn(Func<HistoryPoint, HistoryNavigationInfo?, ICRMGameState> getGameStateFn)
        {
            GetGameStateFn = getGameStateFn;
        }

        public ICRMGameState GetGameState(HistoryPoint historyPoint)
        {
            return GetGameStateFn(historyPoint, this);
        }
    }
}
