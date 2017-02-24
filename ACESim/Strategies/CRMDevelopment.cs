using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class CRMDevelopment : IStrategiesDeveloper
    {
        public List<Strategy> Strategies { get; set; }

        public EvolutionSettings EvolutionSettings { get; set; }

        public GameDefinition GameDefinition { get; set; }

        public IGameFactory GameFactory { get; set; }

        public CurrentExecutionInformation CurrentExecutionInformation { get; set; }

        /// <summary>
        /// A game history tree. On internal nodes, the object contained is the information set of the player making the decision (or null for a chance decision). On the leaf nodes, the object contained are the players' utilities.
        /// </summary>
        public NWayTreeStorageInternal<object> GameHistoryTree;

        public bool ChancePlayerExists;

        // these are for walking the game history and are not serialized
        [NonSerialized]
        public NWayTreeStorage<object> WalkHistoryPoint;
        [NonSerialized]
        public NWayTreeStorage<object>[] WalkInformationSets;

        public CRMDevelopment()
        {

        }

        public CRMDevelopment(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition, IGameFactory gameFactory, CurrentExecutionInformation currentExecutionInformation)
        {
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = gameFactory;
            CurrentExecutionInformation = currentExecutionInformation;
            ChancePlayerExists = GameDefinition.Players.Any(x => x.PlayerIsChance);
        }

        public IStrategiesDeveloper DeepCopy()
        {
            return new CRMDevelopment()
            {
                Strategies = Strategies.Select(x => x.DeepCopy()).ToList(),
                EvolutionSettings = EvolutionSettings.DeepCopy(),
                GameDefinition = GameDefinition,
                GameFactory = GameFactory,
                CurrentExecutionInformation = CurrentExecutionInformation
            };
        }

        public void WalkGameHistoryTrees(GameProgress gameProgress)
        {
            // initialize walking
            WalkHistoryPoint = GameHistoryTree;
            if (WalkInformationSets == null)
                WalkInformationSets = new NWayTreeStorage<object>[Strategies.Count];
            for (int i = 0; i < WalkInformationSets.Length; i++)
                WalkInformationSets[i] = Strategies[i].InformationSetTree;

            // Simple algorithm: First add the utilities at the end of the tree. Then walk through the game tree, and at each non-chance decision point, add a link to the information set.

            GameHistory gameHistory = gameProgress.GameHistory;

        }

        public void DevelopStrategies()
        {
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);

            Type theType = GameFactory.GetSimulationSettingsType();
            InputVariables inputVariables = new InputVariables(CurrentExecutionInformation);
            GameInputs inputs = inputVariables.GetGameInputs(theType, 1, new IterationID(1), CurrentExecutionInformation);
            
            // Create game trees
            GameHistoryTree = new NWayTreeStorageInternal<object>(GameDefinition.DecisionsExecutionOrder.First().NumActions);
            foreach (Strategy s in Strategies)
            {
                if (!s.PlayerInfo.PlayerIsChance)
                    s.CreateInformationSetTree(GameDefinition.DecisionsExecutionOrder.First(x => x.PlayerNumber == s.PlayerInfo.PlayerNumber).NumActions);
            }
            
            int numPlayed = 0;
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                numPlayed++;

                // First, add the utilities at the end of the tree.
                var actionsEnumerator = progress.GameHistory.GetActions().GetEnumerator();
                var numPossibleActionsEnumerator = progress.GameHistory.GetNumPossibleActions().GetEnumerator();
                GameHistoryTree.AddValue(actionsEnumerator, numPossibleActionsEnumerator, true, progress.GetNonChancePlayerUtilities());

                // Go through each non-chance decision point and make sure that the information set tree extends there.
                foreach (var informationSetHistory in progress.GameHistory.GetInformationSetHistoryItems())
                {
                    if (!GameDefinition.Players[informationSetHistory.PlayerMakingDecision].PlayerIsChance)
                    {
                        Strategies[informationSetHistory.PlayerMakingDecision].
                        informationSetHistory.InformationSet
                    }
                    WalkHistoryPoint = WalkHistoryPoint.GetChildTree(informationSetHistory.ActionChosen);
                    if (informationSetHistory.IsTerminalAction)
                        WalkHistoryPoint.StoredValue = progress.GameHistory.GetNonChancePlayerUtilities();
                }
            }

            // DEBUG
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                var path = progress.GameHistory.GetActions();
                var utilities = GetUtilities(path);
                Debug.WriteLine($"Utilities: P {utilities[0]}, D {utilities[1]}");
            }
        }

        public double GetUtility(IEnumerable<byte> path, byte player)
        {
            byte index = player;
            if (ChancePlayerExists)
                index--;
            return GetUtilities(path)[index];
        }

        private double[] GetUtilities(IEnumerable<byte> path)
        {
            return GameHistoryTree.GetValue(path.GetEnumerator());
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }
    }
}
