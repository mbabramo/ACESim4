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

        public NWayTreeStorageInternal<double[]> Utilities;

        public bool ChancePlayerExists;

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

        public void DevelopStrategies()
        {
            GamePlayer player = new GamePlayer(Strategies, GameFactory, EvolutionSettings.ParallelOptimization, GameDefinition);

            Type theType = GameFactory.GetSimulationSettingsType();
            InputVariables inputVariables = new InputVariables(CurrentExecutionInformation);
            GameInputs inputs = inputVariables.GetGameInputs(theType, 1, new IterationID(1), CurrentExecutionInformation);

            bool initializedUtilities = false;
            int numPlayed = 0;
            foreach (var progress in player.PlayAllPaths(inputs))
            {
                numPlayed++;
                if (initializedUtilities == false)
                {
                    var numActionsForFirstDecision = progress.GameHistory.GetNumPossibleActions().First();
                    Utilities = new NWayTreeStorageInternal<double[]>(false, numActionsForFirstDecision);
                }
                var actionsEnumerator = progress.GameHistory.GetActions().GetEnumerator();
                var numPossibleActionsEnumerator = progress.GameHistory.GetNumPossibleActions().GetEnumerator();
                Utilities.AddValue(actionsEnumerator, numPossibleActionsEnumerator, true, progress.GetNonChancePlayerUtilities());
            }

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
            return Utilities.GetValue(path.GetEnumerator());
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }
    }
}
