using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    /// <summary>
    /// Manages a List of Populations for one Game, where each Population corresponds to a Decision in the Game.
    /// Called by commands to effect simulation, e.g. PlayCommand and EvolveCommand.   Uses DecisionEvolver and
    /// GamePlayer as helpers.
    /// </summary>
    public class SimulationCoordinator
    {
        public const string RepetitionVariableName = "Repetition";

        public CRMDevelopment CRMDeveloper;
        public EvolutionSettings EvolutionSettings;
        public GameDefinition GameDefinition;
        private List<Strategy> Strategies;

        /// <summary>
        /// A reference to interact with the simulation host
        /// </summary>
        public SimulationInteraction SimulationInteraction;

        public SimulationCoordinator(SimulationInteraction simulationInteraction)
        {
            SimulationInteraction = simulationInteraction;
            GameDefinition = simulationInteraction.GetGameDefinition();

            // Initialize program variables.  Otherwise when simulationInteraction.GetEvolutionSettings() is called there will be an error.
            SimulationInteraction.ReportVariableFromProgram(RepetitionVariableName, -1);
            SimulationInteraction.ReportVariableFromProgram("EvolveStepPct", 0.0);
            SimulationInteraction.ReportVariableFromProgram("EvolveStepPctThisDecision", 0.0);

            EvolutionSettings theEvolutionSettings = SimulationInteraction.GetEvolutionSettings();

            Strategy[] loadedStrategies = SimulationInteraction.LoadEvolvedStrategies(GameDefinition);

            SetUpStrategies(simulationInteraction, theEvolutionSettings, loadedStrategies);
        }

        private void SetUpStrategies(SimulationInteraction simulationInteraction, EvolutionSettings theEvolutionSettings, Strategy[] loadedStrategies)
        {
            if (
                loadedStrategies != null &&
                loadedStrategies.Length == GameDefinition.NumPlayers
                )
            {
                Strategies = new List<Strategy>(loadedStrategies);
                for (int i = 0; i < Strategies.Count; i++)
                {
                    if (Strategies[i] == null) // loading of some strategies and not others
                        Strategies[i] = new Strategy();
                    if (theEvolutionSettings != null)
                        Strategies[i].EvolutionSettings = theEvolutionSettings;
                    Strategies[i].PlayerInfo = GameDefinition.Players[i];
                    Strategies[i].AllStrategies = Strategies;
                    Strategies[i].SimulationInteraction = SimulationInteraction;
                }
            }
            else
            {
                // make the starter strategies empty
                Strategies = new List<Strategy>();
                for (int i = 0; i < GameDefinition.NumPlayers; i++)
                {
                    var aStrategy = new Strategy();
                    aStrategy.EvolutionSettings = simulationInteraction.GetEvolutionSettings();
                    aStrategy.SimulationInteraction = simulationInteraction;
                    aStrategy.PlayerInfo = GameDefinition.Players[i];
                    Strategies.Add(aStrategy);
                }
                for (int i = 0; i < GameDefinition.DecisionsExecutionOrder.Count; i++)
                {
                    Strategies[i].AllStrategies = Strategies;
                }
            }
        }

        /// <summary>
        /// Returns the current best strategy for each decision, eliminating zero terms where possible.
        /// </summary>
        /// <returns></returns>
        public List<Strategy> GetStrategies()
        {
            return Strategies;
        }


        public void Evolve(string theBaseOutputDirectory, bool doNotEvolveByDefault, ProgressResumptionManager prm)
        {
            EvolutionSettings = SimulationInteraction.GetEvolutionSettings();
            List<Strategy> bestStrategies = Strategies;

            int numPhases = EvolutionSettings.NumPhases;
            // Actually do the evolution
            if (numPhases > 0)
                SimulationInteraction.GetCurrentProgressStep().AddChildSteps(numPhases, "EvolvePhases");
            bool stop = false;
            int startingPhase = 0;
            if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
            {
                startingPhase = prm.Info.SimulationCoordinatorPhase;
                SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(startingPhase, "EvolvePhases"); 
            }
            for (int phase = startingPhase; phase < numPhases; phase++)
            {
                prm.Info.SimulationCoordinatorPhase = phase;
                Debug.WriteLine(String.Format("Phase {0} of (0,{1})... ", phase, numPhases - 1));
                SimulationInteraction.ReportVariableFromProgram("EvolveStepPct", ((double)phase) / ((double)(numPhases - 1)));
                EvolutionSettings = SimulationInteraction.GetEvolutionSettings(); // update evolution settings (in case it has changed, for example because something is dependent on EvolveStepPct)

                SimulationInteraction.ReportTextToUser(String.Format(Environment.NewLine + "Phase {0} of (0,{1})... ", phase, numPhases - 1), true);

                ExecuteEvolvePhase(out stop);
                if (stop || SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint)
                    break;

                SimulationInteraction.ReportVariableFromProgram(RepetitionVariableName, phase);

                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "EvolvePhases");
            }
            SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint = false; // reset
            if (!stop)
                ReportEvolvedStrategies();
            SimulationInteraction.ExportAll2DCharts();
            SimulationInteraction.CloseAllCharts();
        }

        public void ReportEvolvedStrategies()
        {
            SimulationInteraction.ReportEvolvedStrategies(Strategies);
        }


        public void ReportVariableFromProgram(string name, double value)
        {
            SimulationInteraction.ReportVariableFromProgram(name, value);
        }

        protected void RunInterimReports(List<ReportCommand> reportsToRun)
        {
            if (reportsToRun != null && reportsToRun.Any() && (reportsToRun.Where(x => !x.requireCumulativeDistributions).Any() || SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved != null) /* to run the games all the way through, we need to make sure that we've had a chance to update the cumulative distributions */)
            {
                int numObservationsForEmbeddedReports = 100; // leave it as int so that we can change it by putting in a breakpoint for a particular report
                Play(numObservationsForEmbeddedReports, true);
                List<GameProgressReportable> played = SimulationInteraction.CurrentExecutionInformation.Outputs.ToList();
                foreach (var report in reportsToRun)
                {
                    if (!report.requireCumulativeDistributions || SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved != null)
                    {
                        report.theOutputs = played;
                        report.CommandSetStartTime = SimulationInteraction.CurrentExecutionInformation.CurrentCommand.CommandSetStartTime;
                        report.isInterimReport = true;
                        report.Execute(SimulationInteraction);
                    }
                }
                SimulationInteraction.CurrentExecutionInformation.Outputs = new System.Collections.Concurrent.ConcurrentStack<GameProgressReportable>();
            }
        }

        protected void InitializeCRMDevelopment()
        {
            CRMDeveloper = new CRMDevelopment(Strategies, EvolutionSettings, GameDefinition, SimulationInteraction.CurrentExecutionInformation.GameFactory, SimulationInteraction.CurrentExecutionInformation);
        }

        /// <summary>
        /// Executes a development phase.
        /// </summary>
        protected void ExecuteEvolvePhase(out bool stop)
        {
            stop = false;
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;

            if (CRMDeveloper == null)
                InitializeCRMDevelopment();

            CRMDeveloper.DevelopStrategies();

            RunInterimReports(((EvolveCommand)SimulationInteraction.CurrentExecutionInformation.CurrentCommand).ReportsAfterEvolvePhases);
        }
        
        private void ContinueProgressFromWhereLeftOff(int decisionNumber, ProgressResumptionManager prm)
        {
            string path;
            string filenameBase;
            Strategies[decisionNumber].GetSerializedStrategiesPathAndFilenameBase(prm.Info.NumStrategyStatesSerialized, out path, out filenameBase); // since we're in the same place in the program, this will get the same filename base as when it was saved
            var evolutionSettingsForStrategies = Strategies.Select(x => x == null ? null : x.EvolutionSettings).ToList();
            Strategies = StrategyStateSerialization.DeserializeStrategyStateFromFiles(path, filenameBase, new List<Tuple<int, string>>()).AllStrategies;
            for (int sind = 0; sind < Strategies.Count(); sind++)
            {
                Strategy s = Strategies[sind];
                s.SimulationInteraction = SimulationInteraction;
                s.EvolutionSettings = evolutionSettingsForStrategies[sind]; // makes it possible to resume progress but change the settings
                s.AllStrategies = Strategies;
            }
            prm.ProgressResumptionOption = ProgressResumptionOptions.ProceedNormallySavingPastProgress;
        }

        public IEnumerable<GameProgress> Play(int numberOfIterations, bool parallelReporting)
        {
            EvolutionSettings = SimulationInteraction.GetEvolutionSettings();

            IGameFactory gameFactory = SimulationInteraction.CurrentExecutionInformation.GameFactory;
            GamePlayer player = new GamePlayer(Strategies, gameFactory, parallelReporting, GameDefinition);

            int decisionNumber = GameDefinition.DecisionsExecutionOrder.Count - 1; // play up through last decision of the game.

            IEnumerable<GameProgress> completedGameProgressInfos =
                player.PlayStrategy(
                    Strategies[decisionNumber],
                    decisionNumber,
                    null,
                    numberOfIterations,
                    SimulationInteraction,
                    returnCompletedGameProgressInfos: true);
            foreach (var completedProgress in completedGameProgressInfos)
            {
                completedProgress.Report(SimulationInteraction);
            }
            return completedGameProgressInfos; // ordinarily disregarded
        }


    }
}
