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
    public static class MultiRoundCooperationGameRunner
    {
        public static int StartGameNumber = 10000;
        public static int NumRepetitions = 10;
        public const bool UseRegretAndStrategyDiscounting = true;

        private static EvolutionSettings GetEvolutionSettings()
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 1, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = false, // Gibson may not properly support parallel
                SuppressReportPrinting = false,

                GameNumber = StartGameNumber,

                Algorithm = GameApproximationAlgorithm.HedgeVanilla,
                
                UseRandomPathsForBestResponse = false,
                ReportEveryNIterations = 1000,
                NumRandomIterationsForSummaryTable = 1_000,
                GenerateReportsByPlaying = true,
                PrintInformationSets = false,
                RestrictToTheseInformationSets = null, // new List<int>() {0, 34, 5, 12},
                PrintGameTree = false,
                AlwaysUseAverageStrategyInReporting = true, // IMPORTANT NOTE: Using average strategy here
                BestResponseEveryMIterations = 1000, //EvolutionSettings.EffectivelyNever, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games

                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.5,
                MinBackupRegretsTrigger = 10,
                TriggerIncreaseOverTime = 0,

                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalProbingCFRIterations = 10000000,
                TotalVanillaCFRIterations = 100_000,

                // algorithm settings
                UseRegretAndStrategyDiscounting = UseRegretAndStrategyDiscounting,
            };
            return evolutionSettings;
        }

        public static string EvolveGame()
        {
             string result = ProcessSingleOptionSet_Serial("MultiRoundCooperation", true, StartGameNumber, NumRepetitions);
            Console.WriteLine(result);
            return result;
        }

        private static string ProcessSingleOptionSet_Serial(string reportName, bool includeFirstLine, int startGameNumber, int numRepetitions)
        { 
            var developer = GetDeveloper();
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
        
        private static string GetSingleRepetitionReport(string reportName, int i, CounterfactualRegretMinimization developer)
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
            if (report == null)
                return report; // Gibson probing doesn't send back report.
            string singleRepetitionReport = SimpleReportMerging.AddReportInformationColumns(report, reportName, reportIteration, i == 0);
            return singleRepetitionReport;
        }

        private static CounterfactualRegretMinimization GetDeveloper()
        {
            MultiRoundCooperationGameDefinition gameDefinition = new MultiRoundCooperationGameDefinition();
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
    }
}
