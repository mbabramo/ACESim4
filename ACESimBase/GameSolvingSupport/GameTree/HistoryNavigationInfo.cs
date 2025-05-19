using ACESim;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    [Serializable]
    public readonly struct HistoryNavigationInfo
    {
        public readonly InformationSetLookupApproach LookupApproach;
        public readonly List<Strategy> Strategies;
        public readonly GameDefinition GameDefinition;
        public readonly List<InformationSetNode> InformationSets;
        public readonly List<ChanceNode> ChanceNodes;
        public readonly List<FinalUtilitiesNode> FinalUtilitiesNodes;
        public readonly EvolutionSettings EvolutionSettings;

        public delegate IGameState GameStateFunction(in HistoryPoint historyPoint, HistoryNavigationInfo? navigation);

        [NonSerialized]
        public readonly GameStateFunction StoredGameStateFunction;

        public HistoryNavigationInfo(InformationSetLookupApproach lookupApproach, List<Strategy> strategies, GameDefinition gameDefinition, List<InformationSetNode> informationSets, List<ChanceNode> chanceNodes, List<FinalUtilitiesNode> finalUtilitiesNodes, GameStateFunction gameStateFunction, EvolutionSettings evolutionSettings)
        {
            LookupApproach = lookupApproach;
            Strategies = strategies;
            GameDefinition = gameDefinition;
            InformationSets = informationSets;
            ChanceNodes = chanceNodes;
            FinalUtilitiesNodes = finalUtilitiesNodes;
            StoredGameStateFunction = gameStateFunction;
            EvolutionSettings = evolutionSettings;
        }

        // The reason that we need to refer to the algorithm to set game state is that when we are navigating, we sometimes arrive at a game path that hasn't been played. We need the algorithm to play the game and add it to the navigation tree. 

        public HistoryNavigationInfo WithGameStateFunction(GameStateFunction gameStateFunction) => new HistoryNavigationInfo(LookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, gameStateFunction, EvolutionSettings);


        public HistoryNavigationInfo WithLookupApproach(InformationSetLookupApproach lookupApproach) => new HistoryNavigationInfo(lookupApproach, Strategies, GameDefinition, InformationSets, ChanceNodes, FinalUtilitiesNodes, StoredGameStateFunction, EvolutionSettings);

        public IGameState GetGameState(in HistoryPoint historyPoint)
        {
            return StoredGameStateFunction(in historyPoint, this);
        }
    }
}
