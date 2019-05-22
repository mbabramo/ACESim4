﻿using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameRunner
    {
        // IMPORTANT: Make sure to run in Release mode when not debugging.
        private static bool HigherRiskAversion = false;
        private static bool PRiskAverse = false;
        public static bool DRiskAverse = false;
        public static bool TestDisputeGeneratorVariations = false;
        public static bool IncludeRunningSideBetVariations = false;
        public static bool LimitToAmerican = true;
        public static double[] CostsMultipliers = new double[] { 1.0 }; // 0.1, 0.25, 0.5, 1.0, 1.5, 2.0, 4.0 };
        public const double StdevPlayerNoise = 0.3; // baseline is 0.3

        private const GameApproximationAlgorithm Algorithm = GameApproximationAlgorithm.HedgeVanilla;

        private const int ProbingIterations = 20_000_000;
        private const int VanillaIterations = 250_000;
        private const int VanillaReportEveryNIterations = 25_000;
        private const int VanillaBestResponseEveryMIterations = EvolutionSettings.EffectivelyNever; 
        private const int MiniReportEveryPIterations = 5000;
        private const int CorrelatedEquilibriumCalculationsEveryNIterations = 25_000;
        private const int RecordPastValuesEveryNIterations = 1000; // used for correlated equilibrium calculations
        private const bool UseRandomPathsForReporting = true;
        private const int SummaryTableRandomPathsIterations = 2_000;
        
        private const bool UseRegretAndStrategyDiscounting = true;

        private const int StartGameNumber = 1;
        private static bool SingleGameMode = true;
        private static int NumRepetitions = 1;

        private static bool LocalDistributedProcessing = true; // this should be false if actually running on service fabric
        public static string OverrideDateTimeString = null; // "2017-10-11 10:18"; // use this if termination finished unexpectedly
        public static string MasterReportNameForDistributedProcessing = "AMONLY";
        private static bool ParallelizeOptionSets = false;
        private static bool ParallelizeIndividualExecutions = true; // only affects SingleGameMode or if no local distributed processing

        private static EvolutionSettings GetEvolutionSettings()
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
                     //ActionStrategies.AverageStrategy
                 },
                TotalProbingCFRIterations = ProbingIterations,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 10,
                TriggerIncreaseOverTime = 0,

                UseRegretAndStrategyDiscounting = UseRegretAndStrategyDiscounting,

                TotalAvgStrategySamplingCFRIterations = ProbingIterations,
                TotalVanillaCFRIterations = VanillaIterations,
            };
            return evolutionSettings;
        }

        public static async Task<string> EvolveMyGame()
        {
            string result;
            if (SingleGameMode)
                result = await EvolveMyGame_Single();
            else
                result = await EvolveMyGame_Multiple();
            Console.WriteLine(result);
            return result;
        }

        public static async Task<string> EvolveMyGame_Single()
        {
            var options = MyGameOptionsGenerator.SingleRound();
            options.LoserPays = false;
            options.CostsMultiplier = 1.0;
            options.IncludeSignalsReport = false;
            options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 0.5,
                StdevNoiseToProduceLitigationQuality = 0.3
            };
            options.PNoiseStdev = options.DNoiseStdev = StdevPlayerNoise;
            //options.NumSignals = options.NumLitigationQualityPoints = options.NumOffers = 3; 
            //options.NumPotentialBargainingRounds = 1;
            //options.MyGameRunningSideBets = new MyGameRunningSideBets()
            //{
            //    MaxChipsPerRound = 2,
            //    ValueOfChip = 50000,
            //    CountAllChipsInAbandoningRound = true,
            //    TrialCostsMultiplierAsymptote = 3.0,
            //    TrialCostsMultiplierWithDoubleStakes = 1.3,
            //};
            // options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (MyGameActionsGenerator.PBetsHeavilyWithGoodSignal, "PBetsHeavilyWithGoodSignal") };
            //options.IncludeSignalsReport = true;
            //options.IncludeCourtSuccessReport = true;
            string report = "";
            await ProcessSingleOptionSet(options, "Report", "Single", true);
            return report;
        }

        public static async Task<string> EvolveMyGame_Multiple()
        {
            var optionSets = GetOptionsSets();
            string combined = LocalDistributedProcessing ? await SimulateDistributedProcessingAlgorithm() : await ProcessAllOptionSetsLocally();
            return combined;
        }

        private static List<(string reportName, MyGameOptions options)> GetOptionsSets()
        {
            List<(string reportName, MyGameOptions options)> optionSets = new List<(string reportName, MyGameOptions options)>();

            List<IMyGameDisputeGenerator> disputeGenerators;

            if (TestDisputeGeneratorVariations)
                disputeGenerators = new List<IMyGameDisputeGenerator>()
                {
                    new MyGameNegligenceDisputeGenerator(),
                    new MyGameAppropriationDisputeGenerator(),
                    new MyGameContractDisputeGenerator(),
                    new MyGameDiscriminationDisputeGenerator(),
                };
            else
                disputeGenerators = new List<IMyGameDisputeGenerator>()
                {
                    new MyGameExogenousDisputeGenerator()
                    {
                        ExogenousProbabilityTrulyLiable = 0.5,
                        StdevNoiseToProduceLitigationQuality = 0.3
                    }
                };

            foreach (double costMultiplier in CostsMultipliers)
                foreach (IMyGameDisputeGenerator d in disputeGenerators)
                {
                    var options = MyGameOptionsGenerator.BaseForMultipleOptionsSets();
                    options.PNoiseStdev = options.DNoiseStdev = StdevPlayerNoise;
                    options.CostsMultiplier = costMultiplier;
                    if (HigherRiskAversion)
                    {
                        if (PRiskAverse)
                            options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { Alpha = 10.0 / 1000000.0 };
                        if (DRiskAverse)
                            options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { Alpha = 10.0 / 1000000.0 };
                    }
                    else
                    {
                        if (PRiskAverse)
                            options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
                        if (DRiskAverse)
                            options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };
                    }
                    options.MyGameDisputeGenerator = d;
                    string generatorName = d.GetGeneratorName();
                    string fullName = generatorName;
                    if (costMultiplier != 1)
                        fullName += $" costs {costMultiplier}";
                    optionSets.AddRange(GetOptionsVariations(fullName, () => options));
                }
            return optionSets;
        }

        private static List<(string reportName, MyGameOptions options)> GetOptionsVariations(string description, Func<MyGameOptions> initialOptionsFunc)
        {
            var list = new List<(string reportName, MyGameOptions options)>();
            MyGameOptions options;

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSide", options));
            }

            if (IncludeRunningSideBetVariations)
            {
                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = false,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSideEscap", options));

                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 100000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSideLarge", options));

                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 2.0,
                };
                list.Add((description + " RunSideExp", options));
            }

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.LoserPays = true;
                options.LoserPaysMultiple = 1.0;
                options.LoserPaysAfterAbandonment = false;
                options.IncludeAgreementToBargainDecisions = true;
                list.Add((description + " British", options));
            }

            options = initialOptionsFunc();
            list.Add((description + " American", options));

            return list;
        }

        private static async Task<string> ProcessAllOptionSetsLocally()
        {
            string masterReportName = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<(string optionSetName, MyGameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;
            string[] results = new string[optionSets.Count];

            async Task SingleOptionSetAction(long index)
            {
                var optionSet = optionSets[(int) index];
                string optionSetResults = await ProcessSingleOptionSet(masterReportName, (int) index);
                results[index] = optionSetResults;
            }

            Parallelizer.MaxDegreeOfParallelism = Environment.ProcessorCount;
            await Parallelizer.GoAsync(ParallelizeOptionSets, 0, optionSets.Count, SingleOptionSetAction);
            string combinedResults = CombineResultsOfAllOptionSets(masterReportName, results.ToList());
            return combinedResults;
        }

        private static async Task<string> ProcessSingleOptionSet(string masterReportName, int optionSetIndex)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            return await ProcessSingleOptionSet(options, masterReportName, optionSet.reportName, includeFirstLine);
        }

        private static async Task<string> ProcessSingleOptionSet(MyGameOptions options, string masterReportName, string optionSetName, bool includeFirstLine)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            if (options.IncludeCourtSuccessReport || options.IncludeSignalsReport)
                if (NumRepetitions > 1)
                    throw new Exception("Can include multiple reports only with 1 repetition. Use console output rather than string copied."); // problem is that we can't merge the reports if NumRepetitions > 1 when we have more than one report.  
            var developer = GetDeveloper(options);
            developer.EvolutionSettings.GameNumber = StartGameNumber;
            List<string> combinedReports = new List<string>();
            for (int i = 0; i < NumRepetitions; i++)
            {
                string singleRepetitionReport = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSetName, i, developer);
                combinedReports.Add(singleRepetitionReport);
                // AzureBlob.SerializeObject("results", reportName + " CRM", true, developer);
            }
            if (azureEnabled)
                return CombineResultsOfRepetitionsOfOptionSets(masterReportName, optionSetName, includeFirstLine, combinedReports);
            else
                return "";
        }


        public static async Task<string> GetSingleRepetitionReportAndSave(string masterReportName, int optionSetIndex, int repetition, CounterfactualRegretMinimization developer)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            int numRepetitionsPerOptionSet = NumRepetitions;
            string result = await GetSingleRepetitionReportAndSave(masterReportName, options, optionSets[optionSetIndex].reportName, repetition, developer);
            return result;
        }

        static bool azureEnabled = false; // DEBUG
        public static async Task<string> GetSingleRepetitionReportAndSave(string masterReportName, MyGameOptions options, string optionSetName, int repetition, CounterfactualRegretMinimization developer)
        {
            string masterReportNamePlusOptionSet = $"{masterReportName} {optionSetName}";
            if (developer == null)
                throw new Exception("Developer must be set"); // should call GetDeveloper(options) before calling this (note: earlier version passed developer as ref so that it could be set here)
            var result = await GetSingleRepetitionReport(optionSetName, repetition, developer);
            string azureBlobInterimReportName = masterReportNamePlusOptionSet + $" {repetition}";
            if (azureEnabled)
                AzureBlob.WriteTextToBlob("results", azureBlobInterimReportName, true, result); // we write to a blob in case this times out and also to allow individual report to be taken out
            return result;
        }

        private static async Task<string> GetSingleRepetitionReport(string optionSetName, int i, CounterfactualRegretMinimization developer)
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

        public static string CombineResultsOfAllOptionSets(string masterReportName)
        {
            return CombineResultsOfAllOptionSets(masterReportName, null);
        }

        private static string CombineResultsOfAllOptionSets(string masterReportName, List<string> results)
        {
            if (results == null)
            { // load all the Azure blobs to get the results to combine; this is useful if we haven't done all the results consecutively
                results = new List<string>();
                List<(string optionSetName, MyGameOptions options)> optionSets = GetOptionsSets();
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

        public static string CombineResultsOfRepetitionsOfOptionSets(string masterReportName, int optionSetIndex)
        {
            bool includeFirstLine = optionSetIndex == 0;
            var optionSet = GetOptionsSets()[optionSetIndex];
            var options = optionSet.options;
            var optionSetName = optionSet.reportName;
            return CombineResultsOfRepetitionsOfOptionSets(masterReportName, optionSetName, includeFirstLine, null);
        }

        private static string CombineResultsOfRepetitionsOfOptionSets(string masterReportName, string optionSetName, bool includeFirstLine, List<string> combinedReports)
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

        public static Dictionary<int, (int optionSetIndex, CounterfactualRegretMinimization developer)> LastDeveloperOnThread = new Dictionary<int, (int optionSetIndex, CounterfactualRegretMinimization developer)>();
        public static object LockObj = new object();
        private static CounterfactualRegretMinimization GetDeveloper(int optionSetIndex)
        {
            lock (LockObj)
            {
                int currentThreadID = Thread.CurrentThread.ManagedThreadId;

                if (LastDeveloperOnThread.ContainsKey(currentThreadID) && LastDeveloperOnThread[currentThreadID].optionSetIndex == optionSetIndex)
                    return LastDeveloperOnThread[currentThreadID].developer;
                var optionSet = GetOptionsSets()[optionSetIndex];
                var options = optionSet.options;
                LastDeveloperOnThread[currentThreadID] = (optionSetIndex, GetDeveloper(options));
                return LastDeveloperOnThread[currentThreadID].developer;
            }
        }

        private static CounterfactualRegretMinimization GetDeveloper(MyGameOptions options)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            CounterfactualRegretMinimization developer =
                new CounterfactualRegretMinimization(starterStrategies, evolutionSettings, gameDefinition);
            return developer;
        }

        #region Azure distributed processing

        private static async Task<string> SimulateDistributedProcessingAlgorithm()
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

        public static async Task ParticipateInDistributedProcessing(string masterReportName, CancellationToken cancellationToken, Action actionEachTime = null)
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

        private static async Task CompleteIndividualTask(string masterReportName, IndividualTask taskToDo)
        {
            if (taskToDo.Name == "Optimize")
            {
                CounterfactualRegretMinimization developer = GetDeveloper(taskToDo.ID);
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

        private static void InitiateNonLocalOptionSetsProcessing(string masterReportName)
        {
            List<(string optionSetName, MyGameOptions options)> optionSets = GetOptionsSets();
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

        #endregion

        #region Azure functions

        private static async Task<string> ProcessAllOptionSetsOnAzureFunctions()
        {
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;


            if (optionSets.Any(x => x.options.IncludeCourtSuccessReport || x.options.IncludeSignalsReport))
                throw new Exception("Multiple supports not supported with Azure option.");

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

        private static async Task<string> ProcessSingleOptionSet_AzureFunctions(int optionSetIndex, string azureBlobReportName)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            string reportName = optionSets[optionSetIndex].reportName;
            int numRepetitionsPerOptionSet = NumRepetitions;
            var developer = GetDeveloper(options);
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

        private async static Task<string> GetSingleRepetitionReport_AzureFunctions(int optionSetIndex, int repetition, string azureBlobReportName)
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

        // The following is used by the test classes
        public static MyGameProgress PlayMyGameOnce(MyGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
            MyGameProgress gameProgress = (MyGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
