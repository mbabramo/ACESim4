using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace ACESim
{
    /// <summary>
    /// A <c>Command</c> containing all the information to perform evolution.
    /// </summary>
    [Serializable]
    public class EvolveCommand : Command
    {
        public string EvolutionSettingsName;
        public string InitializeStrategiesFile;
        public bool UseInitializeStrategiesFile;
        public string StoreStrategiesFile;
        public bool EvolveOnlyIfNecessary;
        public string BaseOutputDirectory;
        public List<ReportCommand> ReportsAfterEvolvePhases;
        [NonSerialized]
        public ProgressResumptionManager ProgressResumptionManager;

        public EvolveCommand(
            MultiPartCommand theMultipartCommand,
            string theEvolutionSettingsName,
            string theInitializeStrategiesFile,
            bool theUseInitializeStrategiesFile,
            string theStoreStrategiesFile,
            bool settingEvolveOnlyIfNecessary,
            string theBaseOutputDirectory,
            List<ReportCommand> theReportsBetweenDecisions,
            List<ReportCommand> theReportsAfterEvolveSteps
            )
            : base(theMultipartCommand)
        {
            CommandDifficulty = 80;
            EvolutionSettingsName = theEvolutionSettingsName;
            StoreStrategiesFile = theStoreStrategiesFile;
            InitializeStrategiesFile = theInitializeStrategiesFile;
            UseInitializeStrategiesFile = theUseInitializeStrategiesFile;
            EvolveOnlyIfNecessary = settingEvolveOnlyIfNecessary;
            BaseOutputDirectory = theBaseOutputDirectory;
            ReportsAfterEvolvePhases = theReportsAfterEvolveSteps;
        }

        public void SetProgressResumptionManager(ProgressResumptionManager prm)
        {
            ProgressResumptionManager = prm;
        }

        public override void Execute(SimulationInteraction simulationInteraction)
        {
            DateTime startTime = DateTime.Now;
            storedSettingsInfo = null;
            //if (
            //    EvolveOnlyIfNecessary &&
            //    StrategySerialization.DeserializeStrategies(Path.Combine(BaseOutputDirectory, "Strategies", StoreStrategiesFile)) != null
            //    //XMLSerialization.GetSerializedObject(StoreStrategiesFile, typeof(object[])) != null
            //    )
            //    return;

            SimulationCoordinator coordinator = new SimulationCoordinator(simulationInteraction);
            coordinator.Evolve(BaseOutputDirectory, EvolveOnlyIfNecessary, ProgressResumptionManager);
            TimeSpan runTime = DateTime.Now - startTime;
            TabbedText.WriteLine("Total run time {0:dd\\.hh\\:mm\\:ss} days", runTime);
            string outputFileName = simulationInteraction.CurrentExecutionInformation.NameOfRunAndCommandSet + ".txt";
            TabbedText.WriteToFile(BaseOutputDirectory, SimulationInteraction.reportsSubdirectory, outputFileName, true);
        }
    }
}
