using System;
using System.Collections.Generic;
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

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
            MyGameProgress gameProgress = (MyGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }

        public static void EvolveMyGame()
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            var options = MyGameOptionsGenerator.FourBargainingRounds_PerfectInformation();
            //var options = MyGameOptionsGenerator.UsingRawSignals_10Points_1Round();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 2,
                ParallelOptimization = true,

                InitialRandomSeed = 30,

                Algorithm = GameApproximationAlgorithm.AbramowiczProbing,
                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalVanillaCFRIterations = 100_000_000,

                ReportEveryNIterations = 50000,
                NumRandomIterationsForReporting = 500,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever,
                PrintInformationSetsAfterReport = false,
                PrintGameTreeAfterReport = false,

                TotalProbingCFRIterations = 300_000,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 3,
                TriggerIncreaseOverTime = 20,

                OverrideForAlternativeReport = null //MyGameActionsGenerator.PlaintiffShouldOffer10IfReceivingAtLeastSignal9
            };
            const int numRepetitions = 20;
            for (int i = 0; i < numRepetitions; i++)
            {
                CounterfactualRegretMaximization developer =
                    new CounterfactualRegretMaximization(starterStrategies, evolutionSettings, gameDefinition);
                developer.DevelopStrategies();
            }
        }
    }
}
