using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameRunner
    {

        private const int StartGameNumber = 1;
        private static bool SingleGameMode = false;
        private static int NumRepetitions = 5;

        private static EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 1, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = true,

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
                StdevNoiseToProduceLitigationQuality = 0.1
            };
            // options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (MyGameActionsGenerator.PBetsHeavilyWithGoodSignal, "PBetsHeavilyWithGoodSignal") };
            //options.IncludeSignalsReport = true;
            //options.IncludeCourtSuccessReport = true;
            string report = RepeatedlyOptimizeLocal(options, "Report", true, StartGameNumber, NumRepetitions);
            return report;
        }

        public static string EvolveMyGame_Multiple()
        {
            List <(string reportName, MyGameOptions options)> optionSets = new List<(string reportName, MyGameOptions options)>();

            foreach (IMyGameDisputeGenerator d in new IMyGameDisputeGenerator[]
            {
                //new MyGameNegligenceDisputeGenerator(),
                //new MyGameAppropriationDisputeGenerator(), 
                //new MyGameContractDisputeGenerator(), 
                //new MyGameDiscriminationDisputeGenerator(), 
                new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.3
                }
            })
            {
                var options = MyGameOptionsGenerator.Standard();
                options.MyGameDisputeGenerator = d;
                optionSets.AddRange(GetOptionsVariations(d.GetGeneratorName(), () => options));
            }
            string combined = RepeatedlyOptimize(optionSets, NumRepetitions);
            return combined;
        }

        private static List<(string reportName, MyGameOptions options)> GetOptionsVariations(string description, Func<MyGameOptions> initialOptionsFunc)
        {
            var list = new List<(string reportName, MyGameOptions options)>();
            MyGameOptions options;

            options = initialOptionsFunc();
            options.MyGameRunningSideBets = new MyGameRunningSideBets()
            {
                MaxChipsPerRound = 2,
                ValueOfChip = 50000,
                CountAllChipsInAbandoningRound = true
            };
            list.Add((description + " RunSide", options));

            options = initialOptionsFunc();
            options.MyGameRunningSideBets = new MyGameRunningSideBets()
            {
                MaxChipsPerRound = 2,
                ValueOfChip = 50000,
                CountAllChipsInAbandoningRound = false
            };
            list.Add((description + " RunSideEscap", options));

            options = initialOptionsFunc();
            options.MyGameRunningSideBets = new MyGameRunningSideBets()
            {
                MaxChipsPerRound = 2,
                ValueOfChip = 100000,
                CountAllChipsInAbandoningRound = true
            };
            options.CostsMultiplier = 1.0;
            list.Add((description + " RunSideLarge", options));

            options = initialOptionsFunc();
            options.MyGameRunningSideBets = new MyGameRunningSideBets()
            {
                MaxChipsPerRound = 2,
                ValueOfChip = 50000
            };
            options.CostsMultiplier = 2.0;
            list.Add((description + " RunSideExp", options));

            options = initialOptionsFunc();
            options.LoserPays = true;
            options.LoserPaysMultiple = 1.0;
            options.LoserPaysAfterAbandonment = false;
            options.IncludeAgreementToBargainDecisions = true;
            options.CostsMultiplier = 1.0;
            list.Add((description + " British", options));

            options = initialOptionsFunc();
            list.Add((description + " American", options));

            return list;
        }

        private static string RepeatedlyOptimize(List<(string reportName, MyGameOptions options)> optionSets, int numRepetitionsPerOptionSet)
        {
            Debug; // make an option that can parallelize each option set.
            List<string> results = new List<string>();
            for (var index = 0; index < optionSets.Count; index++)
            {
                var optionSet = optionSets[index];
                results.Add(RepeatedlyOptimizeLocal(optionSet.options, optionSet.reportName, index == 0, StartGameNumber, NumRepetitions));
            }
            string combinedResults = String.Join("", results);
            return combinedResults;
        }

        private static string RepeatedlyOptimizeLocal(MyGameOptions options, string reportName, bool includeFirstLine, int startGameNumber, int numRepetitions)
        {
            if (options.IncludeCourtSuccessReport || options.IncludeSignalsReport)
                if (NumRepetitions > 1)
                    throw new Exception("Can include multiple reports only with 1 repetition. Use console output rather than string copied."); // problem is that we can't merge the reports if NumRepetitions > 1 when we have more than one report. TODO: Fix this. 
            var developer = GetDeveloper(options);
            developer.EvolutionSettings.GameNumber = startGameNumber;
            List<string> combinedReports = new List<string>();
            for (int i = 0; i < numRepetitions; i++)
            {
                if (i > 0)
                    developer.EvolutionSettings.GameNumber++;
                string singleRepetitionReport = GetSingleRepetitionReport(reportName, i, developer);
                combinedReports.Add(singleRepetitionReport);
            }
            string combinedRepetitionsReport = String.Join("", combinedReports);
            string mergedReport = SimpleReportMerging.GetMergedReports(combinedRepetitionsReport, reportName, includeFirstLine);
            return mergedReport;
        }

        private static string GetSingleRepetitionReport(string reportName, int i, CounterfactualRegretMaximization developer)
        {
            string reportIteration = i.ToString();
            if (i > 0)
                developer.Reinitialize();
            string report;
            retry:
            try
            {
                Console.WriteLine(reportName);
                report = developer.DevelopStrategies();
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
