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
                NumRandomIterationsForSummaryTable = 5_000,
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
            bool single = false;
            if (single)
                return EvolveMyGame_Single();
            else
                return EvolveMyGame_Multiple();
        }

        public static string EvolveMyGame_Single()
        {
            var options = MyGameOptionsGenerator.Standard();
            options.MyGameDisputeGenerator = new MyGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 0.5,
                StdevNoiseToProduceLitigationQuality = 0.5
            };
            options.LoserPays = true;
            options.LoserPaysMultiple = 10.0;
            options.LoserPaysAfterAbandonment = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.MyGamePretrialDecisionGeneratorGenerator = null;
            //options.AdditionalTableOverrides = new List<(Func<Decision, GameProgress, byte>, string)>() { (MyGameActionsGenerator.PGivesNoGroundWithMaxSignal, "PGivesNoGroundWithMaxSignal") };
            //options.MyGamePretrialDecisionGeneratorGenerator = new MyGameSideBet() { DamagesMultipleForChallengedToPay = 6.0, DamagesMultipleForChallengerToPay = 6.0 };
            //options.IncludeSignalsReport = true;
            //options.IncludeCourtSuccessReport = true;
            string sideBetReport = PerformEvolution(options, "SideBet", true);
            return sideBetReport;
        }

        static int NumRepetitions = 10;

        public static string EvolveMyGame_Multiple()
        {
            var options = MyGameOptionsGenerator.Standard();
            string combined = "";
            foreach (IMyGameDisputeGenerator d in new IMyGameDisputeGenerator[]
            {
                //new MyGameNegligenceDisputeGenerator(),
                //new MyGameAppropriationDisputeGenerator(), 
                //new MyGameContractDisputeGenerator(), 
                //new MyGameDiscriminationDisputeGenerator(), 
                new MyGameExogenousDisputeGenerator()
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLitigationQuality = 0.5
                }
            })
            {
                string generatorString = d.GetGeneratorName();
                options.MyGameDisputeGenerator = d;
                combined += ApplyDifferentRegimes(options, generatorString) + "\n";
            }
            return combined;
        }

        private static string ApplyDifferentRegimes(MyGameOptions options, string description)
        {
            string amRuleReport = "", brRuleReport = "", brPlusReport ="", sideBetReport = "", sideBetLargeReport = "";
            options.LoserPays = true;
            options.LoserPaysMultiple = 1.0;
            options.LoserPaysAfterAbandonment = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.MyGamePretrialDecisionGeneratorGenerator = null;
            brRuleReport = PerformEvolution(options, description + " British", false);
            Debug.WriteLine(brRuleReport);

            options.LoserPays = true;
            options.LoserPaysMultiple = 10.0;
            options.LoserPaysAfterAbandonment = true;
            options.IncludeAgreementToBargainDecisions = true;
            options.MyGamePretrialDecisionGeneratorGenerator = null;
            brPlusReport = PerformEvolution(options, description + " BrPlus", false);
            Debug.WriteLine(brPlusReport);

            options.LoserPays = false;
            options.MyGamePretrialDecisionGeneratorGenerator = null;
            amRuleReport = PerformEvolution(options, description + " American", true);
            Debug.WriteLine(amRuleReport);

            options.LoserPays = false;
            options.MyGamePretrialDecisionGeneratorGenerator = new MyGameSideBet() {DamagesMultipleForChallengedToPay = 1.0, DamagesMultipleForChallengerToPay = 1.0};
            sideBetReport = PerformEvolution(options, description + " SideBet", true);
            Debug.WriteLine(sideBetReport);

            options.LoserPays = false;
            options.MyGamePretrialDecisionGeneratorGenerator = new MyGameSideBet() { DamagesMultipleForChallengedToPay = 5.0, DamagesMultipleForChallengerToPay = 5.0 };
            sideBetLargeReport = PerformEvolution(options, description + " SideLarge", true);
            Debug.WriteLine(sideBetLargeReport);

            string combined = amRuleReport + brRuleReport + brPlusReport + sideBetReport + sideBetLargeReport;
            return combined;
        }

        private static string PerformEvolution(MyGameOptions options, string reportName, bool includeFirstLine)
        {
            if (options.IncludeCourtSuccessReport || options.IncludeSignalsReport)
                if (NumRepetitions > 1)
                    throw new Exception("Can include multiple reports only with 1 repetition. Use console output rather than string copied."); // problem is that we can't merge the reports if NumRepetitions > 1 when we have more than one report. TODO: Fix this. 
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            var evolutionSettings = GetEvolutionSettings();
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            string cumulativeReport = "";
            for (int i = 0; i < NumRepetitions; i++)
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
