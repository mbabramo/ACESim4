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
        public List<InformationSetNodeTally> InformationSets;

        public delegate IGameState GameStateFunction(ref HistoryPoint historyPoint, HistoryNavigationInfo? navigation);

        [NonSerialized]
        public GameStateFunction StoredGameStateFunction;

        public HistoryNavigationInfo(InformationSetLookupApproach lookupApproach, List<Strategy> strategies, GameDefinition gameDefinition, List<InformationSetNodeTally> informationSets, GameStateFunction gameStateFunction)
        {
            LookupApproach = lookupApproach;
            Strategies = strategies;
            GameDefinition = gameDefinition;
            InformationSets = informationSets;
            StoredGameStateFunction = gameStateFunction;
        }

        // The reason that we need to refer to the algorithm to set game state is that when we are navigating, we sometimes arrive at a game path that hasn't been played. We need the algorithm to play the game and add it to the navigation tree. 

        public void SetGameStateFunction(GameStateFunction getGameStateFunction)
        {
            StoredGameStateFunction = getGameStateFunction;
        }

        public IGameState GetGameState(ref HistoryPoint historyPoint)
        {
            return StoredGameStateFunction(ref historyPoint, this);
        }
    }
}
