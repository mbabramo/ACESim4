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
            Func<Decision, byte> actionsOverride)
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
            var options = MyGameOptionsGenerator.TwoAlternatingOffers_FivePossibilities_LowNoise();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 2,
                ParallelOptimization = true,
                Algorithm = CRMAlgorithm.Probing,
                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalProbingCFRIterations = 1000000,
                TotalVanillaCFRIterations = 100000000,
                ReportEveryNIterations = 1000,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever
            };
            CRMDevelopment developer = new CRMDevelopment(starterStrategies, evolutionSettings, gameDefinition);
            developer.DevelopStrategies();
        }
    }
}
