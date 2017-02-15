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

        public EvolutionSettings evolutionSettings;
        public GameDefinition GameDefinition;

        /// <summary>
        /// A reference to interact with the simulation host
        /// </summary>
        public SimulationInteraction SimulationInteraction;

        private List<Strategy> strategies;

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
                loadedStrategies.Length == GameDefinition.DecisionsExecutionOrder.Count
                )
            {
                strategies = new List<Strategy>(loadedStrategies);
                for (int i = 0; i < strategies.Count; i++)
                {
                    if (strategies[i] == null) // loading of some strategies and not others
                        strategies[i] = new Strategy();
                    if (theEvolutionSettings != null)
                        strategies[i].EvolutionSettings = theEvolutionSettings;
                    strategies[i].Decision = GameDefinition.DecisionsExecutionOrder[i];
                    strategies[i].ActionGroup = GameDefinition.DecisionPointsExecutionOrder[i].ActionGroup;
                    strategies[i].DecisionNumber = i;
                    strategies[i].AllStrategies = strategies;
                    strategies[i].SimulationInteraction = SimulationInteraction;
                }
            }
            else
            {
                // make the starter strategies empty
                strategies = new List<Strategy>();
                for (int i = 0; i < GameDefinition.DecisionsExecutionOrder.Count; i++)
                {
                    var aStrategy = new Strategy();
                    aStrategy.EvolutionSettings = simulationInteraction.GetEvolutionSettings();
                    aStrategy.SimulationInteraction = simulationInteraction;
                    aStrategy.Decision = GameDefinition.DecisionsExecutionOrder[i];
                    aStrategy.ActionGroup = GameDefinition.DecisionPointsExecutionOrder[i].ActionGroup;
                    aStrategy.DecisionNumber = i;
                    strategies.Add(aStrategy);
                }
                for (int i = 0; i < GameDefinition.DecisionsExecutionOrder.Count; i++)
                {
                    strategies[i].AllStrategies = strategies;
                }
            }
        }

        /// <summary>
        /// Returns the current best strategy for each decision, eliminating zero terms where possible.
        /// </summary>
        /// <returns></returns>
        public List<Strategy> GetStrategies()
        {
            return strategies;
        }


        public void Evolve(string theBaseOutputDirectory, bool doNotEvolveByDefault, ProgressResumptionManager prm)
        {
            evolutionSettings = SimulationInteraction.GetEvolutionSettings();
            List<Strategy> bestStrategies = strategies;

            int numRepetitions = 1;
            // Actually do the evolution
            if (numRepetitions > 0)
                SimulationInteraction.GetCurrentProgressStep().AddChildSteps(numRepetitions, "EvolveRepetitions");
            bool stop = false;
            int numDecisionsEvolved = 0;
            int startingRepetition = 0;
            if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
            {
                startingRepetition = prm.Info.SimulationCoordinatorRepetition;
                SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(startingRepetition, "EvolveRepetitions"); 
            }
            for (int repetition = startingRepetition; repetition < numRepetitions; repetition++)
            {
                prm.Info.SimulationCoordinatorRepetition = repetition;
                Debug.WriteLine(String.Format("Repetition {0} of (0,{1})... ", repetition, numRepetitions - 1));
                SimulationInteraction.ReportVariableFromProgram("EvolveStepPct", ((double)repetition) / ((double)(numRepetitions - 1)));
                evolutionSettings = SimulationInteraction.GetEvolutionSettings(); // update evolution settings (in case it has changed, for example because something is dependent on EvolveStepPct)

                SimulationInteraction.ReportTextToUser(String.Format(Environment.NewLine + "Repetition {0} of (0,{1})... ", repetition, numRepetitions - 1), true);

                ExecuteEvolveStep(evolutionSettings, doNotEvolveByDefault, theBaseOutputDirectory, repetition, numRepetitions, repetition == numRepetitions, prm, ref numDecisionsEvolved, out stop);
                if (stop || SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint)
                    break;

                SimulationInteraction.ReportVariableFromProgram(RepetitionVariableName, repetition);

                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "EvolveRepetitions");
            }
            SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint = false; // reset
            if (!stop)
                ReportEvolvedStrategies();
            SimulationInteraction.ExportAll2DCharts();
            SimulationInteraction.CloseAllCharts();
        }

        public void ReportEvolvedStrategies()
        {
            SimulationInteraction.ReportEvolvedStrategies(strategies);
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

        /// <summary>
        /// Executes evolve steps for all decisions except those in skip decisions.
        /// </summary>
        protected void ExecuteEvolveStep(EvolutionSettings evolveSettings, bool doNotEvolveByDefault, string theBaseOutputDirectory, int stepNumber, int totalSteps, bool isLastEvolveStep, ProgressResumptionManager prm, ref int numDecisionsEvolved, out bool stop)
        {
            stop = false;
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;

            int? setBreakAtGameNumberWithThatDecisionEvolution = null; // 1097;

            //SimulationInteraction.GetCurrentProgressStep().AddChildSteps(decisionsAffectingProgressStep, "DecisionsWithinExecuteEvolveStep");
            

            if (!doNotEvolveByDefault)
                RunInterimReports(((EvolveCommand)SimulationInteraction.CurrentExecutionInformation.CurrentCommand).ReportsAfterEvolveSteps);
        }
        
        private void ContinueProgressFromWhereLeftOff(int decisionNumber, ProgressResumptionManager prm)
        {
            string path;
            string filenameBase;
            strategies[decisionNumber].GetSerializedStrategiesPathAndFilenameBase(prm.Info.NumStrategyStatesSerialized, out path, out filenameBase); // since we're in the same place in the program, this will get the same filename base as when it was saved
            var evolutionSettingsForStrategies = strategies.Select(x => x == null ? null : x.EvolutionSettings).ToList();
            strategies = StrategyStateSerialization.DeserializeStrategyStateFromFiles(path, filenameBase, new List<Tuple<int, string>>()).AllStrategies;
            for (int sind = 0; sind < strategies.Count(); sind++)
            {
                Strategy s = strategies[sind];
                s.SimulationInteraction = SimulationInteraction;
                s.EvolutionSettings = evolutionSettingsForStrategies[sind]; // makes it possible to resume progress but change the settings
                s.AllStrategies = strategies;
            }
            prm.ProgressResumptionOption = ProgressResumptionOptions.ProceedNormallySavingPastProgress;
        }

        public IEnumerable<GameProgress> Play(int numberOfIterations, bool parallelReporting)
        {
            evolutionSettings = SimulationInteraction.GetEvolutionSettings();

            IGameFactory gameFactory = SimulationInteraction.CurrentExecutionInformation.GameFactory;
            GamePlayer player = new GamePlayer(strategies, gameFactory, parallelReporting, GameDefinition);

            int decisionNumber = GameDefinition.DecisionsExecutionOrder.Count - 1; // play up through last decision of the game.

            IEnumerable<GameProgress> completedGameProgressInfos =
                player.PlayStrategy(
                    strategies[decisionNumber],
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
