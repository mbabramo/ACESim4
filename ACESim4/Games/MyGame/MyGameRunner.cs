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


        private static EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 1, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = true,

                InitialRandomSeed = 100,

                Algorithm = GameApproximationAlgorithm.AbramowiczProbing,

                ReportEveryNIterations = 100_000,
                NumRandomIterationsForSummaryTable = 5000,
                PrintSummaryTable = true,
                PrintInformationSets = false,
                RestrictToTheseInformationSets = null, // new List<int>() {0, 34, 5, 12},
                PrintGameTree = false,
                AlwaysUseAverageStrategyInReporting = false,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games

                TotalProbingCFRIterations = 100_000,
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
            var options = MyGameOptionsGenerator.Standard();
            options.MyGameDisputeGenerator = new MyGameDiscriminationDisputeGenerator();
            //options.MyGameDisputeGenerator = new MyGameAppropriationDisputeGenerator();
            //options.MyGameDisputeGenerator = new MyGameContractDisputeGenerator();
            //options.MyGameDisputeGenerator = new MyGameEqualQualityProbabilitiesDisputeGenerator()
            //{
            //    ProbabilityTrulyLiable_LitigationQuality75 = 0.75,
            //    ProbabilityTrulyLiable_LitigationQuality90 = 0.90,
            //    NumPointsToDetermineTrulyLiable = 100,
            //};
            //options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            //{
            //    ExogenousProbabilityTrulyLiable = 0.5,
            //    StdevNoiseToProduceLitigationQuality = 0.5
            //};
            //var options = MyGameOptionsGenerator.UsingRawSignals_10Points_1Round();
            options.LoserPays = true;
            string brRuleReport = PerformEvolution(options, "British", false);
            Debug.WriteLine(brRuleReport);
            options.LoserPays = false;
            string amRuleReport = PerformEvolution(options, "American", true);
            Debug.WriteLine(amRuleReport);
            string combined = amRuleReport + brRuleReport;
            return combined;
        }

        private static string PerformEvolution(MyGameOptions options, string reportName, bool includeFirstLine)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            const int numRepetitions = 3;
            string cumulativeReport = "";
            for (int i = 0; i < numRepetitions; i++)
            {
                string reportIteration = i.ToString();
                CounterfactualRegretMaximization developer =
                    new CounterfactualRegretMaximization(starterStrategies, evolutionSettings, gameDefinition);
                string report = developer.DevelopStrategies();
                string differentiatedReport = SimpleReportMerging.AddReportInformationColumns(report, reportName, reportIteration, i == 0);
                cumulativeReport += differentiatedReport;
            }
            Debug.WriteLine(cumulativeReport);
            string mergedReport = SimpleReportMerging.GetMergedReports(cumulativeReport, reportName, includeFirstLine);
            return mergedReport;
        }
    }
}
