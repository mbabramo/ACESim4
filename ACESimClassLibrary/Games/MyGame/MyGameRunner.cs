using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public static class MyGameRunner
    {
        private const bool PRiskAverse = false;
        public const bool DRiskAverse = false;
        public const bool TestDisputeGeneratorVariations = false; 
        public const bool IncludeRunningSideBetVariations = false; 
        public const bool LimitToAmerican = false;
        public const double CostsMultiplier = 2.0; // DEBUG

        private const int StartGameNumber = 1;
        private static bool SingleGameMode = false;
        private static int NumRepetitions = 30;
        private static bool UseAzure = false; // MAKE SURE TO UPDATE THE FUNCTION APP AND CHECK THE NUMBER OF ITERATIONS, REPETITIONS, ETC. (NOTE: NOT REALLY FULLY WORKING.)
        private static bool ParallelizeOptionSets = true; 
        private static bool ParallelizeIndividualExecutions = false;

        private static EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 1, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = !UseAzure && ParallelizeIndividualExecutions && !ParallelizeOptionSets,
                SuppressReportPrinting = !SingleGameMode && ParallelizeOptionSets,

                GameNumber = StartGameNumber,

                Algorithm = GameApproximationAlgorithm.AbramowiczProbing,

                ReportEveryNIterations = 10_000,
                NumRandomIterationsForSummaryTable = 10_000,
                PrintSummaryTable = true,
                PrintInformationSets = false,
                RestrictToTheseInformationSets = null, // new List<int>() {0, 34, 5, 12},
                PrintGameTree = false,
                AlwaysUseAverageStrategyInReporting = false,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games

                TotalProbingCFRIterations = 10_000,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 3,
                TriggerIncreaseOverTime = 0,

                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalVanillaCFRIterations = 100_000_000,
            };
            return evolutionSettings;
        }

        public static string EvolveMyGame()
        {
            string result;
            if (SingleGameMode)
                result = EvolveMyGame_Single();
            else
                result = EvolveMyGame_Multiple();
            Console.WriteLine(result);
            return result;
        }

        public static string EvolveMyGame_Single()
        {
            var options = MyGameOptionsGenerator.Standard();
            options.LoserPays = false;
            options.MyGameRunningSideBets = new MyGameRunningSideBets()
            {
                MaxChipsPerRound = 2,
                ValueOfChip = 50000,
                CountAllChipsInAbandoningRound = true
            };
            options.CostsMultiplier = 1.0;
            options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 0.5,
                StdevNoiseToProduceLitigationQuality = 0.3
            };
            // options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (MyGameActionsGenerator.PBetsHeavilyWithGoodSignal, "PBetsHeavilyWithGoodSignal") };
            //options.IncludeSignalsReport = true;
            //options.IncludeCourtSuccessReport = true;
            string report = ProcessSingleOptionSet_Serial(options, "Report", true, StartGameNumber, NumRepetitions);
            return report;
        }

        public static string EvolveMyGame_Multiple()
        {
            var optionSets = GetOptionsSets();
            if (UseAzure)
            {
                string combined = ProcessAllOptionSetsOnAzure().GetAwaiter().GetResult();
                return combined;
            }
            else
            {
                string combined = ProcessAllOptionSetsLocally();
                return combined;
            }
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

            foreach (IMyGameDisputeGenerator d in disputeGenerators)
            {
                var options = MyGameOptionsGenerator.Standard();
                options.CostsMultiplier = CostsMultiplier;
                if (PRiskAverse)
                    options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() {InitialWealth = options.PInitialWealth};
                if (DRiskAverse)
                    options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() {InitialWealth = options.DInitialWealth};
                options.MyGameDisputeGenerator = d;
                optionSets.AddRange(GetOptionsVariations(d.GetGeneratorName(), () => options));
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
                options.CostsMultiplier = 1.0;
                list.Add((description + " British", options));
            }

            options = initialOptionsFunc();
            list.Add((description + " American", options));

            return list;
        }

        private static string ProcessAllOptionSetsLocally()
        {
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;
            string[] results = new string[optionSets.Count];

            void SingleOptionSetAction(int index)
            {
                var optionSet = optionSets[index];
                string optionSetResults = ProcessSingleOptionSet_Serial(optionSet.options, optionSet.reportName, index == 0, StartGameNumber, numRepetitionsPerOptionSet);
                results[index] = optionSetResults;
            }

            Parallelizer.MaxDegreeOfParallelism = Environment.ProcessorCount;
            Parallelizer.Go(ParallelizeOptionSets, 0, optionSets.Count, (Action<int>) SingleOptionSetAction);
            string combinedResults = String.Join("", results);
            AzureBlob.WriteTextToBlob("results", DateTime.Now.ToString("yyyy-MM-dd-HH-mm"), true, combinedResults);
            return combinedResults;
        }

        private static async Task<string> ProcessAllOptionSetsOnAzure()
        {
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            int numRepetitionsPerOptionSet = NumRepetitions;


            if (optionSets.Any(x => x.options.IncludeCourtSuccessReport || x.options.IncludeSignalsReport))
                throw new Exception("Multiple supports not supported with Azure option.");

            Console.WriteLine($"Number of option sets: {optionSets.Count} repetitions {numRepetitionsPerOptionSet} => {optionSets.Count*numRepetitionsPerOptionSet}");
            Console.WriteLine("IMPORTANT: This will run on Azure. Have you published to Azure? Press G to continue on Azure.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.G);
            Console.WriteLine("Processing on Azure...");

            string azureBlobReportName = "Report" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm");

            List<Task<string>> tasks = new List<Task<string>>();
            for (int i = 0; i < optionSets.Count; i++)
            {
                tasks.Add(ProcessSingleOptionSet_Azure(i, azureBlobReportName));
            }
            var taskResults = await Task.WhenAll(tasks);
            List<string> stringResults = new List<string>();
            foreach (var taskResult in taskResults)
                stringResults.Add(taskResult);
            string combinedResults = String.Join("", stringResults);
            AzureBlob.WriteTextToBlob("results", azureBlobReportName, true, combinedResults);
            return combinedResults;
        }

        private static string ProcessSingleOptionSet_Serial(MyGameOptions options, string reportName, bool includeFirstLine, int startGameNumber, int numRepetitions)
        {
            if (options.IncludeCourtSuccessReport || options.IncludeSignalsReport)
                if (NumRepetitions > 1)
                    throw new Exception("Can include multiple reports only with 1 repetition. Use console output rather than string copied."); // problem is that we can't merge the reports if NumRepetitions > 1 when we have more than one report.  
            var developer = GetDeveloper(options);
            developer.EvolutionSettings.GameNumber = startGameNumber;
            List<string> combinedReports = new List<string>();
            for (int i = 0; i < numRepetitions; i++)
            {
                string singleRepetitionReport = GetSingleRepetitionReport(reportName, i, developer);
                combinedReports.Add(singleRepetitionReport);
            }
            string combinedRepetitionsReport = String.Join("", combinedReports);
            string mergedReport = SimpleReportMerging.GetMergedReports(combinedRepetitionsReport, reportName, includeFirstLine);
            return mergedReport;
        }

        private static async Task<string> ProcessSingleOptionSet_Azure(int optionSetIndex, string azureBlobReportName)
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
                tasks.Add(GetSingleRepetitionReport_Azure(optionSetIndex, i, azureBlobReportName));
            }
            await Task.WhenAll(tasks);

            for (int i = 0; i < NumRepetitions; i++)
                combinedReports[i] = tasks[i].Result;
            string combinedRepetitionsReport = String.Join("", combinedReports);
            try // DEBUG
            {
                string mergedReport = SimpleReportMerging.GetMergedReports(combinedRepetitionsReport, reportName, includeFirstLine);
                return mergedReport;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async static Task<string> GetSingleRepetitionReport_Azure(int optionSetIndex, int repetition, string azureBlobReportName)
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

        public static string GetSingleRepetitionReport(int optionSetIndex, int repetition, string azureBlobReportName)
        {
            bool includeFirstLine = optionSetIndex == 0;
            List<(string reportName, MyGameOptions options)> optionSets = GetOptionsSets();
            var options = optionSets[optionSetIndex].options;
            string reportName = optionSets[optionSetIndex].reportName;
            int numRepetitionsPerOptionSet = NumRepetitions;
            string result = GetSingleRepetitionReport(options, reportName, repetition);
            string azureBlobInterimReportName = azureBlobReportName + $" I{optionSetIndex}:{repetition}";
            AzureBlob.WriteTextToBlob("results", azureBlobInterimReportName, true, result); // we write to a blob in case this times out and also to allow individual report to be taken out
            return result;
        }

        private static string GetSingleRepetitionReport(MyGameOptions options, string reportName, int i)
        {
            return GetSingleRepetitionReport(reportName, i, GetDeveloper(options));
        }

        private static string GetSingleRepetitionReport(string reportName, int i, CounterfactualRegretMaximization developer)
        {
            developer.EvolutionSettings.GameNumber = StartGameNumber + i;
            string reportIteration = i.ToString();
            if (i > 0)
                developer.Reinitialize();
            string report;
            retry:
            try
            {
                report = developer.DevelopStrategies(reportName);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
                Console.WriteLine(e.StackTrace);
                goto retry;
            }
            string singleRepetitionReport = SimpleReportMerging.AddReportInformationColumns(report, reportName, reportIteration, i == 0);
            return singleRepetitionReport;
        }

        private static CounterfactualRegretMaximization GetDeveloper(MyGameOptions options)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            CounterfactualRegretMaximization developer =
                new CounterfactualRegretMaximization(starterStrategies, evolutionSettings, gameDefinition);
            return developer;
        }

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
