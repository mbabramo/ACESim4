using ACESim;
using ACESim.Util;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract class Launcher
    {
        // IMPORTANT: Make sure to run in Release mode when not debugging.

        #region Settings

        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.RegretMatching; // DEBUG // use RegretMatching etc. for GeneralizedVanilla

        public const int VanillaIterations = 1000; // Note: Also used for GeneralizedVanilla, DeepCFR
        public const int VanillaReportEveryNIterations =  VanillaIterations; // DEBUG EffectivelyNever
        public int? SuppressReportBeforeIteration = null;
        public const int VanillaBestResponseEveryMIterations = VanillaIterations; 
        public int? SuppressBestResponseBeforeIteration = null; 
        public const bool CalculatePerturbedBestResponseRefinement = true;
        public const int MiniReportEveryPIterations = EffectivelyNever;
        public const bool AlwaysSuppressDisplayReportOnScreen = false; 
        public const int CorrelatedEquilibriumCalculationsEveryNIterations = EffectivelyNever;
        public const bool UseRandomPathsForReporting = false; // DEBUG
        public const int SummaryTableRandomPathsIterations = 1_000;
        public const int ProbingIterations = 20_000_000;

        public int MaxParallelDepth = 3;
        public bool ParallelizeOptionSets = false; // run multiple option sets at same time on computer (in which case each individually will be run not in parallel)
        public bool ParallelizeIndividualExecutions = false; // only if !ParallelizeOptionSets && (LaunchSingleOptionsSetOnly || !DistributedProcessing)
        public bool DynamicSetParallelIfPossible = false; 
        public bool DynamicSetParallel => DistributedProcessing && DynamicSetParallelIfPossible;
        public bool ParallelizeIndividualExecutionsAlways = false; // Note -- maybe not really working // will always take precedence

        public const int StartGameNumber = 1;
        public bool LaunchSingleOptionsSetOnly = false; // will be automatically set by ACESimConsole
        public int NumRepetitions = 1;
        public bool SaveToAzureBlob = false;
        public bool DistributedProcessing => !LaunchSingleOptionsSetOnly && UseDistributedProcessingForMultipleOptionsSets; // this should be true if running on the local service fabric or usign ACESimDistributed
        public string MasterReportNameForDistributedProcessing = "R362"; // ******* IMPORTANT: (1) Delete bin/obj in AceSimDistributed before building. (2) Must update this (or delete the Coordinator) when deploying service fabric. 
        public bool UseDistributedProcessingForMultipleOptionsSets = false; 
        public bool SeparateScenariosWhenUsingDistributedProcessing = true;
        public static bool MaxOneReportPerDistributedProcess = false;
        public bool CombineResultsOfAllOptionSetsAfterExecution = false;
        const int EffectivelyNever = EvolutionSettings.EffectivelyNever;

        #endregion

        #region Interface implementation

        public abstract GameDefinition GetGameDefinition();

        public abstract GameOptions GetSingleGameOptions();
        public abstract List<(string optionSetName, GameOptions options)> GetOptionsSets();

        #endregion

        #region Launching

        public async Task<ReportCollection> Launch()
        {
            ReportCollection result;
            if (LaunchSingleOptionsSetOnly)
                result = await Launch_Single();
            else
                result = await Launch_Multiple();
            return result;
        }

        public async Task<ReportCollection> Launch_Single()
        {
            var options = GetSingleGameOptions();
            ReportCollection reportCollection = await ProcessSingleOptionSetLocally(options, "Report", "Single", true, false);
            reportCollection.SaveLatestLocally();
            return reportCollection;
        }

        public async Task<ReportCollection> Launch_Multiple()
        {
            var optionSets = GetOptionsSets();
            var combined = DistributedProcessing ? await LaunchDistributedProcessingParticipation() : await ProcessAllOptionSetsLocally();
            return combined;
        }

        public IStrategiesDeveloper GetInitializedDeveloper(GameOptions options, string optionSetName)
        {
            GameDefinition gameDefinition = GetGameDefinition();
            gameDefinition.Setup(options);
            gameDefinition.OptionSetName = optionSetName;
            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            options.ModifyEvolutionSettings?.Invoke(evolutionSettings);
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            var developer = GetStrategiesDeveloper(starterStrategies, evolutionSettings, gameDefinition);
            return developer;
        }

        public IStrategiesDeveloper GetStrategiesDeveloper(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            TabbedText.WriteLine($"Using {evolutionSettings.Algorithm}"); // may differ from default algorithm if ModifyEvolutionSettings is set for game options
            TabbedText.WriteLine($"Game: {gameDefinition}");
            switch (evolutionSettings.Algorithm)
            {
                case GameApproximationAlgorithm.Vanilla:
                    return new VanillaCFR(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.GibsonProbing:
                    return new GibsonProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.ModifiedGibsonProbing:
                    return new ModifiedGibsonProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.MultiplicativeWeights:
                    return new GeneralizedVanilla(existingStrategyState, evolutionSettings, gameDefinition, new PostIterationUpdater_MultiplicativeWeights());
                case GameApproximationAlgorithm.RegretMatching:
                    return new GeneralizedVanilla(existingStrategyState, evolutionSettings, gameDefinition, new PostIterationUpdater_RegretMatching());
                case GameApproximationAlgorithm.AverageStrategySampling:
                    return new AverageStrategiesSampling(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.PureStrategyFinder:
                    return new PureStrategiesFinder(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.FictitiousPlay:
                    return new FictitiousPlay(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.BestResponseDynamics:
                    evolutionSettings.BestResponseDynamics = true;
                    return new FictitiousPlay(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.GreedyFictitiousPlay:
                    return new GreedyFictitiousPlay(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.GeneticAlgorithm:
                    return new GeneticAlgorithm(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.DeepCFR:
                    return new DeepCFR(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.SequenceForm:
                    return new SequenceForm(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.PlaybackOnly:
                    return new PlaybackOnly(existingStrategyState, evolutionSettings, gameDefinition);
                default:
                    throw new NotImplementedException();
            }
        }

        public EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                SaveToAzureBlob = SaveToAzureBlob,

                MaxParallelDepth = MaxParallelDepth,
                ParallelOptimization = ParallelizeIndividualExecutionsAlways ||
                            (ParallelizeIndividualExecutions && !ParallelizeOptionSets && (LaunchSingleOptionsSetOnly || !DistributedProcessing)),
                DynamicSetParallel = DynamicSetParallel,
                SuppressReportDisplayOnScreen = AlwaysSuppressDisplayReportOnScreen || (!LaunchSingleOptionsSetOnly && (ParallelizeOptionSets || DistributedProcessing)),

                GameNumber = StartGameNumber,

                Algorithm = Algorithm,

                UseRandomPathsForReporting = UseRandomPathsForReporting,
                ReportEveryNIterations = VanillaReportEveryNIterations,
                SuppressReportBeforeIteration = SuppressReportBeforeIteration,
                CorrelatedEquilibriumCalculationsEveryNIterations = CorrelatedEquilibriumCalculationsEveryNIterations,
                BestResponseEveryMIterations = VanillaBestResponseEveryMIterations, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games
                SuppressBestResponseBeforeIteration = SuppressBestResponseBeforeIteration,
                CalculatePerturbedBestResponseRefinement = CalculatePerturbedBestResponseRefinement,
                MiniReportEveryPIterations = MiniReportEveryPIterations,

                NumRandomIterationsForSummaryTable = SummaryTableRandomPathsIterations,
                GenerateReportsByPlaying = true,
                PrintInformationSets = false, 
                RestrictToTheseInformationSets = null, // new List<int>() {0, 34, 5, 12},
                PrintGameTree = false, 
                ActionStrategiesToUseInReporting =
                 new List<ActionStrategies>() {
                     //ActionStrategies.CorrelatedEquilibrium,
                     //ActionStrategies.BestResponseVsCorrelatedEquilibrium,
                     //ActionStrategies.CorrelatedEquilibriumVsBestResponse,
                     ActionStrategies.CurrentProbability, 
                     //ActionStrategies.AverageStrategy
                 },
                TotalProbingCFRIterations = ProbingIterations,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 10,
                TriggerIncreaseOverTime = 0,

                TotalAvgStrategySamplingCFRIterations = ProbingIterations,
                TotalIterations = VanillaIterations,
            };
            return evolutionSettings;
        }

        #endregion

        #region Distributed processing

        public async Task<ReportCollection> LaunchDistributedProcessingParticipation()
        {
            string masterReportName = MasterReportNameForDistributedProcessing;
            bool singleThread = false;
            if (singleThread)
            {
                await ParticipateInDistributedProcessing(masterReportName, new CancellationToken(false));
            }
            else
            {
                TabbedText.DisableOutput();
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < Environment.ProcessorCount - 1; i++)
                    tasks.Add(Task.Run(() => ParticipateInDistributedProcessing(masterReportName, new CancellationToken(false))));
                await Task.WhenAll(tasks.ToArray());
                TabbedText.EnableOutput();
            }
            var result = AzureBlob.GetBlobText("results", $"{masterReportName} AllCombined.csv");
            string folderFullName = ReportFolder();
            TextFileCreate.CreateTextFile(Path.Combine(folderFullName, "csvreport.csv"), result);
            return new ReportCollection("", result);
        }

        public static string ReportFolder()
        {
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            return folderFullName;
        }

        public async Task ParticipateInDistributedProcessing(string masterReportName, CancellationToken cancellationToken, Action<string> logAction = null)
        {
            InitializeTaskCoordinatorIfNecessary(masterReportName);
            IndividualTask taskToDo = null, taskCompleted = null;
            bool complete = false;
            if (logAction == null)
                logAction = s => Debug.WriteLine(s);
            logAction?.Invoke("Starting participation in distributed processing");
            TabbedText.WriteLine($"Starting participation in {MasterReportNameForDistributedProcessing}");
            while (!complete)
            {
                IndividualTask theCompletedTask = taskCompleted; // avoid problem with closure
                bool readyForAnotherTask = !cancellationToken.IsCancellationRequested;
                Stopwatch s = new Stopwatch();
                s.Start();
                // We are serializing the TaskCoordinator to synchronize information. Thus, we need to update the task coordinator to report that this job is complete. 
                AzureBlob.TransformSharedBlobOrFileObject(ReportFolder(), "results", masterReportName + " Coordinator", o =>
                {
                    TaskCoordinator taskCoordinator = (TaskCoordinator)o; // because of TransformSharedBlobObject, changes to this will be persisted
                    if (taskCoordinator == null)
                        throw new Exception("Corrupted or nonexistent task coordinator blob");

                    // Uncomment this to redo a particular completed step or steps so that it can be debugged without doing all earlier steps
                    //foreach (IndividualTask taskToChange in taskCoordinator.Stages.SelectMany(x => x.RepeatedTasks.SelectMany(y => y.IndividualTasks)))
                    //    if (taskToChange.TaskType == "CompletePCA")
                    //        taskToChange.Complete = false;

                    taskCoordinator.Update(theCompletedTask, readyForAnotherTask, out taskToDo, out complete);
                    TabbedText.WriteLineEvenIfDisabled($"");
                    TabbedText.WriteLineEvenIfDisabled($"Percentage Complete {100.0 * taskCoordinator.ProportionComplete}% of {taskCoordinator.IndividualTaskCount}");
                    if (taskToDo != null)
                        TabbedText.WriteLineEvenIfDisabled($"Task to do: {taskToDo}");
                    return taskCoordinator;
                }, SaveToAzureBlob);
                TabbedText.WriteLine($"Updated status (total {s.ElapsedMilliseconds} milliseconds)");
                if (!complete)
                {
                    if (taskToDo == null)
                    {
                        await Task.Delay(1000 * 60 * 1); // wait a minute for another task
                        TabbedText.WriteLine("Waiting before trying to find a task");
                    }
                    else
                    {
                        logAction($"Beginning task {taskToDo.TaskType} (ID {taskToDo.ID})");
                        await CompleteIndividualTask(masterReportName, taskToDo, logAction);
                        logAction($"Completed task {taskToDo.TaskType} (ID {taskToDo.ID})");
                        taskCompleted = taskToDo;
                    }
                }
                if (MaxOneReportPerDistributedProcess)
                    return;
            }
        }

        private async Task CompleteIndividualTask(string masterReportName, IndividualTask taskToDo, Action<string> logAction = null)
        {
            if (logAction == null)
                logAction = s => Debug.WriteLine(s);
            ReportCollection reportCollection = null;
            string optionSetName = null;
            if (taskToDo.TaskType == "Optimize")
            {
                IStrategiesDeveloper developer = GetDeveloper(taskToDo.ID);
                (reportCollection, optionSetName) = await GetSingleRepetitionReportAndSave(masterReportName, taskToDo.ID, taskToDo.Repetition, true, developer, taskToDo.RestrictToScenarioIndex, logAction);
            }
            else if (taskToDo.TaskType == "CombineRepetitions")
            {
                if (NumRepetitions > 1)
                    CombineResultsOfRepetitionsOfOptionSets(masterReportName, taskToDo.ID);
            }
            else if (taskToDo.TaskType == "CombineOptionSets")
            {
                if (CombineResultsOfAllOptionSetsAfterExecution)
                    CombineResultsOfAllOptionSets(masterReportName);
            }
            else if (taskToDo.TaskType == "CompletePCA")
            {
                await CompletePCATask(taskToDo);
            }
            else
                throw new NotImplementedException();
            AzureBlob.WriteTextToFileOrAzure("results", ReportFolder(), $"{masterReportName}-{(optionSetName != null ? optionSetName + taskToDo.RestrictToScenarioIndex?.ToString() : taskToDo.TaskType)}-log.txt", true, TabbedText.AccumulatedText.ToString(), SaveToAzureBlob);
            TabbedText.ResetAccumulated();
        }

        private async Task CompletePCATask(IndividualTask taskToDo)
        {
            IStrategiesDeveloper developer = GetDeveloper(taskToDo.ID); // note that this specifies the option set
            if (developer is StrategiesDeveloperBase strategiesDeveloper)
            {
                // must start developing a strategy for an iteration just to get information sets etc into memory. Then we will do the principal components analysis. This means that we don't want to save the model data from this iteration. 
                var firstOptionSet = GetOptionsSets().First();
                var evolutionSettings = GetEvolutionSettings();
                int totalIterations = evolutionSettings.TotalIterations;
                evolutionSettings.TotalIterations = 1;
                bool originalPerformPrincipalComponentAnalysis = evolutionSettings.PCA_PerformPrincipalComponentAnalysis;
                evolutionSettings.PCA_PerformPrincipalComponentAnalysis = false;
                await developer.DevelopStrategies(firstOptionSet.optionSetName, -1 /* it doesn't matter what scenario we do, because the real action is where we perform analysis below */, MasterReportNameForDistributedProcessing);
                evolutionSettings.TotalIterations = totalIterations;
                evolutionSettings.PCA_PerformPrincipalComponentAnalysis = originalPerformPrincipalComponentAnalysis;
                ReportCollection reportCollection = new ReportCollection();
                await strategiesDeveloper.RecoverSavedPCAModelDataAndPerformAnalysis(reportCollection, MasterReportNameForDistributedProcessing);
            }
        }

        private void InitializeTaskCoordinatorIfNecessary(string masterReportName)
        {
            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            int optionSetsCount = optionSets.Count();
            int? scenarios = null;
            if (DistributedProcessing && SeparateScenariosWhenUsingDistributedProcessing)
                scenarios = GetGameDefinition().NumScenarioPermutations;
            var taskStages = new List<TaskStage>()
            {
                new TaskStage(Enumerable.Range(0, optionSetsCount).Select(x => new RepeatedTask("Optimize", x, NumRepetitions, scenarios)).ToList())
            };
            if (DistributedProcessing && SeparateScenariosWhenUsingDistributedProcessing)
                taskStages.Add(new TaskStage(Enumerable.Range(0, optionSetsCount).Select(x => new RepeatedTask("CompletePCA", x, 1, null)).ToList()));
            if (NumRepetitions > 1)
                taskStages.Add(new TaskStage(Enumerable.Range(0, optionSetsCount).Select(x => new RepeatedTask("CombineRepetitions", x, 1, null)).ToList()));
            if (optionSetsCount > 1)
                taskStages.Add(new TaskStage(Enumerable.Range(0, 1).Select(x => new RepeatedTask("CombineOptionSets", x, 1, null) { AvoidRedundantExecution = true }).ToList()));
            TaskCoordinator tasks = new TaskCoordinator(taskStages);
            var result = AzureBlob.TransformSharedBlobOrFileObject(ReportFolder(), "results", masterReportName + " Coordinator", o =>
            {
                if (o == null)
                    return tasks; // create a new file
                return null; // this will leave the existing file unchanged, and shortly we'll look at the existing file
            }, SaveToAzureBlob); // return null if the task coordinator object is already created
            //if (result != null)
            //    Debug.WriteLine(result);
        }

        private bool SaveLastDeveloperOnThread = false; // NOTE: If true, we get memory leaks
        private Dictionary<int, (int optionSetIndex, IStrategiesDeveloper developer)> LastDeveloperOnThread = new Dictionary<int, (int optionSetIndex, IStrategiesDeveloper developer)>();
        private object LockObj = new object();

        private IStrategiesDeveloper GetDeveloper(int optionSetIndex)
        {
            lock (LockObj)
            {
                int currentThreadID = Thread.CurrentThread.ManagedThreadId;

                if (LastDeveloperOnThread.ContainsKey(currentThreadID) && LastDeveloperOnThread[currentThreadID].optionSetIndex == optionSetIndex)
                    return LastDeveloperOnThread[currentThreadID].developer;
                var optionSet = GetOptionsSets()[optionSetIndex];
                var options = optionSet.options;
                LastDeveloperOnThread[currentThreadID] = (optionSetIndex, GetInitializedDeveloper(options, optionSet.optionSetName));
                var result = LastDeveloperOnThread[currentThreadID].developer;
                if (!SaveLastDeveloperOnThread)
                    LastDeveloperOnThread = new Dictionary<int, (int optionSetIndex, IStrategiesDeveloper developer)>();
                return result;
            }
        }

        #endregion

        #region Combining report results

        public string CombineResultsOfAllOptionSets(string masterReportName)
        {
            return CombineResultsOfAllOptionSets(masterReportName, null);
        }

        public string CombineResultsOfAllOptionSets(string masterReportName, List<string> results)
        {
            ReportCollection reportCollection = new ReportCollection();
            if (results == null)
            { // load all the Azure blobs to get the results to combine; this is useful if we haven't done all the results consecutively
                List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
                foreach (var optionSet in optionSets)
                {
                    string masterReportNamePlusOptionSet = $"{masterReportName} {optionSet.optionSetName}";
                    string csvResult = AzureBlob.GetBlobText("results", masterReportNamePlusOptionSet + ".csv");
                    reportCollection.Add("", csvResult);
                }
            }
            // it is possible that we have multiple csvReports -- meaning that different simulations produced
            // different first lines. that's OK, we need to output all of them.
            string combinedResults = reportCollection.csvReports.FirstOrDefault();
            AzureBlob.WriteTextToFileOrAzure("results", ReportFolder(), $"{masterReportName} AllCombined.csv", true, combinedResults, SaveToAzureBlob);
            return combinedResults;
        }

        public string CombineResultsOfRepetitionsOfOptionSets(string masterReportName, int optionSetIndex)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            var optionSetName = optionSet.optionSetName;
            return CombineResultsOfRepetitionsOfOptionSets(masterReportName, optionSetName, includeFirstLine, null);
        }

        public string CombineResultsOfRepetitionsOfOptionSets(string masterReportName, string optionSetName, bool includeFirstLine, List<string> combinedReports)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            if (combinedReports == null)
            { // load all the Azure blobs to get the results to combine; this is useful if we haven't done all the results consecutively
                combinedReports = new List<string>();
                for (int repetition = 0; repetition < NumRepetitions; repetition++)
                {
                    string specificRepetitionReportName = masterReportNamePlusOptionSet + $" {repetition}";
                    string result = AzureBlob.GetBlobText("results", specificRepetitionReportName + ".csv");
                    combinedReports.Add(result);
                }
            }
            string combinedRepetitionsReport = String.Join("", combinedReports);
            string mergedReport = SimpleReportMerging.GetDistributionReports(combinedRepetitionsReport, optionSetName, includeFirstLine);
            AzureBlob.WriteTextToFileOrAzure("results", ReportFolder(), masterReportNamePlusOptionSet, true, mergedReport + ".csv", SaveToAzureBlob);
            TabbedText.ResetAccumulated();
            return mergedReport;
        }

        #endregion

        #region Local processing

        private object LocalOptionSetsLock = new object();

        public async Task<ReportCollection> ProcessAllOptionSetsLocally()
        {
            string masterReportName = DateTime.Now.ToString("yyyy-MM-dd HH-mm"); // avoid colons and slashes

            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;
            ReportCollection results = new ReportCollection();

            async Task SingleOptionSetAction(long index)
            {
                var optionSet = optionSets[(int)index];
                TabbedText.WriteLine($"Option set {index} of {optionSets.Count()}: {optionSet.optionSetName}");
                var optionSetResults = await ProcessSingleOptionSet(masterReportName, (int)index, true);
                lock (LocalOptionSetsLock)
                    results.Add(optionSetResults);
            }

            Parallelizer.MaxDegreeOfParallelism = Environment.ProcessorCount;
            if (ParallelizeOptionSets)
            {
                TabbedText.WriteLine("Suppressing output due to parallelization");
                TabbedText.DisableOutput(); // TODO: We could put each parallel item in a separate output
            }
            await Parallelizer.GoAsync(ParallelizeOptionSets, 0, optionSets.Count, SingleOptionSetAction);
            return results;
        }

        private async Task<ReportCollection> ProcessSingleOptionSet(string masterReportName, int optionSetIndex, bool addOptionSetColumns)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            return await ProcessSingleOptionSetLocally(options, masterReportName, optionSet.optionSetName, includeFirstLine, addOptionSetColumns);
        }

        private async Task<ReportCollection> ProcessSingleOptionSetLocally(GameOptions options, string masterReportName, string optionSetName, bool includeFirstLine, bool addOptionSetColumns)
        {
            var developer = GetInitializedDeveloper(options, optionSetName);
            developer.EvolutionSettings.GameNumber = StartGameNumber;
            ReportCollection result = new ReportCollection();
            for (int i = 0; i < NumRepetitions; i++)
            {
                var repetitionReport = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSetName, i, addOptionSetColumns, developer, null);
                result.Add(repetitionReport);
            }
            return result;
        }

        private async Task<(ReportCollection report, string optionSetName)> GetSingleRepetitionReportAndSave(string masterReportName, int optionSetIndex, int repetition, bool addOptionSetColumns, IStrategiesDeveloper developer, int? restrictToScenarioIndex, Action<string> logAction = null)
        {
            if (logAction == null)
                logAction = s => Debug.WriteLine(s);
            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            var result = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSets[optionSetIndex].optionSetName, repetition, addOptionSetColumns, developer, restrictToScenarioIndex, logAction);
            return (result, optionSets[optionSetIndex].optionSetName);
        }

        private async Task<ReportCollection> GetSingleRepetitionReportAndSave(string masterReportName, GameOptions options, string optionSetName, int repetition, bool addOptionSetColumns, IStrategiesDeveloper developer, int? restrictToScenarioIndex, Action<string> logAction = null)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            if (logAction == null)
                logAction = s => Debug.WriteLine(s);
            if (developer == null)
                throw new Exception("Developer must be set"); // should call GetDeveloper(options) before calling this (note: earlier version passed developer as ref so that it could be set here)
            try
            {
                var result = await GetSingleRepetitionReport(optionSetName, repetition, addOptionSetColumns, developer, restrictToScenarioIndex, logAction);
                logAction("Writing report to blob");
                if (result.csvReports.Any())
                {
                    for (int c = 0; c < result.csvReports.Count; c++)
                    {
                        string reportName = masterReportNamePlusOptionSet ?? "results";
                        if (result.ReportNames.Count() > c && result.ReportNames[c] is (not null) and string reportName2)
                            reportName += "-" + reportName2;

                        AzureBlob.WriteTextToFileOrAzure("results", ReportFolder(), reportName + ".csv", true, result.csvReports[c], SaveToAzureBlob); // we write to a blob in case this times out and also to allow individual report to be taken out
                    }
                }
                logAction("Report written to blob");
                return result;
            }
            catch (Exception ex)
            {
                logAction(ex.Message + ex.StackTrace);
                throw;
            }
        }

        private async Task<ReportCollection> GetSingleRepetitionReport(string optionSetName, int i, bool addOptionSetColumns, IStrategiesDeveloper developer, int? restrictToScenarioIndex, Action<string> logAction = null)
        {
            if (logAction == null)
                logAction = s => Debug.WriteLine(s);
            developer.EvolutionSettings.GameNumber = StartGameNumber + i;
            string reportIteration = i.ToString();
            if (i > 0)
                developer.Reinitialize();
            ReportCollection reportCollection = new ReportCollection();
        retry:
            try
            {
                reportCollection = await developer.DevelopStrategies(optionSetName, restrictToScenarioIndex, MasterReportNameForDistributedProcessing);
            }
            catch (Exception e)
            {
                logAction(e.Message + e.StackTrace);
                TabbedText.WriteLine($"Error: {e}");
                TabbedText.WriteLine(e.StackTrace);
                goto retry;
            }
            string singleRepetitionReport = addOptionSetColumns ? SimpleReportMerging.AddCSVReportInformationColumns(reportCollection.csvReports.FirstOrDefault(), optionSetName, reportIteration, i == 0) : reportCollection.csvReports.FirstOrDefault(); 
            return reportCollection;
        }

        #endregion

        #region Azure functions

        public async Task<string> ProcessAllOptionSetsOnAzureFunctions()
        {
            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;

            TabbedText.WriteLine($"Number of option sets: {optionSets.Count} repetitions {numRepetitionsPerOptionSet} => {optionSets.Count * numRepetitionsPerOptionSet}");
            TabbedText.WriteLine("IMPORTANT: This will run on Azure. Have you published to Azure? Press G to continue on Azure.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.G);
            TabbedText.WriteLine("Processing on Azure...");

            string azureBlobReportName = "Report" + DateTime.Now.ToString("yyyy-MM-dd HH-mm");  // avoid colons and slashes

            List<Task<string>> tasks = new List<Task<string>>();
            for (int i = 0; i < optionSets.Count; i++)
            {
                tasks.Add(ProcessSingleOptionSet_AzureFunctions(i, azureBlobReportName));
            }
            var taskResults = await Task.WhenAll(tasks);
            List<string> stringResults = new List<string>();
            foreach (var taskResult in taskResults)
                stringResults.Add(taskResult);
            string combinedResults = String.Join("", stringResults);
            AzureBlob.WriteTextToFileOrAzure("results", ReportFolder(), azureBlobReportName + " allsets.csv", true, combinedResults, SaveToAzureBlob);
            return combinedResults;
        }

        public async Task<string> ProcessSingleOptionSet_AzureFunctions(int optionSetIndex, string azureBlobReportName)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            string optionSetName = optionSets[optionSetIndex].optionSetName;
            int numRepetitionsPerOptionSet = NumRepetitions;
            var developer = GetInitializedDeveloper(options, optionSetName);
            developer.EvolutionSettings.GameNumber = StartGameNumber;
            string[] combinedReports = new string[NumRepetitions];
            List<Task<string>> tasks = new List<Task<string>>();
            for (int i = 0; i < NumRepetitions; i++)
            {
                tasks.Add(GetSingleRepetitionReport_AzureFunctions(optionSetIndex, i, azureBlobReportName));
            }
            await Task.WhenAll(tasks);

            for (int i = 0; i < NumRepetitions; i++)
                combinedReports[i] = tasks[i].Result;
            string combinedRepetitionsReport = String.Join("", combinedReports);

            string mergedReport = SimpleReportMerging.GetDistributionReports(combinedRepetitionsReport, optionSetName, includeFirstLine);
            return mergedReport;
        }

        public async Task<string> GetSingleRepetitionReport_AzureFunctions(int optionSetIndex, int repetition, string azureBlobReportName)
        {
            string apiURL2 = "https://acesimfuncs.azurewebsites.net/api/GetReport?code=GbM1qaVgKmlBFvbzMGzInPjMTuGmdsfzoMfV6K//wJVv811t4sFbnQ==&clientId=default";
            int retryInterval = 10; // start with 10 milliseconds delay after first failure
            const int maxAttempts = 5;
            AzureFunctionResult result = null;
            for (int attempt = 1; attempt <= maxAttempts; ++attempt)
            {
                try
                {
                    string azureBlobInterimReportName = azureBlobReportName + $" I{optionSetIndex}:{repetition}";
                    var task = Util.RunAzureFunction.RunFunction(apiURL2, new { optionSet = $"{optionSetIndex}", repetition = $"{repetition}", azureBlobReportName = azureBlobReportName }, azureBlobInterimReportName);
                    result = await task;
                    if (result.Success)
                    {
                        TabbedText.WriteLine($"Successfully processed {optionSetIndex}:{repetition}");
                        return result.Info;
                    }
                    else
                    {
                        TabbedText.WriteLine($"Failure on {optionSetIndex}:{repetition} attempt {attempt} message {result.Info}");
                    }
                }
                catch
                {
                }

                System.Threading.Thread.Sleep(retryInterval);

                retryInterval *= 2;
            }

            TabbedText.WriteLine($"Complete failure on {optionSetIndex}:{repetition} message {result?.Info}");
            return ""; // just return empty string on failure

            // The following simulates the basic algorithm without actually using Azure.
            // Task<string> t = Task<string>.Factory.StartNew(() => GetSingleRepetitionReport(optionSetIndex, repetition));
            // return await t;
        }
        #endregion
    }
}
