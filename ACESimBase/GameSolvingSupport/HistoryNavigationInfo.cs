using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public readonly struct HistoryNavigationInfo
    {
        public readonly InformationSetLookupApproach LookupApproach;
        public readonly List<Strategy> Strategies;
        public readonly GameDefinition GameDefinition;
        public readonly List<InformationSetNodeTally> InformationSets;
        public readonly List<ChanceNodeSettings> ChanceNodes;
        public readonly List<FinalUtilities> FinalUtilitiesNodes;

        public delegate IGameState GameStateFunction(ref HistoryPoint historyPoint, HistoryNavigationInfo? navigation);

        [NonSerialized]
        public readonly GameStateFunction StoredGameStateFunction;

        public HistoryNavigationInfo(InformationSetLookupApproach lookupApproach, List<Strategy> strategies, GameDefinition gameDefinition, List<InformationSetNodeTally> informationSets, List<ChanceNodeSettings> chanceNodes, List<FinalUtilities> finalUtilitiesNodes, GameStateFunction gameStateFunction)
        {
            LookupApproach = lookupApproach;
            Strategies = strategies;
            GameDefinition = gameDefinition;
            InformationSets = informationSets;
            ChanceNodes = chanceNodes;
            FinalUtilitiesNodes = finalUtilitiesNodes;
            StoredGameStateFunction = gameStateFunction;
        }

        // The reason that we need to refer to the algorithm to set game state is that when we are navigating, we sometimes arrive at a game path that hasn't been played. We need the algorithm to play the game and add it to the navigation tree. 

        public HistoryNavigationInfo WithGameStateFunction(GameStateFunction gameStateFunction) => new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, gameStateFunction);


        public HistoryNavigationInfo WithLookupApproach(InformationSetLookupApproach lookupApproach) => new HistoryNavigationInfo(lookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, StoredGameStateFunction);

        public IGameState GetGameState(ref HistoryPoint historyPoint)
        {
            return StoredGameStateFunction(ref historyPoint, this);
        }
    }
}
