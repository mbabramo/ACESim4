using ACESim;
using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract class Launcher
    {
        // IMPORTANT: Make sure to run in Release mode when not debugging.

        #region Settings

        public GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.HedgeVanilla;

        public const int ProbingIterations = 20_000_000;
        public const int VanillaIterations = 50_000;
        public const int VanillaReportEveryNIterations = VanillaIterations;
        public const int VanillaBestResponseEveryMIterations = 1000;
        public const int MiniReportEveryPIterations = 1000; 
        public const int CorrelatedEquilibriumCalculationsEveryNIterations = EvolutionSettings.EffectivelyNever; // DEBUG
        public const int RecordPastValuesEveryNIterations = 100; // DEBUG // used for correlated equilibrium calculations
        public const bool UseRandomPathsForReporting = true; 
        public const int SummaryTableRandomPathsIterations = 10_000;

        public const bool UseRegretAndStrategyDiscounting = true;

        public const int StartGameNumber = 1;
        public bool SingleGameMode = true;
        public int NumRepetitions = 1;

        public bool AzureEnabled = false;

        public bool LocalDistributedProcessing = true; // this should be false if actually running on service fabric
        public bool ParallelizeOptionSets = false;
        public bool ParallelizeIndividualExecutions = true; // only affects SingleGameMode or if no local distributed processing

        public string OverrideDateTimeString = null; // "2017-10-11 10:18"; // use this if termination finished unexpectedly
        public string MasterReportNameForDistributedProcessing = "AMONLY";

        #endregion

        #region Interface implementation

        public abstract GameDefinition GetGameDefinition();

        public abstract GameOptions GetSingleGameOptions();
        public abstract List<(string reportName, GameOptions options)> GetOptionsSets();

        #endregion

        #region Launching

        public async Task<string> Launch()
        {
            string result;
            if (SingleGameMode)
                result = await Launch_Single();
            else
                result = await Launch_Multiple();
            Console.WriteLine(result);
            return result;
        }

        public async Task<string> Launch_Single()
        {
            var options = GetSingleGameOptions();
            string report = "";
            await ProcessSingleOptionSet(options, "Report", "Single", true);
            return report;
        }

        public async Task<string> Launch_Multiple()
        {
            var optionSets = GetOptionsSets();
            string combined = LocalDistributedProcessing ? await SimulateDistributedProcessingAlgorithm() : await ProcessAllOptionSetsLocally();
            return combined;
        }

        public IStrategiesDeveloper GetInitializedDeveloper(GameOptions options)
        {
            GameDefinition gameDefinition = GetGameDefinition();
            gameDefinition.Setup(options);
            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            var developer = GetStrategiesDeveloper(starterStrategies, evolutionSettings, gameDefinition);
            return developer;
        }

        public IStrategiesDeveloper GetStrategiesDeveloper(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            Console.WriteLine($"Using {Algorithm}");
            switch (Algorithm)
            {
                case GameApproximationAlgorithm.Vanilla:
                    return new VanillaCFR(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.GibsonProbing:
                    return new GibsonProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.ModifiedGibsonProbing:
                    return new ModifiedGibsonProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.ExploratoryProbing:
                    return new ExploratoryProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.HedgeProbing:
                    return new HedgeProbing(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.HedgeVanilla:
                    return new HedgeVanilla(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.AverageStrategySampling:
                    return new AverageStrategiesSampling(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.PureStrategyFinder:
                    return new PureStrategiesFinder(existingStrategyState, evolutionSettings, gameDefinition);
                case GameApproximationAlgorithm.FictitiousSelfPlay:
                    return new FictitiousSelfPlay(existingStrategyState, evolutionSettings, gameDefinition);
                default:
                    throw new NotImplementedException();
            }
        }

        public EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 3, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = ParallelizeIndividualExecutions && !ParallelizeOptionSets && (SingleGameMode || !LocalDistributedProcessing),
                SuppressReportPrinting = !SingleGameMode && (ParallelizeOptionSets || LocalDistributedProcessing),

                GameNumber = StartGameNumber,

                Algorithm = Algorithm,

                UseRandomPathsForReporting = UseRandomPathsForReporting,
                ReportEveryNIterations = Algorithm == GameApproximationAlgorithm.ExploratoryProbing ? 500_000 : VanillaReportEveryNIterations,
                CorrelatedEquilibriumCalculationsEveryNIterations = CorrelatedEquilibriumCalculationsEveryNIterations,
                BestResponseEveryMIterations = Algorithm == GameApproximationAlgorithm.ExploratoryProbing ? EvolutionSettings.EffectivelyNever : VanillaBestResponseEveryMIterations, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games
                MiniReportEveryPIterations = Algorithm == GameApproximationAlgorithm.ExploratoryProbing ? EvolutionSettings.EffectivelyNever : MiniReportEveryPIterations,
                RecordPastValuesEveryN = RecordPastValuesEveryNIterations,
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
                     ActionStrategies.AverageStrategy
                 },
                TotalProbingCFRIterations = ProbingIterations,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 10,
                TriggerIncreaseOverTime = 0,

                UseDiscounting = UseRegretAndStrategyDiscounting,

                TotalAvgStrategySamplingCFRIterations = ProbingIterations,
                TotalVanillaCFRIterations = VanillaIterations,
            };
            return evolutionSettings;
        }

        #endregion

        #region Distributed processing

        public async Task<string> SimulateDistributedProcessingAlgorithm()
        {
            string dateTimeString = OverrideDateTimeString ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string masterReportName = MasterReportNameForDistributedProcessing + " " + dateTimeString;
            bool singleThread = false;
            if (singleThread)
            {
                await ParticipateInDistributedProcessing(masterReportName, new CancellationToken(false));
            }
            else
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < Environment.ProcessorCount; i++)
                    tasks.Add(Task.Run(() => ParticipateInDistributedProcessing(masterReportName, new CancellationToken(false))));
                await Task.WhenAll(tasks.ToArray());
            }
            var result = AzureBlob.GetBlobText("results", $"{masterReportName} AllCombined");
            return result;
        }

        public async Task ParticipateInDistributedProcessing(string masterReportName, CancellationToken cancellationToken, Action actionEachTime = null)
        {
            InitiateNonLocalOptionSetsProcessing(masterReportName);
            IndividualTask taskToDo = null, taskCompleted = null;
            bool complete = false;
            while (!complete)
            {
                actionEachTime?.Invoke();
                IndividualTask theCompletedTask = taskCompleted; // avoid problem with closure
                var blockBlob = AzureBlob.GetLeasedBlockBlob("results", masterReportName + " Coordinator", true);
                bool readyForAnotherTask = !cancellationToken.IsCancellationRequested;
                AzureBlob.TransformSharedBlobObject(blockBlob.blob, blockBlob.lease, o =>
                {
                    TaskCoordinator taskCoordinator = (TaskCoordinator)o;
                    if (taskCoordinator == null)
                        throw new NotImplementedException();
                    Debug.WriteLine(taskCoordinator);
                    taskCoordinator.Update(theCompletedTask, readyForAnotherTask, out taskToDo);
                    Console.WriteLine($"Percentage Complete {100.0 * taskCoordinator.ProportionComplete}%");
                    if (taskToDo != null)
                        Debug.WriteLine($"Task to do: {taskToDo}");
                    return taskCoordinator;
                });
                complete = taskToDo == null;
                if (!complete)
                {
                    await CompleteIndividualTask(masterReportName, taskToDo);
                    Debug.WriteLine($"Completed task {taskToDo.Name} {taskToDo.ID}");
                    taskCompleted = taskToDo;
                }
            }
        }

        public async Task CompleteIndividualTask(string masterReportName, IndividualTask taskToDo)
        {
            if (taskToDo.Name == "Optimize")
            {
                IStrategiesDeveloper developer = GetDeveloper(taskToDo.ID);
                await GetSingleRepetitionReportAndSave(masterReportName, taskToDo.ID, taskToDo.Repetition, developer);
            }
            else if (taskToDo.Name == "CombineRepetitions")
            {
                CombineResultsOfRepetitionsOfOptionSets(masterReportName, taskToDo.ID);
            }
            else if (taskToDo.Name == "CombineOptionSets")
            {
                CombineResultsOfAllOptionSets(masterReportName);
            }
            else
                throw new NotImplementedException();
        }

        public void InitiateNonLocalOptionSetsProcessing(string masterReportName)
        {
            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            int optionSetsCount = optionSets.Count();
            TaskCoordinator tasks = new TaskCoordinator(new List<TaskStage>()
            {
                new TaskStage(Enumerable.Range(0, optionSetsCount).Select(x => new RepeatedTask("Optimize", x, NumRepetitions)).ToList()),
                new TaskStage(Enumerable.Range(0, optionSetsCount).Select(x => new RepeatedTask("CombineRepetitions", x, 1)).ToList()),
                new TaskStage(Enumerable.Range(0, 1).Select(x => new RepeatedTask("CombineOptionSets", x, 1)).ToList()),
            });
            var blockBlob = AzureBlob.GetLeasedBlockBlob("results", masterReportName + " Coordinator", true);
            var result = AzureBlob.TransformSharedBlobObject(blockBlob.blob, blockBlob.lease, o => o == null ? tasks : null); // return null if the object is already created
            //if (result != null)
            //    Debug.WriteLine(result);
        }



        public async Task<string> ProcessAllOptionSetsLocally()
        {
            string masterReportName = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;
            string[] results = new string[optionSets.Count];

            async Task SingleOptionSetAction(long index)
            {
                var optionSet = optionSets[(int)index];
                string optionSetResults = await ProcessSingleOptionSet(masterReportName, (int)index);
                results[index] = optionSetResults;
            }

            Parallelizer.MaxDegreeOfParallelism = Environment.ProcessorCount;
            await Parallelizer.GoAsync(ParallelizeOptionSets, 0, optionSets.Count, SingleOptionSetAction);
            string combinedResults = CombineResultsOfAllOptionSets(masterReportName, results.ToList());
            return combinedResults;
        }

        public async Task<string> ProcessSingleOptionSet(string masterReportName, int optionSetIndex)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            return await ProcessSingleOptionSet(options, masterReportName, optionSet.reportName, includeFirstLine);
        }

        public async Task<string> ProcessSingleOptionSet(GameOptions options, string masterReportName, string optionSetName, bool includeFirstLine)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            var developer = GetInitializedDeveloper(options);
            developer.EvolutionSettings.GameNumber = StartGameNumber;
            List<string> combinedReports = new List<string>();
            for (int i = 0; i < NumRepetitions; i++)
            {
                string singleRepetitionReport = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSetName, i, developer);
                combinedReports.Add(singleRepetitionReport);
                // AzureBlob.SerializeObject("results", reportName + " CRM", true, developer);
            }
            if (AzureEnabled)
                return CombineResultsOfRepetitionsOfOptionSets(masterReportName, optionSetName, includeFirstLine, combinedReports);
            else
                return "";
        }


        public async Task<string> GetSingleRepetitionReportAndSave(string masterReportName, int optionSetIndex, int repetition, IStrategiesDeveloper developer)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string reportName, GameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            int numRepetitionsPerOptionSet = NumRepetitions;
            string result = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSets[optionSetIndex].reportName, repetition, developer);
            return result;
        }
        public async Task<string> GetSingleRepetitionReportAndSave(string masterReportName, GameOptions options, string optionSetName, int repetition, IStrategiesDeveloper developer)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            if (developer == null)
                throw new Exception("Developer must be set"); // should call GetDeveloper(options) before calling this (note: earlier version passed developer as ref so that it could be set here)
            var result = await GetSingleRepetitionReport(optionSetName, repetition, developer);
            string azureBlobInterimReportName = masterReportNamePlusOptionSet + $" {repetition}";
            if (AzureEnabled)
                AzureBlob.WriteTextToBlob("results", azureBlobInterimReportName, true, result); // we write to a blob in case this times out and also to allow individual report to be taken out
            return result;
        }

        public async Task<string> GetSingleRepetitionReport(string optionSetName, int i, IStrategiesDeveloper developer)
        {
            developer.EvolutionSettings.GameNumber = StartGameNumber + i;
            string reportIteration = i.ToString();
            if (i > 0)
                developer.Reinitialize();
            string report;
        retry:
            try
            {
                report = await developer.DevelopStrategies(optionSetName);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
                Console.WriteLine(e.StackTrace);
                goto retry;
            }
            string singleRepetitionReport = SimpleReportMerging.AddReportInformationColumns(report, optionSetName, reportIteration, i == 0);
            return singleRepetitionReport;
        }

        public string CombineResultsOfAllOptionSets(string masterReportName)
        {
            return CombineResultsOfAllOptionSets(masterReportName, null);
        }

        public string CombineResultsOfAllOptionSets(string masterReportName, List<string> results)
        {
            if (results == null)
            { // load all the Azure blobs to get the results to combine; this is useful if we haven't done all the results consecutively
                results = new List<string>();
                List<(string optionSetName, GameOptions options)> optionSets = GetOptionsSets();
                foreach (var optionSet in optionSets)
                {
                    string masterReportNamePlusOptionSet = $"{masterReportName} {optionSet.optionSetName}";
                    string result = AzureBlob.GetBlobText("results", masterReportNamePlusOptionSet);
                    results.Add(result);
                }
            }
            string combinedResults = String.Join("", results);
            AzureBlob.WriteTextToBlob("results", $"{masterReportName} AllCombined", true, combinedResults);
            return combinedResults;
        }

        public string CombineResultsOfRepetitionsOfOptionSets(string masterReportName, int optionSetIndex)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            var optionSetName = optionSet.reportName;
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
                    string result = AzureBlob.GetBlobText("results", specificRepetitionReportName);
                    combinedReports.Add(result);
                }
            }
            string combinedRepetitionsReport = String.Join("", combinedReports);
            string mergedReport = SimpleReportMerging.GetMergedReports(combinedRepetitionsReport, optionSetName, includeFirstLine);
            AzureBlob.WriteTextToBlob("results", masterReportNamePlusOptionSet, true, mergedReport);
            return mergedReport;
        }

        public Dictionary<int, (int optionSetIndex, IStrategiesDeveloper developer)> LastDeveloperOnThread = new Dictionary<int, (int optionSetIndex, IStrategiesDeveloper developer)>();
        public object LockObj = new object();

        public IStrategiesDeveloper GetDeveloper(int optionSetIndex)
        {
            lock (LockObj)
            {
                int currentThreadID = Thread.CurrentThread.ManagedThreadId;

                if (LastDeveloperOnThread.ContainsKey(currentThreadID) && LastDeveloperOnThread[currentThreadID].optionSetIndex == optionSetIndex)
                    return LastDeveloperOnThread[currentThreadID].developer;
                var optionSet = GetOptionsSets()[optionSetIndex];
                var options = optionSet.options;
                LastDeveloperOnThread[currentThreadID] = (optionSetIndex, GetInitializedDeveloper(options));
                return LastDeveloperOnThread[currentThreadID].developer;
            }
        }

        #endregion

        #region Azure functions

        public async Task<string> ProcessAllOptionSetsOnAzureFunctions()
        {
            List<(string reportName, GameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;

            Console.WriteLine($"Number of option sets: {optionSets.Count} repetitions {numRepetitionsPerOptionSet} => {optionSets.Count * numRepetitionsPerOptionSet}");
            Console.WriteLine("IMPORTANT: This will run on Azure. Have you published to Azure? Press G to continue on Azure.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.G);
            Console.WriteLine("Processing on Azure...");

            string azureBlobReportName = "Report" + DateTime.Now.ToString("yyyy-MM-dd HH:mm");

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
            AzureBlob.WriteTextToBlob("results", azureBlobReportName + " allsets", true, combinedResults);
            return combinedResults;
        }

        public async Task<string> ProcessSingleOptionSet_AzureFunctions(int optionSetIndex, string azureBlobReportName)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string reportName, GameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            string reportName = optionSets[optionSetIndex].reportName;
            int numRepetitionsPerOptionSet = NumRepetitions;
            var developer = GetInitializedDeveloper(options);
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

            string mergedReport = SimpleReportMerging.GetMergedReports(combinedRepetitionsReport, reportName, includeFirstLine);
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
                        Console.WriteLine($"Successfully processed {optionSetIndex}:{repetition}");
                        return result.Info;
                    }
                    else
                    {
                        Console.WriteLine($"Failure on {optionSetIndex}:{repetition} attempt {attempt} message {result.Info}");
                    }
                }
                catch
                {
                }

                System.Threading.Thread.Sleep(retryInterval);

                retryInterval *= 2;
            }

            Console.WriteLine($"Complete failure on {optionSetIndex}:{repetition} message {result?.Info}");
            return ""; // just return empty string on failure

            // The following simulates the basic algorithm without actually using Azure.
            // Task<string> t = Task<string>.Factory.StartNew(() => GetSingleRepetitionReport(optionSetIndex, repetition));
            // return await t;
        }
        #endregion
    }
}
