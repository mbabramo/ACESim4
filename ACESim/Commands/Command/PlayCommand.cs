using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Path = System.IO.Path;

namespace ACESim
{
    [Serializable]
    public class PlayCommand : Command
    {
        public bool SavePlayedGamesInMemoryOnly;
        public ConcurrentStack<GameProgressReportable> InMemoryStoredGames = null;

        public int NumberIterations;
        public string StoreStrategiesFile;
        public string StrategyName;
        public string StoreIterationResultsFile;
        public Strategy[] StoredStrategies;
        public ChangeSimulationSettingContainer ChangeSimulationSettingContainer;
        private string baseOutputDirectory;
        public bool ParallelReporting;

        public PlayCommand(
            MultiPartCommand theMultipartCommand,
            int theNumberIterations,
            string theStoreStrategiesFile,
            string theStoreIterationResultsFile,
            ChangeSimulationSettingContainer theChangeSimulationSettingContainer,
            string theBaseOutputDirectory, 
            bool parallelReporting,
            bool theSavePlayedGamesInMemoryOnly)
            : base(theMultipartCommand)
        {
            CommandDifficulty = 20;
            NumberIterations = theNumberIterations;
            StoreStrategiesFile = theStoreStrategiesFile;
            StoreIterationResultsFile = theStoreIterationResultsFile;
            ChangeSimulationSettingContainer = theChangeSimulationSettingContainer;
            baseOutputDirectory = theBaseOutputDirectory;
            ParallelReporting = parallelReporting;
            SavePlayedGamesInMemoryOnly = theSavePlayedGamesInMemoryOnly;
        }

        internal void PlayGame(SimulationInteraction simulationInteraction)
        {
            SimulationCoordinator coordinator = new SimulationCoordinator(simulationInteraction);

            coordinator.SimulationInteraction.GetCurrentProgressStep().AddChildSteps(1, "DecisionsInPlay");
            coordinator.SimulationInteraction.CurrentExecutionInformation.InputSeedsSet.randomizationApproach = InputSeedsRandomization.useOrdinary;
            coordinator.SimulationInteraction.CurrentExecutionInformation.InputSeedsSet.enableInputMirroring = true;

            coordinator.Play(NumberIterations, ParallelReporting);
            coordinator.SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "DecisionsInPlay");
        }

        internal bool prepared = false;
        public void PrepareToExecute()
        {
            StoredStrategies = StrategySerialization.DeserializeStrategies(Path.Combine(baseOutputDirectory, "Strategies", StoreStrategiesFile));
            prepared = true;
        }

        public override void Execute(SimulationInteraction simulationInteraction)
        {
            storedSettingsInfo = null;
            if (!prepared)
                throw new Exception("PrepareToExecute must be called before Execute on the PlayCommand.");
            List<List<Setting>> changeSimulationSettings = ChangeSimulationSettingContainer.GenerateAll();
            simulationInteraction.GetCurrentProgressStep().AddChildSteps(Math.Max(1, changeSimulationSettings.Count), "PlayGameSet");
            if (changeSimulationSettings.Count == 0)
            {
                PlayGame(simulationInteraction);
                simulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "PlayGameSet");
            }
            else
            {
                foreach (var settingsChangeList in changeSimulationSettings)
                {
                    ChangeSimulationSettings converted = new ChangeSimulationSettings(settingsChangeList);
                    simulationInteraction.CurrentExecutionInformation.SettingOverride = converted;
                    storedSettingsInfo = null;
                    PlayGame(simulationInteraction);
                    simulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "PlayGameSet");
                }
            }
            if (!String.IsNullOrEmpty(StoreIterationResultsFile))
            {
                if (SavePlayedGamesInMemoryOnly)
                {
                    InMemoryStoredGames = simulationInteraction.CurrentExecutionInformation.Outputs;
                }
                else
                    BinarySerialization.SerializeObject(
                        Path.Combine(baseOutputDirectory, SimulationInteraction.iterationsSubdirectory, StoreIterationResultsFile),
                        simulationInteraction.CurrentExecutionInformation.Outputs);
            }
            StoredStrategies = null;
        }
    }
}
