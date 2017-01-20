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
            if (loadedStrategies != null)
                foreach (Strategy s in loadedStrategies)
                    if (s != null)
                        s.StrategyDeserializedFromDisk = true;

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

            int numRepetitions = evolutionSettings.RepetitionsOfEntireSmoothingProcess;
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
            if (reportsToRun != null && reportsToRun.Any() && SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved != null /* to run the games all the way through, we need to make sure that we've had a chance to update the cumulative distributions (todo: add a flag indicating whether cumulative distributions are used to make sure that this will work even in a game without cumulative distributions) */)
            {
                int numObservationsForEmbeddedReports = 1000; // leave it as int so that we can change it by putting in a breakpoint for a particular report
                Play(numObservationsForEmbeddedReports, true);
                List<GameProgressReportable> played = SimulationInteraction.CurrentExecutionInformation.Outputs.ToList();
                foreach (var report in reportsToRun)
                {
                    report.theOutputs = played;
                    report.CommandSetStartTime = SimulationInteraction.CurrentExecutionInformation.CurrentCommand.CommandSetStartTime;
                    report.isInterimReport = true;
                    report.Execute(SimulationInteraction);
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

            int? setBreakWhenNumDecisionsEvolvedIs = null; // 33; // set this to disable parallel execution at a particular point in the evolutionary process (useful for finding problems)
            int? setBreakAtGameNumberWithThatDecisionEvolution = null; // 1097;

            int decisionsAffectingProgressStep = GameDefinition.DecisionPointsEvolutionOrder.Where(x => 
                !(
                    (GameDefinition.GameModules != null && GameDefinition.GameModules.Any() && GameDefinition.GetOriginalGameModuleForDecisionNumber((int)x.DecisionNumber).IgnoreWhenCountingProgress) // decisions to ignore in counting progress
                    || ((doNotEvolveByDefault && strategies[(int)x.DecisionNumber].StrategyDeserializedFromDisk && !x.Decision.EvolveThisDecisionEvenWhenSkippingByDefault) || (!doNotEvolveByDefault && strategies[(int)x.DecisionNumber].StrategyDeserializedFromDisk && x.Decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved)) // decisions to skip
                    || (GameDefinition.DecisionsExecutionOrder[(int)x.DecisionNumber].MaxEvolveRepetitions < stepNumber)
                )
                ).Count();
            if (decisionsAffectingProgressStep > 0)
                SimulationInteraction.GetCurrentProgressStep().AddChildSteps(decisionsAffectingProgressStep, "DecisionsWithinExecuteEvolveStep");

            // Before doing the evolution, go through once to update the highest cumulative distribution that has already evolved.
            //foreach (DecisionPoint dp in GameDefinition.DecisionPointsEvolutionOrder)
            int startingDPIndex = 0;
            if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
            { 
                startingDPIndex = prm.Info.EvolveDecisionPointIndex;
                int decisionsAffectingProgressStepAlreadyComplete = GameDefinition.DecisionPointsEvolutionOrder.Take(startingDPIndex).Where(x =>
                !(
                    (GameDefinition.GameModules != null && GameDefinition.GetOriginalGameModuleForDecisionNumber((int)x.DecisionNumber).IgnoreWhenCountingProgress) // decisions to ignore in counting progress
                    || ((doNotEvolveByDefault && strategies[(int)x.DecisionNumber].StrategyDeserializedFromDisk && !x.Decision.EvolveThisDecisionEvenWhenSkippingByDefault) || (!doNotEvolveByDefault && strategies[(int)x.DecisionNumber].StrategyDeserializedFromDisk && x.Decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved)) // decisions to skip
                    || (GameDefinition.DecisionsExecutionOrder[(int)x.DecisionNumber].MaxEvolveRepetitions < stepNumber)
                )
                ).Count();
                SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(decisionsAffectingProgressStepAlreadyComplete, "DecisionsWithinExecuteEvolveStep"); 
            }
            for (int dpIndex = 0; dpIndex < GameDefinition.DecisionPointsEvolutionOrder.Count(); dpIndex++)
            {
                DecisionPoint dp = GameDefinition.DecisionPointsEvolutionOrder[dpIndex];
                SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint = SimulationInteraction.CheckStopSoon();
                if (SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint)
                    break;
                int decisionNumber = (int)dp.DecisionNumber;
                bool skip = dpIndex < startingDPIndex || (doNotEvolveByDefault && strategies[decisionNumber].StrategyDeserializedFromDisk && !dp.Decision.EvolveThisDecisionEvenWhenSkippingByDefault) || (!doNotEvolveByDefault && strategies[decisionNumber].StrategyDeserializedFromDisk && dp.Decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved) ||(GameDefinition.DecisionsExecutionOrder[decisionNumber].MaxEvolveRepetitions < stepNumber);
                if (skip)
                    UpdateHighestCumulativeDistributionUpdateIndexEvolved(dp);
                else
                    strategies[decisionNumber].StrategyStillToEvolveThisEvolveStep = true;
            }

            // Now, do the evolution, and again update the highest cumulative distribution.
            SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint = SimulationInteraction.CheckStopSoon();
            if (!SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint)
            {
                for (int dpIndex = startingDPIndex; dpIndex < GameDefinition.DecisionPointsEvolutionOrder.Count(); dpIndex++)
                {
                    prm.Info.EvolveDecisionPointIndex = dpIndex;
                    DecisionPoint dp = GameDefinition.DecisionPointsEvolutionOrder[dpIndex];
                    foreach (Tuple<string,List<int?>> tagList in GameDefinition.AutomaticRepetitionsIndexNumbersInEvolutionOrder)
                        if (tagList.Item2[dpIndex] != null)
                            GameDefinition.GameModules[(int)dp.ActionGroup.ModuleNumber].UpdateBasedOnTagInfo(
                                tagList.Item1, 
                                (int) tagList.Item2[dpIndex], 
                                tagList.Item2.Where(y => y != null).Max(y => (int) y), 
                                ref dp.Decision
                                );
                    SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint = SimulationInteraction.CheckStopSoon();
                    if (SimulationInteraction.StopAfterOptimizingCurrentDecisionPoint)
                        break;
                    int decisionNumber = (int)dp.DecisionNumber;
                    bool skip = (doNotEvolveByDefault && strategies[decisionNumber].StrategyDeserializedFromDisk && !dp.Decision.EvolveThisDecisionEvenWhenSkippingByDefault) || (!doNotEvolveByDefault && strategies[decisionNumber].StrategyDeserializedFromDisk && dp.Decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved) || (GameDefinition.DecisionsExecutionOrder[decisionNumber].MaxEvolveRepetitions < stepNumber);
                    if (!skip)
                    {
                        EvolveDecision(ref evolveSettings, theBaseOutputDirectory, stepNumber, totalSteps, isLastEvolveStep, ref numDecisionsEvolved, ref stop, setBreakWhenNumDecisionsEvolvedIs, setBreakAtGameNumberWithThatDecisionEvolution, decisionsAffectingProgressStep, decisionNumber, prm);
                        RunInterimReports(((EvolveCommand)SimulationInteraction.CurrentExecutionInformation.CurrentCommand).ReportsBetweenDecisions);
                        UpdateHighestCumulativeDistributionUpdateIndexEvolved(dp);
                    }
                    strategies[decisionNumber].StrategyStillToEvolveThisEvolveStep = false;
                }
            }

            foreach (var strategy in strategies)
                strategy.CyclesStrategyDevelopmentThisEvolveStep = 0; // reset this

            if (!doNotEvolveByDefault)
                RunInterimReports(((EvolveCommand)SimulationInteraction.CurrentExecutionInformation.CurrentCommand).ReportsAfterEvolveSteps);
        }

        private void UpdateHighestCumulativeDistributionUpdateIndexEvolved(DecisionPoint dp)
        {
            CumulativeDistributionUpdateInfo cdUpdateInfo = dp.ActionGroup.ActionGroupSettings as CumulativeDistributionUpdateInfo;
            if (cdUpdateInfo != null)
                if (SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved == null || cdUpdateInfo.UpdateIndex > SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved)
                    SimulationInteraction.HighestCumulativeDistributionUpdateIndexEvolved = cdUpdateInfo.UpdateIndex;
        }

        private void EvolveDecision(ref EvolutionSettings evolveSettings, string theBaseOutputDirectory, int stepNumber, int totalSteps, bool isLastEvolveStep, ref int numDecisionsEvolved, ref bool stop, int? setBreakWhenNumDecisionsEvolvedIs, int? setBreakAtGameNumberWithThatDecisionEvolution, int decisionsAffectingProgressStep, int decisionNumber, ProgressResumptionManager prm)
        {
            bool originalParallelOptimization = evolveSettings.ParallelOptimization;
            if (numDecisionsEvolved == setBreakWhenNumDecisionsEvolvedIs)
            {
                evolveSettings.ParallelOptimization = false;
                Game.BreakAtNumGamesPlayedDuringEvolutionOfThisDecision = setBreakAtGameNumberWithThatDecisionEvolution;
                Game.RestartFromBeginningOfGame = true; // should facilitate tracking down problems
            }
            Game.NumGamesPlayedDuringEvolutionOfThisDecision = 0;

            SimulationInteraction.CurrentExecutionInformation.InputSeedsSet.randomizationApproach = (GameDefinition.DecisionsExecutionOrder[decisionNumber].UseAlternativeGameInputs ? InputSeedsRandomization.useAlternate : InputSeedsRandomization.useOrdinary);
            SimulationInteraction.CurrentExecutionInformation.InputSeedsSet.enableInputMirroring = true; // todo: make this an option; right now, we do it when optimizing, but not when playing.

            SimulationInteraction.ReportDecisionNumber(decisionNumber + 1);
            int decisionSkipIndex = decisionNumber;
            bool skip;
            skip = false; // The skipping now takes place in the method calling this one. (decisionsToSkip != null && decisionsToSkip.Contains(decisionSkipIndex)) || (GameDefinition.DecisionsExecutionOrder[decisionNumber].MaxEvolveRepetitions < stepNumber);
            if (!skip)
            {
                EvolveDecisionNotSkipped(ref evolveSettings, theBaseOutputDirectory, stepNumber, totalSteps, isLastEvolveStep, ref stop, decisionsAffectingProgressStep, decisionNumber, evolveSettings.StopParallelOptimizationAfterOptimizingNDecisions >= 0 && numDecisionsEvolved > evolveSettings.StopParallelOptimizationAfterOptimizingNDecisions, prm);
            }
            numDecisionsEvolved++;
            evolveSettings.ParallelOptimization = originalParallelOptimization;
        }

        private void EvolveDecisionNotSkipped(ref EvolutionSettings evolveSettings, string theBaseOutputDirectory, int stepNumber, int totalSteps, bool isLastEvolveStep, ref bool stop, int decisionsAffectingProgressStep, int decisionNumber, bool disableParallelOptimization, ProgressResumptionManager prm)
        {
            string decisionName = GameDefinition.DecisionsExecutionOrder[decisionNumber].Name + GameDefinition.DecisionPointsExecutionOrder[decisionNumber].ActionGroup.RepetitionTagString();
            string reportString = String.Format("Decision index {0} ({1}) of 0..{2} in Step {3}... ", decisionNumber, decisionName, GameDefinition.DecisionsExecutionOrder.Count - 1, stepNumber);
            double evolveStepPctThisDecision = (double)stepNumber / (double)Math.Min(GameDefinition.DecisionsExecutionOrder[decisionNumber].MaxEvolveRepetitions, totalSteps);
            SimulationInteraction.ReportVariableFromProgram("EvolveStepPctThisDecision", evolveStepPctThisDecision);
            evolveSettings = SimulationInteraction.GetEvolutionSettings(); // update settings, since changing EvolveStepPctThisDecision can change intensity of optimization
            if (disableParallelOptimization)
                evolveSettings.ParallelOptimization = false;
            strategies[decisionNumber].EvolutionSettings = evolveSettings;
            SimulationInteraction.ReportTextToUser(reportString, true);
            TabbedText.WriteLine(reportString);
            TabbedText.Tabs++;

            // If we have been skipping past progress to resume where we left off, 
            // when we get here, it is time to load the strategy state and continue
            // where we left off.
            if (prm != null && prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
                ContinueProgressFromWhereLeftOff(decisionNumber, prm);
            strategies[decisionNumber].DevelopStrategy(false, prm, out stop); // This is where we actually want to do the evolution

            DecisionPoint dp = GameDefinition.DecisionPointForDecisionNumber(decisionNumber);
            GameModule module = GameDefinition.GameModules == null ? null : GameDefinition.GetOriginalGameModuleForDecisionNumber(decisionNumber);
            if (decisionsAffectingProgressStep > 0 && (module == null || !module.IgnoreWhenCountingProgress))
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "DecisionsWithinExecuteEvolveStep");

            bool doAll = isLastEvolveStep || GameDefinition.DecisionsExecutionOrder[decisionNumber].MaxEvolveRepetitions == stepNumber;
            if (doAll || strategies[decisionNumber].Decision.StrategyGraphInfos.Any(x => x.ReportAfterEachEvolutionStep))
                strategies[decisionNumber].Decision.AddToStrategyGraphs(theBaseOutputDirectory, true, true, strategies[decisionNumber], GameDefinition.DecisionPointsExecutionOrder[decisionNumber].ActionGroup);

            TabbedText.Tabs--;
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
                s.IStrategyComponentsInDevelopmentOrder = s.GetIStrategyComponentsInDevelopmentOrder();
                s.AllStrategies = strategies;
                s.ActiveInputGroupPlus = null;
            }
            prm.ProgressResumptionOption = ProgressResumptionOptions.ProceedNormallySavingPastProgress;
        }

        public void Play(int numberOfIterations, bool parallelReporting)
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
        }


    }
}
